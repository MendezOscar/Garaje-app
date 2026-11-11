using System.Globalization;
using System.Text;
using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Sales;
using Garaj.Application.Tenants;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Documents;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Reportes de ingresos.
/// </summary>
/// <remarks>
/// El desglose entre repuestos y mano de obra sale de <c>SaleLine.LineType</c>, no de una
/// clasificación aparte: por eso siempre cuadra con lo facturado. Las agregaciones se hacen
/// en SQL —nada de traer filas a memoria—, salvo el agrupamiento por periodo, que se hace en
/// C# porque necesita la zona horaria del taller y Postgres no la conoce.
/// </remarks>
public class ReportService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ITenantService tenants) : IReportService
{
    private static readonly CultureInfo Culture = new("es-HN");

    /// <summary>
    /// Honduras no tiene horario de verano, así que un desplazamiento fijo basta y evita
    /// depender de la base de datos de zonas horarias del contenedor.
    /// </summary>
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(-6);

    public async Task<RevenueReportDto> RevenueAsync(
        RevenueQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureManagement();

        var (from, to) = ResolveRange(query);
        var sales = ScopedSales(scope, query.BranchId, from, to, query.TechnicianId);

        // El técnico sale de la orden, no de la venta: una venta de mostrador no tiene orden,
        // y por eso el join es por la izquierda y el id queda nulo.
        var lines = from line in db.SaleLines.AsNoTracking()
                    join sale in sales on line.SaleId equals sale.Id
                    join order in db.WorkOrders.AsNoTracking()
                        on sale.WorkOrderId equals (Guid?)order.Id into matched
                    from order in matched.DefaultIfEmpty()
                    select new
                    {
                        sale.Id,
                        sale.SaleDate,
                        sale.BranchId,
                        sale.WorkOrderId,
                        TechnicianId = order == null ? null : order.AssignedTechnicianId,
                        line.LineType,
                        line.Total,
                        line.Quantity,
                        line.UnitCost,
                        line.PartId
                    };

        // Una sola pasada por la base; el resto se agrupa en memoria sobre este conjunto,
        // que es del tamaño de las ventas del rango, no del histórico.
        var rows = await lines.ToListAsync(ct);

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct);

        // El armado de motos entra como una tercera fuente de ingreso, al lado de repuestos y
        // mano de obra. Se fecha por el día en que se cerró el corte —el día en que se le
        // facturó al contratista—, no por los días en que se armaron las motos.
        var armado = tenant?.AssemblyEnabled == true
            ? await ArmadoDelRangoAsync(from, to, query.BranchId, ct)
            : [];

        var armadoPorPeriodo = armado
            .GroupBy(a => PeriodStart(a.ClosedAt, query.GroupBy))
            .ToDictionary(g => g.Key, g => (Cobro: g.Sum(x => x.Charge), Gasto: g.Sum(x => x.Expense)));

        var periodos = rows.Select(r => PeriodStart(r.SaleDate, query.GroupBy))
            .Concat(armadoPorPeriodo.Keys)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        var porPeriodo = rows
            .GroupBy(r => PeriodStart(r.SaleDate, query.GroupBy))
            .ToDictionary(g => g.Key, g => g.ToList());

        var points = periodos
            .Select(p => BuildPoint(
                p,
                query.GroupBy,
                porPeriodo.GetValueOrDefault(p, [])
                    .Select(x => (x.Id, x.LineType, x.Total, x.Quantity, x.UnitCost)),
                armadoPorPeriodo.GetValueOrDefault(p)))
            .ToList();

        var branchNames = await db.Branches.AsNoTracking()
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var branches = rows
            .GroupBy(r => r.BranchId)
            .Select(g => new BranchRevenueDto(
                g.Key,
                branchNames.GetValueOrDefault(g.Key, "—"),
                g.Where(x => x.LineType == LineType.Part).Sum(x => x.Total),
                g.Where(x => x.LineType == LineType.Labor).Sum(x => x.Total),
                g.Sum(x => x.Total),
                g.Select(x => x.Id).Distinct().Count()))
            .OrderByDescending(b => b.Total)
            .ToList();

        var technicianIds = rows
            .Where(r => r.TechnicianId is not null)
            .Select(r => r.TechnicianId!.Value)
            .Distinct()
            .ToList();

        var technicianNames = technicianIds.Count == 0
            ? []
            : await db.UsersInTenant
                .Where(u => technicianIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var technicians = rows
            .GroupBy(r => r.TechnicianId)
            .Select(g => new TechnicianRevenueDto(
                g.Key,
                g.Key is { } id
                    ? technicianNames.GetValueOrDefault(id, "—")
                    // Venta de mostrador, u orden que se cerró sin asignar a nadie.
                    : "Sin técnico",
                g.Where(x => x.LineType == LineType.Part).Sum(x => x.Total),
                g.Where(x => x.LineType == LineType.Labor).Sum(x => x.Total),
                g.Sum(x => x.Total),
                g.Sum(x => x.Quantity * x.UnitCost),
                g.Sum(x => x.Total) - g.Sum(x => x.Quantity * x.UnitCost),
                g.Select(x => x.Id).Distinct().Count()))
            .OrderByDescending(t => t.Total)
            .ToList();

        var partIds = rows.Where(r => r.PartId is not null).Select(r => r.PartId!.Value).Distinct().ToList();
        var parts = partIds.Count == 0
            ? []
            : await db.Parts.AsNoTracking()
                .Where(p => partIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Sku, p.Name })
                .ToDictionaryAsync(p => p.Id, ct);

        var topParts = rows
            .Where(r => r.PartId is not null)
            .GroupBy(r => r.PartId!.Value)
            .Select(g => new TopPartDto(
                g.Key,
                parts.GetValueOrDefault(g.Key)?.Sku ?? "—",
                parts.GetValueOrDefault(g.Key)?.Name ?? "—",
                g.Sum(x => x.Quantity),
                g.Sum(x => x.Total),
                g.Sum(x => x.Total) - g.Sum(x => x.Quantity * x.UnitCost)))
            .OrderByDescending(p => p.Revenue)
            .Take(10)
            .ToList();

        var partsRevenue = rows.Where(r => r.LineType == LineType.Part).Sum(r => r.Total);
        var laborRevenue = rows.Where(r => r.LineType == LineType.Labor).Sum(r => r.Total);

        // Sin orden de trabajo es una venta de mostrador: alguien entró, compró un repuesto
        // y se fue. Es la pregunta de «cuánto deja solo vender», que en el total de repuestos
        // no se distingue de los que se le montaron a un vehículo.
        var counter = rows.Where(r => r.WorkOrderId == null).ToList();
        var counterPartsRevenue = counter.Where(r => r.LineType == LineType.Part).Sum(r => r.Total);
        var counterSaleCount = counter.Select(r => r.Id).Distinct().Count();
        var armadoCobro = armado.Sum(a => a.Charge);
        var total = partsRevenue + laborRevenue + armadoCobro;
        var cost = rows.Sum(r => r.Quantity * r.UnitCost) + armado.Sum(a => a.Expense);

        return new RevenueReportDto(
            from,
            to,
            query.GroupBy,
            tenant?.Currency ?? "HNL",
            partsRevenue,
            laborRevenue,
            total,
            cost,
            total - cost,
            total == 0 ? 0 : Math.Round((total - cost) / total * 100m, 1),
            rows.Select(r => r.Id).Distinct().Count(),
            counterPartsRevenue,
            counterSaleCount,
            armadoCobro,
            armado.Select(a => a.SettlementId).Distinct().Count(),
            points,
            branches,
            technicians,
            topParts);
    }

    public async Task<DashboardDto> DashboardAsync(Guid? branchId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureManagement();

        if (branchId is { } id) scope.EnsureBranchAllowed(id);

        var now = clock.UtcNow;
        var todayStart = StartOfLocalDay(now);
        var weekStart = todayStart.AddDays(-(int)ToLocal(now).DayOfWeek);
        var monthStart = StartOfLocalMonth(now);

        var monthSales = ScopedSales(scope, branchId, monthStart, now);

        var monthLines = await (
            from line in db.SaleLines.AsNoTracking()
            join sale in monthSales on line.SaleId equals sale.Id
            select new { sale.SaleDate, line.Total, line.Quantity, line.UnitCost, line.LineType }
        ).ToListAsync(ct);

        // Lo que el Encargado puede mirar: sus sucursales. El Dueño, null, que es «todas».
        var suyas = scope.BranchFilter();

        var orders = db.WorkOrders.AsNoTracking();
        if (branchId is { } b) orders = orders.Where(w => w.BranchId == b);
        if (suyas is not null) orders = orders.Where(w => suyas.Contains(w.BranchId));

        var byStatus = await orders
            .Where(w => w.Status != WorkOrderStatus.Delivered && w.Status != WorkOrderStatus.Cancelled)
            .GroupBy(w => w.Status)
            .Select(g => new StatusCountDto(g.Key, g.Count()))
            .ToListAsync(ct);

        var late = await orders.CountAsync(
            w => w.PromisedAt != null && w.PromisedAt < now
                 && w.Status != WorkOrderStatus.Delivered && w.Status != WorkOrderStatus.Cancelled,
            ct);

        var pendingRequests = await db.ServiceRequests.AsNoTracking()
            .CountAsync(r => r.Status == ServiceRequestStatus.Pending
                             && (branchId == null || r.BranchId == branchId)
                             && (suyas == null || suyas.Contains(r.BranchId)), ct);

        var awaitingQuotes = await db.Quotes.AsNoTracking()
            .CountAsync(q => q.Status == QuoteStatus.Sent && q.RespondedAt == null
                             && (branchId == null || q.BranchId == branchId)
                             && (suyas == null || suyas.Contains(q.BranchId)), ct);

        // Cuentas por cobrar: lo facturado que todavía no entró en caja. Se calcula en SQL
        // sobre las ventas vivas; las anuladas ya no se cobran.
        var pending = await ScopedSales(scope, branchId, DateTimeOffset.MinValue, now)
            .Select(s => new
            {
                s.DueDate,
                Balance = s.Total - (s.Payments.Sum(p => (decimal?)p.Amount) ?? 0)
            })
            .Where(x => x.Balance > 0)
            .ToListAsync(ct);

        // La agenda del día y lo que espera respuesta: es trabajo que ya está comprometido y
        // se pierde si nadie lo mira.
        var hoy = clock.Today();
        var citas = db.Appointments.AsNoTracking()
            .Where(a => (branchId == null || a.BranchId == branchId)
                        && (suyas == null || suyas.Contains(a.BranchId)));

        var citasPorConfirmar = await citas.CountAsync(
            a => a.Status == AppointmentStatus.Requested && a.Day >= hoy, ct);

        var citasDeHoy = await citas.CountAsync(
            a => a.Status == AppointmentStatus.Confirmed && a.Day == hoy, ct);

        var belowMinimum = await db.StockItems.AsNoTracking()
            .CountAsync(s => s.MinQuantity > 0 && s.Quantity <= s.MinQuantity
                             && (branchId == null || s.BranchId == branchId)
                             && (suyas == null || suyas.Contains(s.BranchId)), ct);

        // Los últimos 14 días para la mini gráfica del tablero.
        var chartFrom = todayStart.AddDays(-13);
        var chartLines = await (
            from line in db.SaleLines.AsNoTracking()
            join sale in ScopedSales(scope, branchId, chartFrom, now) on line.SaleId equals sale.Id
            select new { sale.Id, sale.SaleDate, line.LineType, line.Total, line.Quantity, line.UnitCost }
        ).ToListAsync(ct);

        var lastDays = Enumerable.Range(0, 14)
            .Select(offset => chartFrom.AddDays(offset))
            .Select(day => BuildPoint(
                day,
                RevenueGrouping.Day,
                chartLines
                    .Where(l => PeriodStart(l.SaleDate, RevenueGrouping.Day) == day)
                    .Select(l => (l.Id, l.LineType, l.Total, l.Quantity, l.UnitCost))))
            .ToList();

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct);

        var monthTotal = monthLines.Sum(l => l.Total);

        return new DashboardDto(
            tenant?.Currency ?? "HNL",
            monthLines.Where(l => l.SaleDate >= todayStart).Sum(l => l.Total),
            monthLines.Where(l => l.SaleDate >= weekStart).Sum(l => l.Total),
            monthTotal,
            monthTotal - monthLines.Sum(l => l.Quantity * l.UnitCost),
            byStatus.Sum(s => s.Count),
            pendingRequests,
            late,
            awaitingQuotes,
            belowMinimum,
            citasPorConfirmar,
            citasDeHoy,
            pending.Sum(x => x.Balance),
            pending.Where(x => x.DueDate != null && x.DueDate < todayStart).Sum(x => x.Balance),
            byStatus.OrderBy(s => s.Status).ToList(),
            lastDays);
    }

    public async Task<byte[]> RevenueCsvAsync(RevenueQuery query, CancellationToken ct = default)
    {
        var report = await RevenueAsync(query, ct);

        var csv = new StringBuilder();
        csv.AppendLine("Periodo;Repuestos;Mano de obra;Total;Costo;Margen;Ventas");

        foreach (var point in report.Points)
        {
            csv.AppendLine(string.Join(';', [
                point.PeriodLabel,
                Number(point.PartsRevenue),
                Number(point.LaborRevenue),
                Number(point.Total),
                Number(point.Cost),
                Number(point.Margin),
                point.SaleCount.ToString(Culture)
            ]));
        }

        // El reparto por técnico va debajo, en su propio bloque: en una sola tabla no cabe
        // sin repetir el periodo en cada fila, y así se pega a una hoja aparte sin tocarlo.
        if (report.Technicians.Count > 0)
        {
            csv.AppendLine();
            csv.AppendLine("Técnico;Repuestos;Mano de obra;Total;Costo;Margen;Ventas");

            foreach (var technician in report.Technicians)
            {
                csv.AppendLine(string.Join(';', [
                    technician.TechnicianName,
                    Number(technician.PartsRevenue),
                    Number(technician.LaborRevenue),
                    Number(technician.Total),
                    Number(technician.Cost),
                    Number(technician.Margin),
                    technician.SaleCount.ToString(Culture)
                ]));
            }
        }

        csv.AppendLine();
        csv.AppendLine(string.Join(';', [
            "Solo venta de repuestos",
            Number(report.CounterPartsRevenue),
            Number(0),
            Number(report.CounterPartsRevenue),
            "",
            "",
            report.CounterSaleCount.ToString(Culture)
        ]));

        csv.AppendLine(string.Join(';', [
            "TOTAL",
            Number(report.PartsRevenue),
            Number(report.LaborRevenue),
            Number(report.Total),
            Number(report.Cost),
            Number(report.Margin),
            report.SaleCount.ToString(Culture)
        ]));

        // Con BOM: sin él, Excel en Windows abre los acentos rotos.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    // ---------- Libro de ventas ----------

    public async Task<byte[]> SalesBookCsvAsync(
        int year, int month, Guid? branchId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        // El libro de ventas es del taller, no de una sucursal: es lo que se le entrega al
        // contador y lleva los números anulados. Queda en el Dueño aunque el Encargado sí vea
        // los ingresos de su sucursal.
        scope.EnsureOwner();

        if (month is < 1 or > 12) throw new AppException("El mes tiene que estar entre 1 y 12.");
        if (year is < 2000 or > 2100) throw new AppException("El año no parece un año.");

        // El mes del taller, no el UTC: una factura del 31 a las 7 de la noche pertenece a ese
        // mes, y agrupando por la fecha UTC caería en el siguiente.
        var from = new DateTimeOffset(year, month, 1, 0, 0, 0, LocalOffset);
        var to = from.AddMonths(1);
        var (utcFrom, utcTo) = (from.ToUniversalTime(), to.ToUniversalTime());

        // Aquí sí entran las anuladas: el régimen exige reportar el número anulado, no
        // esconderlo, y por eso este listado no reusa `ScopedSales`.
        var q = db.Sales.AsNoTracking()
            .Where(s => s.SaleDate >= utcFrom && s.SaleDate < utcTo);

        if (branchId is { } id) q = q.Where(s => s.BranchId == id);

        var rows = await q
            .OrderBy(s => s.SaleDate).ThenBy(s => s.Number)
            .Select(s => new
            {
                s.SaleDate,
                s.Number,
                s.FiscalNumber,
                s.FiscalCai,
                BranchName = s.Branch.Name,
                Customer = s.CustomerName
                    ?? db.Customers.Where(c => c.Id == s.CustomerId)
                        .Select(c => c.FullName).FirstOrDefault(),
                s.CustomerTaxId,
                s.Subtotal,
                s.DiscountTotal,
                s.TaxTotal,
                s.Total,
                s.IsVoided
            })
            .ToListAsync(ct);

        var csv = new StringBuilder();
        csv.AppendLine(
            "Fecha;Factura;Numero fiscal;CAI;Sucursal;Cliente;RTN;Exento;Gravado;ISV;Total;Estado");

        foreach (var row in rows)
        {
            // Todo se factura gravado al 15%: el gravado es la base y el exento va en cero.
            // Si algún día hay líneas exentas, esta es la columna que cambia.
            var gravado = row.Subtotal - row.DiscountTotal;

            csv.AppendLine(string.Join(';', [
                ToLocal(row.SaleDate).ToString("dd/MM/yyyy", Culture),
                row.Number,
                row.FiscalNumber ?? "",
                row.FiscalCai ?? "",
                Clean(row.BranchName),
                Clean(row.Customer ?? "Consumidor final"),
                row.CustomerTaxId ?? "",
                Number(0),
                Number(gravado),
                Number(row.TaxTotal),
                Number(row.Total),
                row.IsVoided ? "ANULADA" : ""
            ]));
        }

        csv.AppendLine();

        var vivas = rows.Where(r => !r.IsVoided).ToList();
        csv.AppendLine(string.Join(';', [
            "TOTAL", "", "", "", "", "", "",
            Number(0),
            Number(vivas.Sum(r => r.Subtotal - r.DiscountTotal)),
            Number(vivas.Sum(r => r.TaxTotal)),
            Number(vivas.Sum(r => r.Total)),
            $"{vivas.Count} facturas, {rows.Count - vivas.Count} anuladas"
        ]));

        // Con BOM: sin él, Excel en Windows abre los acentos rotos.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    /// <summary>Un punto y coma dentro de un nombre partiría la fila en dos columnas.</summary>
    private static string Clean(string value) => value.Replace(';', ',');

    // ---------- Cierre de caja ----------

    public async Task<CashCloseDto> CashCloseAsync(
        DateTimeOffset? day, Guid? branchId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureManagement();

        // El día es el del taller: un abono de las 7 de la noche cae en la caja de ese día, y
        // agrupando por la fecha UTC caería en la del siguiente.
        var start = StartOfLocalDay(day ?? clock.UtcNow);
        var end = start.AddDays(1);
        var (utcFrom, utcTo) = (start.ToUniversalTime(), end.ToUniversalTime());

        var payments = db.SalePayments.AsNoTracking()
            .Where(p => p.PaidAt >= utcFrom && p.PaidAt < utcTo);

        if (branchId is { } id) payments = payments.Where(p => p.Sale.BranchId == id);

        if (scope.BranchFilter() is { } suyas)
            payments = payments.Where(p => suyas.Contains(p.Sale.BranchId));

        var rows = await payments
            .OrderBy(p => p.PaidAt)
            .Select(p => new
            {
                p.PaidAt,
                p.Method,
                p.Reference,
                p.Amount,
                p.CreatedByUserId,
                p.Sale.Number,
                p.Sale.IsVoided,
                BranchName = p.Sale.Branch.Name,
                // El nombre congelado al facturar manda sobre el de la ficha, igual que en la
                // factura: la caja de ayer no cambia porque hoy le corrijan el nombre.
                CustomerName = p.Sale.CustomerName
                    ?? db.Customers.Where(c => c.Id == p.Sale.CustomerId)
                        .Select(c => c.FullName).FirstOrDefault()
            })
            .ToListAsync(ct);

        // Una venta anulada no entró en caja, pero sus abonos siguen en la base: se apartan y
        // se informan aparte en lugar de desaparecer sin explicación.
        var anuladas = rows.Where(r => r.IsVoided).ToList();
        var vivas = rows.Where(r => !r.IsVoided).ToList();

        var userIds = vivas.Where(r => r.CreatedByUserId is not null)
            .Select(r => r.CreatedByUserId!.Value)
            .Distinct()
            .ToList();

        var names = userIds.Count == 0
            ? []
            : await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        string Receiver(Guid? userId) =>
            userId is { } uid ? names.GetValueOrDefault(uid, "—") : "—";

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct);

        var branchName = branchId is { } bid
            ? await db.Branches.AsNoTracking()
                .Where(b => b.Id == bid).Select(b => b.Name).FirstOrDefaultAsync(ct)
            : null;

        return new CashCloseDto(
            start,
            start.ToString("dddd d 'de' MMMM 'de' yyyy", Culture),
            branchId,
            branchName,
            tenant?.Currency ?? "HNL",
            vivas.Sum(r => r.Amount),
            vivas.Count,
            vivas.GroupBy(r => r.Method)
                .OrderBy(g => g.Key)
                .Select(g => new CashCloseMethodDto(g.Key, g.Sum(r => r.Amount), g.Count()))
                .ToList(),
            vivas.GroupBy(r => Receiver(r.CreatedByUserId))
                .OrderByDescending(g => g.Sum(r => r.Amount))
                .Select(g => new CashCloseReceiverDto(g.Key, g.Sum(r => r.Amount), g.Count()))
                .ToList(),
            vivas.Select(r => new CashClosePaymentDto(
                    r.PaidAt, r.Number, r.CustomerName, r.BranchName, r.Method, r.Reference,
                    Receiver(r.CreatedByUserId), r.Amount))
                .ToList(),
            anuladas.Count,
            anuladas.Sum(r => r.Amount));
    }

    public async Task<byte[]> CashClosePdfAsync(
        DateTimeOffset? day, Guid? branchId, CancellationToken ct = default)
    {
        var cierre = await CashCloseAsync(day, branchId, ct);

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct)
            ?? throw new NotFoundException("El taller no existe.");

        var logo = await tenants.TryGetLogoBytesAsync(tenant.Id, ct);

        return CashClosePdf.Render(cierre, tenant.Name, tenant.LegalName, tenant.Phone, logo);
    }

    // ---------- Interno ----------

    /// <summary>Las ventas anuladas no cuentan: un reporte que las incluyera mentiría.</summary>
    public async Task<TechnicianTimeReportDto> TechnicianTimeAsync(
        RevenueQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureManagement();

        var (from, to) = ResolveRange(query);
        var (utcFrom, utcTo) = (from.ToUniversalTime(), to.ToUniversalTime());
        var ahora = clock.UtcNow;

        // Un tramo entra al rango por donde empezó: el que arrancó ayer y terminó hoy es
        // trabajo de ayer, que es como lo cuenta quien lo hizo.
        var tramos = await (
            from t in db.WorkOrderTimeEntries.AsNoTracking()
            join o in db.WorkOrders.AsNoTracking() on t.WorkOrderId equals o.Id
            where t.StartedAt >= utcFrom && t.StartedAt <= utcTo
            select new
            {
                t.WorkOrderId,
                t.UserId,
                t.StartedAt,
                t.EndedAt,
                t.AutoClosed,
                o.BranchId,
                o.AssignedTechnicianId
            }).ToListAsync(ct);

        if (query.BranchId is { } sucursal)
            tramos = tramos.Where(t => t.BranchId == sucursal).ToList();

        if (scope.BranchFilter() is { } suyas)
            tramos = tramos.Where(t => suyas.Contains(t.BranchId)).ToList();

        if (query.TechnicianId is { } tecnico)
            tramos = tramos.Where(t => t.UserId == tecnico).ToList();

        var minutos = tramos.ToDictionary(
            t => (t.WorkOrderId, t.UserId, t.StartedAt),
            t => (int)Duracion(t.StartedAt, t.EndedAt, ahora).TotalMinutes);

        var ordenIds = tramos.Select(t => t.WorkOrderId).Distinct().ToList();
        var userIds = tramos.Select(t => t.UserId).Distinct().ToList();

        var nombres = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        // Lo presupuestado de cada orden: las horas de sus pasos. El trabajo de precio fijo
        // no las tiene y queda en cero, que es lo honesto: no hay con qué compararlo.
        var horasPorOrden = await db.WorkOrderTasks.AsNoTracking()
            .Where(t => ordenIds.Contains(t.WorkOrderId))
            .GroupBy(t => t.WorkOrderId)
            .Select(g => new
            {
                WorkOrderId = g.Key,
                Horas = g.Sum(t => (t.ActualHours ?? t.EstimatedHours) ?? 0)
            })
            .ToDictionaryAsync(x => x.WorkOrderId, x => x.Horas, ct);

        var ordenes = await db.WorkOrders.AsNoTracking()
            .Where(o => ordenIds.Contains(o.Id))
            .Select(o => new
            {
                o.Id,
                o.Number,
                o.OpenedAt,
                Vehiculo = o.Vehicle == null ? null : o.Vehicle.Brand + " " + o.Vehicle.Model,
                Equipo = o.Equipment == null ? null : o.Equipment.Brand + " " + o.Equipment.Model
            })
            .ToListAsync(ct);

        var filas = tramos
            .GroupBy(t => t.UserId)
            .Select(g => new TechnicianTimeRowDto(
                g.Key,
                nombres.GetValueOrDefault(g.Key, "Sin nombre"),
                g.Select(t => t.WorkOrderId).Distinct().Count(),
                g.Sum(t => minutos[(t.WorkOrderId, t.UserId, t.StartedAt)]),
                g.Select(t => t.WorkOrderId).Distinct()
                    .Sum(id => horasPorOrden.GetValueOrDefault(id, 0)),
                g.Count(t => t.AutoClosed)))
            .OrderByDescending(f => f.Minutes)
            .ToList();

        var porOrden = ordenes
            .Select(o =>
            {
                var suyos = tramos.Where(t => t.WorkOrderId == o.Id).ToList();

                return new WorkOrderTimeRowDto(
                    o.Id,
                    o.Number,
                    o.Vehiculo ?? o.Equipo ?? "",
                    string.Join(", ", suyos.Select(t => nombres.GetValueOrDefault(t.UserId, "Sin nombre"))
                        .Distinct()),
                    o.OpenedAt,
                    suyos.Sum(t => minutos[(t.WorkOrderId, t.UserId, t.StartedAt)]),
                    horasPorOrden.GetValueOrDefault(o.Id, 0),
                    suyos.Any(t => t.EndedAt is null));
            })
            .OrderByDescending(o => o.Minutes)
            .ToList();

        return new TechnicianTimeReportDto(
            from, to, filas.Sum(f => f.Minutes), filas, porOrden);
    }

    /// <summary>
    /// Lo que duró un tramo, recortado a una jornada: el cronómetro que nadie paró el viernes
    /// no puede decir que se trabajaron sesenta horas.
    /// </summary>
    private static TimeSpan Duracion(DateTimeOffset desde, DateTimeOffset? hasta, DateTimeOffset ahora)
    {
        var duracion = (hasta ?? ahora) - desde;

        if (duracion < TimeSpan.Zero) return TimeSpan.Zero;

        return duracion > WorkOrderTimeEntry.Jornada ? WorkOrderTimeEntry.Jornada : duracion;
    }

    private IQueryable<Sale> ScopedSales(
        AccessScope scope, Guid? branchId, DateTimeOffset from, DateTimeOffset to,
        Guid? technicianId = null)
    {
        // Los límites se calculan en la hora del taller, pero Npgsql solo acepta UTC para
        // `timestamptz`. Se normalizan aquí, en el único punto donde tocan la base; el
        // agrupamiento por periodo sigue trabajando con la hora local, que es la que importa
        // para decidir a qué día pertenece una venta.
        var (utcFrom, utcTo) = (from.ToUniversalTime(), to.ToUniversalTime());

        var q = db.Sales.AsNoTracking()
            .Where(s => !s.IsVoided && s.SaleDate >= utcFrom && s.SaleDate <= utcTo);

        if (branchId is { } id) q = q.Where(s => s.BranchId == id);

        // El Encargado solo cuenta lo de sus sucursales, haya pedido una o no: sin esto,
        // «todas las sucursales» le enseñaría los ingresos del taller entero.
        if (scope.BranchFilter() is { } suyas) q = q.Where(s => suyas.Contains(s.BranchId));

        // Filtrar por técnico deja fuera el mostrador a propósito: esas ventas no pasaron
        // por nadie, y sumárselas a alguien sería inventarle trabajo.
        if (technicianId is { } technician)
        {
            q = q.Where(s => db.WorkOrders
                .Any(w => w.Id == s.WorkOrderId && w.AssignedTechnicianId == technician));
        }

        return q;
    }

    private static RevenuePointDto BuildPoint(
        DateTimeOffset periodStart,
        RevenueGrouping grouping,
        IEnumerable<(Guid SaleId, LineType LineType, decimal Total, decimal Quantity, decimal UnitCost)> lines,
        (decimal Cobro, decimal Gasto) armado = default)
    {
        var materialized = lines.ToList();

        var parts = materialized.Where(l => l.LineType == LineType.Part).Sum(l => l.Total);
        var labor = materialized.Where(l => l.LineType == LineType.Labor).Sum(l => l.Total);
        var cost = materialized.Sum(l => l.Quantity * l.UnitCost) + armado.Gasto;
        var total = parts + labor + armado.Cobro;

        return new RevenuePointDto(
            periodStart,
            Label(periodStart, grouping),
            parts,
            labor,
            total,
            cost,
            total - cost,
            materialized.Select(l => l.SaleId).Distinct().Count(),
            armado.Cobro);
    }

    /// <summary>
    /// Lo que el armado facturó en el rango, fila por moto: con qué corte salió, cuándo se
    /// cerró ese corte, cuánto se cobró y cuánto le costó al taller armarla.
    /// </summary>
    /// <remarks>
    /// Se cuenta desde la moto y no desde el total del corte para que el filtro por sucursal
    /// signifique algo: el corte no tiene sucursal, cada moto sí. El pago al técnico no entra
    /// en el costo: ese es un gasto de salario, y sumarlo aquí lo restaría dos veces.
    /// </remarks>
    private async Task<List<(Guid SettlementId, DateTimeOffset ClosedAt, decimal Charge, decimal Expense)>>
        ArmadoDelRangoAsync(
            DateTimeOffset from, DateTimeOffset to, Guid? branchId, CancellationToken ct)
    {
        var cortes = db.AssemblySettlements.AsNoTracking()
            .Where(c => c.Kind == AssemblySettlementKind.Billing
                && c.CreatedAt >= from && c.CreatedAt <= to);

        var q = from m in db.MotorcycleAssemblies.AsNoTracking()
                join c in cortes on m.BillingSettlementId equals c.Id
                select new { c.Id, c.CreatedAt, m.BranchId, m.Charge, m.Expense };

        if (branchId is { } id) q = q.Where(x => x.BranchId == id);

        return (await q.ToListAsync(ct))
            .Select(x => (x.Id, x.CreatedAt, x.Charge, x.Expense))
            .ToList();
    }

    /// <summary>
    /// El rango que se va a consultar, en instantes.
    /// </summary>
    /// <remarks>
    /// La pantalla manda días sueltos —<c>2026-11-11</c>—, que se atan a las <b>00:00</b>. Sin
    /// corregirlo, el último día del rango quedaba fuera entero: «Este mes» no enseñaba lo de
    /// hoy, y el total del reporte no cuadraba con el «Este mes» del tablero, que sí lo
    /// cuenta. Un día suelto significa el día completo del taller, de su medianoche a su
    /// 23:59:59, en hora de Honduras y no en UTC —seis horas de diferencia son seis horas de
    /// ventas que caen en el día equivocado—.
    ///
    /// Si viene con hora, se respeta tal cual: entonces quien pregunta quiere un instante.
    /// </remarks>
    private (DateTimeOffset From, DateTimeOffset To) ResolveRange(RevenueQuery query)
    {
        var to = query.To is { } hasta ? FinDelDia(hasta) : clock.UtcNow;

        // Por defecto, el mes en curso: es lo que el dueño mira cuando entra sin filtrar.
        var from = query.From is { } desde ? InicioDelDia(desde) : StartOfLocalMonth(to);

        if (from > to) throw new AppException("La fecha inicial es posterior a la final.");

        return (from, to);
    }

    /// <summary>
    /// Un día suelto es la medianoche del taller; con hora, se deja como vino.
    /// </summary>
    /// <remarks>
    /// Sale en <b>UTC</b>, no con el desplazamiento de Honduras: Npgsql rechaza escribir un
    /// <c>DateTimeOffset</c> cuyo desplazamiento no sea cero contra una columna
    /// <c>timestamptz</c>, y estos dos valores viajan como parámetro de casi todas las
    /// consultas de esta clase. Es el mismo instante, escrito como lo entiende la base.
    /// </remarks>
    private static DateTimeOffset InicioDelDia(DateTimeOffset value) =>
        value.TimeOfDay == TimeSpan.Zero
            ? new DateTimeOffset(value.Year, value.Month, value.Day, 0, 0, 0, LocalOffset)
                .ToUniversalTime()
            : value;

    /// <summary>Y termina a las 23:59:59.999 de ese mismo día del taller, también en UTC.</summary>
    private static DateTimeOffset FinDelDia(DateTimeOffset value) =>
        value.TimeOfDay == TimeSpan.Zero
            ? new DateTimeOffset(value.Year, value.Month, value.Day, 23, 59, 59, 999, LocalOffset)
                .ToUniversalTime()
            : value;

    private static DateTimeOffset ToLocal(DateTimeOffset value) => value.ToOffset(LocalOffset);

    private static DateTimeOffset StartOfLocalDay(DateTimeOffset value)
    {
        var local = ToLocal(value);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, LocalOffset);
    }

    private static DateTimeOffset StartOfLocalMonth(DateTimeOffset value)
    {
        var local = ToLocal(value);
        return new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, LocalOffset);
    }

    /// <summary>
    /// El día contable es el del taller, no UTC: una venta de las 7 de la noche del lunes
    /// aparecería el martes si se agrupara por la fecha UTC.
    /// </summary>
    private static DateTimeOffset PeriodStart(DateTimeOffset value, RevenueGrouping grouping)
    {
        var day = StartOfLocalDay(value);

        return grouping switch
        {
            RevenueGrouping.Week => day.AddDays(-(int)day.DayOfWeek),
            RevenueGrouping.Month => new DateTimeOffset(day.Year, day.Month, 1, 0, 0, 0, LocalOffset),
            _ => day
        };
    }

    private static string Label(DateTimeOffset periodStart, RevenueGrouping grouping) => grouping switch
    {
        RevenueGrouping.Week => $"sem. {ISOWeek.GetWeekOfYear(periodStart.DateTime)}",
        RevenueGrouping.Month => periodStart.ToString("MMM yyyy", Culture),
        _ => periodStart.ToString("dd/MM", Culture)
    };

    private static string Number(decimal value) => value.ToString("0.00", Culture);
}

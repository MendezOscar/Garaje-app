using System.Globalization;
using System.Text;
using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Sales;
using Garaj.Domain.Entities;
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
    IDateTimeProvider clock) : IReportService
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
        scope.EnsureOwner();

        var (from, to) = ResolveRange(query);
        var sales = ScopedSales(scope, query.BranchId, from, to);

        var lines = from line in db.SaleLines.AsNoTracking()
                    join sale in sales on line.SaleId equals sale.Id
                    select new { sale.Id, sale.SaleDate, sale.BranchId, line.LineType, line.Total, line.Quantity, line.UnitCost, line.PartId };

        // Una sola pasada por la base; el resto se agrupa en memoria sobre este conjunto,
        // que es del tamaño de las ventas del rango, no del histórico.
        var rows = await lines.ToListAsync(ct);

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct);

        var points = rows
            .GroupBy(r => PeriodStart(r.SaleDate, query.GroupBy))
            .OrderBy(g => g.Key)
            .Select(g => BuildPoint(g.Key, query.GroupBy, g.Select(x => (x.Id, x.LineType, x.Total, x.Quantity, x.UnitCost))))
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
        var total = partsRevenue + laborRevenue;
        var cost = rows.Sum(r => r.Quantity * r.UnitCost);

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
            points,
            branches,
            topParts);
    }

    public async Task<DashboardDto> DashboardAsync(Guid? branchId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

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

        var orders = db.WorkOrders.AsNoTracking();
        if (branchId is { } b) orders = orders.Where(w => w.BranchId == b);

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
                             && (branchId == null || r.BranchId == branchId), ct);

        var awaitingQuotes = await db.Quotes.AsNoTracking()
            .CountAsync(q => q.Status == QuoteStatus.Sent && q.RespondedAt == null
                             && (branchId == null || q.BranchId == branchId), ct);

        var belowMinimum = await db.StockItems.AsNoTracking()
            .CountAsync(s => s.MinQuantity > 0 && s.Quantity <= s.MinQuantity
                             && (branchId == null || s.BranchId == branchId), ct);

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

        csv.AppendLine();
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

    // ---------- Interno ----------

    /// <summary>Las ventas anuladas no cuentan: un reporte que las incluyera mentiría.</summary>
    private IQueryable<Sale> ScopedSales(
        AccessScope scope, Guid? branchId, DateTimeOffset from, DateTimeOffset to)
    {
        // Los límites se calculan en la hora del taller, pero Npgsql solo acepta UTC para
        // `timestamptz`. Se normalizan aquí, en el único punto donde tocan la base; el
        // agrupamiento por periodo sigue trabajando con la hora local, que es la que importa
        // para decidir a qué día pertenece una venta.
        var (utcFrom, utcTo) = (from.ToUniversalTime(), to.ToUniversalTime());

        var q = db.Sales.AsNoTracking()
            .Where(s => !s.IsVoided && s.SaleDate >= utcFrom && s.SaleDate <= utcTo);

        if (branchId is { } id) q = q.Where(s => s.BranchId == id);

        return q;
    }

    private static RevenuePointDto BuildPoint(
        DateTimeOffset periodStart,
        RevenueGrouping grouping,
        IEnumerable<(Guid SaleId, LineType LineType, decimal Total, decimal Quantity, decimal UnitCost)> lines)
    {
        var materialized = lines.ToList();

        var parts = materialized.Where(l => l.LineType == LineType.Part).Sum(l => l.Total);
        var labor = materialized.Where(l => l.LineType == LineType.Labor).Sum(l => l.Total);
        var cost = materialized.Sum(l => l.Quantity * l.UnitCost);

        return new RevenuePointDto(
            periodStart,
            Label(periodStart, grouping),
            parts,
            labor,
            parts + labor,
            cost,
            parts + labor - cost,
            materialized.Select(l => l.SaleId).Distinct().Count());
    }

    private (DateTimeOffset From, DateTimeOffset To) ResolveRange(RevenueQuery query)
    {
        var to = query.To ?? clock.UtcNow;

        // Por defecto, el mes en curso: es lo que el dueño mira cuando entra sin filtrar.
        var from = query.From ?? StartOfLocalMonth(to);

        if (from > to) throw new AppException("La fecha inicial es posterior a la final.");

        return (from, to);
    }

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

using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Sales;
using Garaj.Application.Tenants;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Domain.Rules;
using Garaj.Infrastructure.Documents;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Ventas: el cierre de una orden y la venta directa de mostrador.
/// </summary>
/// <remarks>
/// Una venta es inmutable. Si estuvo mal, se <b>anula</b> con motivo y se hace otra: editar
/// importes ya cobrados dejaría los reportes sin forma de cuadrar con la caja. Al anular, los
/// repuestos vuelven a la bodega con su movimiento, igual que todo lo demás en el inventario.
/// </remarks>
public class SaleService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ITenantService tenants,
    StockService stock) : ISaleService
{
    public async Task<PagedResult<SaleListItemDto>> ListAsync(
        SaleQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var q = Scoped(scope);

        if (!query.IncludeVoided) q = q.Where(s => !s.IsVoided);

        // La suma se hace en SQL: traer los abonos a memoria para restarlos convertiría la
        // lista de cuentas por cobrar en una consulta por cada venta.
        if (query.OnlyUnpaid)
            q = q.Where(s => !s.IsVoided
                             && s.Total > (s.Payments.Sum(p => (decimal?)p.Amount) ?? 0));
        if (query.CustomerId is { } customerId) q = q.Where(s => s.CustomerId == customerId);
        if (query.WorkOrderId is { } workOrderId) q = q.Where(s => s.WorkOrderId == workOrderId);
        if (query.From is { } from) q = q.Where(s => s.SaleDate >= from);
        if (query.To is { } to) q = q.Where(s => s.SaleDate <= to);

        if (query.BranchId is { } branchId)
        {
            scope.EnsureBranchAllowed(branchId);
            q = q.Where(s => s.BranchId == branchId);
        }

        var total = await q.CountAsync(ct);
        var now = clock.UtcNow;

        // En cuentas por cobrar manda el vencimiento —es el orden en que hay que cobrar— y las
        // que no tienen fecha acordada van al final. En el resto, la venta más reciente arriba.
        var ordered = query.OnlyUnpaid
            ? q.OrderBy(s => s.DueDate == null).ThenBy(s => s.DueDate)
            : q.OrderByDescending(s => s.SaleDate).ThenBy(s => s.Number);

        var items = await ordered
            .ThenByDescending(s => s.SaleDate)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(s => new SaleListItemDto(
                s.Id,
                s.Number,
                s.BranchId,
                s.Branch.Name,
                s.CustomerId,
                db.Customers.Where(c => c.Id == s.CustomerId).Select(c => c.FullName).FirstOrDefault(),
                s.WorkOrderId,
                db.WorkOrders.Where(w => w.Id == s.WorkOrderId).Select(w => w.Number).FirstOrDefault(),
                s.SaleDate,
                s.PaymentMethod,
                s.Total,
                s.Payments.Sum(p => (decimal?)p.Amount) ?? 0,
                s.Total - (s.Payments.Sum(p => (decimal?)p.Amount) ?? 0),
                s.DueDate,
                s.DueDate != null
                    && s.DueDate < now
                    && s.Total > (s.Payments.Sum(p => (decimal?)p.Amount) ?? 0),
                s.IsVoided))
            .ToListAsync(ct);

        return new PagedResult<SaleListItemDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<SaleDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        var sale = await Scoped(scope)
            .Include(s => s.Branch)
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("La venta no existe.");

        return await MapAsync(sale, scope, ct);
    }

    public async Task<SaleDetailDto> CreateAsync(
        CreateSaleRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        if (request.Lines.Count == 0)
            throw new AppException("La venta necesita al menos una línea.");

        scope.EnsureBranchAllowed(request.BranchId);

        if (!await db.Branches.AnyAsync(b => b.Id == request.BranchId, ct))
            throw new NotFoundException("La sucursal no existe.");

        if (request.CustomerId is { } customerId
            && !await db.Customers.AnyAsync(c => c.Id == customerId, ct))
            throw new NotFoundException("El cliente no existe.");

        var tenant = await CurrentTenantAsync(ct);

        var sale = new Sale
        {
            BranchId = request.BranchId,
            CustomerId = request.CustomerId,
            Number = await NextNumberAsync(request.BranchId, ct),
            SaleDate = request.SaleDate ?? clock.UtcNow,
            PaymentMethod = request.PaymentMethod,
            TaxRate = request.TaxRate ?? tenant.DefaultTaxRate,
            DueDate = request.DueDate,
            Notes = Truncate(request.Notes, 2000)
        };

        db.Sales.Add(sale);

        if (request.Fiscal)
            await AssignFiscalNumberAsync(sale, request.CustomerTaxId, ct);

        var sequence = 0;
        foreach (var line in request.Lines)
            sequence = await AddLineAsync(sale, line, scope, ++sequence, ct);

        Recalculate(sale);
        await EnsureCustomerIdentifiedAsync(sale, ct);
        SettleOnCreation(sale, request.InitialPayment, request.PaymentMethod);

        await db.SaveChangesAsync(ct);

        return await GetAsync(sale.Id, ct);
    }

    public async Task<SaleDetailDto> CloseWorkOrderAsync(
        CloseWorkOrderRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        var order = await db.WorkOrders
            .Include(w => w.Vehicle)
            .Include(w => w.Tasks)
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, ct)
            ?? throw new NotFoundException("La orden de trabajo no existe.");

        if (order.Status == WorkOrderStatus.Cancelled)
            throw new ConflictException("La orden está cancelada: no se puede facturar.");

        // Una orden solo se cobra una vez. Si hubo que corregir, primero se anula la venta.
        if (await db.Sales.AnyAsync(s => s.WorkOrderId == order.Id && !s.IsVoided, ct))
            throw new ConflictException(
                "Esta orden ya tiene una venta. Anúlela antes de volver a cerrarla.");

        var tenant = await CurrentTenantAsync(ct);

        var sale = new Sale
        {
            BranchId = order.BranchId,
            CustomerId = order.Vehicle.CustomerId,
            WorkOrderId = order.Id,
            Number = await NextNumberAsync(order.BranchId, ct),
            SaleDate = clock.UtcNow,
            PaymentMethod = request.PaymentMethod,
            TaxRate = request.TaxRate ?? tenant.DefaultTaxRate,
            DueDate = request.DueDate,
            Notes = Truncate(request.Notes, 2000)
        };

        db.Sales.Add(sale);

        if (request.Fiscal)
            await AssignFiscalNumberAsync(sale, request.CustomerTaxId, ct);

        var sequence = 0;

        // Los repuestos ya salieron de la bodega al consumirlos en la orden: aquí solo se
        // facturan. Volver a descontarlos duplicaría la salida.
        var parts = await db.WorkOrderParts.AsNoTracking()
            .Where(p => p.WorkOrderId == order.Id)
            .Select(p => new
            {
                p.PartId, p.Part.Name, p.Part.Sku, p.Quantity, p.UnitPrice, p.UnitCost
            })
            .ToListAsync(ct);

        foreach (var part in parts)
        {
            sale.Lines.Add(new SaleLine
            {
                LineType = LineType.Part,
                PartId = part.PartId,
                Description = $"{part.Name} ({part.Sku})",
                Sequence = ++sequence,
                Quantity = part.Quantity,
                UnitPrice = part.UnitPrice,
                UnitCost = part.UnitCost,
                Total = part.Quantity * part.UnitPrice
            });
        }

        // La mano de obra de la cotización aprobada manda sobre la de los pasos: es el precio
        // que el cliente vio y aceptó, y cobrarle otro al entregar es donde se pierden los
        // clientes. Los repuestos no salen de aquí: esos se cobran como salieron de la bodega.
        if (request.IncludeLabor && request.LaborFromQuoteId is { } quoteId)
        {
            var quote = await db.Quotes.AsNoTracking()
                .Include(q => q.Lines)
                .FirstOrDefaultAsync(q => q.Id == quoteId, ct)
                ?? throw new NotFoundException("La cotización no existe.");

            if (quote.WorkOrderId != order.Id)
                throw new AppException("La cotización no es de esta orden de trabajo.");

            foreach (var line in quote.Lines.Where(l => l.LineType == LineType.Labor)
                         .OrderBy(l => l.Sequence))
            {
                sale.Lines.Add(new SaleLine
                {
                    LineType = LineType.Labor,
                    LaborServiceId = line.LaborServiceId,
                    Description = line.Description,
                    Sequence = ++sequence,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Discount = line.Discount,
                    UnitCost = 0,
                    Total = line.Total
                });
            }
        }
        // En modo manual la mano de obra es una sola línea: el taller acordó un precio por el
        // trabajo, no por cada paso, y desglosarlo inventando cifras sería mentir en la factura.
        else if (request.IncludeLabor && order.LaborMode == LaborMode.Manual)
        {
            if (order.ManualLaborTotal is { } manual && manual > 0)
            {
                sale.Lines.Add(new SaleLine
                {
                    LineType = LineType.Labor,
                    Description = "Mano de obra",
                    Sequence = ++sequence,
                    Quantity = 1,
                    UnitPrice = manual,
                    UnitCost = 0,
                    Total = manual
                });
            }
        }
        else if (request.IncludeLabor)
        {
            var serviceIds = order.Tasks.Where(t => t.LaborServiceId is not null)
                .Select(t => t.LaborServiceId!.Value)
                .Distinct()
                .ToList();

            var services = await db.LaborServices.AsNoTracking()
                .Where(s => serviceIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);

            foreach (var task in order.Tasks.OrderBy(t => t.Sequence))
            {
                var service = task.LaborServiceId is { } serviceId
                    ? services.GetValueOrDefault(serviceId)
                    : null;

                // El precio a mano manda; si no lo hay, la tarifa del catálogo con las horas
                // reales, y si el técnico no las registró, las estimadas. Un paso sin precio
                // es trabajo que no se cobra y no genera línea.
                if (task.PriceWith(service) is not { } price || price <= 0) continue;

                sale.Lines.Add(new SaleLine
                {
                    LineType = LineType.Labor,
                    LaborServiceId = service?.Id,
                    Description = task.Title,
                    Sequence = ++sequence,
                    Quantity = 1,
                    UnitPrice = price,
                    UnitCost = 0,
                    Total = price
                });
            }
        }

        if (sale.Lines.Count == 0)
            throw new AppException(
                "La orden no tiene repuestos ni mano de obra que cobrar. Cargue lo trabajado antes de cerrarla.");

        Recalculate(sale);
        await EnsureCustomerIdentifiedAsync(sale, ct);
        SettleOnCreation(sale, request.InitialPayment, request.PaymentMethod);

        if (request.MarkAsDelivered && order.Status != WorkOrderStatus.Delivered)
        {
            // Se salta la validación de transiciones a propósito: cobrar y entregar es un
            // acto único, y la orden puede estar en cualquier estado abierto cuando ocurre.
            var previous = order.Status;
            order.Status = WorkOrderStatus.Delivered;
            order.ClosedAt = clock.UtcNow;

            db.WorkOrderStatusHistory.Add(new WorkOrderStatusHistory
            {
                WorkOrderId = order.Id,
                FromStatus = previous,
                ToStatus = WorkOrderStatus.Delivered,
                ChangedAt = clock.UtcNow,
                ChangedByUserId = scope.UserId,
                Note = $"Vehículo entregado y facturado en {sale.Number}.",
                IsVisibleToCustomer = true
            });
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(sale.Id, ct);
    }

    public async Task<SaleDetailDto> VoidAsync(
        Guid id, VoidSaleRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new AppException("Una anulación necesita motivo.");

        var sale = await db.Sales.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("La venta no existe.");

        if (sale.IsVoided) throw new ConflictException("La venta ya está anulada.");

        sale.IsVoided = true;
        sale.VoidReason = Truncate(request.Reason, 500);

        // Solo se devuelve el stock de las ventas de mostrador: los repuestos de una orden
        // salieron al consumirlos, y su devolución se maneja quitándolos de la orden.
        if (sale.WorkOrderId is null)
        {
            foreach (var line in sale.Lines.Where(l => l.LineType == LineType.Part && l.PartId is not null))
            {
                await stock.ReturnAsync(
                    sale.BranchId, line.PartId!.Value, line.Quantity, Guid.Empty, scope.UserId,
                    $"Anulación de {sale.Number}", ct);
            }
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<SaleDetailDto> RegisterPaymentAsync(
        Guid id, RegisterPaymentRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        var sale = await db.Sales.Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("La venta no existe.");

        if (sale.IsVoided)
            throw new ConflictException("La venta está anulada: no admite abonos.");

        if (request.Amount <= 0)
            throw new AppException("El abono tiene que ser mayor que cero.");

        var balance = sale.Total - sale.Payments.Sum(p => p.Amount);

        if (balance <= 0)
            throw new ConflictException("La venta ya está pagada por completo.");

        // Cobrar de más no es un abono, es otra cosa: o el total está mal —y entonces se
        // anula la venta— o hay que devolver la diferencia. Aceptarlo dejaría un saldo
        // negativo que ningún reporte sabría leer.
        if (request.Amount > balance)
            throw new AppException($"El abono excede el saldo pendiente ({balance:0.00}).");

        db.SalePayments.Add(new SalePayment
        {
            SaleId = sale.Id,
            Amount = request.Amount,
            Method = request.Method,
            PaidAt = request.PaidAt ?? clock.UtcNow,
            Reference = Truncate(request.Reference, 100),
            Notes = Truncate(request.Notes, 500)
        });

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Borrar un abono es corregir una captura, no devolver dinero: para eso se anula la
    /// venta entera. Por eso no pide motivo ni deja rastro más allá del saldo.
    /// </summary>
    public async Task<SaleDetailDto> RemovePaymentAsync(
        Guid id, Guid paymentId, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var payment = await db.SalePayments
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.SaleId == id, ct)
            ?? throw new NotFoundException("El abono no existe.");

        db.SalePayments.Remove(payment);
        await db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    /// <summary>
    /// El Cliente también puede descargarla: <see cref="GetAsync"/> ya le niega las ajenas,
    /// así que no hace falta otra comprobación aquí.
    /// </summary>
    public async Task<byte[]> PdfAsync(Guid id, CancellationToken ct = default)
    {
        var sale = await GetAsync(id, ct);

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct)
            ?? throw new NotFoundException("El taller no existe.");

        var logo = await tenants.TryGetLogoBytesAsync(tenant.Id, ct);

        return InvoicePdf.Render(
            sale, tenant.Name, tenant.LegalName, tenant.Phone, tenant.TaxId,
            tenant.Email, tenant.Address, logo);
    }

    // ---------- Interno ----------

    private IQueryable<Sale> Scoped(AccessScope scope)
    {
        // Las ventas son del negocio: el técnico no las ve, y el cliente solo las suyas
        // —su factura—, nunca las de otros.
        if (scope.IsTechnician)
            throw new ForbiddenException("Las ventas son solo para el Dueño.");

        var q = db.Sales.AsNoTracking();

        return scope.IsCustomer ? q.Where(s => s.CustomerId == scope.CustomerId) : q;
    }

    private async Task<int> AddLineAsync(
        Sale sale, SaleLineRequest request, AccessScope scope, int sequence, CancellationToken ct)
    {
        if (request.Quantity <= 0)
            throw new AppException("La cantidad debe ser mayor que cero.");

        var line = new SaleLine
        {
            LineType = request.LineType,
            Sequence = sequence,
            Quantity = request.Quantity,
            Discount = request.Discount
        };

        if (request.LineType == LineType.Part)
        {
            var part = request.PartId is { } partId
                ? await db.Parts.FirstOrDefaultAsync(p => p.Id == partId, ct)
                    ?? throw new NotFoundException("El repuesto no existe.")
                : throw new AppException("Una línea de repuesto necesita el repuesto.");

            line.PartId = part.Id;
            line.Description = Describe(request.Description, $"{part.Name} ({part.Sku})");
            line.UnitPrice = request.UnitPrice ?? part.SalePrice;
            line.UnitCost = part.CostPrice;

            // La venta de mostrador sí descuenta: nadie pasó por una orden de trabajo.
            await stock.ConsumeAsync(
                sale.BranchId, part.Id, request.Quantity, Guid.Empty, scope.UserId, ct);
        }
        else
        {
            var service = request.LaborServiceId is { } serviceId
                ? await db.LaborServices.FirstOrDefaultAsync(s => s.Id == serviceId, ct)
                    ?? throw new NotFoundException("El servicio no existe.")
                : null;

            line.LaborServiceId = service?.Id;
            line.Description = Describe(request.Description, service?.Name);
            line.UnitPrice = request.UnitPrice
                ?? service?.PriceFor(null) ?? 0;
            line.UnitCost = 0;
        }

        line.Total = Math.Max(0, line.Quantity * line.UnitPrice - line.Discount);
        sale.Lines.Add(line);

        return sequence;
    }

    private static string Describe(string? given, string? fromCatalog) =>
        given?.Trim() is { Length: > 0 } text
            ? text[..Math.Min(text.Length, 500)]
            : fromCatalog ?? throw new AppException("La línea necesita una descripción.");

    /// <summary>
    /// Los importes se calculan aquí y nunca se aceptan del cliente: es lo que se cobra y lo
    /// que después tiene que cuadrar con la caja.
    /// </summary>
    private static void Recalculate(Sale sale)
    {
        sale.Subtotal = sale.Lines.Sum(l => l.Quantity * l.UnitPrice);
        sale.DiscountTotal = sale.Lines.Sum(l => l.Discount);
        sale.CostTotal = sale.Lines.Sum(l => l.Quantity * l.UnitCost);

        var taxable = sale.Subtotal - sale.DiscountTotal;
        sale.TaxTotal = Math.Round(taxable * sale.TaxRate / 100m, 2);
        sale.Total = taxable + sale.TaxTotal;
    }

    /// <summary>
    /// Emite el correlativo fiscal de la sucursal y deja en la venta una copia del CAI, del
    /// rango y de la fecha límite.
    ///
    /// La copia es a propósito: el rango se agota y se reemplaza, y una factura ya impresa no
    /// puede cambiar de contenido porque el taller renovó su CAI. El número consumido no
    /// vuelve al rango ni cuando la factura se anula, que es lo que exige el régimen.
    /// </summary>
    private async Task AssignFiscalNumberAsync(
        Sale sale, string? customerTaxId, CancellationToken ct)
    {
        var range = await db.FiscalRanges
            .FirstOrDefaultAsync(r => r.BranchId == sale.BranchId && r.IsActive, ct)
            ?? throw new AppException(
                "Esta sucursal no tiene CAI registrado. Regístrelo en Taller → Facturación, " +
                "o emita la venta sin CAI.");

        if (range.IssueDeadline <= clock.UtcNow)
            throw new AppException(
                $"El CAI venció el {range.IssueDeadline.ToLocalTime():dd/MM/yyyy}. " +
                "Pida uno nuevo al SAR y regístrelo antes de facturar.");

        if (range.NextNumber > range.RangeEnd)
            throw new AppException(
                "Se agotó el rango autorizado. Pida uno nuevo al SAR y regístrelo antes de facturar.");

        sale.FiscalRangeId = range.Id;
        sale.FiscalNumber = range.Format(range.NextNumber);
        sale.FiscalCai = range.Cai;
        sale.FiscalRangeText = range.RangeText;
        sale.FiscalIssueDeadline = range.IssueDeadline;

        range.NextNumber++;

        // El RTN de la factura: el que venga escrito, y si no el de la ficha del cliente.
        // Sin ninguno, la factura sale a consumidor final, que es lo correcto.
        var explicito = string.IsNullOrWhiteSpace(customerTaxId) ? null : customerTaxId.Trim();

        sale.CustomerTaxId = explicito ?? (sale.CustomerId is { } customerId
            ? await db.Customers.AsNoTracking()
                .Where(c => c.Id == customerId)
                .Select(c => c.TaxId)
                .FirstOrDefaultAsync(ct)
            : null);
    }

    /// <summary>
    /// Arriba de este monto, la factura fiscal no puede salir a nombre de «Consumidor final»:
    /// el régimen obliga a consignar quién compró.
    /// </summary>
    private const decimal LimiteConsumidorFinal = 10_000m;

    /// <summary>
    /// Comprueba que una factura fiscal grande lleve identificado al cliente. Se llama después
    /// de calcular el total, que es cuando se sabe si pasa del límite.
    /// </summary>
    private async Task EnsureCustomerIdentifiedAsync(Sale sale, CancellationToken ct)
    {
        if (sale.FiscalNumber is null || sale.Total <= LimiteConsumidorFinal) return;
        if (!string.IsNullOrWhiteSpace(sale.CustomerTaxId)) return;

        var identidad = sale.CustomerId is { } customerId
            ? await db.Customers.AsNoTracking()
                .Where(c => c.Id == customerId)
                .Select(c => c.DocumentId)
                .FirstOrDefaultAsync(ct)
            : null;

        if (string.IsNullOrWhiteSpace(identidad))
            throw new AppException(
                $"Arriba de L {LimiteConsumidorFinal:N0} la factura no puede salir a consumidor " +
                "final: anote el RTN o el número de identidad del cliente en su ficha.");
    }

    /// <summary>Correlativo por sucursal, ej. "VTA-MTZ-000312".</summary>
    private async Task<string> NextNumberAsync(Guid branchId, CancellationToken ct)
    {
        var branch = await db.Branches.FirstAsync(b => b.Id == branchId, ct);
        branch.SaleSequence++;

        var prefix = string.IsNullOrEmpty(branch.Code) ? "VTA" : $"VTA-{branch.Code}";
        return $"{prefix}-{branch.SaleSequence:D6}";
    }

    private async Task<Tenant> CurrentTenantAsync(CancellationToken ct) =>
        await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct)
        ?? throw new NotFoundException("El taller no existe.");

    private async Task<SaleDetailDto> MapAsync(Sale sale, AccessScope scope, CancellationToken ct)
    {
        var tenant = await CurrentTenantAsync(ct);

        var customer = sale.CustomerId is { } customerId
            ? await db.Customers.AsNoTracking()
                .Where(c => c.Id == customerId)
                .Select(c => new { c.FullName, c.Phone, c.DocumentId })
                .FirstOrDefaultAsync(ct)
            : null;

        var order = sale.WorkOrderId is { } orderId
            ? await db.WorkOrders.AsNoTracking()
                .Where(w => w.Id == orderId)
                .Select(w => new { w.Number, Vehicle = w.Vehicle.Brand + " " + w.Vehicle.Model })
                .FirstOrDefaultAsync(ct)
            : null;

        // El costo y el margen son datos del negocio: al cliente se le muestra su factura,
        // no cuánto ganó el taller con ella.
        var showCost = !scope.IsCustomer;

        var paid = sale.Payments.Sum(p => p.Amount);

        // Quién recibió el dinero. Solo para el taller: al cliente le da igual y es un dato
        // interno de quién estaba en caja.
        var authorIds = showCost
            ? sale.Payments.Where(p => p.CreatedByUserId != null)
                .Select(p => p.CreatedByUserId!.Value).Distinct().ToList()
            : [];

        var names = authorIds.Count == 0
            ? []
            : await db.Users.AsNoTracking()
                .Where(u => authorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return new SaleDetailDto(
            sale.Id,
            sale.Number,
            sale.BranchId,
            sale.Branch.Name,
            string.Join(", ", new[] { sale.Branch.Address, sale.Branch.City }
                .Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } direccion
                ? direccion
                : null,
            sale.CustomerId,
            customer?.FullName,
            customer?.Phone,
            sale.WorkOrderId,
            order?.Number,
            order?.Vehicle,
            sale.SaleDate,
            sale.PaymentMethod,
            sale.Subtotal,
            sale.DiscountTotal,
            sale.TaxRate,
            sale.TaxTotal,
            sale.Total,
            showCost ? sale.CostTotal : 0,
            showCost ? sale.Total - sale.CostTotal : 0,
            tenant.Currency,
            sale.Notes,
            sale.IsVoided,
            sale.VoidReason,
            paid,
            sale.Total - paid,
            sale.DueDate,
            sale.DueDate is { } due && due < clock.UtcNow && sale.Total > paid,
            sale.FiscalNumber,
            sale.FiscalCai,
            sale.FiscalRangeText,
            sale.FiscalIssueDeadline,
            sale.CustomerTaxId,
            customer?.DocumentId,
            sale.Lines
                .OrderBy(l => l.Sequence)
                .Select(l => new SaleLineDto(
                    l.Id, l.LineType, l.PartId, l.LaborServiceId, l.Description, l.Sequence,
                    l.Quantity, l.UnitPrice, showCost ? l.UnitCost : 0, l.Discount, l.Total))
                .ToList(),
            sale.Payments
                .OrderBy(p => p.PaidAt)
                .Select(p => new SalePaymentDto(
                    p.Id, p.Amount, p.Method, p.PaidAt, p.Reference, p.Notes,
                    names.GetValueOrDefault(p.CreatedByUserId ?? Guid.Empty)))
                .ToList());
    }

    /// <summary>
    /// Deja registrado lo que el cliente pagó al momento de facturar.
    /// </summary>
    /// <remarks>
    /// Sin prima indicada se asume que pagó todo: es la venta normal, y es importante que
    /// también genere su abono. Si el contado no dejara rastro, habría dos maneras distintas
    /// de saber qué se cobró —una por el método de pago y otra por los abonos— y la caja del
    /// día dependería de cuál se mirara.
    /// </remarks>
    private void SettleOnCreation(Sale sale, decimal? initialPayment, PaymentMethod method)
    {
        var amount = initialPayment ?? sale.Total;

        if (amount <= 0) return;

        if (amount > sale.Total)
            throw new AppException("El pago inicial no puede superar el total de la venta.");

        sale.Payments.Add(new SalePayment
        {
            Amount = amount,
            Method = method,
            PaidAt = sale.SaleDate,
            Notes = amount < sale.Total ? "Pago inicial" : null
        });
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

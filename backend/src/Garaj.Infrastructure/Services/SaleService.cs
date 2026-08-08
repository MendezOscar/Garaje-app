using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Sales;
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
    StockService stock) : ISaleService
{
    public async Task<PagedResult<SaleListItemDto>> ListAsync(
        SaleQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var q = Scoped(scope);

        if (!query.IncludeVoided) q = q.Where(s => !s.IsVoided);
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

        var items = await q
            .OrderByDescending(s => s.SaleDate)
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
            Notes = Truncate(request.Notes, 2000)
        };

        db.Sales.Add(sale);

        var sequence = 0;
        foreach (var line in request.Lines)
            sequence = await AddLineAsync(sale, line, scope, ++sequence, ct);

        Recalculate(sale);
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
            Notes = Truncate(request.Notes, 2000)
        };

        db.Sales.Add(sale);

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

        if (request.IncludeLabor)
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
                if (task.LaborServiceId is not { } serviceId ||
                    !services.TryGetValue(serviceId, out var service)) continue;

                // Se cobran las horas reales si el técnico las registró; si no, las estimadas.
                var hours = task.ActualHours ?? task.EstimatedHours ?? service.StandardHours;
                var price = service.IsFixedPrice ? service.FixedPrice : hours * service.HourlyRate;

                sale.Lines.Add(new SaleLine
                {
                    LineType = LineType.Labor,
                    LaborServiceId = service.Id,
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

        return InvoicePdf.Render(sale, tenant.Name, tenant.LegalName, tenant.Phone, tenant.TaxId);
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
                ?? (service is null
                    ? 0
                    : service.IsFixedPrice ? service.FixedPrice : service.StandardHours * service.HourlyRate);
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
                .Select(c => new { c.FullName, c.Phone })
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

        return new SaleDetailDto(
            sale.Id,
            sale.Number,
            sale.BranchId,
            sale.Branch.Name,
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
            sale.Lines
                .OrderBy(l => l.Sequence)
                .Select(l => new SaleLineDto(
                    l.Id, l.LineType, l.PartId, l.LaborServiceId, l.Description, l.Sequence,
                    l.Quantity, l.UnitPrice, showCost ? l.UnitCost : 0, l.Discount, l.Total))
                .ToList());
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

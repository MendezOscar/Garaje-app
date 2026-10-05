using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Sales;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Reclamos: el cliente vuelve diciendo que el trabajo quedó mal.
/// </summary>
/// <remarks>
/// Solo el Dueño. Un reclamo es una discusión de plata y de reputación, y decidir si entra en
/// garantía no es del técnico que lo hizo.
/// </remarks>
public class ClaimService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : IClaimService
{
    public async Task<PagedResult<ClaimListItemDto>> ListAsync(
        ClaimQuery query, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var q = db.Claims.AsNoTracking();

        if (query.Status is { } status) q = q.Where(c => c.Status == status);
        if (query.OnlyOpen) q = q.Where(c => c.Status == ClaimStatus.Open);
        if (query.From is { } from) q = q.Where(c => c.ReceivedAt >= from);
        if (query.To is { } to) q = q.Where(c => c.ReceivedAt <= to);

        if (query.CustomerId is { } customerId)
            q = q.Where(c => c.Sale.CustomerId == customerId);

        var total = await q.CountAsync(ct);

        var items = await q
            // Los abiertos primero: son los que hay que atender, no los que ya se cerraron.
            .OrderBy(c => c.Status == ClaimStatus.Open ? 0 : 1)
            .ThenByDescending(c => c.ReceivedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(c => new ClaimListItemDto(
                c.Id,
                c.Number,
                c.Status,
                c.SaleId,
                c.Sale.Number,
                c.Sale.WorkOrderId,
                db.WorkOrders.Where(w => w.Id == c.Sale.WorkOrderId)
                    .Select(w => w.Number).FirstOrDefault(),
                db.Customers.Where(x => x.Id == c.Sale.CustomerId)
                    .Select(x => x.FullName).FirstOrDefault(),
                db.Customers.Where(x => x.Id == c.Sale.CustomerId)
                    .Select(x => x.Phone).FirstOrDefault(),
                db.Vehicles.Where(v => v.Id == c.Sale.VehicleId)
                    .Select(v => v.Brand + " " + v.Model).FirstOrDefault(),
                c.Reason,
                c.WasUnderWarranty,
                c.ReceivedAt,
                c.ResolvedAt,
                c.RepairWorkOrderId,
                db.WorkOrders.Where(w => w.Id == c.RepairWorkOrderId)
                    .Select(w => w.Number).FirstOrDefault()))
            .ToListAsync(ct);

        return new PagedResult<ClaimListItemDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<ClaimDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var claim = await db.Claims.AsNoTracking()
            .Include(c => c.Sale)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("El reclamo no existe.");

        return await MapAsync(claim, ct);
    }

    public async Task<ClaimDetailDto> CreateAsync(
        CreateClaimRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var motivo = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(motivo))
            throw new AppException("Escriba qué fue lo que pasó.");

        var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == request.SaleId, ct)
            ?? throw new NotFoundException("El trabajo no existe.");

        if (sale.IsVoided)
            throw new AppException("Esa venta está anulada: no hay trabajo que reclamar.");

        var ahora = clock.UtcNow;

        var claim = new Claim
        {
            SaleId = sale.Id,
            Number = await NextNumberAsync(ct),
            Reason = motivo[..Math.Min(motivo.Length, 2000)],
            // Se congela: que la garantía venza mañana no puede cambiar lo que el taller
            // aceptó hoy, y es lo primero que se discute cuando el reclamo se demora.
            WasUnderWarranty = sale.WarrantyUntil is { } hasta && hasta >= ahora,
            ReceivedAt = ahora,
            ReceivedByUserId = tenantContext.UserId
        };

        db.Claims.Add(claim);
        await db.SaveChangesAsync(ct);

        claim.Sale = sale;
        return await MapAsync(claim, ct);
    }

    public async Task<ClaimDetailDto> ResolveAsync(
        Guid id, ResolveClaimRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        if (request.Status == ClaimStatus.Open)
            throw new AppException("Para dejarlo abierto no hace falta resolverlo.");

        var resolucion = request.Resolution?.Trim();
        if (string.IsNullOrWhiteSpace(resolucion))
            throw new AppException("Escriba qué se hizo con el reclamo.");

        var claim = await db.Claims
            .Include(c => c.Sale)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("El reclamo no existe.");

        claim.Status = request.Status;
        claim.Resolution = resolucion[..Math.Min(resolucion.Length, 2000)];
        claim.ResolvedAt = clock.UtcNow;
        claim.ResolvedByUserId = tenantContext.UserId;

        if (request.OpenRepairOrder && claim.RepairWorkOrderId is null)
            claim.RepairWorkOrderId = await AbrirOrdenDeReparacionAsync(claim, ct);

        await db.SaveChangesAsync(ct);

        return await MapAsync(claim, ct);
    }

    public async Task<ClaimDetailDto> ReopenAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var claim = await db.Claims
            .Include(c => c.Sale)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("El reclamo no existe.");

        claim.Status = ClaimStatus.Open;
        claim.ResolvedAt = null;
        claim.ResolvedByUserId = null;

        // La resolución no se borra: es lo que se decidió la vez pasada, y leerla es justo lo
        // que hace falta cuando el cliente vuelve porque no quedó conforme.
        await db.SaveChangesAsync(ct);

        return await MapAsync(claim, ct);
    }

    public async Task<ClaimDetailDto> OpenRepairOrderAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var claim = await db.Claims
            .Include(c => c.Sale)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("El reclamo no existe.");

        if (claim.RepairWorkOrderId is not null)
            throw new ConflictException("Este reclamo ya tiene su orden de reparación.");

        claim.RepairWorkOrderId = await AbrirOrdenDeReparacionAsync(claim, ct);
        await db.SaveChangesAsync(ct);

        return await MapAsync(claim, ct);
    }

    /// <summary>
    /// La orden con la que se repara. Es trabajo nuevo —sus pasos, sus repuestos— y por eso
    /// va aparte de la original: así se puede saber cuánto le costó la garantía al taller.
    /// </summary>
    private async Task<Guid> AbrirOrdenDeReparacionAsync(Claim claim, CancellationToken ct)
    {
        var vehicleId = claim.Sale.VehicleId
            ?? await db.WorkOrders.Where(w => w.Id == claim.Sale.WorkOrderId)
                .Select(w => (Guid?)w.VehicleId).FirstOrDefaultAsync(ct)
            ?? throw new AppException(
                "Ese trabajo no tiene vehículo, así que no se le puede abrir una orden. "
                + "Ábrala desde el vehículo y anótelo en la resolución.");

        var branch = await db.Branches.FirstAsync(b => b.Id == claim.Sale.BranchId, ct);
        branch.WorkOrderSequence++;

        var prefix = string.IsNullOrEmpty(branch.Code) ? "ORD" : branch.Code;

        var order = new WorkOrder
        {
            BranchId = branch.Id,
            VehicleId = vehicleId,
            Number = $"{prefix}-{branch.WorkOrderSequence:D6}",
            Status = WorkOrderStatus.Received,
            Description = $"Reclamo {claim.Number}: {claim.Reason}",
            OpenedAt = clock.UtcNow,
            // De qué reclamo viene, para que la orden lo pueda enseñar y para que la decisión
            // de la garantía tenga dónde vivir.
            ClaimId = claim.Id
            // WarrantyCovered se queda en null: se decide en el diagnóstico, no aquí.
        };

        db.WorkOrders.Add(order);
        return order.Id;
    }

    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantContext.TenantId, ct);
        tenant.ClaimSequence++;

        return $"REC-{tenant.ClaimSequence:D6}";
    }

    private async Task<ClaimDetailDto> MapAsync(Claim claim, CancellationToken ct)
    {
        var sale = claim.Sale;

        var customer = sale.CustomerId is { } customerId
            ? await db.Customers.AsNoTracking()
                .Where(c => c.Id == customerId)
                .Select(c => new { c.FullName, c.Phone })
                .FirstOrDefaultAsync(ct)
            : null;

        var vehicleId = sale.VehicleId
            ?? await db.WorkOrders.AsNoTracking()
                .Where(w => w.Id == sale.WorkOrderId)
                .Select(w => (Guid?)w.VehicleId)
                .FirstOrDefaultAsync(ct);

        var vehicle = vehicleId is { } id
            ? await db.Vehicles.AsNoTracking()
                .Where(v => v.Id == id)
                .Select(v => v.Brand + " " + v.Model)
                .FirstOrDefaultAsync(ct)
            : null;

        var orderNumber = sale.WorkOrderId is { } orderId
            ? await db.WorkOrders.AsNoTracking()
                .Where(w => w.Id == orderId).Select(w => w.Number).FirstOrDefaultAsync(ct)
            : null;

        var repair = claim.RepairWorkOrderId is { } repairId
            ? await db.WorkOrders.AsNoTracking()
                .Where(w => w.Id == repairId)
                .Select(w => new { w.Number, w.ManualLaborTotal, w.WarrantyCovered })
                .FirstOrDefaultAsync(ct)
            : null;

        // Lo que costó repararlo: los repuestos de la orden de garantía a su costo, más la
        // mano de obra escrita a mano si la hubo. Es el número que dice cuánto cuesta la
        // garantía, así que van costos y no precios: al cliente no se le cobró.
        var repairCost = claim.RepairWorkOrderId is { } costId
            ? await db.WorkOrderParts.AsNoTracking()
                .Where(p => p.WorkOrderId == costId)
                .SumAsync(p => (decimal?)(p.Quantity * p.UnitCost), ct) ?? 0
            : 0;

        var names = await NombresAsync(claim, ct);

        return new ClaimDetailDto(
            claim.Id,
            claim.Number,
            claim.Status,
            claim.SaleId,
            sale.Number,
            sale.WorkOrderId,
            orderNumber,
            sale.CustomerId,
            customer?.FullName,
            customer?.Phone,
            vehicle,
            claim.Reason,
            claim.WasUnderWarranty,
            sale.WarrantyUntil,
            claim.ReceivedAt,
            names.Received,
            claim.Resolution,
            claim.ResolvedAt,
            names.Resolved,
            claim.RepairWorkOrderId,
            repair?.Number,
            repairCost + (repair?.ManualLaborTotal ?? 0),
            repair?.WarrantyCovered);
    }

    private async Task<(string? Received, string? Resolved)> NombresAsync(
        Claim claim, CancellationToken ct)
    {
        var ids = new[] { claim.ReceivedByUserId, claim.ResolvedByUserId }
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();

        if (ids.Count == 0) return (null, null);

        var names = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return (
            claim.ReceivedByUserId is { } r ? names.GetValueOrDefault(r) : null,
            claim.ResolvedByUserId is { } s ? names.GetValueOrDefault(s) : null);
    }
}

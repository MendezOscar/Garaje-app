using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Inventory;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Existencias por sucursal.
/// </summary>
/// <remarks>
/// La regla de la que depende todo lo demás: <b>el stock nunca se edita, se mueve</b>. Cada
/// cambio deja un <see cref="StockMovement"/> con el saldo resultante, y
/// <see cref="StockItem.Quantity"/> es solo el saldo cacheado que se actualiza en la misma
/// transacción. Así una diferencia de inventario siempre tiene un responsable y una fecha,
/// en vez de aparecer de la nada.
/// </remarks>
public class StockService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : IStockService
{
    public async Task<PagedResult<StockItemDto>> ListAsync(StockQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var q = Scoped(scope);

        if (query.BranchId is { } branchId)
        {
            scope.EnsureBranchAllowed(branchId);
            q = q.Where(s => s.BranchId == branchId);
        }

        if (query.PartId is { } partId) q = q.Where(s => s.PartId == partId);
        if (!string.IsNullOrWhiteSpace(query.Category)) q = q.Where(s => s.Part.Category == query.Category);
        if (query.OnlyBelowMinimum) q = q.Where(s => s.MinQuantity > 0 && s.Quantity <= s.MinQuantity);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(s =>
                EF.Functions.ILike(s.Part.Sku, $"%{term}%")
                || EF.Functions.ILike(s.Part.Name, $"%{term}%")
                || (s.Part.Brand != null && EF.Functions.ILike(s.Part.Brand, $"%{term}%")));
        }

        var total = await q.CountAsync(ct);

        // Se ordena sobre la entidad y se proyecta después: EF no traduce un OrderBy sobre
        // el DTO ya construido cuando hay joins de por medio.
        var items = await Project(q.OrderBy(s => s.Part.Name).ThenBy(s => s.Branch.Name))
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<StockItemDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<StockItemDto>> AlertsAsync(Guid? branchId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var q = Scoped(scope).Where(s => s.MinQuantity > 0 && s.Quantity <= s.MinQuantity);

        if (branchId is { } id)
        {
            scope.EnsureBranchAllowed(id);
            q = q.Where(s => s.BranchId == id);
        }

        // Lo más urgente primero: lo agotado antes que lo que solo está rozando el mínimo.
        return await Project(q.OrderBy(s => s.Quantity).ThenBy(s => s.Part.Name))
            .Take(100)
            .ToListAsync(ct);
    }

    public async Task<PagedResult<StockMovementDto>> MovementsAsync(
        MovementQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        EnsureStaff(scope);

        var q = db.StockMovements.AsNoTracking();

        if (!scope.IsOwner) q = q.Where(m => scope.BranchIds.Contains(m.BranchId));

        if (query.BranchId is { } branchId)
        {
            scope.EnsureBranchAllowed(branchId);
            q = q.Where(m => m.BranchId == branchId);
        }

        if (query.PartId is { } partId) q = q.Where(m => m.PartId == partId);
        if (query.Type is { } type) q = q.Where(m => m.Type == type);
        if (query.From is { } from) q = q.Where(m => m.MovedAt >= from);
        if (query.To is { } to) q = q.Where(m => m.MovedAt <= to);

        var total = await q.CountAsync(ct);

        var ordered = q.OrderByDescending(m => m.MovedAt).ThenByDescending(m => m.CreatedAt);

        var items = await ProjectMovements(ordered)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<StockMovementDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<StockItemDto> ReceiveAsync(ReceiveStockRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();
        EnsurePositive(request.Quantity);

        var item = await GetOrCreateItemAsync(request.BranchId, request.PartId, ct);
        item.Quantity += request.Quantity;

        AddMovement(new StockMovement
        {
            BranchId = item.BranchId,
            PartId = item.PartId,
            Type = StockMovementType.In,
            Quantity = request.Quantity,
            UnitCost = request.UnitCost,
            ResultingQuantity = item.Quantity,
            Reference = Truncate(request.Reference, 100),
            Notes = Truncate(request.Notes, 500),
            MovedAt = clock.UtcNow,
            MovedByUserId = scope.UserId
        });

        // El costo de referencia del catálogo sigue a la última compra: es lo que espera
        // quien cotiza, y el costo histórico de cada entrada queda en su movimiento.
        if (request.UnitCost is { } cost && cost > 0)
        {
            var part = await db.Parts.FirstAsync(p => p.Id == request.PartId, ct);
            part.CostPrice = cost;
        }

        await db.SaveChangesAsync(ct);
        return await GetItemDtoAsync(item.BranchId, item.PartId, ct);
    }

    public async Task<StockItemDto> AdjustAsync(AdjustStockRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        if (request.CountedQuantity < 0)
            throw new AppException("La cantidad contada no puede ser negativa.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new AppException("Un ajuste necesita motivo: es lo que explica la diferencia después.");

        var item = await GetOrCreateItemAsync(request.BranchId, request.PartId, ct);
        var difference = request.CountedQuantity - item.Quantity;

        if (difference == 0)
            return await GetItemDtoAsync(item.BranchId, item.PartId, ct);

        item.Quantity = request.CountedQuantity;

        AddMovement(new StockMovement
        {
            BranchId = item.BranchId,
            PartId = item.PartId,
            Type = StockMovementType.Adjustment,
            // Con signo: el tipo "ajuste" no distingue un sobrante de un faltante.
            Quantity = difference,
            ResultingQuantity = item.Quantity,
            Reference = difference > 0 ? "Sobrante" : "Faltante",
            Notes = Truncate(request.Reason, 500),
            MovedAt = clock.UtcNow,
            MovedByUserId = scope.UserId
        });

        await db.SaveChangesAsync(ct);
        return await GetItemDtoAsync(item.BranchId, item.PartId, ct);
    }

    public async Task<IReadOnlyList<StockItemDto>> TransferAsync(
        TransferStockRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();
        EnsurePositive(request.Quantity);

        if (request.FromBranchId == request.ToBranchId)
            throw new AppException("El origen y el destino no pueden ser la misma sucursal.");

        var origin = await GetOrCreateItemAsync(request.FromBranchId, request.PartId, ct);
        var destination = await GetOrCreateItemAsync(request.ToBranchId, request.PartId, ct);

        EnsureEnough(origin, request.Quantity, await PartNameAsync(request.PartId, ct));

        var now = clock.UtcNow;

        origin.Quantity -= request.Quantity;
        destination.Quantity += request.Quantity;

        // Dos movimientos con la misma hora y cada uno apuntando al otro: el kardex de
        // cada sucursal se lee solo, sin tener que cruzarlo con el de la otra.
        AddMovement(new StockMovement
        {
            BranchId = origin.BranchId,
            PartId = request.PartId,
            Type = StockMovementType.TransferOut,
            Quantity = request.Quantity,
            ResultingQuantity = origin.Quantity,
            CounterpartBranchId = destination.BranchId,
            Notes = Truncate(request.Notes, 500),
            MovedAt = now,
            MovedByUserId = scope.UserId
        });

        AddMovement(new StockMovement
        {
            BranchId = destination.BranchId,
            PartId = request.PartId,
            Type = StockMovementType.TransferIn,
            Quantity = request.Quantity,
            ResultingQuantity = destination.Quantity,
            CounterpartBranchId = origin.BranchId,
            Notes = Truncate(request.Notes, 500),
            MovedAt = now,
            MovedByUserId = scope.UserId
        });

        await db.SaveChangesAsync(ct);

        return
        [
            await GetItemDtoAsync(origin.BranchId, request.PartId, ct),
            await GetItemDtoAsync(destination.BranchId, request.PartId, ct)
        ];
    }

    public async Task<StockItemDto> SaveSettingsAsync(
        SaveStockSettingsRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        if (request.MinQuantity < 0)
            throw new AppException("El mínimo no puede ser negativo.");

        var item = await GetOrCreateItemAsync(request.BranchId, request.PartId, ct);
        item.MinQuantity = request.MinQuantity;
        item.Location = Truncate(request.Location, 80);

        await db.SaveChangesAsync(ct);
        return await GetItemDtoAsync(item.BranchId, item.PartId, ct);
    }

    // ---------- Consumo desde la orden de trabajo ----------

    /// <summary>
    /// Descuenta un repuesto por una orden. Lo llama <see cref="WorkOrderService"/> dentro de
    /// su propia transacción, por eso no guarda: quien lo llama decide cuándo confirmar.
    /// </summary>
    internal async Task<StockItem> ConsumeAsync(
        Guid branchId, Guid partId, decimal quantity, Guid workOrderId, Guid userId, CancellationToken ct)
    {
        var item = await GetOrCreateItemAsync(branchId, partId, ct);
        EnsureEnough(item, quantity, await PartNameAsync(partId, ct));

        item.Quantity -= quantity;

        AddMovement(new StockMovement
        {
            BranchId = branchId,
            PartId = partId,
            Type = StockMovementType.Out,
            Quantity = quantity,
            ResultingQuantity = item.Quantity,
            WorkOrderId = workOrderId,
            MovedAt = clock.UtcNow,
            MovedByUserId = userId
        });

        return item;
    }

    /// <summary>
    /// Devuelve al inventario un repuesto que se había cargado a una orden. No borra el
    /// movimiento de salida: el histórico es inmutable, así que entra uno de devolución.
    /// </summary>
    internal async Task ReturnAsync(
        Guid branchId, Guid partId, decimal quantity, Guid workOrderId, Guid userId,
        string reference, CancellationToken ct)
    {
        var item = await GetOrCreateItemAsync(branchId, partId, ct);
        item.Quantity += quantity;

        AddMovement(new StockMovement
        {
            BranchId = branchId,
            PartId = partId,
            Type = StockMovementType.In,
            Quantity = quantity,
            ResultingQuantity = item.Quantity,
            WorkOrderId = workOrderId,
            Reference = Truncate(reference, 100),
            MovedAt = clock.UtcNow,
            MovedByUserId = userId
        });
    }

    // ---------- Interno ----------

    private IQueryable<StockItem> Scoped(AccessScope scope)
    {
        EnsureStaff(scope);

        var q = db.StockItems.AsNoTracking();

        // El Dueño ve la bodega de todas las sucursales; el técnico, solo la de donde trabaja.
        return scope.IsOwner ? q : q.Where(s => scope.BranchIds.Contains(s.BranchId));
    }

    private static IQueryable<StockItemDto> Project(IQueryable<StockItem> q) =>
        q.Select(s => new StockItemDto(
            s.PartId,
            s.Part.Sku,
            s.Part.Name,
            s.Part.Brand,
            s.Part.Category,
            s.Part.Unit,
            s.BranchId,
            s.Branch.Name,
            s.Quantity,
            s.MinQuantity,
            s.Location,
            s.Part.SalePrice,
            s.MinQuantity > 0 && s.Quantity <= s.MinQuantity));

    private IQueryable<StockMovementDto> ProjectMovements(IQueryable<StockMovement> q) =>
        q.Select(m => new StockMovementDto(
            m.Id,
            m.PartId,
            m.Part.Sku,
            m.Part.Name,
            m.BranchId,
            m.Branch.Name,
            m.Type,
            m.Quantity,
            // El ajuste ya guarda su propio signo; los demás tipos lo determinan por el tipo.
            m.Type == StockMovementType.Out || m.Type == StockMovementType.TransferOut
                ? -m.Quantity
                : m.Quantity,
            m.UnitCost,
            m.ResultingQuantity,
            m.Reference,
            m.Notes,
            db.WorkOrders.Where(w => w.Id == m.WorkOrderId).Select(w => w.Number).FirstOrDefault(),
            db.Branches.Where(b => b.Id == m.CounterpartBranchId).Select(b => b.Name).FirstOrDefault(),
            m.MovedAt,
            db.Users.Where(u => u.Id == m.MovedByUserId).Select(u => u.FullName).FirstOrDefault() ?? "—"));

    /// <summary>
    /// La fila de existencia se crea al vuelo. Un repuesto nuevo no tiene fila en cada
    /// sucursal, y obligar a "inicializar el stock en cero" antes de poder recibirlo sería
    /// un paso administrativo sin ningún sentido para quien está en el mostrador.
    /// </summary>
    private async Task<StockItem> GetOrCreateItemAsync(Guid branchId, Guid partId, CancellationToken ct)
    {
        AccessScope.From(tenantContext).EnsureBranchAllowed(branchId);

        var existing = await db.StockItems
            .FirstOrDefaultAsync(s => s.BranchId == branchId && s.PartId == partId, ct);

        if (existing is not null) return existing;

        if (!await db.Branches.AnyAsync(b => b.Id == branchId, ct))
            throw new NotFoundException("La sucursal no existe.");

        if (!await db.Parts.AnyAsync(p => p.Id == partId, ct))
            throw new NotFoundException("El repuesto no existe.");

        var item = new StockItem { BranchId = branchId, PartId = partId, Quantity = 0 };
        db.StockItems.Add(item);

        return item;
    }

    private void AddMovement(StockMovement movement) => db.StockMovements.Add(movement);

    private async Task<StockItemDto> GetItemDtoAsync(Guid branchId, Guid partId, CancellationToken ct) =>
        await Project(db.StockItems.AsNoTracking().Where(s => s.BranchId == branchId && s.PartId == partId))
            .FirstAsync(ct);

    private async Task<string> PartNameAsync(Guid partId, CancellationToken ct) =>
        await db.Parts.Where(p => p.Id == partId).Select(p => p.Name).FirstOrDefaultAsync(ct)
        ?? "el repuesto";

    /// <summary>
    /// Se bloquea la salida en vez de permitir saldo negativo. Un stock negativo no se nota
    /// hasta que los reportes de costo salen mal, y para entonces nadie recuerda qué pasó;
    /// el mensaje dice cuánto hay para que el Dueño registre la entrada o el ajuste.
    /// </summary>
    private static void EnsureEnough(StockItem item, decimal quantity, string partName)
    {
        if (item.Quantity >= quantity) return;

        throw new ConflictException(
            $"No hay suficiente {partName}: quedan {item.Quantity:0.##} y se piden {quantity:0.##}. " +
            "Registre la entrada o ajuste el inventario antes de consumirlo.");
    }

    private static void EnsurePositive(decimal quantity)
    {
        if (quantity <= 0) throw new AppException("La cantidad debe ser mayor que cero.");
    }

    private static void EnsureStaff(AccessScope scope)
    {
        if (scope.IsCustomer) throw new ForbiddenException("El inventario es solo para el personal del taller.");
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

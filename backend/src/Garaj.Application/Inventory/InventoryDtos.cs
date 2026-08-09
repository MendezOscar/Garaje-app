using Garaj.Application.Common;
using Garaj.Domain.Enums;

namespace Garaj.Application.Inventory;

// ---------- Catálogo ----------

public record PartDto(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    string? Brand,
    string? Category,
    string Unit,
    decimal CostPrice,
    decimal SalePrice,
    bool IsActive,
    // Suma de existencias en las sucursales que el usuario puede ver.
    decimal TotalQuantity);

public record SavePartRequest(
    string Sku,
    string Name,
    string? Description,
    string? Brand,
    string? Category,
    string Unit,
    decimal CostPrice,
    decimal SalePrice,
    bool IsActive = true);

public record PartQuery : PageQuery
{
    /// <summary>Busca por SKU, nombre o marca.</summary>
    public string? Search { get; init; }

    public string? Category { get; init; }
    public bool IncludeInactive { get; init; }
}

// ---------- Existencias ----------

public record StockItemDto(
    Guid PartId,
    string Sku,
    string PartName,
    string? Brand,
    string? Category,
    string Unit,
    Guid BranchId,
    string BranchName,
    decimal Quantity,
    decimal MinQuantity,
    string? Location,
    // Los dos precios del catálogo: sirven para decidir en la misma pantalla si conviene
    // reponer, sin tener que abrir la ficha del repuesto para ver en cuánto se compró.
    decimal CostPrice,
    decimal SalePrice,
    bool IsBelowMinimum);

public record StockQuery : PageQuery
{
    public Guid? BranchId { get; init; }
    public Guid? PartId { get; init; }
    public string? Search { get; init; }
    public string? Category { get; init; }

    /// <summary>Solo lo que está en o por debajo del mínimo: la lista de reposición.</summary>
    public bool OnlyBelowMinimum { get; init; }
}

public record StockMovementDto(
    Guid Id,
    Guid PartId,
    string Sku,
    string PartName,
    Guid BranchId,
    string BranchName,
    StockMovementType Type,
    decimal Quantity,
    // Con signo: lo que sumó o restó al saldo. Es lo que se pinta en el kardex.
    decimal SignedQuantity,
    decimal? UnitCost,
    decimal ResultingQuantity,
    string? Reference,
    string? Notes,
    string? WorkOrderNumber,
    string? CounterpartBranchName,
    DateTimeOffset MovedAt,
    string MovedByName);

public record MovementQuery : PageQuery
{
    public Guid? BranchId { get; init; }
    public Guid? PartId { get; init; }
    public StockMovementType? Type { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

/// <summary>Entrada por compra.</summary>
public record ReceiveStockRequest(
    Guid BranchId,
    Guid PartId,
    decimal Quantity,
    decimal? UnitCost,
    string? Reference,
    string? Notes);

/// <param name="CountedQuantity">
/// Lo que se contó físicamente, no la diferencia. Se pide el absoluto porque es lo que la
/// persona tiene delante al hacer inventario; la diferencia la calcula el sistema.
/// </param>
public record AdjustStockRequest(
    Guid BranchId,
    Guid PartId,
    decimal CountedQuantity,
    string Reason);

public record TransferStockRequest(
    Guid FromBranchId,
    Guid ToBranchId,
    Guid PartId,
    decimal Quantity,
    string? Notes);

/// <summary>Mínimo de reposición y ubicación física: no mueven existencias.</summary>
public record SaveStockSettingsRequest(
    Guid BranchId,
    Guid PartId,
    decimal MinQuantity,
    string? Location);

// ---------- Repuestos de una orden ----------

public record WorkOrderPartDto(
    Guid Id,
    Guid PartId,
    string Sku,
    string PartName,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal UnitCost,
    decimal Total,
    Guid? WorkOrderTaskId,
    string? TaskTitle);

/// <param name="UnitPrice">
/// Si va null se toma el del catálogo. Se permite fijarlo para poder cobrar distinto sin
/// tocar el catálogo; queda congelado en la orden.
/// </param>
public record AddWorkOrderPartRequest(
    Guid PartId,
    decimal Quantity,
    decimal? UnitPrice,
    Guid? WorkOrderTaskId);

public interface IPartService
{
    Task<PagedResult<PartDto>> ListAsync(PartQuery query, CancellationToken ct = default);
    Task<PartDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<PartDto> CreateAsync(SavePartRequest request, CancellationToken ct = default);
    Task<PartDto> UpdateAsync(Guid id, SavePartRequest request, CancellationToken ct = default);

    /// <summary>Categorías en uso, para los filtros de la interfaz.</summary>
    Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct = default);
}

public interface IStockService
{
    Task<PagedResult<StockItemDto>> ListAsync(StockQuery query, CancellationToken ct = default);

    /// <summary>Lo que está en o por debajo del mínimo. Alimenta la alerta de reposición.</summary>
    Task<IReadOnlyList<StockItemDto>> AlertsAsync(Guid? branchId, CancellationToken ct = default);

    Task<PagedResult<StockMovementDto>> MovementsAsync(MovementQuery query, CancellationToken ct = default);

    Task<StockItemDto> ReceiveAsync(ReceiveStockRequest request, CancellationToken ct = default);
    Task<StockItemDto> AdjustAsync(AdjustStockRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<StockItemDto>> TransferAsync(TransferStockRequest request, CancellationToken ct = default);
    Task<StockItemDto> SaveSettingsAsync(SaveStockSettingsRequest request, CancellationToken ct = default);
}

using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Existencia de un repuesto en una sucursal. <see cref="Quantity"/> es un saldo cacheado:
/// la fuente de verdad son los <see cref="StockMovement"/>. Nunca se edita directamente,
/// solo se recalcula al registrar un movimiento.
/// </summary>
public class StockItem : TenantEntity, IBranchEntity
{
    public Guid BranchId { get; set; }
    public Guid PartId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Umbral de reposición. Por debajo, el repuesto aparece en las alertas del dashboard.</summary>
    public decimal MinQuantity { get; set; }

    /// <summary>Ubicación física en la bodega, ej. "Estante B3".</summary>
    public string? Location { get; set; }

    public Branch Branch { get; set; } = null!;
    public Part Part { get; set; } = null!;
}

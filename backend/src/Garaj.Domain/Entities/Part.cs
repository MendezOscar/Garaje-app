using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Repuesto en el catálogo del taller. El catálogo es del tenant; las existencias son por
/// sucursal (<see cref="StockItem"/>).
/// </summary>
public class Part : TenantEntity
{
    public string Sku { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Brand { get; set; }
    public string? Category { get; set; }

    /// <summary>Unidad de medida: "u", "lt", "jgo", etc.</summary>
    public string Unit { get; set; } = "u";

    /// <summary>Costo de referencia. El costo real de cada entrada queda en el StockMovement.</summary>
    public decimal CostPrice { get; set; }

    public decimal SalePrice { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<StockItem> StockItems { get; set; } = new List<StockItem>();
}

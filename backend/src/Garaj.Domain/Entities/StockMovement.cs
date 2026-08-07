using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Movimiento de inventario. Es la única fuente de verdad del stock y es inmutable:
/// un error se corrige con un movimiento de ajuste, nunca editando el histórico.
/// </summary>
public class StockMovement : TenantEntity, IBranchEntity
{
    public Guid BranchId { get; set; }
    public Guid PartId { get; set; }

    public StockMovementType Type { get; set; }

    /// <summary>Siempre positiva. El signo lo determina <see cref="Type"/>.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Costo unitario de la entrada. Null en salidas y ajustes.</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>Saldo de la sucursal después de aplicar este movimiento. Permite auditar sin recalcular todo.</summary>
    public decimal ResultingQuantity { get; set; }

    /// <summary>Orden de trabajo que originó el consumo, si aplica.</summary>
    public Guid? WorkOrderId { get; set; }

    /// <summary>Venta directa de mostrador que originó la salida, si aplica.</summary>
    public Guid? SaleId { get; set; }

    /// <summary>Sucursal contraparte en una transferencia.</summary>
    public Guid? CounterpartBranchId { get; set; }

    /// <summary>Nº de factura de compra, motivo del ajuste, etc.</summary>
    public string? Reference { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset MovedAt { get; set; }
    public Guid MovedByUserId { get; set; }

    public Branch Branch { get; set; } = null!;
    public Part Part { get; set; } = null!;
}

using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Repuesto consumido en una orden de trabajo. Cada registro genera su
/// <see cref="StockMovement"/> de salida; el precio y el costo se congelan aquí para que
/// un cambio posterior en el catálogo no altere órdenes ya facturadas.
/// </summary>
public class WorkOrderPart : TenantEntity
{
    public Guid WorkOrderId { get; set; }
    public Guid PartId { get; set; }
    public Guid? WorkOrderTaskId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Precio de venta al cliente al momento del consumo.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Costo del repuesto al momento del consumo. Alimenta el margen en reportes.</summary>
    public decimal UnitCost { get; set; }

    public WorkOrder WorkOrder { get; set; } = null!;
    public Part Part { get; set; } = null!;
}

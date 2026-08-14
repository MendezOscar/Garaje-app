using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Repuesto consumido en una orden de trabajo. El precio y el costo se congelan aquí para que
/// un cambio posterior en el catálogo no altere órdenes ya facturadas.
/// </summary>
/// <remarks>
/// Hay dos clases de línea y se distinguen por <see cref="PartId"/>:
///
/// - **Del catálogo** (<c>PartId</c> con valor): genera su <see cref="StockMovement"/> de
///   salida y descuenta existencias.
/// - **Manual** (<c>PartId</c> null, <see cref="Description"/> con el concepto escrito a
///   mano): no toca el inventario. Es para lo que se compró de encargo y nunca estuvo en
///   bodega, así que descontarlo de un repuesto que no existe no tendría sentido.
/// </remarks>
public class WorkOrderPart : TenantEntity
{
    public Guid WorkOrderId { get; set; }

    /// <summary>Null en una línea manual: ese repuesto no está en el catálogo.</summary>
    public Guid? PartId { get; set; }

    /// <summary>El concepto escrito a mano. Solo lo llevan las líneas manuales.</summary>
    public string? Description { get; set; }

    public Guid? WorkOrderTaskId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Precio de venta al cliente al momento del consumo.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Costo del repuesto al momento del consumo. Alimenta el margen en reportes. En una línea
    /// manual lo escribe quien la carga, y si no lo sabe queda en cero: el margen de esa venta
    /// saldrá inflado, que es preferible a inventarse un costo.
    /// </summary>
    public decimal UnitCost { get; set; }

    public WorkOrder WorkOrder { get; set; } = null!;
    public Part? Part { get; set; }
}

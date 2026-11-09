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

    /// <summary>
    /// El repuesto se compró en una casa de repuestos, no salió de la bodega del taller. Lo
    /// marca quien carga la línea: no se deduce de <see cref="PartId"/>, porque a mano también
    /// se cargan repuestos propios que sencillamente no están en el catálogo.
    /// </summary>
    public bool BoughtOutside { get; set; }

    /// <summary>
    /// Cuándo se confirmó que este repuesto de verdad se usa. Nulo = <b>por confirmar</b>.
    /// </summary>
    /// <remarks>
    /// Una línea por confirmar está en la orden y cuenta en la cotización, pero <b>no ha
    /// tocado la bodega</b>: el descuento corre al confirmarla. Existe porque el asistente
    /// propone los repuestos antes de que el trabajo se haga, y había que elegir entre
    /// perderlos al recargar la pantalla o descontar de bodega algo que todavía no se usó.
    /// Ninguna de las dos servía.
    ///
    /// Quitar una línea por confirmar no devuelve nada a bodega, porque nunca salió. Y una
    /// orden no se entrega con líneas así: un repuesto cotizado, entregado y nunca descontado
    /// desfasa el inventario en silencio, que es peor que no tener esto.
    /// </remarks>
    public DateTimeOffset? ConfirmedOn { get; set; }

    public Guid? ConfirmedByUserId { get; set; }

    /// <summary>En la orden pero todavía no salió de bodega.</summary>
    public bool PorConfirmar => ConfirmedOn is null;

    /// <summary>
    /// En qué casa de repuestos se compró. Opcional: puede marcarse la línea sin recordar el
    /// nombre. Solo tiene sentido con <see cref="BoughtOutside"/>.
    /// </summary>
    public string? SupplierName { get; set; }

    public WorkOrder WorkOrder { get; set; } = null!;
    public Part? Part { get; set; }
}

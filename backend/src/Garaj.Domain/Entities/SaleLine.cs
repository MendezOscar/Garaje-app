using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Línea de venta. El <see cref="LineType"/> es lo que permite reportar cuánto entró por
/// repuestos y cuánto por mano de obra; <see cref="UnitCost"/> da el margen.
/// </summary>
public class SaleLine : TenantEntity
{
    public Guid SaleId { get; set; }
    public LineType LineType { get; set; }

    public Guid? PartId { get; set; }
    public Guid? LaborServiceId { get; set; }

    public string Description { get; set; } = null!;

    public int Sequence { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>Costo unitario al momento de la venta. 0 en mano de obra.</summary>
    public decimal UnitCost { get; set; }

    public decimal Discount { get; set; }
    public decimal Total { get; set; }

    public Sale Sale { get; set; } = null!;
}

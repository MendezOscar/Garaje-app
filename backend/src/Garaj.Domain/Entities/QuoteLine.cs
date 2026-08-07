using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Línea de cotización. <see cref="LineType"/> separa repuestos de mano de obra, que es la
/// misma división que después alimenta los reportes de ingresos.
/// </summary>
public class QuoteLine : TenantEntity
{
    public Guid QuoteId { get; set; }
    public LineType LineType { get; set; }

    public Guid? PartId { get; set; }
    public Guid? LaborServiceId { get; set; }

    /// <summary>Texto congelado al cotizar: no cambia si luego se renombra el repuesto o servicio.</summary>
    public string Description { get; set; } = null!;

    public int Sequence { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>Descuento en importe, no en porcentaje.</summary>
    public decimal Discount { get; set; }

    public decimal Total { get; set; }

    public Quote Quote { get; set; } = null!;
}

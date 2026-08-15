using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Un repuesto que ese trabajo suele llevar. Como en <see cref="WorkOrderPart"/>, hay dos
/// clases de línea y se distinguen por <see cref="PartId"/>: del catálogo, o manual con el
/// concepto escrito a mano.
/// </summary>
/// <remarks>
/// Aplicar la plantilla **no** carga estos repuestos ni toca la bodega: se proponen para que se
/// carguen uno a uno cuando de verdad se instalan. Un repuesto se descuenta cuando se usa, no
/// cuando se planea usarlo.
/// </remarks>
public class JobTemplatePart : TenantEntity
{
    public Guid JobTemplateId { get; set; }

    /// <summary>Null en una línea manual: ese repuesto no está en el catálogo.</summary>
    public Guid? PartId { get; set; }

    /// <summary>El concepto escrito a mano. Solo lo llevan las líneas manuales.</summary>
    public string? Description { get; set; }

    public decimal Quantity { get; set; }

    public JobTemplate JobTemplate { get; set; } = null!;
    public Part? Part { get; set; }
}

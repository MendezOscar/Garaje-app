using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Un trabajo que el taller hace muchas veces —el cambio de aceite, las pastillas de adelante—
/// guardado con sus pasos y sus repuestos para no volver a teclearlo.
/// </summary>
/// <remarks>
/// Es del **tenant** y no de la sucursal, igual que <see cref="Part"/> y
/// <see cref="LaborService"/>: el trabajo se hace igual en las dos sucursales, y lo que cambia
/// entre ellas —las existencias— no vive aquí.
///
/// No guarda precios, sino referencias al catálogo. El precio lo resuelve
/// <see cref="LaborService.PriceFor"/> y <see cref="Part.SalePrice"/> al aplicarla, para que
/// subir el precio de un repuesto mañana no deje veinte plantillas mintiendo.
/// </remarks>
public class JobTemplate : TenantEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Cuántas veces se ha aplicado. Existe para ordenar la lista por lo más usado: en orden
    /// alfabético, treinta plantillas son inservibles; por uso, las cuatro que importan quedan
    /// siempre arriba.
    /// </summary>
    public int UsageCount { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public ICollection<JobTemplateTask> Tasks { get; set; } = new List<JobTemplateTask>();
    public ICollection<JobTemplatePart> Parts { get; set; } = new List<JobTemplatePart>();
}

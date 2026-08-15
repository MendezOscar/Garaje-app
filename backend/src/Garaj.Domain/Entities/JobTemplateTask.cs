using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Un paso de un trabajo frecuente. Es el espejo de los campos de <see cref="WorkOrderTask"/>
/// que llena una persona: lo que se hace y qué servicio del catálogo lo cobra.
/// </summary>
public class JobTemplateTask : TenantEntity
{
    public Guid JobTemplateId { get; set; }

    public string Title { get; set; } = null!;
    public string? Description { get; set; }

    public int Sequence { get; set; }

    /// <summary>Servicio del catálogo que le pone precio al paso. Sin él, el paso no se cobra.</summary>
    public Guid? LaborServiceId { get; set; }

    public decimal? EstimatedHours { get; set; }

    public JobTemplate JobTemplate { get; set; } = null!;
}

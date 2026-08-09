using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Un paso de la reparación. Es la unidad a la que el técnico adjunta fotos y sobre la que
/// se calcula la mano de obra facturable.
/// </summary>
public class WorkOrderTask : TenantEntity
{
    public Guid WorkOrderId { get; set; }

    public string Title { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Posición en el checklist. Define el orden que ve el técnico y el cliente.</summary>
    public int Sequence { get; set; }

    public bool IsDone { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public Guid? AssignedTechnicianId { get; set; }

    /// <summary>Servicio del catálogo que respalda este paso; define la tarifa de mano de obra.</summary>
    public Guid? LaborServiceId { get; set; }

    /// <summary>
    /// Lo que se cobra por el paso, según el servicio del catálogo que lleve. Solo cuenta en
    /// las órdenes en modo catálogo: en las manuales el precio es uno solo para toda la orden.
    /// </summary>
    public decimal? PriceWith(LaborService? service) =>
        service?.PriceFor(ActualHours ?? EstimatedHours);

    public decimal? EstimatedHours { get; set; }
    public decimal? ActualHours { get; set; }

    /// <summary>Nota del técnico sobre lo que encontró o hizo.</summary>
    public string? TechnicianNotes { get; set; }

    public WorkOrder WorkOrder { get; set; } = null!;
}

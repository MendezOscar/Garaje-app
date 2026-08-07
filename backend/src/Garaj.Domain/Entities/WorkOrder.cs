using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Orden de trabajo: el vehículo ya está en el taller y se le está haciendo algo.
/// Concentra el paso a paso (<see cref="Tasks"/>), la evidencia fotográfica, los repuestos
/// consumidos y el historial de estados que ve el cliente.
/// </summary>
public class WorkOrder : TenantEntity, IBranchEntity
{
    public Guid BranchId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid? ServiceRequestId { get; set; }

    /// <summary>Correlativo legible por sucursal, ej. "SUR-000123".</summary>
    public string Number { get; set; } = null!;

    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Received;

    /// <summary>Técnico responsable. Null mientras el Dueño no la asigne.</summary>
    public Guid? AssignedTechnicianId { get; set; }

    /// <summary>Motivo de ingreso, copiado del requerimiento y editable por el Dueño.</summary>
    public string Description { get; set; } = null!;

    /// <summary>Diagnóstico del técnico tras revisar el vehículo.</summary>
    public string? Diagnosis { get; set; }

    public int? MileageIn { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? PromisedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Venta generada al cerrar la orden. Null mientras no se factura.</summary>
    public Guid? SaleId { get; set; }

    public Branch Branch { get; set; } = null!;
    public Vehicle Vehicle { get; set; } = null!;
    public ICollection<WorkOrderTask> Tasks { get; set; } = new List<WorkOrderTask>();
    public ICollection<WorkOrderPart> Parts { get; set; } = new List<WorkOrderPart>();
    public ICollection<WorkOrderStatusHistory> StatusHistory { get; set; } = new List<WorkOrderStatusHistory>();
}

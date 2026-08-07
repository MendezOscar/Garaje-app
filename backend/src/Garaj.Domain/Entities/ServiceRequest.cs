using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Requerimiento de servicio: la puerta de entrada. Lo crea el Cliente desde la app o el
/// Dueño al recibir el vehículo. Al aprobarse se convierte en una <see cref="WorkOrder"/>.
/// </summary>
public class ServiceRequest : TenantEntity, IBranchEntity
{
    public Guid BranchId { get; set; }
    public Guid VehicleId { get; set; }

    /// <summary>Motivo de ingreso descrito por quien crea el requerimiento.</summary>
    public string Description { get; set; } = null!;

    /// <summary>Síntomas reportados por el cliente (ruidos, fallas, cuándo ocurre).</summary>
    public string? ReportedSymptoms { get; set; }

    public ServiceRequestStatus Status { get; set; } = ServiceRequestStatus.Pending;

    /// <summary>Fecha preferida de ingreso indicada por el cliente.</summary>
    public DateTimeOffset? PreferredDate { get; set; }

    public int? Mileage { get; set; }

    /// <summary>Motivo del rechazo, cuando Status = Rejected.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Orden de trabajo generada al aprobar. Null mientras no se convierta.</summary>
    public Guid? WorkOrderId { get; set; }

    public Branch Branch { get; set; } = null!;
    public Vehicle Vehicle { get; set; } = null!;
}

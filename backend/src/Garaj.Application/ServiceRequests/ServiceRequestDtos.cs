using Garaj.Application.Common;
using Garaj.Domain.Enums;

namespace Garaj.Application.ServiceRequests;

public record ServiceRequestDto(
    Guid Id,
    Guid BranchId,
    string BranchName,
    Guid? VehicleId,
    string VehicleLabel,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    string Description,
    string? ReportedSymptoms,
    ServiceRequestStatus Status,
    DateTimeOffset? PreferredDate,
    int? Mileage,
    string? RejectionReason,
    Guid? WorkOrderId,
    string? WorkOrderNumber,
    DateTimeOffset CreatedAt,
    // Lo que entró al taller, sea vehículo o equipo.
    AssetRefDto? Asset = null);

/// <param name="BranchId">Sucursal donde entra el vehículo. El Cliente elige entre las del taller.</param>
/// <param name="VehicleId">En un taller de vehículos. Excluyente con <paramref name="EquipmentId"/>.</param>
/// <param name="EquipmentId">En un taller de equipos. Uno de los dos tiene que venir.</param>
public record CreateServiceRequestRequest(
    Guid BranchId,
    Guid? VehicleId,
    string Description,
    string? ReportedSymptoms,
    DateTimeOffset? PreferredDate,
    int? Mileage,
    Guid? EquipmentId = null);

/// <summary>
/// Aprobar convierte el requerimiento en orden de trabajo. El técnico es opcional: el Dueño
/// puede recibir el vehículo ahora y asignar después.
/// </summary>
public record ApproveServiceRequestRequest(
    Guid? AssignedTechnicianId,
    DateTimeOffset? PromisedAt,
    string? Note);

public record RejectServiceRequestRequest(string Reason);

public record ServiceRequestQuery : PageQuery
{
    public Guid? BranchId { get; init; }
    public ServiceRequestStatus? Status { get; init; }
    public Guid? VehicleId { get; init; }

    /// <summary>Por fecha de ingreso del requerimiento. Sin rango, todos.</summary>
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public interface IServiceRequestService
{
    Task<PagedResult<ServiceRequestDto>> ListAsync(ServiceRequestQuery query, CancellationToken ct = default);
    Task<ServiceRequestDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ServiceRequestDto> CreateAsync(CreateServiceRequestRequest request, CancellationToken ct = default);

    /// <summary>Devuelve el id de la orden de trabajo creada.</summary>
    Task<Guid> ApproveAsync(Guid id, ApproveServiceRequestRequest request, CancellationToken ct = default);

    Task<ServiceRequestDto> RejectAsync(Guid id, RejectServiceRequestRequest request, CancellationToken ct = default);
}

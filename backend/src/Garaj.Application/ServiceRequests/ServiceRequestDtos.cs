using Garaj.Application.Common;
using Garaj.Domain.Enums;

namespace Garaj.Application.ServiceRequests;

public record ServiceRequestDto(
    Guid Id,
    Guid BranchId,
    string BranchName,
    Guid VehicleId,
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
    DateTimeOffset CreatedAt);

/// <param name="BranchId">Sucursal donde entra el vehículo. El Cliente elige entre las del taller.</param>
public record CreateServiceRequestRequest(
    Guid BranchId,
    Guid VehicleId,
    string Description,
    string? ReportedSymptoms,
    DateTimeOffset? PreferredDate,
    int? Mileage);

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

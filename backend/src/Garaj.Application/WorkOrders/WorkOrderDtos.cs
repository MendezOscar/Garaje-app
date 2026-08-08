using Garaj.Application.Common;
using Garaj.Domain.Enums;

namespace Garaj.Application.WorkOrders;

/// <summary>Fila del tablero: lo mínimo para pintar una tarjeta sin traer toda la orden.</summary>
public record WorkOrderListItemDto(
    Guid Id,
    string Number,
    Guid BranchId,
    string BranchName,
    WorkOrderStatus Status,
    Guid VehicleId,
    string VehicleLabel,
    VehicleType VehicleType,
    string? Plate,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    Guid? AssignedTechnicianId,
    string? AssignedTechnicianName,
    string Description,
    DateTimeOffset OpenedAt,
    DateTimeOffset? PromisedAt,
    int TaskCount,
    int TasksDone);

public record WorkOrderDetailDto(
    Guid Id,
    string Number,
    Guid BranchId,
    string BranchName,
    WorkOrderStatus Status,
    IReadOnlyList<WorkOrderStatus> AllowedNextStatuses,
    Guid VehicleId,
    string VehicleLabel,
    VehicleType VehicleType,
    string? Plate,
    int? VehicleMileage,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    Guid? AssignedTechnicianId,
    string? AssignedTechnicianName,
    string Description,
    string? Diagnosis,
    int? MileageIn,
    DateTimeOffset OpenedAt,
    DateTimeOffset? PromisedAt,
    DateTimeOffset? ClosedAt,
    Guid? ServiceRequestId,
    IReadOnlyList<WorkOrderTaskDto> Tasks,
    IReadOnlyList<WorkOrderStatusEntryDto> Timeline);

public record WorkOrderTaskDto(
    Guid Id,
    string Title,
    string? Description,
    int Sequence,
    bool IsDone,
    Guid? AssignedTechnicianId,
    string? AssignedTechnicianName,
    decimal? EstimatedHours,
    decimal? ActualHours,
    string? TechnicianNotes,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public record WorkOrderStatusEntryDto(
    WorkOrderStatus? FromStatus,
    WorkOrderStatus ToStatus,
    DateTimeOffset ChangedAt,
    string ChangedByName,
    string? Note,
    bool IsVisibleToCustomer);

public record CreateWorkOrderRequest(
    Guid BranchId,
    Guid VehicleId,
    string Description,
    Guid? AssignedTechnicianId,
    int? MileageIn,
    DateTimeOffset? PromisedAt);

public record UpdateWorkOrderRequest(
    string Description,
    string? Diagnosis,
    DateTimeOffset? PromisedAt);

public record AssignTechnicianRequest(Guid? TechnicianId);

/// <param name="IsVisibleToCustomer">
/// Permite dejar notas internas del taller que el cliente no ve en su línea de tiempo.
/// </param>
public record ChangeStatusRequest(
    WorkOrderStatus Status,
    string? Note,
    bool IsVisibleToCustomer = true);

public record SaveWorkOrderTaskRequest(
    string Title,
    string? Description,
    Guid? AssignedTechnicianId,
    Guid? LaborServiceId,
    decimal? EstimatedHours);

public record CompleteTaskRequest(
    bool IsDone,
    decimal? ActualHours,
    string? TechnicianNotes);

public record WorkOrderQuery : PageQuery
{
    public Guid? BranchId { get; init; }
    public WorkOrderStatus? Status { get; init; }
    public Guid? TechnicianId { get; init; }
    public Guid? VehicleId { get; init; }
    public string? Search { get; init; }

    /// <summary>Solo las que siguen vivas en el taller (ni entregadas ni canceladas).</summary>
    public bool OnlyOpen { get; init; }
}

public interface IWorkOrderService
{
    Task<PagedResult<WorkOrderListItemDto>> ListAsync(WorkOrderQuery query, CancellationToken ct = default);
    Task<WorkOrderDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<WorkOrderDetailDto> CreateAsync(CreateWorkOrderRequest request, CancellationToken ct = default);
    Task<WorkOrderDetailDto> UpdateAsync(Guid id, UpdateWorkOrderRequest request, CancellationToken ct = default);
    Task<WorkOrderDetailDto> AssignAsync(Guid id, AssignTechnicianRequest request, CancellationToken ct = default);
    Task<WorkOrderDetailDto> ChangeStatusAsync(Guid id, ChangeStatusRequest request, CancellationToken ct = default);

    Task<WorkOrderTaskDto> AddTaskAsync(Guid workOrderId, SaveWorkOrderTaskRequest request, CancellationToken ct = default);
    Task<WorkOrderTaskDto> UpdateTaskAsync(Guid workOrderId, Guid taskId, SaveWorkOrderTaskRequest request, CancellationToken ct = default);
    Task<WorkOrderTaskDto> CompleteTaskAsync(Guid workOrderId, Guid taskId, CompleteTaskRequest request, CancellationToken ct = default);
    Task DeleteTaskAsync(Guid workOrderId, Guid taskId, CancellationToken ct = default);
}

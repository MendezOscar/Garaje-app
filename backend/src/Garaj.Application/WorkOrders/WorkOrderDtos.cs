using Garaj.Application.Common;
using Garaj.Application.Inventory;
using Garaj.Application.Quotes;
using Garaj.Application.Tenants;
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
    IReadOnlyList<WorkOrderStatusEntryDto> Timeline,
    IReadOnlyList<WorkOrderPartDto> Parts,
    // Lo que suman los repuestos consumidos.
    decimal PartsTotal,
    // La mano de obra que se cobraría hoy: la suma de los pasos en modo catálogo, o el total
    // escrito a mano en modo manual.
    decimal LaborTotal,
    // Cómo se cobra la mano de obra de esta orden.
    LaborMode LaborMode,
    // El total escrito a mano. Solo cuenta en modo manual.
    decimal? ManualLaborTotal);

public record WorkOrderTaskDto(
    Guid Id,
    string Title,
    string? Description,
    int Sequence,
    bool IsDone,
    Guid? AssignedTechnicianId,
    string? AssignedTechnicianName,
    // El servicio del catálogo que le pone precio al paso. Sin él, el paso no se cobra.
    Guid? LaborServiceId,
    string? LaborServiceName,
    decimal? LaborPrice,
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

/// <param name="Total">
/// El total del modo manual. Omitirlo deja el que ya había —el total sobrevive a ir y volver
/// entre modos— y para dejarlo en nada se manda 0. En modo catálogo no tiene efecto: ahí el
/// precio sale de los pasos.
/// </param>
public record SetLaborModeRequest(LaborMode Mode, decimal? Total);

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

// ---------- Seguimiento por enlace ----------

/// <summary>Cuál de los tres mensajes se le arma al cliente para mandarle por WhatsApp.</summary>
public enum OrderMessageKind
{
    /// <summary>Al recibir el vehículo: aquí puede seguir la reparación.</summary>
    Received,

    /// <summary>Ya está listo para entrega, con el total si ya se facturó.</summary>
    Ready,

    /// <summary>Su factura, que se descarga desde la misma página.</summary>
    Invoice
}

/// <summary>
/// La orden como la ve el cliente desde el enlace: en qué va su vehículo y qué se le ha hecho.
/// </summary>
/// <remarks>
/// Es deliberadamente pobre en datos. No lleva ids —ni de la orden, ni del cliente, ni del
/// taller—, ni costo, ni margen, ni el nombre del técnico: solo lo que el dueño del vehículo
/// necesita para no tener que llamar a preguntar.
/// </remarks>
public record OrderTrackingDto(
    string Number,
    string TenantName,
    string? TenantLogoUrl,
    string? TenantPhone,
    string BranchName,
    string CustomerName,
    string VehicleLabel,
    string? Plate,
    WorkOrderStatus Status,
    string StatusLabel,
    string Description,
    DateTimeOffset OpenedAt,
    DateTimeOffset? PromisedAt,
    DateTimeOffset? ClosedAt,
    string Currency,
    IReadOnlyList<OrderTrackingStepDto> Steps,
    IReadOnlyList<OrderTrackingEntryDto> Timeline,
    IReadOnlyList<OrderTrackingPhotoDto> Photos,
    // La factura, cuando la orden ya se cerró. Null mientras el vehículo está en el taller.
    OrderTrackingInvoiceDto? Invoice);

/// <summary>Un paso del trabajo, sin precio: lo que se le hizo al carro, no lo que cuesta.</summary>
public record OrderTrackingStepDto(string Title, bool IsDone, DateTimeOffset? CompletedAt);

public record OrderTrackingEntryDto(
    WorkOrderStatus Status,
    string StatusLabel,
    DateTimeOffset ChangedAt,
    string? Note);

/// <param name="Url">Prefirmada y con caducidad corta: el bucket sigue siendo privado.</param>
public record OrderTrackingPhotoDto(
    string Url,
    string ThumbnailUrl,
    string? Caption,
    DateTimeOffset TakenAt,
    string? StepTitle);

public record OrderTrackingInvoiceDto(
    string Number,
    decimal Total,
    decimal Paid,
    decimal Balance,
    DateTimeOffset? DueDate);

// ---------- Recordatorios del próximo servicio ----------

/// <summary>
/// Un vehículo al que le toca servicio, según lo que el taller recomendó al entregarlo.
/// </summary>
/// <remarks>
/// Es trabajo que hoy se pierde por no acordarse: el cliente vuelve cuando algo suena, no
/// cuando le toca. Lo que dispara el recordatorio es la fecha; el kilometraje se muestra como
/// contexto —«a los 45,000, y la última lectura fue 43,120»— porque hasta que el vehículo no
/// vuelve, el taller no sabe cuánto ha rodado.
/// </remarks>
public record ServiceReminderDto(
    Guid WorkOrderId,
    string OrderNumber,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    Guid VehicleId,
    string VehicleLabel,
    string? Plate,
    string BranchName,
    /// <summary>Qué se le hizo la última vez. Da de qué hablar al llamarlo.</summary>
    string LastService,
    DateTimeOffset? ClosedAt,
    DateTimeOffset NextServiceAt,
    /// <summary>Días hasta que toque. Negativo si ya pasó.</summary>
    int DaysUntil,
    int? NextServiceMileage,
    /// <summary>Último kilometraje que se le leyó al vehículo, y cuándo.</summary>
    int? LastMileage,
    DateTimeOffset? RemindedAt);

public record ServiceReminderQuery
{
    public Guid? BranchId { get; init; }

    /// <summary>Cuánto se mira hacia adelante. Un mes por defecto: es cuando conviene llamar.</summary>
    public int WithinDays { get; init; } = 30;

    /// <summary>Solo los que ya se pasaron de fecha, o solo los que están por tocar.</summary>
    public bool? Overdue { get; init; }

    /// <summary>
    /// Incluye los que ya se recordaron. Fuera por defecto: llamar dos veces en la semana
    /// molesta más que no llamar.
    /// </summary>
    public bool IncludeReminded { get; init; }

    /// <summary>Cliente, teléfono o placa.</summary>
    public string? Search { get; init; }
}

public interface IWorkOrderService
{
    Task<PagedResult<WorkOrderListItemDto>> ListAsync(WorkOrderQuery query, CancellationToken ct = default);
    Task<WorkOrderDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<WorkOrderDetailDto> CreateAsync(CreateWorkOrderRequest request, CancellationToken ct = default);
    Task<WorkOrderDetailDto> UpdateAsync(Guid id, UpdateWorkOrderRequest request, CancellationToken ct = default);
    Task<WorkOrderDetailDto> AssignAsync(Guid id, AssignTechnicianRequest request, CancellationToken ct = default);

    /// <summary>Elige si la mano de obra sale del catálogo o de un total escrito a mano.</summary>
    Task<WorkOrderDetailDto> SetLaborModeAsync(Guid id, SetLaborModeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Borra la orden creada por error: devuelve sus repuestos a bodega, borra sus fotos y sus
    /// pasos, y suelta la cotización y el requerimiento que la mencionaban. Responde 409 si ya
    /// está facturada.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<WorkOrderDetailDto> ChangeStatusAsync(Guid id, ChangeStatusRequest request, CancellationToken ct = default);

    Task<WorkOrderTaskDto> AddTaskAsync(Guid workOrderId, SaveWorkOrderTaskRequest request, CancellationToken ct = default);
    Task<WorkOrderTaskDto> UpdateTaskAsync(Guid workOrderId, Guid taskId, SaveWorkOrderTaskRequest request, CancellationToken ct = default);
    Task<WorkOrderTaskDto> CompleteTaskAsync(Guid workOrderId, Guid taskId, CompleteTaskRequest request, CancellationToken ct = default);
    Task DeleteTaskAsync(Guid workOrderId, Guid taskId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkOrderPartDto>> ListPartsAsync(Guid workOrderId, CancellationToken ct = default);

    /// <summary>Carga el repuesto a la orden y lo descuenta de la bodega de su sucursal.</summary>
    Task<WorkOrderPartDto> AddPartAsync(
        Guid workOrderId, AddWorkOrderPartRequest request, CancellationToken ct = default);

    /// <summary>Lo quita de la orden y lo devuelve a la bodega con un movimiento de entrada.</summary>
    Task RemovePartAsync(Guid workOrderId, Guid partLineId, CancellationToken ct = default);

    /// <summary>
    /// El enlace de seguimiento con el mensaje de WhatsApp ya escrito, para que en mostrador
    /// solo haya que enviarlo.
    /// </summary>
    Task<WhatsAppLinkDto> TrackingLinkAsync(
        Guid id, OrderMessageKind kind, CancellationToken ct = default);

    /// <summary>La orden vista desde el enlace, sin sesión. El token es la credencial.</summary>
    Task<OrderTrackingDto> TrackingPublicAsync(Guid token, CancellationToken ct = default);

    /// <summary>El logo del taller para el encabezado de esa página. Null si no tiene.</summary>
    Task<TenantLogo?> TrackingLogoPublicAsync(Guid token, CancellationToken ct = default);

    /// <summary>Los vehículos a los que les toca servicio, el más atrasado primero.</summary>
    Task<IReadOnlyList<ServiceReminderDto>> ServiceRemindersAsync(
        ServiceReminderQuery query, CancellationToken ct = default);

    /// <summary>
    /// El enlace de WhatsApp para recordarle el servicio, y deja constancia de que ya se le
    /// avisó: así no se le llama dos veces la misma semana.
    /// </summary>
    Task<WhatsAppLinkDto> ServiceReminderLinkAsync(Guid workOrderId, CancellationToken ct = default);
}

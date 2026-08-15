namespace Garaj.Application.WorkOrders;

/// <summary>
/// Un trabajo frecuente con todo lo que lleva. Los totales son **a precios de hoy**: salen del
/// catálogo cada vez que se pide, no de lo que costaba cuando se guardó la plantilla.
/// </summary>
public record JobTemplateDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int UsageCount,
    DateTimeOffset? LastUsedAt,
    IReadOnlyList<JobTemplateTaskDto> Tasks,
    IReadOnlyList<JobTemplatePartDto> Parts,
    decimal LaborTotal,
    decimal PartsTotal,
    decimal Total);

public record JobTemplateTaskDto(
    Guid Id,
    string Title,
    string? Description,
    int Sequence,
    Guid? LaborServiceId,
    string? LaborServiceName,
    decimal? EstimatedHours,
    /// <summary>Lo que se cobraría por el paso hoy. Null si no lleva servicio del catálogo.</summary>
    decimal? Price);

public record JobTemplatePartDto(
    Guid Id,
    Guid? PartId,
    string Sku,
    string PartName,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total);

public record SaveJobTemplateRequest(
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<SaveJobTemplateTaskRequest> Tasks,
    IReadOnlyList<SaveJobTemplatePartRequest> Parts);

public record SaveJobTemplateTaskRequest(
    string Title,
    string? Description,
    Guid? LaborServiceId,
    decimal? EstimatedHours);

/// <param name="PartId">Null en una línea manual, que entonces necesita <paramref name="Description"/>.</param>
public record SaveJobTemplatePartRequest(
    Guid? PartId,
    string? Description,
    decimal Quantity);

/// <summary>
/// Guardar una orden ya hecha como trabajo frecuente. Es el camino principal: los pasos, sus
/// servicios y sus repuestos ya están ahí y ya están bien, porque salieron de un trabajo real.
/// </summary>
public record CreateJobTemplateFromWorkOrderRequest(
    Guid WorkOrderId,
    string Name,
    string? Description);

public record ApplyJobTemplateRequest(Guid TemplateId);

/// <summary>
/// Lo que dejó aplicar la plantilla: los pasos ya creados en la orden y los repuestos
/// <b>propuestos</b>.
/// </summary>
/// <remarks>
/// Los repuestos no se cargan solos a propósito. Cargar un repuesto del catálogo descuenta la
/// bodega en ese mismo momento, y al aplicar la plantilla el trabajo todavía no se ha hecho:
/// se estarían consumiendo piezas que siguen en el estante. Además, una sola sin existencia
/// haría fallar la aplicación entera y el técnico se quedaría sin sus pasos por culpa de un
/// empaque. Se proponen, y se cargan uno a uno cuando de verdad se instalan.
/// </remarks>
public record ApplyJobTemplateResultDto(
    string TemplateName,
    IReadOnlyList<WorkOrderTaskDto> Tasks,
    IReadOnlyList<SuggestedPartDto> SuggestedParts);

/// <param name="Available">
/// Existencia en la bodega de la sucursal de la orden, para que se vea antes de intentar
/// cargarlo que de ese repuesto no hay. En una línea manual va en cero: no está en el catálogo.
/// </param>
public record SuggestedPartDto(
    Guid? PartId,
    string Sku,
    string PartName,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal Available,
    string? Description);

public interface IJobTemplateService
{
    /// <summary>Los trabajos frecuentes, el más usado primero.</summary>
    Task<IReadOnlyList<JobTemplateDto>> ListAsync(
        bool includeInactive, CancellationToken ct = default);

    Task<JobTemplateDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<JobTemplateDto> CreateAsync(SaveJobTemplateRequest request, CancellationToken ct = default);
    Task<JobTemplateDto> UpdateAsync(
        Guid id, SaveJobTemplateRequest request, CancellationToken ct = default);

    /// <summary>Se da de baja, no se borra: hay órdenes que se armaron con ella.</summary>
    Task DeactivateAsync(Guid id, CancellationToken ct = default);

    Task<JobTemplateDto> CreateFromWorkOrderAsync(
        CreateJobTemplateFromWorkOrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Anexa los pasos de la plantilla a la orden —no reemplaza los que ya tenga, porque un
    /// trabajo real es «aceite <i>y</i> frenos»— y devuelve sus repuestos como sugerencia.
    /// </summary>
    Task<ApplyJobTemplateResultDto> ApplyToWorkOrderAsync(
        Guid workOrderId, ApplyJobTemplateRequest request, CancellationToken ct = default);
}

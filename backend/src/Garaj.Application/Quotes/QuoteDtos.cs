using Garaj.Application.Common;
using Garaj.Domain.Enums;

namespace Garaj.Application.Quotes;

// ---------- Catálogo de mano de obra ----------

public record LaborServiceDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string? Category,
    decimal StandardHours,
    decimal HourlyRate,
    bool IsFixedPrice,
    decimal FixedPrice,
    bool IsActive,
    // Lo que se cobraría por una unidad de este servicio, ya resuelto.
    decimal Price);

public record SaveLaborServiceRequest(
    string Code,
    string Name,
    string? Description,
    string? Category,
    decimal StandardHours,
    decimal HourlyRate,
    bool IsFixedPrice,
    decimal FixedPrice,
    bool IsActive = true);

// ---------- Cotizaciones ----------

public record QuoteLineDto(
    Guid Id,
    LineType LineType,
    Guid? PartId,
    Guid? LaborServiceId,
    string Description,
    int Sequence,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal Total);

public record QuoteListItemDto(
    Guid Id,
    string Number,
    QuoteStatus Status,
    Guid BranchId,
    string BranchName,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    string? VehicleLabel,
    Guid? WorkOrderId,
    string? WorkOrderNumber,
    decimal Total,
    DateTimeOffset? ValidUntil,
    DateTimeOffset? SentAt,
    DateTimeOffset? RespondedAt,
    DateTimeOffset CreatedAt,
    bool IsExpired);

public record QuoteDetailDto(
    Guid Id,
    string Number,
    QuoteStatus Status,
    Guid BranchId,
    string BranchName,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    Guid? VehicleId,
    string? VehicleLabel,
    string? Plate,
    Guid? ServiceRequestId,
    Guid? WorkOrderId,
    string? WorkOrderNumber,
    string? Notes,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxRate,
    decimal TaxTotal,
    decimal Total,
    string Currency,
    DateTimeOffset? ValidUntil,
    DateTimeOffset? SentAt,
    DateTimeOffset? RespondedAt,
    string? CustomerResponseNote,
    DateTimeOffset CreatedAt,
    bool IsExpired,
    bool IsEditable,
    // Link público que se comparte por WhatsApp. Solo se llena una vez enviada.
    string? PublicUrl,
    IReadOnlyList<QuoteLineDto> Lines);

public record CreateQuoteRequest(
    Guid CustomerId,
    Guid? VehicleId,
    Guid? BranchId,
    Guid? ServiceRequestId,
    Guid? WorkOrderId,
    DateTimeOffset? ValidUntil,
    string? Notes,
    // Si va null se usa el impuesto por defecto del taller.
    decimal? TaxRate);

public record UpdateQuoteRequest(
    DateTimeOffset? ValidUntil,
    string? Notes,
    decimal? TaxRate);

/// <param name="Description">
/// Opcional: si va vacío se toma el del catálogo. Se congela en la línea, para que
/// renombrar un repuesto mañana no cambie una cotización ya enviada.
/// </param>
public record SaveQuoteLineRequest(
    LineType LineType,
    Guid? PartId,
    Guid? LaborServiceId,
    string? Description,
    decimal Quantity,
    decimal? UnitPrice,
    decimal Discount = 0);

/// <summary>Todo lo que hace falta para armar la cotización desde una orden de trabajo.</summary>
public record QuoteFromWorkOrderRequest(
    Guid WorkOrderId,
    DateTimeOffset? ValidUntil,
    string? Notes,
    // Incluye los repuestos ya consumidos como líneas de la cotización.
    bool IncludeParts = true,
    // Incluye los pasos de la reparación que tengan servicio de mano de obra asignado.
    bool IncludeTasks = true);

public record QuoteQuery : PageQuery
{
    public QuoteStatus? Status { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? WorkOrderId { get; init; }
    public Guid? BranchId { get; init; }

    /// <summary>Por fecha de creación de la cotización. Sin rango, todas.</summary>
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

/// <param name="Url">`https://wa.me/504…?text=…`, listo para abrir.</param>
public record WhatsAppLinkDto(string Url, string Phone, string Message);

// ---------- Vista pública ----------

/// <summary>
/// Lo que ve el cliente al abrir el link de WhatsApp. Sin datos internos del taller y sin
/// ids que permitan navegar a otra cosa: es una página anónima.
/// </summary>
/// <param name="TenantLogoUrl">
/// Ruta del logo relativa a la base de la API, o null. Va aquí porque la página es del
/// taller: lo primero que ve el cliente tiene que ser la marca de su taller, no la nuestra.
/// </param>
public record PublicQuoteDto(
    string Number,
    QuoteStatus Status,
    string TenantName,
    string? TenantLogoUrl,
    string? TenantPhone,
    string BranchName,
    string CustomerName,
    string? VehicleLabel,
    string? Plate,
    string? Notes,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxRate,
    decimal TaxTotal,
    decimal Total,
    string Currency,
    DateTimeOffset? ValidUntil,
    DateTimeOffset? RespondedAt,
    bool IsExpired,
    bool CanRespond,
    IReadOnlyList<PublicQuoteLineDto> Lines);

public record PublicQuoteLineDto(
    LineType LineType,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal Total);

public record RespondToQuoteRequest(bool Approve, string? Note);

public interface ILaborServiceCatalog
{
    Task<IReadOnlyList<LaborServiceDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<LaborServiceDto> CreateAsync(SaveLaborServiceRequest request, CancellationToken ct = default);
    Task<LaborServiceDto> UpdateAsync(Guid id, SaveLaborServiceRequest request, CancellationToken ct = default);
}

public interface IQuoteService
{
    Task<PagedResult<QuoteListItemDto>> ListAsync(QuoteQuery query, CancellationToken ct = default);
    Task<QuoteDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    Task<QuoteDetailDto> CreateAsync(CreateQuoteRequest request, CancellationToken ct = default);

    /// <summary>Arma la cotización con lo que la orden ya tiene: repuestos consumidos y pasos.</summary>
    Task<QuoteDetailDto> CreateFromWorkOrderAsync(
        QuoteFromWorkOrderRequest request, CancellationToken ct = default);

    Task<QuoteDetailDto> UpdateAsync(Guid id, UpdateQuoteRequest request, CancellationToken ct = default);

    Task<QuoteDetailDto> AddLineAsync(Guid id, SaveQuoteLineRequest request, CancellationToken ct = default);
    Task<QuoteDetailDto> UpdateLineAsync(
        Guid id, Guid lineId, SaveQuoteLineRequest request, CancellationToken ct = default);
    Task<QuoteDetailDto> RemoveLineAsync(Guid id, Guid lineId, CancellationToken ct = default);

    /// <summary>La marca como enviada y devuelve el link de WhatsApp con el mensaje armado.</summary>
    Task<WhatsAppLinkDto> SendAsync(Guid id, CancellationToken ct = default);

    /// <summary>El link de WhatsApp sin cambiar el estado, para reenviar.</summary>
    Task<WhatsAppLinkDto> WhatsAppLinkAsync(Guid id, CancellationToken ct = default);

    Task<byte[]> PdfAsync(Guid id, CancellationToken ct = default);

    /// <summary>Respuesta desde dentro de la app (el Cliente autenticado o el Dueño por teléfono).</summary>
    Task<QuoteDetailDto> RespondAsync(
        Guid id, RespondToQuoteRequest request, CancellationToken ct = default);

    // ---- Sin autenticación, resueltos por token ----

    Task<PublicQuoteDto> GetPublicAsync(Guid token, CancellationToken ct = default);
    Task<PublicQuoteDto> RespondPublicAsync(
        Guid token, RespondToQuoteRequest request, CancellationToken ct = default);
    Task<byte[]> PdfPublicAsync(Guid token, CancellationToken ct = default);

    /// <summary>
    /// El logo del taller para la página pública. Va por el token de la cotización y no por
    /// el id del taller: en esta página el token es la única credencial.
    /// </summary>
    Task<Tenants.TenantLogo?> LogoPublicAsync(Guid token, CancellationToken ct = default);
}

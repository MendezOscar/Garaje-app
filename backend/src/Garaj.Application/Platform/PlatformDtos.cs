namespace Garaj.Application.Platform;

/// <summary>
/// Un taller visto desde nuestro lado: no lo que hace, sino cómo va con la mensualidad.
/// </summary>
/// <remarks>
/// Nótese lo que <b>no</b> lleva: ni una orden, ni un cliente, ni un peso facturado. El usuario
/// de plataforma administra el cobro; el trabajo del taller es del taller.
/// </remarks>
/// <param name="State">`Active`, `DueSoon`, `Grace`, `ReadOnly` o `Suspended`, ya calculado.</param>
/// <param name="LastPaymentOn">Cuándo pagó por última vez, o null si nunca.</param>
public record PlatformTenantDto(
    Guid Id,
    string Name,
    string? LegalName,
    string? Phone,
    string? Email,
    string? PlanName,
    decimal MonthlyFee,
    string Currency,
    DateOnly? PaidThrough,
    int GraceDays,
    DateOnly? UnblockedThrough,
    string? UnblockNote,
    bool IsActive,
    string State,
    int? DaysLeft,
    DateOnly? ReadOnlyOn,
    DateOnly? LastPaymentOn,
    int BranchCount,
    DateTimeOffset CreatedAt);

public record PlatformTenantDetailDto(
    PlatformTenantDto Tenant,
    IReadOnlyList<SubscriptionPaymentDto> Payments);

public record SubscriptionPaymentDto(
    Guid Id,
    DateOnly PaidOn,
    decimal Amount,
    string Currency,
    string? Method,
    string? Reference,
    DateOnly CoversThrough,
    string? Note,
    DateTimeOffset CreatedAt);

/// <summary>
/// Alta de un taller nuevo desde el panel. Los primeros cuatro campos son los mismos que pide
/// el comando de consola: por dentro es el mismo <c>TenantProvisioner</c>.
/// </summary>
/// <param name="PaidThrough">
/// Hasta cuándo queda pagado de entrada. Si se omite, un mes desde hoy: lo normal es que el
/// taller pague la instalación el día que se instala.
/// </param>
public record CreateTenantRequest(
    string Name,
    string OwnerEmail,
    string OwnerName,
    string BranchName,
    string? BranchCode = null,
    string? City = null,
    string? Address = null,
    string? LegalName = null,
    string? TaxId = null,
    string? Phone = null,
    string? Email = null,
    string? PlanName = null,
    decimal MonthlyFee = 0,
    DateOnly? PaidThrough = null,
    int? GraceDays = null);

/// <summary>
/// La contraseña del Dueño se devuelve <b>una sola vez</b>, al crear: no se guarda en claro en
/// ninguna parte y no hay forma de volver a consultarla. Quien la crea la entrega y ya.
/// </summary>
public record CreatedTenantDto(Guid TenantId, Guid BranchId, string OwnerEmail, string Password);

/// <param name="Months">Cuántos meses cubre el pago. Casi siempre 1.</param>
/// <param name="PaidOn">Cuándo pagó el taller. Si se omite, hoy.</param>
public record RegisterPaymentRequest(
    decimal Amount,
    DateOnly? PaidOn = null,
    string? Method = null,
    string? Reference = null,
    int Months = 1,
    string? Note = null);

/// <summary>El acuerdo de pago: hasta cuándo se le suelta el freno y por qué.</summary>
public record PaymentAgreementRequest(DateOnly UnblockedThrough, string? Note);

public record UpdateSubscriptionRequest(
    string? PlanName,
    decimal MonthlyFee,
    DateOnly? PaidThrough,
    int GraceDays);

/// <summary>
/// Lo que hacemos nosotros con nuestros clientes: darlos de alta, cobrarles y, si hace falta,
/// cortarles. Ninguna de estas operaciones toca datos de trabajo de ningún taller.
/// </summary>
public interface IPlatformService
{
    Task<IReadOnlyList<PlatformTenantDto>> ListAsync(CancellationToken ct = default);

    Task<PlatformTenantDetailDto> GetAsync(Guid tenantId, CancellationToken ct = default);

    Task<CreatedTenantDto> CreateTenantAsync(CreateTenantRequest request, CancellationToken ct = default);

    /// <summary>Registra el pago y corre la fecha de vencimiento. Cancela el acuerdo si lo había.</summary>
    Task<PlatformTenantDetailDto> RegisterPaymentAsync(
        Guid tenantId, RegisterPaymentRequest request, CancellationToken ct = default);

    Task<PlatformTenantDto> SetAgreementAsync(
        Guid tenantId, PaymentAgreementRequest request, CancellationToken ct = default);

    /// <summary>Quita el acuerdo antes de tiempo: el taller vuelve a lo que digan sus fechas.</summary>
    Task<PlatformTenantDto> ClearAgreementAsync(Guid tenantId, CancellationToken ct = default);

    Task<PlatformTenantDto> UpdateSubscriptionAsync(
        Guid tenantId, UpdateSubscriptionRequest request, CancellationToken ct = default);

    /// <summary>Suspender corta el acceso entero, no solo la escritura: el taller ni entra.</summary>
    Task<PlatformTenantDto> SetActiveAsync(Guid tenantId, bool active, CancellationToken ct = default);
}

using Garaj.Domain.Enums;

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
    DateTimeOffset CreatedAt,
    /// <summary>Con qué trabaja el taller. Lo decidimos nosotros, no él.</summary>
    WorkshopKind WorksWith = WorkshopKind.Vehicles);

public record PlatformTenantDetailDto(
    PlatformTenantDto Tenant,
    IReadOnlyList<SubscriptionPaymentDto> Payments);

/// <param name="InvoiceId">El documento que dejó este pago, o null si se registró sin emitir.</param>
public record SubscriptionPaymentDto(
    Guid Id,
    DateOnly PaidOn,
    decimal Amount,
    string Currency,
    string? Method,
    string? Reference,
    DateOnly CoversThrough,
    string? Note,
    DateTimeOffset CreatedAt,
    Guid? InvoiceId = null,
    string? InvoiceNumber = null,
    bool InvoiceIsFiscal = false,
    bool InvoiceIsVoided = false);

/// <summary>
/// Alta de un taller nuevo desde el panel. Los primeros cuatro campos son los mismos que pide
/// el comando de consola: por dentro es el mismo <c>TenantProvisioner</c>.
/// </summary>
/// <param name="PaidThrough">
/// Hasta cuándo queda pagado de entrada. Si se omite, un mes desde hoy: lo normal es que el
/// taller pague la instalación el día que se instala.
/// </param>
/// <param name="Password">
/// Contraseña del Dueño. Opcional a propósito: al instalar suele estar sentado al lado y
/// prefiere escribir la suya —se la lleva sabida y no hay papel que perder—, pero si no está,
/// se genera una y se entrega. Con cualquiera de las dos puede cambiarla después en Usuarios.
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
    int? GraceDays = null,
    string? Password = null,
    /// <summary>Con qué trabaja: vehículos o equipos. Se decide aquí, al darlo de alta.</summary>
    WorkshopKind WorksWith = WorkshopKind.Vehicles);

/// <summary>
/// La contraseña del Dueño se devuelve <b>una sola vez</b>, al crear —la haya escrito él o la
/// haya generado el sistema—: no se guarda en claro en ninguna parte y no hay forma de volver a
/// consultarla. Quien la crea la entrega y ya.
/// </summary>
public record CreatedTenantDto(Guid TenantId, Guid BranchId, string OwnerEmail, string Password);

/// <param name="Months">Cuántos meses cubre el pago. Casi siempre 1.</param>
/// <param name="PaidOn">Cuándo pagó el taller. Si se omite, hoy.</param>
/// <param name="Invoice">
/// Si el pago deja documento. Verdadero es lo normal; se apaga para registrar un pago viejo
/// que ya se facturó por fuera, o un ajuste que no se le entrega a nadie.
/// </param>
/// <param name="Fiscal">
/// Si el documento sale con CAI. Sin rango vigente no se puede y el servidor lo dice, en vez
/// de emitir en silencio un comprobante donde se esperaba una factura.
/// </param>
/// <param name="Lines">
/// Los conceptos. Vacío factura una sola línea «Mensualidad» por el neto del pago: es el 90%
/// de los casos, y escribir lo mismo todos los meses no es trabajo de nadie.
/// </param>
public record RegisterPaymentRequest(
    decimal Amount,
    DateOnly? PaidOn = null,
    string? Method = null,
    string? Reference = null,
    int Months = 1,
    string? Note = null,
    bool Invoice = true,
    bool Fiscal = false,
    IReadOnlyList<SaveInvoiceLineRequest>? Lines = null);

/// <summary>
/// Alta de un Dueño para un taller que ya existe. Es el rescate: si el único Dueño perdió la
/// contraseña, borró su cuenta desde la app o se fue del negocio, el taller queda con sus datos
/// intactos pero sin nadie que pueda entrar, y desde adentro no hay forma de arreglarlo.
/// </summary>
public record CreateOwnerRequest(string Email, string FullName, string? Password = null);

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
public record SetWorksWithRequest(WorkshopKind WorksWith);

public interface IPlatformService
{
    Task<IReadOnlyList<PlatformTenantDto>> ListAsync(CancellationToken ct = default);

    Task<PlatformTenantDetailDto> GetAsync(Guid tenantId, CancellationToken ct = default);

    Task<CreatedTenantDto> CreateTenantAsync(CreateTenantRequest request, CancellationToken ct = default);

    /// <summary>Le crea un Dueño nuevo a un taller existente. La contraseña vuelve una sola vez.</summary>
    Task<CreatedTenantDto> CreateOwnerAsync(
        Guid tenantId, CreateOwnerRequest request, CancellationToken ct = default);

    /// <summary>Registra el pago y corre la fecha de vencimiento. Cancela el acuerdo si lo había.</summary>
    Task<PlatformTenantDetailDto> RegisterPaymentAsync(
        Guid tenantId, RegisterPaymentRequest request, CancellationToken ct = default);

    Task<PlatformTenantDto> SetAgreementAsync(
        Guid tenantId, PaymentAgreementRequest request, CancellationToken ct = default);

    /// <summary>Quita el acuerdo antes de tiempo: el taller vuelve a lo que digan sus fechas.</summary>
    Task<PlatformTenantDto> ClearAgreementAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Corrige con qué trabaja el taller. Se fija al darlo de alta; esto es para el error de
    /// dedo del primer día, no para que un taller cambie de rubro con órdenes abiertas.
    /// </summary>
    Task<PlatformTenantDto> SetWorksWithAsync(
        Guid tenantId, WorkshopKind worksWith, CancellationToken ct = default);

    Task<PlatformTenantDto> UpdateSubscriptionAsync(
        Guid tenantId, UpdateSubscriptionRequest request, CancellationToken ct = default);

    /// <summary>Suspender corta el acceso entero, no solo la escritura: el taller ni entra.</summary>
    Task<PlatformTenantDto> SetActiveAsync(Guid tenantId, bool active, CancellationToken ct = default);
}

/// <summary>
/// Lo nuestro como empresa: quiénes somos en el documento, nuestro CAI, y las facturas que le
/// entregamos a cada taller. Separado de <see cref="IPlatformService"/> —que administra el
/// cobro— porque esto es el papel, no la cuenta.
/// </summary>
public interface IPlatformBillingService
{
    Task<PlatformSettingsDto> GetSettingsAsync(CancellationToken ct = default);

    Task<PlatformSettingsDto> SaveSettingsAsync(
        SavePlatformSettingsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Registra un rango autorizado nuevo y da de baja el anterior: emitir con dos rangos
    /// vivos es la forma de saltarse un número sin enterarse.
    /// </summary>
    Task<PlatformSettingsDto> SaveFiscalRangeAsync(
        SavePlatformFiscalRangeRequest request, CancellationToken ct = default);

    Task<SubscriptionInvoiceDto> GetInvoiceAsync(Guid id, CancellationToken ct = default);

    /// <summary>El PDF que se le manda al taller por WhatsApp o por correo.</summary>
    Task<byte[]> InvoicePdfAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Anula el documento. No lo borra ni toca el pago: el dinero entró igual, y un número
    /// autorizado que desaparece es el que el SAR viene a preguntar.
    /// </summary>
    Task<SubscriptionInvoiceDto> VoidInvoiceAsync(
        Guid id, VoidInvoiceRequest request, CancellationToken ct = default);
}

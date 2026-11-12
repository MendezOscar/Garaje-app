using Garaj.Domain.Enums;

namespace Garaj.Application.Tenants;

/// <summary>
/// La ficha del taller. Estos datos se imprimen en la cotización y en la factura que recibe
/// el cliente final, así que el Dueño tiene que poder corregirlos sin pedírselo a nadie.
/// </summary>
/// <param name="LogoUrl">
/// Ruta <b>relativa a la base de la API</b> (`/api/tenants/{id}/logo`), o null si el taller no
/// ha subido logo. Relativa a propósito: el panel y la app ya tienen configurada la base
/// (`VITE_API_URL`, `--dart-define=API_URL`), así que no hace falta otra variable ni depender
/// de las cabeceras que reescribe el proxy de Render.
/// </param>
public record TenantSettingsDto(
    Guid Id,
    string Name,
    string? LegalName,
    string? TaxId,
    string? Phone,
    string? Email,
    string? Address,
    string Currency,
    // Con qué trabaja el taller: vehículos o equipos. De aquí sale qué se le puede dar de
    // alta a un cliente y cómo se llama en las pantallas.
    WorkshopKind WorksWith,
    decimal DefaultTaxRate,
    // Si los precios del catálogo ya llevan el ISV adentro. De aquí sale el texto que ven el
    // cliente y el mostrador, y el cálculo de la venta y de la cotización.
    bool PricesIncludeTax,
    string DefaultPhoneCountryCode,
    // Días de garantía que lleva un trabajo por defecto. Cero es sin garantía.
    int DefaultWarrantyDays,
    // Bodegaje: si se cobra, cuántos días de gracia hay desde el aviso, y cuánto por día.
    bool ChargesStorage,
    int StorageFreeDays,
    decimal StorageDailyRate,
    // Si el técnico ve precios de repuestos, de mano de obra y los totales de la orden.
    bool TechniciansSeePrices,
    // Citas: el token del enlace público y si está recibiendo solicitudes.
    Guid BookingToken,
    bool BookingEnabled,
    string? BookingUrl,
    string? LogoUrl,
    // El asistente: si lo tiene y con cuántos créditos. Lo enciende la plataforma, no el
    // taller, así que viaja de solo lectura. El tope en lempiras no sale de aquí: ese es
    // nuestro, y enseñárselo al taller invitaría a discutirlo.
    bool AssistantEnabled = false,
    int AssistantCreditsPerMonth = 0,
    int AssistantCreditsPerDay = 0,
    // Los pasos opcionales que usa el taller. Va al final y con valor por defecto porque la
    // app publicada arma esta ficha sin saber que existen.
    WorkOrderSteps WorkOrderSteps = WorkOrderSteps.All,
    // Cuántas sucursales activas puede tener. Null es sin tope. Viaja de solo lectura: el
    // tope lo movemos nosotros desde el panel de plataforma, no el taller.
    int? BranchLimit = null);

public record UpdateTenantRequest(
    string Name,
    string? LegalName,
    string? TaxId,
    string? Phone,
    string? Email,
    string? Address,
    decimal DefaultTaxRate,
    string? DefaultPhoneCountryCode,
    int DefaultWarrantyDays = 0,
    bool ChargesStorage = false,
    int StorageFreeDays = 3,
    decimal StorageDailyRate = 0,
    bool TechniciansSeePrices = true,
    bool BookingEnabled = true,
    bool PricesIncludeTax = true,
    // Los pasos de la orden. **Nulo es no tocarlos**, y es lo que manda la app publicada:
    // guardar la ficha del taller desde el teléfono no puede devolverle el flujo completo a
    // quien lo apagó desde el panel.
    WorkOrderSteps? WorkOrderSteps = null);

/// <param name="ContentType">Tipo del objeto guardado; hoy siempre `image/png`.</param>
public record TenantLogo(byte[] Bytes, string ContentType);

// ---------- Régimen de facturación (CAI) ----------

/// <summary>
/// Un rango autorizado por el SAR, como se ve desde el panel: con lo que queda por emitir y si
/// está por vencerse, que es lo que hay que saber antes de quedarse sin poder facturar.
/// </summary>
public record FiscalRangeDto(
    Guid Id,
    Guid BranchId,
    string BranchName,
    string Cai,
    string EstablishmentCode,
    string PointOfSaleCode,
    string DocumentType,
    int RangeStart,
    int RangeEnd,
    int NextNumber,
    int Remaining,
    string RangeText,
    string NextFiscalNumber,
    DateTimeOffset IssueDeadline,
    bool IsActive,
    bool IsExpired,
    bool IsExhausted);

public record SaveFiscalRangeRequest(
    Guid BranchId,
    string Cai,
    string EstablishmentCode,
    string PointOfSaleCode,
    string DocumentType,
    int RangeStart,
    int RangeEnd,
    DateTimeOffset IssueDeadline);

public interface ITenantService
{
    Task<TenantSettingsDto> GetAsync(CancellationToken ct = default);

    Task<TenantSettingsDto> UpdateAsync(UpdateTenantRequest request, CancellationToken ct = default);

    /// <summary>
    /// Guarda el logo del taller. Normaliza a PNG de 512 px de lado mayor: el archivo que
    /// manda un cliente puede venir en cualquier tamaño y el logo se pinta a 30 px en una
    /// barra y a 70 pt en un PDF.
    /// </summary>
    Task<TenantSettingsDto> SetLogoAsync(Stream content, string contentType, CancellationToken ct = default);

    Task<TenantSettingsDto> RemoveLogoAsync(CancellationToken ct = default);

    /// <summary>
    /// El afiche del enlace de citas, en PDF: una hoja con el QR grande para imprimir y pegar
    /// en el mostrador. Es publicidad del taller, no un documento del negocio.
    /// </summary>
    Task<byte[]> BookingPosterAsync(CancellationToken ct = default);

    /// <summary>
    /// Los bytes del logo de un taller cualquiera, sin contexto de sesión: lo sirve la ruta
    /// pública que consumen las etiquetas &lt;img&gt;. Null si ese taller no tiene logo.
    /// </summary>
    Task<TenantLogo?> GetLogoAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// El logo del taller de la petición en curso, para el encabezado de los PDF. Devuelve
    /// null también cuando el almacenamiento falla: un bucket caído no puede impedir facturar.
    /// </summary>
    Task<byte[]?> TryGetLogoBytesAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>La ruta relativa del logo, o null. La comparten los DTO que ya llevan el nombre del taller.</summary>
    static string LogoPath(Guid tenantId) => $"/api/tenants/{tenantId}/logo";

    /// <summary>
    /// Los rangos de facturación del taller, el vigente de cada sucursal primero. Vacío en un
    /// taller que no factura con CAI, que es lo normal hasta que el SAR le autoriza uno.
    /// </summary>
    Task<IReadOnlyList<FiscalRangeDto>> ListFiscalRangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Registra un rango nuevo y desactiva el que la sucursal tuviera: el SAR autoriza uno a
    /// la vez, y dos activos dejarían el correlativo a suerte de cuál se lea primero.
    /// </summary>
    Task<FiscalRangeDto> SaveFiscalRangeAsync(
        SaveFiscalRangeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deja de emitir con este rango. No se borra: las facturas emitidas guardan su copia de
    /// estos datos y el rango es lo que explica de dónde salió cada número.
    /// </summary>
    Task DeactivateFiscalRangeAsync(Guid id, CancellationToken ct = default);
}

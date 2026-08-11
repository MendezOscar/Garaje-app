using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Tenants;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// La ficha del taller y su logo. Es lo que convierte el sistema en «el sistema del taller»:
/// el logo viaja en el panel, en la app, en la cotización que el cliente abre por WhatsApp y
/// en los dos PDF.
/// </summary>
public class TenantService(
    GarajDbContext db,
    IStorageService storage,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<TenantService> logger) : ITenantService
{
    /// <summary>Tope de entrada. Un logo real no pasa de unos cientos de kilobytes.</summary>
    private const long MaxLogoBytes = 2 * 1024 * 1024;

    /// <summary>Lado mayor del PNG que se guarda: alcanza para el encabezado de un PDF a 300 ppp.</summary>
    private const int MaxLogoSide = 512;

    private const string LogoContentType = "image/png";

    /// <summary>
    /// SVG queda fuera a propósito: puede traer script y se serviría desde el origen de la
    /// API, que es donde vive la sesión de todos los talleres.
    /// </summary>
    private static readonly string[] AllowedContentTypes = ["image/png", "image/jpeg"];

    public async Task<TenantSettingsDto> GetAsync(CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        return Map(await CurrentAsync(ct));
    }

    public async Task<TenantSettingsDto> UpdateAsync(
        UpdateTenantRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new AppException("El nombre del taller es obligatorio.");

        if (request.DefaultTaxRate is < 0 or > 100)
            throw new AppException("La tasa de impuesto va entre 0 y 100.");

        var tenant = await CurrentAsync(ct);

        tenant.Name = name;
        tenant.LegalName = Trim(request.LegalName);
        tenant.TaxId = Trim(request.TaxId);
        tenant.Phone = Trim(request.Phone);
        tenant.Email = Trim(request.Email);
        tenant.DefaultTaxRate = request.DefaultTaxRate;

        // El código de país arma los links de WhatsApp: si llega vacío se queda el que había.
        if (Trim(request.DefaultPhoneCountryCode) is { } code)
            tenant.DefaultPhoneCountryCode = code;

        await db.SaveChangesAsync(ct);

        return Map(tenant);
    }

    public async Task<TenantSettingsDto> SetLogoAsync(
        Stream content, string contentType, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        if (!AllowedContentTypes.Contains(contentType.Trim().ToLowerInvariant()))
            throw new AppException("El logo tiene que ser PNG o JPEG.");

        // A memoria antes de decodificar: el stream del formulario no siempre admite Seek y
        // SkiaSharp lo lee dos veces. Con el tope de 2 MB no hay riesgo de reventar memoria.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        if (buffer.Length == 0)
            throw new AppException("El archivo llegó vacío.");

        if (buffer.Length > MaxLogoBytes)
            throw new AppException($"El logo no puede pasar de {MaxLogoBytes / (1024 * 1024)} MB.");

        buffer.Position = 0;
        var png = Normalize(buffer)
            ?? throw new AppException("No se pudo leer la imagen. Pruebe con un PNG o un JPEG.");

        var tenant = await CurrentAsync(ct);
        var key = LogoKey(tenant.Id);

        using var upload = new MemoryStream(png);
        await storage.UploadAsync(key, upload, LogoContentType, ct);

        tenant.LogoStorageKey = key;
        await db.SaveChangesAsync(ct);

        return Map(tenant);
    }

    public async Task<TenantSettingsDto> RemoveLogoAsync(CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var tenant = await CurrentAsync(ct);

        if (tenant.LogoStorageKey is { } key)
        {
            // Primero la fila: si el borrado del objeto falla, el taller queda sin logo igual
            // y el objeto huérfano no le hace daño a nadie.
            tenant.LogoStorageKey = null;
            await db.SaveChangesAsync(ct);

            try
            {
                await storage.DeleteAsync(key, ct);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "No se pudo borrar el logo {Key} del almacenamiento.", key);
            }
        }

        return Map(tenant);
    }

    public async Task<TenantLogo?> GetLogoAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Ruta pública: no hay sesión ni tenant en contexto, así que el filtro global no
        // aplica y el tenant se filtra a mano.
        var key = await db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Id == tenantId && t.IsActive)
            .Select(t => t.LogoStorageKey)
            .FirstOrDefaultAsync(ct);

        if (key is null) return null;

        await using var stream = await storage.DownloadAsync(key, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);

        return new TenantLogo(buffer.ToArray(), LogoContentType);
    }

    public async Task<byte[]?> TryGetLogoBytesAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            return (await GetLogoAsync(tenantId, ct))?.Bytes;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "No se pudo cargar el logo del taller {TenantId}.", tenantId);
            return null;
        }
    }

    // ---------- Régimen de facturación (CAI) ----------

    public async Task<IReadOnlyList<FiscalRangeDto>> ListFiscalRangesAsync(
        CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var ranges = await db.FiscalRanges.AsNoTracking()
            .Include(r => r.Branch)
            .OrderBy(r => r.Branch.Name)
            .ThenByDescending(r => r.IsActive)
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return ranges.Select(Map).ToList();
    }

    public async Task<FiscalRangeDto> SaveFiscalRangeAsync(
        SaveFiscalRangeRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();
        scope.EnsureBranchAllowed(request.BranchId);

        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == request.BranchId, ct)
            ?? throw new NotFoundException("La sucursal no existe.");

        var cai = (request.Cai ?? "").Trim().ToUpperInvariant();
        if (cai.Length is < 20 or > 50)
            throw new AppException("El CAI no tiene la forma que entrega el SAR.");

        if (request.RangeStart <= 0 || request.RangeEnd < request.RangeStart)
            throw new AppException("El rango va del número menor al mayor, y empieza en 1 o más.");

        if (request.IssueDeadline <= clock.UtcNow)
            throw new AppException("La fecha límite de emisión ya pasó.");

        // Un rango nuevo reemplaza al anterior de esa sucursal.
        var previos = await db.FiscalRanges
            .Where(r => r.BranchId == request.BranchId && r.IsActive)
            .ToListAsync(ct);

        foreach (var previo in previos) previo.IsActive = false;

        var range = new FiscalRange
        {
            BranchId = request.BranchId,
            Cai = cai,
            EstablishmentCode = Code(request.EstablishmentCode, 3, "000"),
            PointOfSaleCode = Code(request.PointOfSaleCode, 3, "001"),
            DocumentType = Code(request.DocumentType, 2, "01"),
            RangeStart = request.RangeStart,
            RangeEnd = request.RangeEnd,
            NextNumber = request.RangeStart,
            IssueDeadline = request.IssueDeadline,
            IsActive = true
        };

        db.FiscalRanges.Add(range);
        await db.SaveChangesAsync(ct);

        range.Branch = branch;
        return Map(range);
    }

    public async Task DeactivateFiscalRangeAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var range = await db.FiscalRanges.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("El rango no existe.");

        range.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Rellena con ceros a la izquierda: el SAR numera "000", "001", "01".</summary>
    private static string Code(string? value, int length, string fallback)
    {
        var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return fallback;

        return digits.Length > length
            ? digits[^length..]
            : digits.PadLeft(length, '0');
    }

    private FiscalRangeDto Map(FiscalRange r) => new(
        r.Id,
        r.BranchId,
        r.Branch?.Name ?? "",
        r.Cai,
        r.EstablishmentCode,
        r.PointOfSaleCode,
        r.DocumentType,
        r.RangeStart,
        r.RangeEnd,
        r.NextNumber,
        r.Remaining,
        r.RangeText,
        r.Format(Math.Min(r.NextNumber, r.RangeEnd)),
        r.IssueDeadline,
        r.IsActive,
        r.IssueDeadline <= clock.UtcNow,
        r.NextNumber > r.RangeEnd);

    private async Task<Tenant> CurrentAsync(CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId
            ?? throw new UnauthorizedException("La petición no está autenticada.");

        return await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new NotFoundException("El taller no existe.");
    }

    /// <summary>
    /// Reduce y reencoda a PNG. Devuelve null si el archivo no es una imagen que se pueda
    /// decodificar: alguien renombró un PDF a .png.
    /// </summary>
    private static byte[]? Normalize(Stream original)
    {
        using var bitmap = SKBitmap.Decode(original);
        if (bitmap is null) return null;

        var scale = (float)MaxLogoSide / Math.Max(bitmap.Width, bitmap.Height);

        var width = scale < 1 ? (int)Math.Round(bitmap.Width * scale) : bitmap.Width;
        var height = scale < 1 ? (int)Math.Round(bitmap.Height * scale) : bitmap.Height;

        using var resized = bitmap.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default);
        if (resized is null) return null;

        using var image = SKImage.FromBitmap(resized);
        // PNG y no JPEG: el logo de un taller suele traer fondo transparente y el JPEG lo
        // aplanaría a negro sobre la barra oscura del panel.
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data?.ToArray();
    }

    /// <summary>Una sola clave por taller: cambiar el logo sobrescribe y no deja basura atrás.</summary>
    private static string LogoKey(Guid tenantId) => $"tenants/{tenantId}/logo.png";

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TenantSettingsDto Map(Tenant tenant) => new(
        tenant.Id,
        tenant.Name,
        tenant.LegalName,
        tenant.TaxId,
        tenant.Phone,
        tenant.Email,
        tenant.Currency,
        tenant.DefaultTaxRate,
        tenant.DefaultPhoneCountryCode,
        tenant.LogoStorageKey is null ? null : ITenantService.LogoPath(tenant.Id));
}

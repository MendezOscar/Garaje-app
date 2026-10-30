using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Platform;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Documents;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// El papel de nuestra cobranza: quiénes somos en el documento, nuestro CAI y las facturas
/// que ya se emitieron.
/// </summary>
/// <remarks>
/// La emisión no está aquí, sino en <see cref="PlatformService"/>: nace con el pago y en su
/// misma transacción. Aquí vive lo que se configura una vez y lo que se consulta después.
/// </remarks>
public class PlatformBillingService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : IPlatformBillingService
{
    public async Task<PlatformSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var settings = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? new PlatformSettings();

        return await MapAsync(settings, ct);
    }

    public async Task<PlatformSettingsDto> SaveSettingsAsync(
        SavePlatformSettingsRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        if (request.DefaultTaxRate is < 0 or > 100)
            throw new AppException("El ISV tiene que ir entre 0 y 100.");

        var settings = await db.PlatformSettings.FirstOrDefaultAsync(ct);

        if (settings is null)
        {
            settings = new PlatformSettings();
            db.PlatformSettings.Add(settings);
        }

        settings.Name = request.Name.Trim();
        settings.LegalName = Trim(request.LegalName);
        settings.TaxId = Trim(request.TaxId);
        settings.Phone = Trim(request.Phone);
        settings.Email = Trim(request.Email);
        settings.Address = Trim(request.Address);
        settings.DefaultTaxRate = request.DefaultTaxRate;
        settings.PricesIncludeTax = request.PricesIncludeTax;

        // El prefijo manda sobre números ya emitidos: cambiarlo a media numeración deja dos
        // series conviviendo, así que se limpia y se deja en mayúsculas.
        var prefijo = Trim(request.InvoicePrefix)?.ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(prefijo)) settings.InvoicePrefix = prefijo;

        await db.SaveChangesAsync(ct);

        return await MapAsync(settings, ct);
    }

    public async Task<PlatformSettingsDto> SaveFiscalRangeAsync(
        SavePlatformFiscalRangeRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        if (request.RangeEnd < request.RangeStart)
            throw new AppException("El final del rango no puede ser menor que el inicio.");

        if (request.IssueDeadline < clock.UtcNow)
            throw new AppException("La fecha límite de emisión ya pasó: revise el CAI.");

        // Uno vivo a la vez. Con dos rangos activos, el siguiente número depende de cuál se
        // leyó primero, y así es como se salta un correlativo sin enterarse.
        var anteriores = await db.PlatformFiscalRanges.Where(r => r.IsActive).ToListAsync(ct);
        foreach (var anterior in anteriores) anterior.IsActive = false;

        db.PlatformFiscalRanges.Add(new PlatformFiscalRange
        {
            Cai = request.Cai.Trim(),
            EstablishmentCode = request.EstablishmentCode.Trim(),
            PointOfSaleCode = request.PointOfSaleCode.Trim(),
            DocumentType = request.DocumentType.Trim(),
            RangeStart = request.RangeStart,
            RangeEnd = request.RangeEnd,
            NextNumber = request.RangeStart,
            IssueDeadline = request.IssueDeadline,
            IsActive = true
        });

        await db.SaveChangesAsync(ct);

        return await GetSettingsAsync(ct);
    }

    public async Task<SubscriptionInvoiceDto> GetInvoiceAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        return Map(await FindAsync(id, ct));
    }

    public async Task<byte[]> InvoicePdfAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var factura = await FindAsync(id, ct);
        var settings = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? new PlatformSettings();

        return SubscriptionInvoicePdf.Render(Map(factura), await MapAsync(settings, ct));
    }

    public async Task<SubscriptionInvoiceDto> VoidInvoiceAsync(
        Guid id, VoidInvoiceRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new AppException("Diga por qué se anula: es lo que explica el número que falta.");

        var factura = await db.SubscriptionInvoices
            .Include(f => f.Lines)
            .FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new NotFoundException("El documento no existe.");

        if (factura.IsVoided) throw new ConflictException("Ese documento ya está anulado.");

        factura.IsVoided = true;
        factura.VoidReason = request.Reason.Trim();
        factura.VoidedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        return Map(factura);
    }

    private async Task<SubscriptionInvoice> FindAsync(Guid id, CancellationToken ct) =>
        await db.SubscriptionInvoices
            .AsNoTracking()
            .Include(f => f.Lines)
            .FirstOrDefaultAsync(f => f.Id == id, ct)
        ?? throw new NotFoundException("El documento no existe.");

    private async Task<PlatformSettingsDto> MapAsync(PlatformSettings settings, CancellationToken ct)
    {
        var rango = await db.PlatformFiscalRanges
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return new PlatformSettingsDto(
            settings.Name, settings.LegalName, settings.TaxId, settings.Phone, settings.Email,
            settings.Address, settings.DefaultTaxRate, settings.PricesIncludeTax,
            settings.InvoicePrefix, settings.NextInvoiceNumber, settings.Currency,
            rango is null ? null : Map(rango, clock.UtcNow));
    }

    private static PlatformFiscalRangeDto Map(PlatformFiscalRange r, DateTimeOffset ahora) =>
        new(r.Id, r.Cai, r.EstablishmentCode, r.PointOfSaleCode, r.DocumentType,
            r.RangeStart, r.RangeEnd, r.NextNumber, r.Format(r.NextNumber), r.RangeText,
            r.IssueDeadline, r.IsActive, r.Remaining,
            r.IssueDeadline < ahora, r.NextNumber > r.RangeEnd);

    private static SubscriptionInvoiceDto Map(SubscriptionInvoice f) =>
        new(f.Id, f.TenantId, f.Number, f.IssuedOn, f.CustomerName, f.CustomerLegalName,
            f.CustomerTaxId, f.Subtotal, f.TaxRate, f.TaxTotal, f.Total, f.PricesIncludedTax,
            f.Currency, f.Note, f.IsFiscal, f.FiscalCai, f.FiscalRangeText,
            f.FiscalIssueDeadline, f.IsVoided, f.VoidReason,
            f.Lines.Select(l => new SubscriptionInvoiceLineDto(
                l.Description, l.Quantity, l.UnitPrice, l.Total)).ToList());

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

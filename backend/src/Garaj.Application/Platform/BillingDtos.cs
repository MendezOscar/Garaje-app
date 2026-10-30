namespace Garaj.Application.Platform;

/// <summary>
/// Nuestros datos de emisor, los que salen en el documento del taller.
/// </summary>
/// <param name="PricesIncludeTax">
/// Falso —como cobramos hoy— significa que la mensualidad es neta y el ISV se le suma.
/// </param>
public record PlatformSettingsDto(
    string Name,
    string? LegalName,
    string? TaxId,
    string? Phone,
    string? Email,
    string? Address,
    decimal DefaultTaxRate,
    bool PricesIncludeTax,
    string InvoicePrefix,
    int NextInvoiceNumber,
    string Currency,
    /// <summary>El rango autorizado vigente, o null mientras no haya CAI.</summary>
    PlatformFiscalRangeDto? FiscalRange);

public record SavePlatformSettingsRequest(
    string Name,
    string? LegalName,
    string? TaxId,
    string? Phone,
    string? Email,
    string? Address,
    decimal DefaultTaxRate,
    bool PricesIncludeTax,
    string InvoicePrefix);

/// <param name="Remaining">Cuántos números quedan, para pedir el siguiente rango a tiempo.</param>
public record PlatformFiscalRangeDto(
    Guid Id,
    string Cai,
    string EstablishmentCode,
    string PointOfSaleCode,
    string DocumentType,
    int RangeStart,
    int RangeEnd,
    int NextNumber,
    string NextFiscalNumber,
    string RangeText,
    DateTimeOffset IssueDeadline,
    bool IsActive,
    int Remaining,
    bool IsExpired,
    bool IsExhausted);

public record SavePlatformFiscalRangeRequest(
    string Cai,
    string EstablishmentCode,
    string PointOfSaleCode,
    string DocumentType,
    int RangeStart,
    int RangeEnd,
    DateTimeOffset IssueDeadline);

/// <param name="Fiscal">
/// Si el documento sale con CAI. Sin rango vigente no se puede, y entonces sale el comprobante
/// con numeración propia.
/// </param>
public record SubscriptionInvoiceDto(
    Guid Id,
    Guid TenantId,
    string Number,
    DateOnly IssuedOn,
    string CustomerName,
    string? CustomerLegalName,
    string? CustomerTaxId,
    decimal Subtotal,
    decimal TaxRate,
    decimal TaxTotal,
    decimal Total,
    bool PricesIncludedTax,
    string Currency,
    string? Note,
    bool Fiscal,
    string? FiscalCai,
    string? FiscalRangeText,
    DateTimeOffset? FiscalIssueDeadline,
    bool IsVoided,
    string? VoidReason,
    IReadOnlyList<SubscriptionInvoiceLineDto> Lines);

public record SubscriptionInvoiceLineDto(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total);

/// <summary>Un concepto del documento. Sin líneas, se factura «Mensualidad» por el monto.</summary>
public record SaveInvoiceLineRequest(string Description, decimal Quantity, decimal UnitPrice);

public record VoidInvoiceRequest(string Reason);

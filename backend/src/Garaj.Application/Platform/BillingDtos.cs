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
    /// <summary>El taller, en los documentos de GarajApp. Null en los de los otros servicios.</summary>
    Guid? TenantId,
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

// ---------- Los servicios que cobramos, y su llave ----------

/// <param name="ApiKeyHint">Las cuatro últimas de su llave. Null si no tiene llave todavía.</param>
public record PlatformProductDto(
    Guid Id,
    string Name,
    bool IsActive,
    string? ApiKeyHint,
    DateTimeOffset? ApiKeyIssuedOn,
    int InvoiceCount);

public record SavePlatformProductRequest(string Name, bool IsActive = true);

/// <summary>
/// La llave recién generada. Es la única vez que existe en claro: después solo queda su hash.
/// </summary>
public record NewApiKeyDto(Guid ProductId, string Name, string ApiKey);

// ---------- Emitir desde el portal de otro servicio ----------

/// <param name="Customer">A quién se le factura. Se copia en el documento, no se guarda aparte.</param>
/// <param name="ExternalCustomerRef">
/// El id del cliente en el sistema que cobró. Sirve para juntar sus documentos sin que su ficha
/// viva aquí.
/// </param>
/// <param name="Fiscal">
/// Con CAI. En falso sale un comprobante con nuestro correlativo propio, que es lo que se le da
/// a quien no pide factura.
/// </param>
public record IssueInvoiceRequest(
    InvoiceCustomerDto Customer,
    IReadOnlyList<SaveInvoiceLineRequest> Lines,
    string? ExternalCustomerRef = null,
    bool Fiscal = true,
    DateOnly? PaidOn = null,
    string? PaymentReference = null,
    string? Note = null);

public record InvoiceCustomerDto(string Name, string? LegalName = null, string? TaxId = null);

/// <summary>Lo que el portal necesita para enseñarle el documento a su cliente.</summary>
public record IssuedInvoiceDto(
    Guid Id,
    string Number,
    DateOnly IssuedOn,
    bool IsFiscal,
    string? Cai,
    string? RangeText,
    DateTimeOffset? IssueDeadline,
    decimal Subtotal,
    decimal TaxRate,
    decimal TaxTotal,
    decimal Total,
    string Currency,
    /// <summary>Ruta del PDF, relativa a la base de la API.</summary>
    string PdfPath,
    /// <summary>Verdadero cuando la petición era un reintento y se devolvió lo ya emitido.</summary>
    bool AlreadyIssued);

// ---------- El libro de ventas ----------

/// <param name="Taxable">Lo gravado: el total menos el ISV, en los documentos con impuesto.</param>
/// <param name="Exempt">Lo exento: el total de los documentos que salieron sin impuesto.</param>
public record SalesBookRowDto(
    DateOnly IssuedOn,
    string Number,
    string? Cai,
    string? ProductName,
    string CustomerName,
    string? CustomerTaxId,
    decimal Taxable,
    decimal Exempt,
    decimal Tax,
    decimal Total,
    bool IsVoided,
    string? VoidReason);

/// <summary>
/// El libro de ventas del periodo: lo que se le entrega al contador y lo que respalda ante el
/// SAR.
/// </summary>
/// <param name="MissingNumbers">
/// Los números del rango que se tomaron y no quedaron en ningún documento, entre el primero y
/// el último del periodo. Van dichos a propósito: un hueco sin explicar es lo que levanta la
/// pregunta en una revisión.
/// </param>
/// <param name="VoidedCount">
/// Cuántos están anulados. No suman: aparecen en el libro porque el número existió.
/// </param>
public record SalesBookDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<SalesBookRowDto> Rows,
    IReadOnlyList<string> MissingNumbers,
    decimal Taxable,
    decimal Exempt,
    decimal Tax,
    decimal Total,
    int Count,
    int VoidedCount,
    string Currency);

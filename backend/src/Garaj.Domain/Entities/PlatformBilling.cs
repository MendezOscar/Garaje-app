using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Quiénes somos en el documento que le damos al taller: razón social, RTN y cómo cobramos.
/// </summary>
/// <remarks>
/// Es una sola fila, y vive en la base y no en la configuración del servidor porque el RTN o
/// la dirección cambian un martes cualquiera y eso no puede pedir un despliegue.
///
/// Ninguna de estas entidades implementa <c>ITenantEntity</c>, igual que
/// <see cref="SubscriptionPayment"/>: no son de ningún taller, son nuestras.
/// </remarks>
public class PlatformSettings : AuditableEntity
{
    public string Name { get; set; } = "GarajApp";
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }

    /// <summary>El ISV que le cargamos a la mensualidad. En cero, el documento va sin impuesto.</summary>
    public decimal DefaultTaxRate { get; set; } = 15m;

    /// <summary>
    /// Si el precio de la mensualidad ya lleva el impuesto adentro. En falso —que es como
    /// cobramos— el ISV se suma sobre el neto y el taller paga el total.
    /// </summary>
    public bool PricesIncludeTax { get; set; }

    /// <summary>Lo que va delante del correlativo propio: «C-000001».</summary>
    public string InvoicePrefix { get; set; } = "C";

    /// <summary>El siguiente número propio, el que se usa mientras no haya CAI.</summary>
    public int NextInvoiceNumber { get; set; } = 1;

    public string Currency { get; set; } = "HNL";
}

/// <summary>
/// Nuestro rango autorizado por el SAR. Es el mismo trato que <see cref="FiscalRange"/> le da
/// al del taller, sin sucursal: aquí el establecimiento emisor es uno solo.
/// </summary>
public class PlatformFiscalRange : AuditableEntity
{
    public string Cai { get; set; } = null!;

    public string EstablishmentCode { get; set; } = "000";
    public string PointOfSaleCode { get; set; } = "001";
    public string DocumentType { get; set; } = "01";

    public int RangeStart { get; set; }
    public int RangeEnd { get; set; }

    /// <summary>El próximo número a emitir. Avanza solo, y nunca hacia atrás.</summary>
    public int NextNumber { get; set; }

    public DateTimeOffset IssueDeadline { get; set; }

    public bool IsActive { get; set; } = true;

    public int Remaining => Math.Max(0, RangeEnd - NextNumber + 1);

    public string Format(int number) =>
        $"{EstablishmentCode}-{PointOfSaleCode}-{DocumentType}-{number:D8}";

    public string RangeText => $"{Format(RangeStart)} a {Format(RangeEnd)}";
}

/// <summary>
/// El documento que le entregamos al taller por su mensualidad. Nace del pago —se emite
/// cuando el dinero ya entró—, así que no hay facturas por cobrar que después haya que anular.
/// </summary>
/// <remarks>
/// Los datos del taller se <b>copian</b> al emitir y no se leen después por relación: si el
/// taller corrige su RTN el mes que viene, la factura de este mes tiene que seguir diciendo lo
/// que decía cuando se emitió.
/// </remarks>
public class SubscriptionInvoice : AuditableEntity
{
    public Guid TenantId { get; set; }

    /// <summary>El pago que la originó. Uno a uno: cada pago registrado deja su documento.</summary>
    public Guid SubscriptionPaymentId { get; set; }

    /// <summary>Lo que se le enseña al taller: el correlativo propio o el autorizado.</summary>
    public string Number { get; set; } = null!;

    public DateOnly IssuedOn { get; set; }

    public string CustomerName { get; set; } = null!;
    public string? CustomerLegalName { get; set; }
    public string? CustomerTaxId { get; set; }

    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }

    /// <summary>Cómo se entendía el precio al emitirla. Se congela con el documento.</summary>
    public bool PricesIncludedTax { get; set; }

    public string Currency { get; set; } = "HNL";

    public string? Note { get; set; }

    // --- Lo fiscal, null mientras no haya CAI --------------------------------------------
    public Guid? PlatformFiscalRangeId { get; set; }
    public string? FiscalCai { get; set; }
    public string? FiscalRangeText { get; set; }
    public DateTimeOffset? FiscalIssueDeadline { get; set; }

    /// <summary>Con CAI es una factura; sin él, un comprobante con numeración propia.</summary>
    public bool IsFiscal => FiscalCai is not null;

    // --- Anulación: no se borra, se anula y queda dicho por qué ---------------------------
    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }

    public Guid? IssuedByUserId { get; set; }

    public ICollection<SubscriptionInvoiceLine> Lines { get; set; } = new List<SubscriptionInvoiceLine>();
}

/// <summary>
/// Un concepto de la factura: la mensualidad, una sucursal adicional, la puesta en marcha.
/// </summary>
public class SubscriptionInvoiceLine : AuditableEntity
{
    public Guid SubscriptionInvoiceId { get; set; }

    public string Description { get; set; } = null!;

    public decimal Quantity { get; set; } = 1;

    /// <summary>Precio unitario sin impuesto, que es como cobramos la mensualidad.</summary>
    public decimal UnitPrice { get; set; }

    public decimal Total { get; set; }

    public SubscriptionInvoice SubscriptionInvoice { get; set; } = null!;
}

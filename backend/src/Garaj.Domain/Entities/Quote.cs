using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Cotización enviada al cliente. Se comparte por WhatsApp como un link público basado en
/// <see cref="PublicToken"/>, así que el cliente la abre y la aprueba sin necesidad de login.
/// </summary>
public class Quote : TenantEntity, IBranchEntity
{
    public Guid BranchId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid? ServiceRequestId { get; set; }
    public Guid? WorkOrderId { get; set; }

    /// <summary>Correlativo legible por sucursal, ej. "COT-SUR-000045".</summary>
    public string Number { get; set; } = null!;

    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;

    /// <summary>
    /// Token del link público (`/q/{token}`). Aleatorio e imposible de adivinar: es la única
    /// credencial que protege la cotización, por eso no se reutiliza entre cotizaciones.
    /// </summary>
    public Guid PublicToken { get; set; } = Guid.NewGuid();

    public DateTimeOffset? ValidUntil { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }

    /// <summary>Comentario del cliente al aprobar o rechazar desde el link público.</summary>
    public string? CustomerResponseNote { get; set; }

    public string? Notes { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }

    public Branch Branch { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public ICollection<QuoteLine> Lines { get; set; } = new List<QuoteLine>();
}

using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// El taller como negocio. Es la raíz de aislamiento: todo lo demás cuelga de aquí.
/// No implementa ITenantEntity porque *es* el tenant.
/// </summary>
public class Tenant : AuditableEntity
{
    public string Name { get; set; } = null!;
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? LogoStorageKey { get; set; }

    /// <summary>Código de país por defecto para armar los links de WhatsApp (ej. "504" = Honduras).</summary>
    public string DefaultPhoneCountryCode { get; set; } = "504";

    /// <summary>Moneda ISO-4217 usada en cotizaciones y ventas.</summary>
    public string Currency { get; set; } = "HNL";

    /// <summary>Porcentaje de impuesto por defecto (ej. 15.00 = 15%).</summary>
    public decimal DefaultTaxRate { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}

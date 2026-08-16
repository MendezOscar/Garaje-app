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

    /// <summary>
    /// Dirección de la casa matriz. El régimen de facturación la pide aparte de la del
    /// establecimiento que emite, que es la de la sucursal.
    /// </summary>
    public string? Address { get; set; }

    public string? LogoStorageKey { get; set; }

    /// <summary>Código de país por defecto para armar los links de WhatsApp (ej. "504" = Honduras).</summary>
    public string DefaultPhoneCountryCode { get; set; } = "504";

    /// <summary>Moneda ISO-4217 usada en cotizaciones y ventas.</summary>
    public string Currency { get; set; } = "HNL";

    /// <summary>Porcentaje de impuesto por defecto (ej. 15.00 = 15%).</summary>
    public decimal DefaultTaxRate { get; set; }

    /// <summary>
    /// Suspensión manual: el corte definitivo, decidido por nosotros. A diferencia del vencimiento
    /// —que solo deja el taller en modo lectura— un taller suspendido no puede ni iniciar sesión.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // ---------- Suscripción ----------
    // Lo que el taller nos paga a nosotros. Vive aquí y no en una entidad aparte porque es
    // parte de lo que define al taller como cliente nuestro, y porque así una sola lectura
    // por clave primaria resuelve si puede trabajar. El historial de pagos sí va aparte.

    /// <summary>Nombre del plan contratado, tal como se le ofreció. Informativo.</summary>
    public string? PlanName { get; set; }

    /// <summary>Cuota mensual acordada, en la moneda del taller.</summary>
    public decimal MonthlyFee { get; set; }

    /// <summary>
    /// Hasta qué día está pagada la suscripción. Es el corazón de todo: de aquí salen el aviso
    /// que ve el Dueño y el bloqueo.
    ///
    /// Es <c>DateOnly</c> y no un instante a propósito: una fecha de pago es un día, no una
    /// hora. El «hoy» contra el que se compara es el de Honduras y no el del servidor —ver
    /// <c>DateTimeProviderExtensions.Today</c>—, porque el día UTC cambia a las 6 de la tarde
    /// de acá y le cortaría el sistema al taller justo mientras cierra el día.
    /// </summary>
    public DateOnly? PaidThrough { get; set; }

    /// <summary>
    /// Días de tolerancia después de <see cref="PaidThrough"/> en los que el taller sigue
    /// trabajando con aviso. Por taller, no global: al primer cliente se le puede dar más.
    /// </summary>
    public int GraceDays { get; set; } = 5;

    /// <summary>
    /// Acuerdo de pago: hasta este día el taller trabaja aunque esté vencido. Es la válvula para
    /// el que quedó debiendo y va a pagar; cortarle sería perder un cliente que se recupera.
    /// </summary>
    public DateOnly? UnblockedThrough { get; set; }

    /// <summary>Por qué se le dio el acuerdo. Sin esto, en dos meses nadie se acuerda.</summary>
    public string? UnblockNote { get; set; }

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}

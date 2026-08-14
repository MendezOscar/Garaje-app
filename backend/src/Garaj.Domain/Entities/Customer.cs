using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Cliente del taller. Pertenece al tenant, no a una sucursal: puede dejar su vehículo
/// en cualquiera. <see cref="AppUserId"/> es null cuando no tiene acceso a la app.
/// </summary>
public class Customer : TenantEntity
{
    public string FullName { get; set; } = null!;

    /// <summary>En formato E.164 sin '+' (ej. "50498881111"). Es la clave del link de WhatsApp.</summary>
    public string Phone { get; set; } = null!;

    public string? Email { get; set; }

    /// <summary>Número de identidad. No sirve para la factura: para eso está el RTN.</summary>
    public string? DocumentId { get; set; }

    /// <summary>RTN, para la factura con CAI. Solo lo tienen los clientes que la piden.</summary>
    public string? TaxId { get; set; }

    /// <summary>
    /// A nombre de quién sale la factura, cuando no es el mismo que trae el carro: el RTN
    /// suele ser el de la empresa donde trabaja. Null significa que va a su propio nombre.
    /// </summary>
    public string? BillingName { get; set; }

    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Usuario de login asociado, si el cliente usa la app.</summary>
    public Guid? AppUserId { get; set; }

    /// <summary>
    /// Token del enlace de su estado de cuenta. Igual que en la cotización, el token **es** la
    /// credencial: quien tenga el enlace ve lo que debe, sin cuenta ni contraseña. Es lo que
    /// permite mandárselo por WhatsApp a alguien que nunca va a instalar la app.
    ///
    /// Solo expone su nombre y sus facturas con saldo. Aun así, si un enlace se filtra, se
    /// corta cambiando este token.
    /// </summary>
    public Guid PublicToken { get; set; } = Guid.NewGuid();

    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
}

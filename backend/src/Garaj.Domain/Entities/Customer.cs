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
    public string? DocumentId { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Usuario de login asociado, si el cliente usa la app.</summary>
    public Guid? AppUserId { get; set; }

    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
}

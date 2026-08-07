using Garaj.Domain.Common;
using Garaj.Domain.Entities;

namespace Garaj.Infrastructure.Identity;

/// <summary>
/// Acceso de un usuario a una sucursal. El Dueño no necesita filas aquí: ve todo el tenant.
/// Los técnicos sí, y es lo que limita qué órdenes aparecen en su bandeja.
/// </summary>
public class UserBranch : ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid BranchId { get; set; }

    public AppUser User { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
}

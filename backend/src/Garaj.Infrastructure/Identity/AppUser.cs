using Garaj.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace Garaj.Infrastructure.Identity;

/// <summary>
/// Usuario de login. Vive en Infrastructure porque depende de ASP.NET Identity; el dominio
/// se refiere a los usuarios solo por Guid para no arrastrar esa dependencia.
/// </summary>
/// <remarks>
/// A diferencia del resto de entidades, <b>no</b> lleva global query filter por tenant: el
/// login tiene que poder encontrar al usuario por email antes de saber a qué taller pertenece.
/// Para listar usuarios use <c>GarajDbContext.UsersInTenant</c>, que sí filtra.
/// </remarks>
public class AppUser : IdentityUser<Guid>, ITenantEntity
{
    public Guid TenantId { get; set; }

    public string FullName { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    /// <summary>Cliente asociado. Solo tiene valor en usuarios con rol Customer.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Token de Firebase para push. Se actualiza en cada login del móvil.</summary>
    public string? PushToken { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<UserBranch> Branches { get; set; } = new List<UserBranch>();
}

public class AppRole : IdentityRole<Guid>
{
    public AppRole() { }

    public AppRole(string name) : base(name) { }
}

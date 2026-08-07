using Garaj.Application.Abstractions;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Implementación scoped: una instancia por petición. Es un contenedor mutable a propósito,
/// para que el middleware la rellene al inicio y el resto de la petición solo la lea.
/// No depende de ASP.NET, así que el seeder y los tests la usan igual.
/// </summary>
public class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Role { get; private set; }
    public IReadOnlyCollection<Guid> BranchIds { get; private set; } = [];
    public Guid? CustomerId { get; private set; }

    public bool IsAuthenticated => UserId is not null;

    public bool BypassTenantFilter { get; set; }

    public void Initialize(
        Guid? tenantId,
        Guid? userId,
        string? role,
        IReadOnlyCollection<Guid> branchIds,
        Guid? customerId)
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        BranchIds = branchIds;
        CustomerId = customerId;
    }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;
}

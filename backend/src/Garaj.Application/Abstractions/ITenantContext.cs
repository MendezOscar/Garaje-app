namespace Garaj.Application.Abstractions;

/// <summary>
/// Identidad de la petición en curso, resuelta desde los claims del JWT.
/// El <c>GarajDbContext</c> depende de esto para aplicar el filtro por tenant, así que si
/// una petición autenticada llega sin TenantId es un error de configuración, no un caso normal.
/// </summary>
public interface ITenantContext
{
    /// <summary>Taller de la petición. Null en endpoints públicos o anónimos.</summary>
    Guid? TenantId { get; }

    Guid? UserId { get; }

    string? Role { get; }

    /// <summary>Sucursales visibles para este usuario. Vacío para el Dueño, que las ve todas.</summary>
    IReadOnlyCollection<Guid> BranchIds { get; }

    /// <summary>Id del cliente asociado, solo en perfil Cliente.</summary>
    Guid? CustomerId { get; }

    bool IsAuthenticated { get; }

    /// <summary>
    /// Desactiva el filtro por tenant para la operación en curso (seeder, migraciones, jobs
    /// de mantenimiento). Nunca debe usarse dentro de una petición HTTP.
    /// </summary>
    bool BypassTenantFilter { get; set; }

    /// <summary>
    /// Carga la identidad de la petición. La llama el middleware a partir de los claims del JWT,
    /// y el seeder o los tests para simular un usuario.
    /// </summary>
    void Initialize(
        Guid? tenantId,
        Guid? userId,
        string? role,
        IReadOnlyCollection<Guid> branchIds,
        Guid? customerId);

    /// <summary>Fija solo el tenant, fuera de una petición HTTP (seeder, jobs de mantenimiento).</summary>
    void SetTenant(Guid tenantId);
}

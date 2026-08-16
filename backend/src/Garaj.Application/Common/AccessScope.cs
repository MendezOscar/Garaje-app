using Garaj.Application.Abstractions;

namespace Garaj.Application.Common;

/// <summary>
/// Qué subconjunto de los datos del taller puede ver el usuario de la petición.
/// </summary>
/// <remarks>
/// El global query filter del DbContext resuelve el aislamiento <b>entre</b> talleres.
/// Esto resuelve el aislamiento <b>dentro</b> de un taller, que es otra cosa: un técnico no
/// debe ver las órdenes de otro, y un cliente solo las de sus propios vehículos.
/// Se centraliza aquí para que la regla no se reescriba —y se olvide— en cada servicio.
/// </remarks>
public sealed class AccessScope
{
    private AccessScope(string role, Guid userId, Guid? customerId, IReadOnlyCollection<Guid> branchIds)
    {
        Role = role;
        UserId = userId;
        CustomerId = customerId;
        BranchIds = branchIds;
    }

    public string Role { get; }
    public Guid UserId { get; }
    public Guid? CustomerId { get; }

    /// <summary>Sucursales asignadas. Vacío en el Dueño, que ve todas las del taller.</summary>
    public IReadOnlyCollection<Guid> BranchIds { get; }

    public bool IsOwner => Role == AppRoles.Owner;
    public bool IsTechnician => Role == AppRoles.Technician;
    public bool IsCustomer => Role == AppRoles.Customer;

    /// <summary>Nosotros. No pertenece a ningún taller y no ve los datos de ninguno.</summary>
    public bool IsPlatform => Role == AppRoles.Platform;

    /// <summary>Construye el alcance desde la petición en curso, o falla si no hay sesión.</summary>
    public static AccessScope From(ITenantContext context)
    {
        if (context.UserId is not { } userId || string.IsNullOrEmpty(context.Role))
            throw new UnauthorizedException("La petición no está autenticada.");

        // Un usuario con perfil Cliente sin CustomerId no puede resolver "lo suyo": es un
        // dato mal creado, y dejarlo pasar significaría mostrarle datos de otros.
        if (context.Role == AppRoles.Customer && context.CustomerId is null)
            throw new ForbiddenException("El usuario no tiene un cliente asociado.");

        return new AccessScope(context.Role, userId, context.CustomerId, context.BranchIds);
    }

    /// <summary>Lanza si el usuario no puede operar sobre la sucursal indicada.</summary>
    public void EnsureBranchAllowed(Guid branchId)
    {
        if (IsOwner || BranchIds.Contains(branchId)) return;

        throw new NotFoundException();
    }

    /// <summary>Solo el Dueño administra catálogos y configuración del taller.</summary>
    public void EnsureOwner()
    {
        if (!IsOwner) throw new ForbiddenException("Solo el Dueño puede realizar esta operación.");
    }

    /// <summary>
    /// Solo nosotros damos de alta talleres y les movemos la mensualidad. La policy de la API ya
    /// lo exige; se repite aquí porque es la operación más delicada del sistema y no debe
    /// depender de que un controlador nuevo se acuerde de ponerse el atributo.
    /// </summary>
    public void EnsurePlatform()
    {
        if (!IsPlatform) throw new ForbiddenException("Solo GarajApp puede realizar esta operación.");
    }
}

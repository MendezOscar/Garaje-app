namespace Garaj.Application.Common;

/// <summary>Los tres perfiles del sistema. Constantes, no enum, porque ASP.NET Identity trabaja con strings.</summary>
public static class AppRoles
{
    /// <summary>Dueño: recibe requerimientos, asigna tareas, cotiza y ve las ventas.</summary>
    public const string Owner = "Owner";

    /// <summary>Técnico: solo sus asignaciones, fotos del proceso y cambios de estado.</summary>
    public const string Technician = "Technician";

    /// <summary>Cliente: crea requerimientos y sigue el proceso de sus propios vehículos.</summary>
    public const string Customer = "Customer";

    /// <summary>
    /// Nosotros, los dueños de GarajApp: damos de alta talleres y les cobramos la mensualidad.
    /// No pertenece a ningún taller y **no puede leer datos de ninguno**: su token va sin taller,
    /// así que el global query filter le devuelve cero filas de órdenes, clientes e inventario.
    /// Solo se crea por línea de comandos; desde el panel no se puede crear otro.
    /// </summary>
    public const string Platform = "Platform";

    public static readonly string[] All = [Owner, Technician, Customer, Platform];
}

/// <summary>Claims propios del token, además de los estándar de JWT.</summary>
public static class AppClaims
{
    public const string TenantId = "tenant_id";

    /// <summary>Sucursales a las que el usuario tiene acceso, separadas por coma. El Dueño las tiene todas.</summary>
    public const string BranchIds = "branch_ids";

    /// <summary>Id del <c>Customer</c> asociado, presente solo en tokens de perfil Cliente.</summary>
    public const string CustomerId = "customer_id";
}

/// <summary>Nombres de las policies de autorización registradas en la API.</summary>
public static class AppPolicies
{
    public const string OwnerOnly = "OwnerOnly";
    public const string PlatformOnly = "PlatformOnly";
    public const string StaffOnly = "StaffOnly";
    public const string TechnicianOrOwner = "TechnicianOrOwner";
}

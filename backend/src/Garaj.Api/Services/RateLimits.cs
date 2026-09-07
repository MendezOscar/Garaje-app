namespace Garaj.Api.Services;

/// <summary>
/// Nombres de las políticas de límite de tasa. Constantes y no cadenas sueltas: escribir mal el
/// nombre en el atributo no falla al compilar, falla en producción dejando el endpoint sin
/// límite.
/// </summary>
public static class RateLimits
{
    /// <summary>Login y refresco: lo que un desconocido puede intentar a fuerza bruta.</summary>
    public const string Login = "login";

    /// <summary>El enlace de cotización que le llega al cliente por WhatsApp, sin sesión.</summary>
    public const string Publico = "publico";
}

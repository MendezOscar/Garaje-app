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

    /// <summary>
    /// Generar un PDF es lo más caro que hace el servidor: varias consultas, bajar las fotos
    /// del bucket y renderizar. Va aparte del resto de lo público, que es barato.
    /// </summary>
    public const string Documento = "documento";

    /// <summary>
    /// Cargar un catálogo desde Excel: se lee un libro entero en memoria y se escriben miles
    /// de filas. Es lo más caro que puede pedir alguien con sesión, y se hace dos veces por
    /// archivo —la vista previa y el guardado—, así que el cupo da para corregir y reintentar
    /// sin dar para repetirlo en bucle.
    /// </summary>
    public const string Carga = "carga";
}

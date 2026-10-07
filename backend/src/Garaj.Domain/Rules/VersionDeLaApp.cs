namespace Garaj.Domain.Rules;

/// <summary>Qué hacer con la versión que trae el teléfono que está llamando.</summary>
public enum ExigenciaDeVersion
{
    /// <summary>Al día, o no se sabe qué versión es: pasa.</summary>
    Pasa = 0,

    /// <summary>Hay una más nueva. Se le avisa, pero puede seguir trabajando.</summary>
    Avisar = 1,

    /// <summary>Demasiado vieja para esta API: no se le deja seguir hasta que actualice.</summary>
    Bloquear = 2
}

/// <summary>
/// El portón de versión: hasta qué versión de la app se le sigue hablando.
/// </summary>
/// <remarks>
/// Ninguna tienda sabe forzar una actualización —Google tiene la suya y Apple no tiene
/// ninguna—, así que lo decide el servidor: el teléfono manda su versión en cada petición y
/// aquí se dice si pasa, si se le avisa o si se le para.
///
/// Los dos números viven en la configuración y no en el código: el día que una migración
/// rompa la compatibilidad, se sube el mínimo en Render y los teléfonos viejos se enteran en
/// la siguiente petición, sin publicar nada.
///
/// **En cero no estorba a nadie**, que es como viene: bloquear por omisión dejaría al taller
/// sin app el día que alguien olvide poner el número.
/// </remarks>
public static class VersionDeLaApp
{
    /// <summary>
    /// El número de compilación que viene en la cabecera `X-Garaj-Cliente`, con la forma
    /// `GarajApp/1.3.0+12`. Null cuando no viene o no se entiende: el panel web no la manda,
    /// y a lo que no se identifica no se le exige nada.
    /// </summary>
    public static int? LeerCompilacion(string? cabecera)
    {
        if (string.IsNullOrWhiteSpace(cabecera)) return null;

        var mas = cabecera.LastIndexOf('+');
        if (mas < 0 || mas == cabecera.Length - 1) return null;

        return int.TryParse(cabecera[(mas + 1)..].Trim(), out var build) && build > 0
            ? build
            : null;
    }

    public static ExigenciaDeVersion Decidir(int? compilacion, int minima, int recomendada)
    {
        // Sin versión no hay nada que exigir: es el panel web, una prueba o algo que no es la
        // app. Bloquear aquí sería tumbar el panel por una cabecera que nunca mandó.
        if (compilacion is not { } build) return ExigenciaDeVersion.Pasa;

        if (minima > 0 && build < minima) return ExigenciaDeVersion.Bloquear;
        if (recomendada > 0 && build < recomendada) return ExigenciaDeVersion.Avisar;

        return ExigenciaDeVersion.Pasa;
    }

    /// <summary>Lo mismo, leyendo la cabecera de una vez.</summary>
    public static ExigenciaDeVersion DecidirPorCabecera(string? cabecera, int minima, int recomendada) =>
        Decidir(LeerCompilacion(cabecera), minima, recomendada);
}

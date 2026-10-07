namespace Garaj.Api;

/// <summary>
/// Hasta qué versión de la app se le sigue hablando. Vive en la configuración —variables de
/// Render— para poder subir el mínimo sin publicar nada en las tiendas.
/// </summary>
/// <remarks>
/// Los dos números son **compilaciones**, no versiones: el `+12` de `1.3.0+12`. Es el único
/// número que crece siempre y que las dos tiendas obligan a subir en cada entrega.
///
/// En cero —como vienen— no estorban a nadie. Es deliberado: dejar al taller sin app por un
/// número que alguien olvidó poner sería peor que cualquier incompatibilidad.
/// </remarks>
public class AppVersionOptions
{
    public const string SectionName = "AppVersion";

    /// <summary>Debajo de esto la app no deja trabajar hasta actualizar.</summary>
    public int MinimumBuild { get; set; }

    /// <summary>Debajo de esto se le avisa, pero puede seguir.</summary>
    public int RecommendedBuild { get; set; }

    /// <summary>Por qué hay que actualizar, con las palabras del taller. Opcional.</summary>
    public string? Message { get; set; }

    public string IosUrl { get; set; } = "https://apps.apple.com/app/id6805656010";

    public string AndroidUrl { get; set; } =
        "https://play.google.com/store/apps/details?id=com.garaj.garaj_app";
}

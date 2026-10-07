using Garaj.Application.Common;
using Garaj.Domain.Rules;
using Microsoft.Extensions.Options;

namespace Garaj.Api.Middleware;

/// <summary>
/// Para a la app demasiado vieja para esta API.
/// </summary>
/// <remarks>
/// Ninguna tienda sabe forzar una actualización, así que la fuerza el servidor: la app manda
/// su versión en cada petición y, por debajo del mínimo, aquí se le responde 426 con el
/// mensaje ya escrito. La app lo entiende y enseña la pantalla que no deja pasar.
///
/// Corre antes de todo lo que toca la base: si la app ya no sirve, no vale la pena ni resolver
/// de qué taller es la petición.
///
/// **Dos cosas pasan siempre**, y no son concesiones: lo que no manda versión —el panel web,
/// las páginas públicas de la cotización y el seguimiento, el `/health` de Render— porque a lo
/// que no se identifica no se le exige nada; y `/api/app/version`, porque es justo lo que la
/// app bloqueada necesita preguntar para saber a qué tienda mandar al usuario.
/// </remarks>
public class AppVersionGuardMiddleware(RequestDelegate next, IOptions<AppVersionOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var o = options.Value;

        if (o.MinimumBuild > 0 && !EsSiempreAbierto(context.Request.Path))
        {
            var cabecera = context.Request.Headers["X-Garaj-Cliente"].FirstOrDefault();

            if (VersionDeLaApp.DecidirPorCabecera(cabecera, o.MinimumBuild, o.RecommendedBuild)
                == ExigenciaDeVersion.Bloquear)
            {
                throw new UpgradeRequiredException(
                    o.Message ?? "Esta versión de la app ya no funciona con el sistema. "
                    + "Actualícela desde la tienda para seguir trabajando.");
            }
        }

        await next(context);
    }

    private static bool EsSiempreAbierto(PathString path) =>
        path.StartsWithSegments("/api/app")
        || path.StartsWithSegments("/public")
        || path.StartsWithSegments("/health");
}

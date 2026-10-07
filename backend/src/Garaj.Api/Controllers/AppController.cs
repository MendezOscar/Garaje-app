using Garaj.Domain.Rules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Garaj.Api.Controllers;

/// <param name="Required">
/// Qué le toca a quien está preguntando: `pasa`, `avisar` o `bloquear`. Sale de la versión que
/// mandó en su cabecera, así que el teléfono no tiene que comparar números él mismo.
/// </param>
public record AppVersionDto(
    int MinimumBuild,
    int RecommendedBuild,
    string Required,
    string? Message,
    string StoreUrl);

/// <summary>
/// Qué versión de la app hace falta. Sin autenticación: es lo primero que pregunta el
/// teléfono, y una app bloqueada tiene que poder preguntarlo aunque su sesión ya no sirva.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/app")]
public class AppController(IOptions<AppVersionOptions> options) : ControllerBase
{
    [HttpGet("version")]
    public ActionResult<AppVersionDto> Version()
    {
        var o = options.Value;
        var cabecera = Request.Headers["X-Garaj-Cliente"].FirstOrDefault();
        var exigencia = VersionDeLaApp.DecidirPorCabecera(cabecera, o.MinimumBuild, o.RecommendedBuild);

        // La tienda que le toca a quien pregunta: la app no sabe cuál es su enlace, y el que
        // se le mande tiene que abrir su propia ficha o el botón no sirve de nada.
        var android = (cabecera ?? string.Empty).Contains("Android", StringComparison.OrdinalIgnoreCase)
            || Request.Headers.UserAgent.ToString().Contains("Android", StringComparison.OrdinalIgnoreCase);

        return Ok(new AppVersionDto(
            o.MinimumBuild,
            o.RecommendedBuild,
            exigencia switch
            {
                ExigenciaDeVersion.Bloquear => "bloquear",
                ExigenciaDeVersion.Avisar => "avisar",
                _ => "pasa"
            },
            o.Message,
            android ? o.AndroidUrl : o.IosUrl));
    }
}

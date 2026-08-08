using Garaj.Application.Common;
using Garaj.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

/// <param name="Confirm">Debe ser exactamente <c>BORRAR Y SEMBRAR</c>.</param>
/// <param name="Weeks">Semanas de historia a generar. Entre 1 y 26.</param>
public record SeedDemoRequest(string Confirm, int Weeks = 6);

/// <summary>
/// Siembra la base de demostración. <b>Borra todo lo que haya antes.</b>
/// </summary>
/// <remarks>
/// Tres cerrojos, porque la operación no tiene vuelta atrás: hay que ser Dueño, la
/// configuración <c>Demo:AllowSeeding</c> tiene que estar encendida —lo que obliga a entrar
/// al panel del servidor a propósito— y el cuerpo tiene que traer la frase de confirmación.
///
/// Antes de que el taller empiece a operar de verdad hay que <b>quitar esa variable</b>. Con
/// ella apagada este endpoint responde 404 y no existe para nadie.
/// </remarks>
[ApiController]
[Authorize(Policy = AppPolicies.OwnerOnly)]
[Route("api/demo")]
public class DemoController(DemoSeeder seeder, IConfiguration configuration) : ControllerBase
{
    [HttpPost("seed")]
    public async Task<ActionResult<DemoSeedSummary>> Seed(SeedDemoRequest request, CancellationToken ct)
    {
        // 404 y no 403: si la siembra está apagada, lo correcto es que el endpoint no exista.
        if (!configuration.GetValue("Demo:AllowSeeding", false))
            return NotFound();

        if (request.Confirm != "BORRAR Y SEMBRAR")
            throw new AppException(
                "Para sembrar la demostración hay que confirmar con la frase «BORRAR Y SEMBRAR». " +
                "La operación borra todos los datos del sistema.");

        return Ok(await seeder.RunAsync(request.Weeks, ct));
    }
}

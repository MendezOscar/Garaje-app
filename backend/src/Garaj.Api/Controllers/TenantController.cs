using Garaj.Application.Common;
using Garaj.Application.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

/// <summary>
/// La ficha del taller: lo que se imprime en la cotización y en la factura del cliente final.
/// Solo el Dueño la toca.
/// </summary>
[ApiController]
[Authorize(Policy = AppPolicies.OwnerOnly)]
[Route("api/tenant")]
public class TenantController(ITenantService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TenantSettingsDto>> Get(CancellationToken ct)
        => Ok(await service.GetAsync(ct));

    [HttpPut]
    public async Task<ActionResult<TenantSettingsDto>> Update(
        UpdateTenantRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(request, ct));

    /// <summary>
    /// El logo sí pasa por la API, al revés que las fotos de las órdenes: es un archivo
    /// pequeño que se sube una vez, y de paso el servidor lo valida y lo normaliza a PNG.
    /// </summary>
    [HttpPost("logo")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<ActionResult<TenantSettingsDto>> SetLogo(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new AppException("Adjunte el archivo del logo.");

        await using var stream = file.OpenReadStream();
        return Ok(await service.SetLogoAsync(stream, file.ContentType, ct));
    }

    [HttpDelete("logo")]
    public async Task<ActionResult<TenantSettingsDto>> RemoveLogo(CancellationToken ct)
        => Ok(await service.RemoveLogoAsync(ct));
}

/// <summary>
/// El logo servido por la API, sin sesión.
///
/// Es anónimo a propósito: una etiqueta &lt;img&gt; no manda cabecera de autorización, y una
/// URL prefirmada caduca a los 15 minutos, así que dejaría el logo roto en un panel abierto
/// toda la tarde. Ese logo, además, ya viaja en cada cotización que el taller manda por
/// WhatsApp: no es un dato reservado. El guid no abre nada más.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/tenants")]
public class TenantLogoController(ITenantService service) : ControllerBase
{
    [HttpGet("{tenantId:guid}/logo")]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        var logo = await service.GetLogoAsync(tenantId, ct);
        if (logo is null) return NotFound();

        // Una hora de caché: el logo cambia una vez en la vida del taller y esta ruta la
        // piden todas las pantallas del panel y de la app.
        Response.Headers.CacheControl = "public, max-age=3600";

        return File(logo.Bytes, logo.ContentType);
    }
}

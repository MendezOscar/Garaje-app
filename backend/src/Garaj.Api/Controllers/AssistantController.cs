using Garaj.Application.Assistant;
using Garaj.Application.Common;
using Garaj.Api.Services;
using Garaj.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Garaj.Api.Controllers;

/// <summary>
/// La lectura del negocio: qué pasó con las ventas, el inventario y los técnicos.
/// </summary>
/// <remarks>
/// Solo el Dueño. Es un reporte sobre su negocio y sobre su gente; el Encargado ve su sucursal
/// en los reportes de siempre, no el juicio del conjunto.
/// </remarks>
[ApiController]
[Authorize(Policy = AppPolicies.OwnerOnly)]
[Route("api/assistant/business-readings")]
public class BusinessReadingsController(IBusinessReadingService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BusinessReadingListItemDto>>> List(
        CancellationToken ct)
        => Ok(await service.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BusinessReadingDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    /// <summary>
    /// Arma la lectura del periodo cerrado —la semana pasada o el mes pasado—. Si ya existe,
    /// devuelve la que hay sin volver a llamar al modelo.
    /// </summary>
    /// <remarks>
    /// Sin volumen responde 200 con el motivo en <c>message</c> y sin contenido: un taller con
    /// dos órdenes en la semana no tiene lectura que dar, y decirlo es la respuesta correcta.
    /// </remarks>
    [HttpPost]
    [EnableRateLimiting(RateLimits.Asistente)]
    public async Task<ActionResult<BusinessReadingDto>> Generate(
        [FromQuery] BusinessReadingKind kind, CancellationToken ct)
        => Ok(await service.GenerateAsync(kind, ct));

    [HttpGet("{id:guid}/pdf")]
    [EnableRateLimiting(RateLimits.Documento)]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
        => File(await service.PdfAsync(id, ct), "application/pdf", $"lectura-{id:N}.pdf");
}

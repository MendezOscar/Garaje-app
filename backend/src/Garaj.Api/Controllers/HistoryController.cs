using Garaj.Application.Common;
using Garaj.Application.Quotes;
using Garaj.Application.WorkOrders;
using Garaj.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Garaj.Api.Controllers;

/// <summary>
/// Mandarle al cliente el historial de lo que el taller le ha hecho a lo suyo.
/// </summary>
/// <remarks>
/// Va bajo el cliente y no bajo el vehículo porque el enlace es suyo —el mismo token de su
/// estado de cuenta— y le enseña todo lo que trae: el carro, la moto de la muchacha y el
/// equipo, sin un enlace por cada uno.
/// </remarks>
[ApiController]
[Authorize(Policy = AppPolicies.ManagementOnly)]
[Route("api/customers/{customerId:guid}/history")]
public class CustomerHistoryController(IWorkOrderService service) : ControllerBase
{
    /// <summary>El enlace de WhatsApp con el mensaje ya escrito. 400 si no tiene visitas.</summary>
    [HttpGet("whatsapp")]
    public async Task<ActionResult<WhatsAppLinkDto>> WhatsApp(Guid customerId, CancellationToken ct)
        => Ok(await service.HistoryLinkAsync(customerId, ct));
}

/// <summary>
/// El historial visto por el cliente desde el enlace de WhatsApp. Sin autenticación: el token
/// aleatorio de la URL es la única credencial, igual que en la cotización.
/// </summary>
/// <remarks>
/// Expone qué se le hizo y cuándo, y lo que se le facturó por cada visita. Nada más: ni el
/// costo del taller, ni quién lo atendió, ni un id con el que llegar a otra parte de la API.
/// El logo lo sirve la ruta del estado de cuenta, que usa este mismo token.
/// </remarks>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimits.Publico)]
[Route("public/history")]
public class PublicHistoryController(IWorkOrderService service) : ControllerBase
{
    [HttpGet("{token:guid}")]
    public async Task<ActionResult<CustomerHistoryDto>> Get(Guid token, CancellationToken ct)
        => Ok(await service.HistoryPublicAsync(token, ct));
}

using Garaj.Application.Common;
using Garaj.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

/// <summary>
/// La campana. Todo lo de aquí es del usuario de la petición: no hay forma de leer ni de
/// marcar los avisos de otro, ni siquiera siendo Dueño.
/// </summary>
[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController(INotificationService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationDto>>> List(
        [FromQuery] NotificationQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    /// <summary>Lo que pinta el globo rojo. Se consulta seguido, así que devuelve solo el número.</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> UnreadCount(CancellationToken ct)
        => Ok(new { unread = await service.UnreadCountAsync(ct) });

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await service.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<object>> MarkAllRead(CancellationToken ct)
        => Ok(new { marked = await service.MarkAllReadAsync(ct) });

    /// <summary>
    /// Registra el dispositivo para recibir push. La app lo llama en cada arranque: el token
    /// de FCM caduca y se renueva solo, y si no se reenvía el aparato deja de recibir avisos.
    /// </summary>
    [HttpPost("devices")]
    public async Task<IActionResult> RegisterDevice(RegisterDeviceRequest request, CancellationToken ct)
    {
        await service.RegisterDeviceAsync(request, ct);
        return NoContent();
    }

    /// <summary>Al cerrar sesión, para que el siguiente usuario del aparato no reciba lo ajeno.</summary>
    [HttpDelete("devices/{token}")]
    public async Task<IActionResult> UnregisterDevice(string token, CancellationToken ct)
    {
        await service.UnregisterDeviceAsync(token, ct);
        return NoContent();
    }
}

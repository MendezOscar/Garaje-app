using Garaj.Application.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

/// <summary>
/// Evidencia fotográfica. El binario no pasa por la API: el cliente pide una URL prefirmada,
/// sube directo al bucket y confirma. Así una subida lenta desde el taller no ocupa un hilo
/// del servidor, y el móvil puede reintentar sin repetir la petición completa.
/// </summary>
[ApiController]
[Authorize]
[Route("api/media")]
public class MediaController(IMediaService service) : ControllerBase
{
    /// <summary>Paso 1: reserva el adjunto y devuelve a dónde subir el archivo.</summary>
    [HttpPost("upload-url")]
    public async Task<ActionResult<PresignedUploadDto>> CreateUploadUrl(
        CreateUploadRequest request, CancellationToken ct)
        => Ok(await service.CreateUploadUrlAsync(request, ct));

    /// <summary>
    /// Paso 2: publica la foto. Devuelve 409 si el archivo aún no llegó al bucket, para que
    /// el cliente reintente sin pedir otra URL.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<MediaAttachmentDto>> Confirm(Guid id, CancellationToken ct)
        => Ok(await service.ConfirmAsync(id, ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MediaAttachmentDto>>> List(
        [FromQuery] MediaQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    /// <summary>Galería del detalle de orden: sus fotos y las de sus pasos, ordenadas por toma.</summary>
    [HttpGet("work-order/{workOrderId:guid}")]
    public async Task<ActionResult<IReadOnlyList<MediaAttachmentDto>>> ListForWorkOrder(
        Guid workOrderId, CancellationToken ct)
        => Ok(await service.ListForWorkOrderAsync(workOrderId, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}

using Garaj.Application.VehicleScans;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

/// <summary>
/// Las lecturas del adaptador OBD2: los códigos que reportó el vehículo, guardados uno por
/// uno para poder buscarlos después.
/// </summary>
/// <remarks>
/// Es el camino hermano del informe en PDF de la tableta de diagnóstico, que entra por
/// <c>api/media</c> como un adjunto cualquiera. La diferencia no es el aparato sino el
/// formato: aquel es un documento que se enseña, este es dato que se consulta.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/vehicle-scans")]
public class VehicleScansController(IVehicleScanService service) : ControllerBase
{
    /// <summary>Guarda la lectura y deja su informe adjunto a la orden.</summary>
    [HttpPost]
    public async Task<ActionResult<VehicleScanDto>> Create(
        CreateVehicleScanRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(request, ct));

    [HttpGet("work-order/{workOrderId:guid}")]
    public async Task<ActionResult<IReadOnlyList<VehicleScanDto>>> ListForWorkOrder(
        Guid workOrderId, CancellationToken ct)
        => Ok(await service.ListForWorkOrderAsync(workOrderId, ct));

    /// <summary>El historial del vehículo: qué ha reportado cada vez que entró al taller.</summary>
    [HttpGet("vehicle/{vehicleId:guid}")]
    public async Task<ActionResult<IReadOnlyList<VehicleScanDto>>> ListForVehicle(
        Guid vehicleId, CancellationToken ct)
        => Ok(await service.ListForVehicleAsync(vehicleId, ct));

    /// <summary>Dónde ha salido un código en el taller. Admite el principio: «P03».</summary>
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<VehicleScanHitDto>>> Search(
        [FromQuery] string code, CancellationToken ct)
        => Ok(await service.SearchByCodeAsync(code, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}

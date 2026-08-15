using Garaj.Application.Common;
using Garaj.Application.WorkOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

/// <summary>
/// Trabajos frecuentes: el cambio de aceite, las pastillas de adelante, lo que el taller repite
/// cientos de veces al año, guardado con sus pasos y sus repuestos.
/// </summary>
/// <remarks>
/// Es catálogo del taller, así que lo administra el Dueño; el Técnico solo los lista para poder
/// aplicarlos, que es lo que hace desde el patio.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/job-templates")]
public class JobTemplatesController(IJobTemplateService service) : ControllerBase
{
    /// <summary>Los trabajos frecuentes, el más usado primero.</summary>
    [HttpGet]
    [Authorize(Policy = AppPolicies.TechnicianOrOwner)]
    public async Task<ActionResult<IReadOnlyList<JobTemplateDto>>> List(
        [FromQuery] bool includeInactive, CancellationToken ct)
        => Ok(await service.ListAsync(includeInactive, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppPolicies.TechnicianOrOwner)]
    public async Task<ActionResult<JobTemplateDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<JobTemplateDto>> Create(
        SaveJobTemplateRequest request, CancellationToken ct)
    {
        var template = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = template.Id }, template);
    }

    /// <summary>
    /// Guarda una orden ya hecha como trabajo frecuente. Es el camino principal: los pasos, sus
    /// servicios y sus repuestos ya están ahí y ya están bien, porque salieron de un trabajo
    /// real que se cobró.
    /// </summary>
    [HttpPost("from-work-order")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<JobTemplateDto>> CreateFromWorkOrder(
        CreateJobTemplateFromWorkOrderRequest request, CancellationToken ct)
    {
        var template = await service.CreateFromWorkOrderAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = template.Id }, template);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<JobTemplateDto>> Update(
        Guid id, SaveJobTemplateRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    /// <summary>Lo da de baja. No se borra: hay órdenes que se armaron con él.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await service.DeactivateAsync(id, ct);
        return NoContent();
    }
}

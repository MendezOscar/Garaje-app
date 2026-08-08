using Garaj.Application.Common;
using Garaj.Application.ServiceRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/service-requests")]
public class ServiceRequestsController(IServiceRequestService service) : ControllerBase
{
    /// <summary>
    /// Bandeja de entrada del Dueño. Devuelve primero los pendientes; el Cliente ve solo
    /// los de sus vehículos.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ServiceRequestDto>>> List(
        [FromQuery] ServiceRequestQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ServiceRequestDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    /// <summary>Lo crea el Cliente desde la app o el Dueño al recibir el vehículo.</summary>
    [HttpPost]
    public async Task<ActionResult<ServiceRequestDto>> Create(
        CreateServiceRequestRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Convierte el requerimiento en orden de trabajo y devuelve su id.</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<object>> Approve(
        Guid id, ApproveServiceRequestRequest request, CancellationToken ct)
    {
        var workOrderId = await service.ApproveAsync(id, request, ct);
        return Ok(new { workOrderId });
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<ServiceRequestDto>> Reject(
        Guid id, RejectServiceRequestRequest request, CancellationToken ct)
        => Ok(await service.RejectAsync(id, request, ct));
}

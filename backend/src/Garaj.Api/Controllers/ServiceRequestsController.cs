using Garaj.Application.Common;
using Garaj.Application.ServiceRequests;
using Garaj.Application.WorkOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/service-requests")]
public class ServiceRequestsController(
    IServiceRequestService service,
    IWorkOrderService workOrders) : ControllerBase
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

    /// <summary>
    /// La hoja de recepción llenada al recibir, antes de que exista la orden. Null si no se
    /// llenó: es opcional, como la de la orden.
    /// </summary>
    [HttpGet("{id:guid}/reception")]
    public async Task<ActionResult<VehicleReceptionDto>> GetReception(Guid id, CancellationToken ct)
    {
        var reception = await workOrders.GetRequestReceptionAsync(id, ct);
        return reception is null ? NoContent() : Ok(reception);
    }

    /// <summary>
    /// Guarda la hoja con el cliente delante. Firmada queda cerrada: lo que el cliente firmó
    /// no se corrige después.
    /// </summary>
    [HttpPut("{id:guid}/reception")]
    [Authorize(Policy = AppPolicies.TechnicianOrOwner)]
    public async Task<ActionResult<VehicleReceptionDto>> SaveReception(
        Guid id, SaveVehicleReceptionRequest request, CancellationToken ct)
        => Ok(await workOrders.SaveRequestReceptionAsync(id, request, ct));

    /// <summary>La firma del cliente. 404 si no firmó.</summary>
    [HttpGet("{id:guid}/reception/signature")]
    public async Task<IActionResult> ReceptionSignature(Guid id, CancellationToken ct)
    {
        var firma = await workOrders.RequestReceptionSignatureAsync(id, ct);
        return firma is null ? NotFound() : File(firma.Value.Bytes, firma.Value.ContentType);
    }

    /// <summary>Convierte el requerimiento en orden de trabajo y devuelve su id.</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = AppPolicies.ManagementOnly)]
    public async Task<ActionResult<object>> Approve(
        Guid id, ApproveServiceRequestRequest request, CancellationToken ct)
    {
        var workOrderId = await service.ApproveAsync(id, request, ct);
        return Ok(new { workOrderId });
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = AppPolicies.ManagementOnly)]
    public async Task<ActionResult<ServiceRequestDto>> Reject(
        Guid id, RejectServiceRequestRequest request, CancellationToken ct)
        => Ok(await service.RejectAsync(id, request, ct));
}

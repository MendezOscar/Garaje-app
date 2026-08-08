using Garaj.Application.Common;
using Garaj.Application.WorkOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/work-orders")]
public class WorkOrdersController(IWorkOrderService service) : ControllerBase
{
    /// <summary>
    /// Alimenta el kanban del Dueño y la bandeja del Técnico. El alcance lo resuelve el
    /// servicio: el Técnico solo recibe sus asignaciones y el Cliente solo sus vehículos.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<WorkOrderListItemDto>>> List(
        [FromQuery] WorkOrderQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkOrderDetailDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<WorkOrderDetailDto>> Create(
        CreateWorkOrderRequest request, CancellationToken ct)
    {
        var order = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.TechnicianOrOwner)]
    public async Task<ActionResult<WorkOrderDetailDto>> Update(
        Guid id, UpdateWorkOrderRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    /// <summary>Asigna o desasigna el técnico responsable. Pase null para dejarla sin asignar.</summary>
    [HttpPut("{id:guid}/assign")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<WorkOrderDetailDto>> Assign(
        Guid id, AssignTechnicianRequest request, CancellationToken ct)
        => Ok(await service.AssignAsync(id, request, ct));

    /// <summary>
    /// Cambia el estado y deja entrada en la línea de tiempo. Devuelve 409 si la transición
    /// no es válida desde el estado actual.
    /// </summary>
    [HttpPost("{id:guid}/status")]
    [Authorize(Policy = AppPolicies.TechnicianOrOwner)]
    public async Task<ActionResult<WorkOrderDetailDto>> ChangeStatus(
        Guid id, ChangeStatusRequest request, CancellationToken ct)
        => Ok(await service.ChangeStatusAsync(id, request, ct));

    [HttpPost("{id:guid}/tasks")]
    [Authorize(Policy = AppPolicies.TechnicianOrOwner)]
    public async Task<ActionResult<WorkOrderTaskDto>> AddTask(
        Guid id, SaveWorkOrderTaskRequest request, CancellationToken ct)
        => Ok(await service.AddTaskAsync(id, request, ct));

    [HttpPut("{id:guid}/tasks/{taskId:guid}")]
    [Authorize(Policy = AppPolicies.TechnicianOrOwner)]
    public async Task<ActionResult<WorkOrderTaskDto>> UpdateTask(
        Guid id, Guid taskId, SaveWorkOrderTaskRequest request, CancellationToken ct)
        => Ok(await service.UpdateTaskAsync(id, taskId, request, ct));

    /// <summary>Marca o desmarca el paso. Es la acción principal del técnico en el móvil.</summary>
    [HttpPost("{id:guid}/tasks/{taskId:guid}/complete")]
    [Authorize(Policy = AppPolicies.TechnicianOrOwner)]
    public async Task<ActionResult<WorkOrderTaskDto>> CompleteTask(
        Guid id, Guid taskId, CompleteTaskRequest request, CancellationToken ct)
        => Ok(await service.CompleteTaskAsync(id, taskId, request, ct));

    [HttpDelete("{id:guid}/tasks/{taskId:guid}")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<IActionResult> DeleteTask(Guid id, Guid taskId, CancellationToken ct)
    {
        await service.DeleteTaskAsync(id, taskId, ct);
        return NoContent();
    }
}

using Garaj.Application.Common;
using Garaj.Application.Inventory;
using Garaj.Application.Quotes;
using Garaj.Application.Sales;
using Garaj.Application.WorkOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/work-orders")]
public class WorkOrdersController(
    IWorkOrderService service, IJobTemplateService jobTemplates) : ControllerBase
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

    /// <summary>
    /// Borra la orden. Es para la que se abrió por error: devuelve a bodega los repuestos que
    /// tenía cargados, borra sus fotos, pasos e historial, y deja libres la cotización y el
    /// requerimiento que la mencionaban. Devuelve 409 si ya se facturó.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Asigna o desasigna el técnico responsable. Pase null para dejarla sin asignar.</summary>
    [HttpPut("{id:guid}/assign")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<WorkOrderDetailDto>> Assign(
        Guid id, AssignTechnicianRequest request, CancellationToken ct)
        => Ok(await service.AssignAsync(id, request, ct));

    /// <summary>
    /// Elige cómo se cobra la mano de obra: por el catálogo en cada paso, o un total escrito
    /// a mano para toda la orden. Es del Dueño porque decide lo que se le cobra al cliente.
    /// </summary>
    [HttpPut("{id:guid}/labor")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<WorkOrderDetailDto>> SetLaborMode(
        Guid id, SetLaborModeRequest request, CancellationToken ct)
        => Ok(await service.SetLaborModeAsync(id, request, ct));

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

    /// <summary>
    /// Anexa a la orden los pasos de un trabajo frecuente y devuelve sus repuestos como
    /// sugerencia —cargarlos descontaría bodega, y al aplicar la plantilla el trabajo todavía
    /// no se ha hecho—. Se anexa, no se reemplaza: un trabajo real es «aceite y frenos».
    /// </summary>
    [HttpPost("{id:guid}/apply-template")]
    [Authorize(Policy = AppPolicies.TechnicianOrOwner)]
    public async Task<ActionResult<ApplyJobTemplateResultDto>> ApplyTemplate(
        Guid id, ApplyJobTemplateRequest request, CancellationToken ct)
        => Ok(await jobTemplates.ApplyToWorkOrderAsync(id, request, ct));

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

    [HttpGet("{id:guid}/parts")]
    public async Task<ActionResult<IReadOnlyList<WorkOrderPartDto>>> ListParts(
        Guid id, CancellationToken ct)
        => Ok(await service.ListPartsAsync(id, ct));

    /// <summary>
    /// Carga el repuesto en la orden.
    ///
    /// Con <c>partId</c> sale del catálogo y se descuenta de la bodega de la sucursal de la
    /// orden; devuelve 409 si no hay existencia suficiente, diciendo cuánto queda. Sin
    /// <c>partId</c> se carga a mano —hacen falta <c>description</c> y <c>unitPrice</c>— y no
    /// toca el inventario: es lo que se compró de encargo y nunca pasó por bodega.
    /// </summary>
    [HttpPost("{id:guid}/parts")]
    [Authorize(Policy = AppPolicies.TechnicianOrOwner)]
    public async Task<ActionResult<WorkOrderPartDto>> AddPart(
        Guid id, AddWorkOrderPartRequest request, CancellationToken ct)
        => Ok(await service.AddPartAsync(id, request, ct));

    /// <summary>Lo quita de la orden, y lo devuelve a la bodega si había salido de ella.</summary>
    [HttpDelete("{id:guid}/parts/{partLineId:guid}")]
    [Authorize(Policy = AppPolicies.TechnicianOrOwner)]
    public async Task<IActionResult> RemovePart(Guid id, Guid partLineId, CancellationToken ct)
    {
        await service.RemovePartAsync(id, partLineId, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/tasks/{taskId:guid}")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<IActionResult> DeleteTask(Guid id, Guid taskId, CancellationToken ct)
    {
        await service.DeleteTaskAsync(id, taskId, ct);
        return NoContent();
    }

    /// <summary>
    /// El enlace de seguimiento con el mensaje de WhatsApp ya escrito.
    ///
    /// <c>kind=received</c> al recibir el vehículo, <c>ready</c> cuando está listo —lleva el
    /// total si ya se facturó— e <c>invoice</c> para mandar la factura, que responde 400 si la
    /// orden todavía no se ha cerrado.
    /// </summary>
    [HttpGet("{id:guid}/whatsapp")]
    [Authorize(Policy = AppPolicies.TechnicianOrOwner)]
    public async Task<ActionResult<WhatsAppLinkDto>> WhatsApp(
        Guid id, [FromQuery] OrderMessageKind kind, CancellationToken ct)
        => Ok(await service.TrackingLinkAsync(id, kind, ct));

    /// <summary>
    /// Los vehículos a los que les toca servicio, el más atrasado primero. Es trabajo que hoy
    /// se pierde por no acordarse.
    /// </summary>
    [HttpGet("reminders")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<IReadOnlyList<ServiceReminderDto>>> Reminders(
        [FromQuery] ServiceReminderQuery query, CancellationToken ct)
        => Ok(await service.ServiceRemindersAsync(query, ct));

    /// <summary>
    /// El enlace de WhatsApp del recordatorio. Es POST porque deja constancia de que ya se le
    /// avisó: así el mismo cliente no recibe dos llamadas la misma semana.
    /// </summary>
    [HttpPost("{id:guid}/service-reminder")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<WhatsAppLinkDto>> ServiceReminder(Guid id, CancellationToken ct)
        => Ok(await service.ServiceReminderLinkAsync(id, ct));
}

/// <summary>
/// La orden vista por su dueño desde el enlace de WhatsApp. Sin autenticación: el token
/// aleatorio de la URL es la única credencial, igual que en la cotización.
/// </summary>
/// <remarks>
/// Expone el estado, los pasos, las fotos marcadas como visibles al cliente y, cuando ya se
/// facturó, el total y el saldo. Nada más: ni el costo del taller, ni el nombre del técnico, ni
/// un id con el que llegar a otra parte de la API.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("public/work-orders")]
public class PublicWorkOrdersController(
    IWorkOrderService orders, ISaleService sales) : ControllerBase
{
    [HttpGet("{token:guid}")]
    public async Task<ActionResult<OrderTrackingDto>> Get(Guid token, CancellationToken ct)
        => Ok(await orders.TrackingPublicAsync(token, ct));

    /// <summary>La factura de esa orden. 404 mientras el vehículo siga en el taller.</summary>
    [HttpGet("{token:guid}/invoice.pdf")]
    public async Task<IActionResult> Invoice(Guid token, CancellationToken ct)
    {
        var order = await orders.TrackingPublicAsync(token, ct);
        var bytes = await sales.InvoicePdfByOrderTokenAsync(token, ct);

        return File(bytes, "application/pdf", $"Factura - {order.Number}.pdf");
    }

    /// <summary>El logo del taller para el encabezado de la página. 404 si no tiene.</summary>
    [HttpGet("{token:guid}/logo")]
    public async Task<IActionResult> Logo(Guid token, CancellationToken ct)
    {
        var logo = await orders.TrackingLogoPublicAsync(token, ct);
        if (logo is null) return NotFound();

        Response.Headers.CacheControl = "public, max-age=3600";
        return File(logo.Bytes, logo.ContentType);
    }
}

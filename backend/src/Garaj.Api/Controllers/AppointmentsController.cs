using Garaj.Application.Appointments;
using Garaj.Application.Common;
using Garaj.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Garaj.Api.Controllers;

/// <summary>
/// La agenda del taller. El Cliente pide y ve las suyas; el Dueño y el Encargado las
/// administran.
/// </summary>
[ApiController]
[Authorize]
[Route("api/appointments")]
public class AppointmentsController(IAppointmentService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AppointmentDto>>> List(
        [FromQuery] AppointmentQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AppointmentDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    /// <summary>La pide el Cliente desde la app, sobre uno de sus vehículos.</summary>
    [HttpPost("request")]
    public async Task<ActionResult<AppointmentDto>> Request(
        RequestAppointmentRequest request, CancellationToken ct)
    {
        var created = await service.RequestAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>La anota el taller, por teléfono o en el mostrador. Nace confirmada.</summary>
    [HttpPost]
    [Authorize(Policy = AppPolicies.ManagementOnly)]
    public async Task<ActionResult<AppointmentDto>> Create(
        CreateAppointmentRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = AppPolicies.ManagementOnly)]
    public async Task<ActionResult<AppointmentDto>> Confirm(
        Guid id, ConfirmAppointmentRequest request, CancellationToken ct)
        => Ok(await service.ConfirmAsync(id, request, ct));

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = AppPolicies.ManagementOnly)]
    public async Task<ActionResult<AppointmentDto>> Reject(
        Guid id, RejectAppointmentRequest request, CancellationToken ct)
        => Ok(await service.RejectAsync(id, request, ct));

    /// <summary>Llegó el vehículo. El id de la orden es opcional: se recibe y después se abre.</summary>
    [HttpPost("{id:guid}/attended")]
    [Authorize(Policy = AppPolicies.ManagementOnly)]
    public async Task<ActionResult<AppointmentDto>> Attended(
        Guid id, [FromBody] MarkAttendedBody? body, CancellationToken ct)
        => Ok(await service.MarkAttendedAsync(id, body?.WorkOrderId, ct));

    public record MarkAttendedBody(Guid? WorkOrderId);
}

/// <summary>
/// Pedir cita sin cuenta: el enlace que el taller manda por WhatsApp o pega en un QR.
/// </summary>
/// <remarks>
/// Expone el nombre del taller, su logo y sus sucursales —lo que ya está escrito en la puerta
/// del local— y recibe la solicitud. Nada más: ni clientes, ni órdenes, ni precios, ni un id
/// con el que llegar a otra parte de la API. La cita que se crea nace sin cliente y sin
/// vehículo del padrón: los datos de verdad se toman cuando el carro llega.
/// </remarks>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimits.Publico)]
[Route("public/booking")]
public class PublicBookingController(IAppointmentService service) : ControllerBase
{
    [HttpGet("{bookingToken:guid}")]
    public async Task<ActionResult<BookingPageDto>> Page(Guid bookingToken, CancellationToken ct)
        => Ok(await service.BookingPageAsync(bookingToken, ct));

    [HttpGet("{bookingToken:guid}/logo")]
    public async Task<IActionResult> Logo(Guid bookingToken, CancellationToken ct)
    {
        var logo = await service.LogoPublicAsync(bookingToken, ct);
        if (logo is null) return NotFound();

        Response.Headers.CacheControl = "public, max-age=3600";
        return File(logo.Bytes, logo.ContentType);
    }

    [HttpPost("{bookingToken:guid}")]
    public async Task<ActionResult<PublicAppointmentDto>> Book(
        Guid bookingToken, BookAppointmentRequest request, CancellationToken ct)
        => Ok(await service.BookAsync(bookingToken, request, ct));

    /// <summary>La cita del cliente, por su propio token. Es la página que puede volver a abrir.</summary>
    [HttpGet("appointments/{token:guid}")]
    public async Task<ActionResult<PublicAppointmentDto>> Mine(Guid token, CancellationToken ct)
        => Ok(await service.GetPublicAsync(token, ct));

    [HttpPost("appointments/{token:guid}/cancel")]
    public async Task<ActionResult<PublicAppointmentDto>> Cancel(Guid token, CancellationToken ct)
        => Ok(await service.CancelPublicAsync(token, ct));
}

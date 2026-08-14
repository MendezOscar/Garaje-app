using Garaj.Application.Common;
using Garaj.Application.Quotes;
using Garaj.Application.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

/// <summary>
/// El estado de cuenta de un cliente: lo que debe hoy, factura por factura y con sus abonos.
/// </summary>
/// <remarks>
/// Va bajo el cliente y no bajo las ventas porque es una pregunta sobre él —«¿cuánto me debe
/// este señor?»— y no sobre una factura en particular. Solo el Dueño: el saldo de un cliente no
/// es asunto del técnico.
/// </remarks>
[ApiController]
[Authorize(Policy = AppPolicies.OwnerOnly)]
[Route("api/customers/{customerId:guid}/statement")]
public class StatementsController(ISaleService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CustomerStatementDto>> Get(Guid customerId, CancellationToken ct)
        => Ok(await service.StatementAsync(customerId, ct));

    /// <summary>Para imprimirlo o adjuntarlo. El cliente recibe el mismo por su enlace.</summary>
    [HttpGet("pdf")]
    public async Task<IActionResult> Pdf(Guid customerId, CancellationToken ct)
    {
        var statement = await service.StatementAsync(customerId, ct);
        var bytes = await service.StatementPdfAsync(customerId, ct);

        return File(bytes, "application/pdf", $"Estado de cuenta - {statement.CustomerName}.pdf");
    }

    /// <summary>
    /// El enlace de WhatsApp con el mensaje ya escrito. Devuelve 400 si el cliente no debe
    /// nada: mandarle un estado de cuenta en cero solo lo confunde.
    /// </summary>
    [HttpGet("whatsapp")]
    public async Task<ActionResult<WhatsAppLinkDto>> WhatsApp(Guid customerId, CancellationToken ct)
        => Ok(await service.StatementLinkAsync(customerId, ct));
}

/// <summary>
/// El estado de cuenta visto por el cliente desde el enlace de WhatsApp. Sin autenticación: el
/// token aleatorio de la URL es la única credencial, igual que en la cotización.
/// </summary>
/// <remarks>
/// Expone su nombre y sus facturas con saldo, nada más: ni el costo del taller, ni quién
/// recibió cada abono, ni un id con el que llegar a otra parte de la API.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("public/statements")]
public class PublicStatementsController(ISaleService service) : ControllerBase
{
    [HttpGet("{token:guid}")]
    public async Task<ActionResult<CustomerStatementDto>> Get(Guid token, CancellationToken ct)
        => Ok(await service.StatementPublicAsync(token, ct));

    [HttpGet("{token:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid token, CancellationToken ct)
    {
        var statement = await service.StatementPublicAsync(token, ct);
        var bytes = await service.StatementPdfPublicAsync(token, ct);

        return File(bytes, "application/pdf", $"Estado de cuenta - {statement.CustomerName}.pdf");
    }

    /// <summary>El logo del taller para el encabezado de la página. 404 si no tiene.</summary>
    [HttpGet("{token:guid}/logo")]
    public async Task<IActionResult> Logo(Guid token, CancellationToken ct)
    {
        var logo = await service.StatementLogoPublicAsync(token, ct);
        if (logo is null) return NotFound();

        Response.Headers.CacheControl = "public, max-age=3600";
        return File(logo.Bytes, logo.ContentType);
    }
}

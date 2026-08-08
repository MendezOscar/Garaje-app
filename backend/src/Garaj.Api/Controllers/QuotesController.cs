using Garaj.Application.Common;
using Garaj.Application.Quotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

[ApiController]
[Authorize(Policy = AppPolicies.OwnerOnly)]
[Route("api/labor-services")]
public class LaborServicesController(ILaborServiceCatalog service) : ControllerBase
{
    /// <summary>El Técnico también lo consulta para asignar un servicio a un paso.</summary>
    [HttpGet]
    [Authorize(Policy = AppPolicies.StaffOnly)]
    public async Task<ActionResult<IReadOnlyList<LaborServiceDto>>> List(
        [FromQuery] bool includeInactive, CancellationToken ct)
        => Ok(await service.ListAsync(includeInactive, ct));

    [HttpPost]
    public async Task<ActionResult<LaborServiceDto>> Create(
        SaveLaborServiceRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LaborServiceDto>> Update(
        Guid id, SaveLaborServiceRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
}

[ApiController]
[Authorize]
[Route("api/quotes")]
public class QuotesController(IQuoteService service) : ControllerBase
{
    /// <summary>El Dueño ve todas; el Cliente, solo las suyas; el Técnico, ninguna.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<QuoteListItemDto>>> List(
        [FromQuery] QuoteQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuoteDetailDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<QuoteDetailDto>> Create(
        CreateQuoteRequest request, CancellationToken ct)
    {
        var quote = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = quote.Id }, quote);
    }

    /// <summary>Arma la cotización con lo que la orden ya tiene: repuestos y pasos.</summary>
    [HttpPost("from-work-order")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<QuoteDetailDto>> CreateFromWorkOrder(
        QuoteFromWorkOrderRequest request, CancellationToken ct)
    {
        var quote = await service.CreateFromWorkOrderAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = quote.Id }, quote);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<QuoteDetailDto>> Update(
        Guid id, UpdateQuoteRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/lines")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<QuoteDetailDto>> AddLine(
        Guid id, SaveQuoteLineRequest request, CancellationToken ct)
        => Ok(await service.AddLineAsync(id, request, ct));

    [HttpPut("{id:guid}/lines/{lineId:guid}")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<QuoteDetailDto>> UpdateLine(
        Guid id, Guid lineId, SaveQuoteLineRequest request, CancellationToken ct)
        => Ok(await service.UpdateLineAsync(id, lineId, request, ct));

    [HttpDelete("{id:guid}/lines/{lineId:guid}")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<QuoteDetailDto>> RemoveLine(
        Guid id, Guid lineId, CancellationToken ct)
        => Ok(await service.RemoveLineAsync(id, lineId, ct));

    /// <summary>La marca como enviada y devuelve el link de WhatsApp con el mensaje armado.</summary>
    [HttpPost("{id:guid}/send")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<WhatsAppLinkDto>> Send(Guid id, CancellationToken ct)
        => Ok(await service.SendAsync(id, ct));

    /// <summary>El mismo link sin cambiar el estado, para volver a mandarlo.</summary>
    [HttpGet("{id:guid}/whatsapp-link")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<WhatsAppLinkDto>> WhatsAppLink(Guid id, CancellationToken ct)
        => Ok(await service.WhatsAppLinkAsync(id, ct));

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var quote = await service.GetAsync(id, ct);
        return File(await service.PdfAsync(id, ct), "application/pdf", $"{quote.Number}.pdf");
    }

    /// <summary>Respuesta desde dentro de la app: el Cliente autenticado, o el Dueño si le contestaron por teléfono.</summary>
    [HttpPost("{id:guid}/respond")]
    public async Task<ActionResult<QuoteDetailDto>> Respond(
        Guid id, RespondToQuoteRequest request, CancellationToken ct)
        => Ok(await service.RespondAsync(id, request, ct));
}

/// <summary>
/// La cotización vista por el cliente desde el link de WhatsApp. Sin autenticación: el token
/// aleatorio de la URL es la única credencial, y por eso no se reutiliza entre cotizaciones
/// ni se expone ningún otro id en la respuesta.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("public/quotes")]
public class PublicQuotesController(IQuoteService service) : ControllerBase
{
    [HttpGet("{token:guid}")]
    public async Task<ActionResult<PublicQuoteDto>> Get(Guid token, CancellationToken ct)
        => Ok(await service.GetPublicAsync(token, ct));

    [HttpPost("{token:guid}/respond")]
    public async Task<ActionResult<PublicQuoteDto>> Respond(
        Guid token, RespondToQuoteRequest request, CancellationToken ct)
        => Ok(await service.RespondPublicAsync(token, request, ct));

    [HttpGet("{token:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid token, CancellationToken ct)
    {
        var quote = await service.GetPublicAsync(token, ct);
        return File(await service.PdfPublicAsync(token, ct), "application/pdf", $"{quote.Number}.pdf");
    }
}

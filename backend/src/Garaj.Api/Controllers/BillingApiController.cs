using Garaj.Api.Services;
using Garaj.Application.Common;
using Garaj.Application.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Garaj.Api.Controllers;

/// <summary>
/// La facturación como servicio: el portal de StockRuta, de Profeapp o del servicio que venga
/// pide aquí el número de su documento, porque el CAI es uno solo para todo lo que cobramos.
/// </summary>
/// <remarks>
/// <b>No hay sesión.</b> Quien llama es un sistema, no una persona, y se identifica con la
/// llave del servicio en la cabecera <c>X-Billing-Key</c> —nunca en la URL, que queda escrita
/// en los registros del servidor y en el historial del navegador—.
///
/// El padrón de clientes no vive aquí: cada portal tiene el suyo. Lo que se manda es a nombre
/// de quién va el documento y qué conceptos lleva, y eso se <b>copia</b> en la factura.
/// </remarks>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimits.Facturacion)]
[Route("api/billing")]
public class BillingApiController(IPlatformBillingService service) : ControllerBase
{
    private const string CabeceraDeLlave = "X-Billing-Key";
    private const string CabeceraDeIdempotencia = "Idempotency-Key";

    /// <summary>
    /// Emite un documento y devuelve su número.
    ///
    /// <c>Idempotency-Key</c> es obligatoria: el mismo valor del mismo servicio devuelve
    /// <b>el documento que ya salió</b>, en vez de quemar otro número del SAR. Mándese una por
    /// cobro —el id del pago en su sistema sirve—, no una por intento.
    /// </summary>
    [HttpPost("invoices")]
    public async Task<ActionResult<IssuedInvoiceDto>> Issue(
        IssueInvoiceRequest request, CancellationToken ct)
    {
        var llave = Request.Headers[CabeceraDeLlave].ToString();
        var idempotencia = Request.Headers[CabeceraDeIdempotencia].ToString();

        if (string.IsNullOrWhiteSpace(idempotencia))
            throw new AppException($"Falta la cabecera {CabeceraDeIdempotencia}.");

        return Ok(await service.IssueAsync(llave, idempotencia, request, ct));
    }

    /// <summary>
    /// El PDF del documento, para que el portal se lo entregue a su cliente. Solo el suyo: el
    /// de otro servicio responde 404.
    /// </summary>
    [HttpGet("invoices/{id:guid}.pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var llave = Request.Headers[CabeceraDeLlave].ToString();
        var pdf = await service.InvoicePdfForProductAsync(llave, id, ct);

        return File(pdf, "application/pdf", $"documento-{id}.pdf");
    }
}

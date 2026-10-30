using Garaj.Application.Common;
using Garaj.Application.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Garaj.Api.Services;

namespace Garaj.Api.Controllers;

/// <summary>
/// Lo nuestro: los talleres como clientes y su mensualidad. Solo entra el perfil Plataforma,
/// que no pertenece a ningún taller y no puede leer los datos de ninguno.
/// </summary>
/// <remarks>
/// El usuario de plataforma <b>no se crea desde aquí</b>, sino con
/// <c>dotnet run --project src/Garaj.Api -- create-platform-user …</c>. Es a propósito: si el
/// panel pudiera crear otro, bastaría una sesión robada para fabricarse llaves maestras nuevas.
/// </remarks>
[ApiController]
[Authorize(Policy = AppPolicies.PlatformOnly)]
[Route("api/platform/tenants")]
public class PlatformController(IPlatformService service) : ControllerBase
{
    /// <summary>Los talleres, con lo que vence primero arriba.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlatformTenantDto>>> List(CancellationToken ct)
        => Ok(await service.ListAsync(ct));

    /// <summary>La ficha de cobro de un taller con su historial de pagos.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlatformTenantDetailDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    /// <summary>
    /// Da de alta el taller, su primera sucursal y el usuario Dueño. La contraseña vuelve una
    /// sola vez en la respuesta: no se guarda en claro y no hay forma de volver a consultarla.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreatedTenantDto>> Create(
        CreateTenantRequest request, CancellationToken ct)
        => Ok(await service.CreateTenantAsync(request, ct));

    /// <summary>
    /// Le crea un Dueño a un taller que ya existe. Es el rescate cuando el taller se quedó sin
    /// nadie que pueda entrar —perdió la contraseña, borró su cuenta desde la app, se fue del
    /// negocio—: sus datos están intactos, pero desde adentro no hay forma de abrirlo.
    /// </summary>
    [HttpPost("{id:guid}/owner")]
    public async Task<ActionResult<CreatedTenantDto>> CreateOwner(
        Guid id, CreateOwnerRequest request, CancellationToken ct)
        => Ok(await service.CreateOwnerAsync(id, request, ct));

    /// <summary>Registra el pago del mes y corre la fecha de vencimiento.</summary>
    [HttpPost("{id:guid}/payments")]
    public async Task<ActionResult<PlatformTenantDetailDto>> RegisterPayment(
        Guid id, RegisterPaymentRequest request, CancellationToken ct)
        => Ok(await service.RegisterPaymentAsync(id, request, ct));

    /// <summary>
    /// Anula un pago registrado por error: devuelve el vencimiento a donde estaba y anula el
    /// documento que hubiera dejado. No lo borra: el historial de cobro es lo que contesta
    /// «¿yo ya te pagué junio?».
    /// </summary>
    [HttpPost("{id:guid}/payments/{paymentId:guid}/void")]
    public async Task<ActionResult<PlatformTenantDetailDto>> VoidPayment(
        Guid id, Guid paymentId, VoidPaymentRequest request, CancellationToken ct)
        => Ok(await service.VoidPaymentAsync(id, paymentId, request, ct));

    /// <summary>Acuerdo de pago: el taller sigue trabajando hasta la fecha acordada.</summary>
    [HttpPut("{id:guid}/agreement")]
    public async Task<ActionResult<PlatformTenantDto>> SetAgreement(
        Guid id, PaymentAgreementRequest request, CancellationToken ct)
        => Ok(await service.SetAgreementAsync(id, request, ct));

    [HttpDelete("{id:guid}/agreement")]
    public async Task<ActionResult<PlatformTenantDto>> ClearAgreement(Guid id, CancellationToken ct)
        => Ok(await service.ClearAgreementAsync(id, ct));

    /// <summary>Cambia plan, cuota, vencimiento y días de gracia.</summary>
    [HttpPut("{id:guid}/subscription")]
    public async Task<ActionResult<PlatformTenantDto>> UpdateSubscription(
        Guid id, UpdateSubscriptionRequest request, CancellationToken ct)
        => Ok(await service.UpdateSubscriptionAsync(id, request, ct));

    /// <summary>Suspender corta el acceso entero: el taller ni entra. Es el último recurso.</summary>
    /// <summary>Corrige con qué trabaja el taller. Se fija al darlo de alta.</summary>
    [HttpPut("{id:guid}/works-with")]
    public async Task<ActionResult<PlatformTenantDto>> SetWorksWith(
        Guid id, SetWorksWithRequest request, CancellationToken ct)
        => Ok(await service.SetWorksWithAsync(id, request.WorksWith, ct));

    [HttpPost("{id:guid}/suspend")]
    public async Task<ActionResult<PlatformTenantDto>> Suspend(Guid id, CancellationToken ct)
        => Ok(await service.SetActiveAsync(id, false, ct));

    [HttpPost("{id:guid}/reactivate")]
    public async Task<ActionResult<PlatformTenantDto>> Reactivate(Guid id, CancellationToken ct)
        => Ok(await service.SetActiveAsync(id, true, ct));
}

/// <summary>
/// Nuestra facturación: quiénes somos en el documento, nuestro CAI y las facturas emitidas.
/// </summary>
/// <remarks>
/// La factura no se emite desde aquí: nace con el pago, en
/// <c>POST /api/platform/tenants/{id}/payments</c>. Esto es lo que se configura una vez y lo
/// que se consulta después.
/// </remarks>
[ApiController]
[Authorize(Policy = AppPolicies.PlatformOnly)]
[Route("api/platform/billing")]
public class PlatformBillingController(IPlatformBillingService service) : ControllerBase
{
    /// <summary>Los datos del emisor y el rango autorizado vigente, si lo hay.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<PlatformSettingsDto>> Settings(CancellationToken ct)
        => Ok(await service.GetSettingsAsync(ct));

    [HttpPut("settings")]
    public async Task<ActionResult<PlatformSettingsDto>> SaveSettings(
        SavePlatformSettingsRequest request, CancellationToken ct)
        => Ok(await service.SaveSettingsAsync(request, ct));

    /// <summary>Registra el CAI nuevo del SAR y da de baja el anterior.</summary>
    [HttpPost("fiscal-range")]
    public async Task<ActionResult<PlatformSettingsDto>> SaveFiscalRange(
        SavePlatformFiscalRangeRequest request, CancellationToken ct)
        => Ok(await service.SaveFiscalRangeAsync(request, ct));

    [HttpGet("invoices/{id:guid}")]
    public async Task<ActionResult<SubscriptionInvoiceDto>> Invoice(Guid id, CancellationToken ct)
        => Ok(await service.GetInvoiceAsync(id, ct));

    /// <summary>El PDF, para mandárselo al taller por WhatsApp o por correo.</summary>
    [HttpGet("invoices/{id:guid}.pdf")]
    [EnableRateLimiting(RateLimits.Documento)]
    public async Task<IActionResult> InvoicePdf(Guid id, CancellationToken ct)
    {
        var factura = await service.GetInvoiceAsync(id, ct);
        var pdf = await service.InvoicePdfAsync(id, ct);

        return File(pdf, "application/pdf", $"{factura.Number}.pdf");
    }

    /// <summary>Anula el documento. El pago queda: el dinero entró igual.</summary>
    [HttpPost("invoices/{id:guid}/void")]
    public async Task<ActionResult<SubscriptionInvoiceDto>> VoidInvoice(
        Guid id, VoidInvoiceRequest request, CancellationToken ct)
        => Ok(await service.VoidInvoiceAsync(id, request, ct));
}

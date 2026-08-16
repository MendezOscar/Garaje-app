using Garaj.Application.Common;
using Garaj.Application.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    /// <summary>Registra el pago del mes y corre la fecha de vencimiento.</summary>
    [HttpPost("{id:guid}/payments")]
    public async Task<ActionResult<PlatformTenantDetailDto>> RegisterPayment(
        Guid id, RegisterPaymentRequest request, CancellationToken ct)
        => Ok(await service.RegisterPaymentAsync(id, request, ct));

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
    [HttpPost("{id:guid}/suspend")]
    public async Task<ActionResult<PlatformTenantDto>> Suspend(Guid id, CancellationToken ct)
        => Ok(await service.SetActiveAsync(id, false, ct));

    [HttpPost("{id:guid}/reactivate")]
    public async Task<ActionResult<PlatformTenantDto>> Reactivate(Guid id, CancellationToken ct)
        => Ok(await service.SetActiveAsync(id, true, ct));
}

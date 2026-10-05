using Garaj.Application.Common;
using Garaj.Application.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

/// <summary>
/// Reclamos: el cliente vuelve diciendo que el trabajo quedó mal.
/// </summary>
/// <remarks>
/// Solo el Dueño, y por eso no lleva política de técnico: decidir si algo entra en garantía
/// no es de quien hizo el trabajo.
/// </remarks>
[ApiController]
[Route("api/claims")]
[Authorize]
public class ClaimsController(IClaimService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ClaimListItemDto>>> List(
        [FromQuery] ClaimQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClaimDetailDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    /// <summary>
    /// Abre el reclamo sobre un trabajo ya facturado. Si la garantía estaba viva se anota en
    /// el momento: vencerla después no cambia lo que el taller aceptó ese día.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ClaimDetailDto>> Create(
        CreateClaimRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(request, ct));

    /// <summary>Lo cierra: reparado en garantía, reparado y cobrado, o no procede.</summary>
    [HttpPost("{id:guid}/resolve")]
    public async Task<ActionResult<ClaimDetailDto>> Resolve(
        Guid id, ResolveClaimRequest request, CancellationToken ct)
        => Ok(await service.ResolveAsync(id, request, ct));

    [HttpPost("{id:guid}/reopen")]
    public async Task<ActionResult<ClaimDetailDto>> Reopen(Guid id, CancellationToken ct)
        => Ok(await service.ReopenAsync(id, ct));
}

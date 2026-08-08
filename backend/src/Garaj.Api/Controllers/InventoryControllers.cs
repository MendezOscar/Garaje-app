using Garaj.Application.Common;
using Garaj.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

/// <summary>Catálogo de repuestos del taller. Las existencias van por sucursal, en /api/stock.</summary>
[ApiController]
[Authorize(Policy = AppPolicies.StaffOnly)]
[Route("api/parts")]
public class PartsController(IPartService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<PartDto>>> List(
        [FromQuery] PartQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<string>>> Categories(CancellationToken ct)
        => Ok(await service.CategoriesAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PartDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<PartDto>> Create(SavePartRequest request, CancellationToken ct)
    {
        var part = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = part.Id }, part);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<PartDto>> Update(
        Guid id, SavePartRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
}

/// <summary>
/// Existencias y movimientos. El stock no se edita: se mueve. Toda variación deja un
/// movimiento con su responsable, su fecha y el saldo resultante.
/// </summary>
[ApiController]
[Authorize(Policy = AppPolicies.StaffOnly)]
[Route("api/stock")]
public class StockController(IStockService service) : ControllerBase
{
    /// <summary>Existencias visibles: el Técnico solo ve las de las sucursales donde trabaja.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<StockItemDto>>> List(
        [FromQuery] StockQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    /// <summary>Lo que está en o por debajo del mínimo: la lista de compras del taller.</summary>
    [HttpGet("alerts")]
    public async Task<ActionResult<IReadOnlyList<StockItemDto>>> Alerts(
        [FromQuery] Guid? branchId, CancellationToken ct)
        => Ok(await service.AlertsAsync(branchId, ct));

    /// <summary>Kardex: el histórico que explica cómo se llegó al saldo actual.</summary>
    [HttpGet("movements")]
    public async Task<ActionResult<PagedResult<StockMovementDto>>> Movements(
        [FromQuery] MovementQuery query, CancellationToken ct)
        => Ok(await service.MovementsAsync(query, ct));

    [HttpPost("receive")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<StockItemDto>> Receive(
        ReceiveStockRequest request, CancellationToken ct)
        => Ok(await service.ReceiveAsync(request, ct));

    /// <summary>Ajuste por conteo físico. Se envía lo contado, no la diferencia.</summary>
    [HttpPost("adjust")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<StockItemDto>> Adjust(
        AdjustStockRequest request, CancellationToken ct)
        => Ok(await service.AdjustAsync(request, ct));

    /// <summary>Traslado entre sucursales. Devuelve el saldo de origen y destino.</summary>
    [HttpPost("transfer")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<IReadOnlyList<StockItemDto>>> Transfer(
        TransferStockRequest request, CancellationToken ct)
        => Ok(await service.TransferAsync(request, ct));

    /// <summary>Mínimo de reposición y ubicación. No mueve existencias.</summary>
    [HttpPut("settings")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<StockItemDto>> SaveSettings(
        SaveStockSettingsRequest request, CancellationToken ct)
        => Ok(await service.SaveSettingsAsync(request, ct));
}

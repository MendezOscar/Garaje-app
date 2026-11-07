using Garaj.Application.Assembly;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

/// <summary>
/// El catálogo del contrato de armado de motos: contratistas, modelos y bodegas.
/// </summary>
/// <remarks>
/// Sin <c>[Authorize(Policy = OwnerOnly)]</c> en la clase a propósito: las listas de modelos y
/// de bodegas también las lee el Encargado —son las que llena quien registra una moto— y quien
/// decide eso es el servicio, que es el único que distingue leer de escribir. Lo que no sale
/// de aquí para nadie que no sea el Dueño es la tabla de precios.
///
/// El taller sin el módulo recibe 404 en todo, no 403.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/assembly")]
public class AssemblyController(IAssemblyCatalogService service) : ControllerBase
{
    [HttpGet("contractors")]
    public async Task<ActionResult<IReadOnlyList<AssemblyContractorDto>>> Contractors(
        CancellationToken ct)
        => Ok(await service.ContractorsAsync(ct));

    [HttpPost("contractors")]
    public async Task<ActionResult<AssemblyContractorDto>> CreateContractor(
        SaveAssemblyContractorRequest request, CancellationToken ct)
        => Ok(await service.SaveContractorAsync(null, request, ct));

    [HttpPut("contractors/{id:guid}")]
    public async Task<ActionResult<AssemblyContractorDto>> UpdateContractor(
        Guid id, SaveAssemblyContractorRequest request, CancellationToken ct)
        => Ok(await service.SaveContractorAsync(id, request, ct));

    /// <summary>La tabla de precios entera de un contratista. Reemplaza lo que había.</summary>
    [HttpPut("contractors/{id:guid}/rates")]
    public async Task<ActionResult<AssemblyContractorDto>> SaveRates(
        Guid id, SaveAssemblyRatesRequest request, CancellationToken ct)
        => Ok(await service.SaveRatesAsync(id, request, ct));

    [HttpGet("models")]
    public async Task<ActionResult<IReadOnlyList<AssemblyModelDto>>> Models(CancellationToken ct)
        => Ok(await service.ModelsAsync(ct));

    [HttpPost("models")]
    public async Task<ActionResult<AssemblyModelDto>> CreateModel(
        SaveAssemblyModelRequest request, CancellationToken ct)
        => Ok(await service.SaveModelAsync(null, request, ct));

    [HttpPut("models/{id:guid}")]
    public async Task<ActionResult<AssemblyModelDto>> UpdateModel(
        Guid id, SaveAssemblyModelRequest request, CancellationToken ct)
        => Ok(await service.SaveModelAsync(id, request, ct));

    [HttpGet("warehouses")]
    public async Task<ActionResult<IReadOnlyList<AssemblyWarehouseDto>>> Warehouses(
        CancellationToken ct)
        => Ok(await service.WarehousesAsync(ct));

    [HttpPost("warehouses")]
    public async Task<ActionResult<AssemblyWarehouseDto>> CreateWarehouse(
        SaveAssemblyWarehouseRequest request, CancellationToken ct)
        => Ok(await service.SaveWarehouseAsync(null, request, ct));

    [HttpPut("warehouses/{id:guid}")]
    public async Task<ActionResult<AssemblyWarehouseDto>> UpdateWarehouse(
        Guid id, SaveAssemblyWarehouseRequest request, CancellationToken ct)
        => Ok(await service.SaveWarehouseAsync(id, request, ct));
}

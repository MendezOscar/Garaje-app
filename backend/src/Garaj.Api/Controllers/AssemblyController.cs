using Garaj.Application.Assembly;
using Garaj.Application.Common;
using Garaj.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
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
    /// <summary>El gasto del taller por cada moto armada. Solo el Dueño.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<AssemblySettingsDto>> Settings(CancellationToken ct)
        => Ok(await service.SettingsAsync(ct));

    [HttpPut("settings")]
    public async Task<ActionResult<AssemblySettingsDto>> SaveSettings(
        SaveAssemblySettingsRequest request, CancellationToken ct)
        => Ok(await service.SaveSettingsAsync(request, ct));

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

/// <summary>
/// Las motos armadas: lo que el técnico registra parado en la bodega.
/// </summary>
/// <remarks>
/// Lo que se le cobra al contratista no viaja en estas respuestas cuando pregunta un técnico.
/// El taller sin el módulo recibe 404.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/assembly/motorcycles")]
public class MotorcyclesController(IMotorcycleAssemblyService service) : ControllerBase
{
    /// <summary>Las tres listas para registrar sin teclear. Sin un solo precio.</summary>
    [HttpGet("pick-lists")]
    public async Task<ActionResult<AssemblyPickListsDto>> PickLists(CancellationToken ct)
        => Ok(await service.PickListsAsync(ct));

    [HttpPost]
    public async Task<ActionResult<MotorcycleAssemblyDto>> Register(
        RegisterMotorcycleRequest request, CancellationToken ct)
        => Ok(await service.RegisterAsync(request, ct));

    /// <summary>Lo que lleva armado quien pregunta, y lo que suma para él.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<MyAssembliesDto>> Mine(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(await service.MineAsync(from, to, ct));

    /// <summary>
    /// Qué modelo suele llevar una serie como esa, aprendido de lo ya registrado en este
    /// taller. Siempre una propuesta: la confirma quien tiene la moto delante.
    /// </summary>
    [HttpGet("guess-model")]
    public async Task<ActionResult<ModelGuessDto>> GuessModel(
        [FromQuery] string serial, CancellationToken ct)
        => Ok(await service.GuessModelAsync(serial ?? string.Empty, ct));
}

/// <summary>
/// El lado del dueño: revisar lo armado, corregirlo, cerrar los cortes y sacar los reportes.
/// </summary>
/// <remarks>
/// Todo aquí lleva el cobro al contratista, así que todo es de quien manda. El técnico tiene
/// su propia puerta —<c>api/assembly/motorcycles</c>— y por ahí ese número no sale.
/// </remarks>
[ApiController]
[Authorize(Policy = AppPolicies.ManagementOnly)]
[Route("api/assembly/admin")]
public class AssemblyAdminController(
    IAssemblyAdminService service,
    IAssemblyReportService reports,
    IAssemblyImportService imports) : ControllerBase
{
    [HttpGet("motorcycles")]
    public async Task<ActionResult<AssemblyPageDto>> List(
        [FromQuery] AssemblyQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpPut("motorcycles/{id:guid}")]
    public async Task<ActionResult<AssemblyRowDto>> Correct(
        Guid id, CorrectMotorcycleRequest request, CancellationToken ct)
        => Ok(await service.CorrectAsync(id, request, ct));

    /// <summary>Borrar la que se registró por error. Solo el Dueño, y solo si no está en un corte.</summary>
    [HttpDelete("motorcycles/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("technicians")]
    public async Task<ActionResult<IReadOnlyList<AssemblyNameDto>>> Technicians(CancellationToken ct)
        => Ok(await service.TechniciansAsync(ct));

    [HttpGet("settlements")]
    public async Task<ActionResult<IReadOnlyList<AssemblySettlementDto>>> Settlements(
        CancellationToken ct)
        => Ok(await service.SettlementsAsync(ct));

    /// <summary>
    /// El dinero del armado en un rango: lo facturado, lo cobrado y lo que sigue pendiente.
    /// Lo leen Reportes, Caja del día y Por cobrar.
    /// </summary>
    [HttpGet("income")]
    public async Task<ActionResult<AssemblyIncomeDto>> Income(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Ok(await service.IncomeAsync(from, to, ct));

    /// <summary>Cerrar un corte: congela lo que incluyó. Es lo que impide cobrar dos veces.</summary>
    [HttpPost("settlements")]
    public async Task<ActionResult<AssemblySettlementDto>> Close(
        CloseSettlementRequest request, CancellationToken ct)
        => Ok(await service.CloseAsync(request, ct));

    /// <summary>
    /// Anotar que el contratista pagó esa factura.
    /// </summary>
    /// <remarks>
    /// Cerrar el corte es facturar; esto es cobrar, y entre las dos cosas pasan semanas.
    /// </remarks>
    [HttpPost("settlements/{id:guid}/collect")]
    public async Task<ActionResult<AssemblySettlementDto>> Collect(
        Guid id, CollectSettlementRequest request, CancellationToken ct)
        => Ok(await service.CollectAsync(id, request, ct));

    /// <summary>Quitarle el cobro: se anotó por error, o el cheque rebotó.</summary>
    [HttpDelete("settlements/{id:guid}/collect")]
    public async Task<ActionResult<AssemblySettlementDto>> Uncollect(Guid id, CancellationToken ct)
        => Ok(await service.UncollectAsync(id, ct));

    /// <summary>Reabrirlo suelta las motos. Es la única manera honesta de corregir un corte.</summary>
    [HttpDelete("settlements/{id:guid}")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct)
    {
        await service.ReopenAsync(id, ct);
        return NoContent();
    }

    [HttpGet("reports/billing")]
    public async Task<ActionResult<AssemblyBillingReportDto>> Billing(
        [FromQuery] AssemblyQuery query, CancellationToken ct)
        => Ok(await reports.BillingAsync(query, ct));

    [HttpGet("reports/billing.xlsx")]
    [EnableRateLimiting(RateLimits.Documento)]
    public async Task<IActionResult> BillingExcel(
        [FromQuery] AssemblyQuery query, CancellationToken ct)
        => File(
            await reports.BillingExcelAsync(query, ct),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Motos armadas.xlsx");

    [HttpGet("reports/billing.pdf")]
    [EnableRateLimiting(RateLimits.Documento)]
    public async Task<IActionResult> BillingPdf(
        [FromQuery] AssemblyQuery query, CancellationToken ct)
        => File(await reports.BillingPdfAsync(query, ct), "application/pdf", "Motos armadas.pdf");

    /// <summary>
    /// La hoja de un corte ya cerrado: exactamente las motos que entraron en él.
    /// </summary>
    /// <remarks>
    /// Es la que se le manda al contratista para que pague, y la que se vuelve a sacar el día
    /// que discuta la factura: sale del corte, que ya congeló qué motos lo componen, no de un
    /// filtro que hoy puede dar otra cosa.
    /// </remarks>
    [HttpGet("settlements/{id:guid}/report.xlsx")]
    [EnableRateLimiting(RateLimits.Documento)]
    public async Task<IActionResult> SettlementExcel(Guid id, CancellationToken ct)
        => File(
            await reports.SettlementExcelAsync(id, ct),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Corte.xlsx");

    [HttpGet("settlements/{id:guid}/report.pdf")]
    [EnableRateLimiting(RateLimits.Documento)]
    public async Task<IActionResult> SettlementPdf(Guid id, CancellationToken ct)
        => File(await reports.SettlementPdfAsync(id, ct), "application/pdf", "Corte.pdf");

    /// <summary>
    /// El del taller: cobro, pago, gasto y lo que queda. <b>No sale de la oficina</b>: ni al
    /// contratista ni al técnico les toca ver lo que el taller gana con ellos.
    /// </summary>
    [HttpGet("reports/shop")]
    public async Task<ActionResult<AssemblyShopReportDto>> Shop(
        [FromQuery] AssemblyQuery query, CancellationToken ct)
        => Ok(await reports.ShopAsync(query, ct));

    [HttpGet("reports/shop.xlsx")]
    [EnableRateLimiting(RateLimits.Documento)]
    public async Task<IActionResult> ShopExcel(
        [FromQuery] AssemblyQuery query, CancellationToken ct)
        => File(
            await reports.ShopExcelAsync(query, ct),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Lo del taller.xlsx");

    [HttpGet("reports/shop.pdf")]
    [EnableRateLimiting(RateLimits.Documento)]
    public async Task<IActionResult> ShopPdf(
        [FromQuery] AssemblyQuery query, CancellationToken ct)
        => File(await reports.ShopPdfAsync(query, ct), "application/pdf", "Lo del taller.pdf");

    [HttpGet("reports/pay")]
    public async Task<ActionResult<AssemblyPayReportDto>> Pay(
        [FromQuery] AssemblyQuery query, CancellationToken ct)
        => Ok(await reports.PayAsync(query, ct));

    [HttpGet("reports/pay.xlsx")]
    [EnableRateLimiting(RateLimits.Documento)]
    public async Task<IActionResult> PayExcel(
        [FromQuery] AssemblyQuery query, CancellationToken ct)
        => File(
            await reports.PayExcelAsync(query, ct),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Pago a tecnicos.xlsx");

    [HttpGet("reports/pay.pdf")]
    [EnableRateLimiting(RateLimits.Documento)]
    public async Task<IActionResult> PayPdf(
        [FromQuery] AssemblyQuery query, CancellationToken ct)
        => File(await reports.PayPdfAsync(query, ct), "application/pdf", "Pago a tecnicos.pdf");

    /// <summary>
    /// El Excel que el taller llevaba antes. Con <c>apply=false</c> solo enseña qué entraría.
    /// </summary>
    /// <remarks>
    /// No crea catálogo: la fila que nombre un modelo, una bodega o un técnico que no existe
    /// se rechaza con el motivo escrito. Crear catálogo desde una hoja ajena es cómo se
    /// terminan con el mismo modelo escrito de tres maneras.
    /// </remarks>
    [HttpPost("import")]
    [EnableRateLimiting(RateLimits.Carga)]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<AssemblyImportPreviewDto>> Import(
        IFormFile file, [FromQuery] Guid contractorId, [FromQuery] bool apply,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new AppException("Adjunte el archivo.");

        await using var stream = file.OpenReadStream();

        return Ok(apply
            ? await imports.CommitAsync(stream, contractorId, ct)
            : await imports.PreviewAsync(stream, contractorId, ct));
    }
}

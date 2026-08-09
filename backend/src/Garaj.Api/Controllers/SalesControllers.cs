using Garaj.Application.Common;
using Garaj.Application.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

/// <summary>
/// Ventas. Una venta es inmutable: si estuvo mal se anula con motivo y se hace otra, para que
/// los reportes siempre cuadren con la caja.
/// </summary>
[ApiController]
[Authorize]
[Route("api/sales")]
public class SalesController(ISaleService service) : ControllerBase
{
    /// <summary>El Dueño ve todas; el Cliente, solo sus facturas; el Técnico, ninguna.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<SaleListItemDto>>> List(
        [FromQuery] SaleQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleDetailDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    /// <summary>Venta directa de mostrador. Descuenta los repuestos de la bodega.</summary>
    [HttpPost]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<SaleDetailDto>> Create(
        CreateSaleRequest request, CancellationToken ct)
    {
        var sale = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = sale.Id }, sale);
    }

    /// <summary>
    /// Cierra la orden: factura los repuestos ya consumidos más la mano de obra de los pasos
    /// y, si se pide, la marca como entregada.
    /// </summary>
    [HttpPost("close-work-order")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<SaleDetailDto>> CloseWorkOrder(
        CloseWorkOrderRequest request, CancellationToken ct)
    {
        var sale = await service.CloseWorkOrderAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = sale.Id }, sale);
    }

    /// <summary>Registra un abono a una venta con saldo.</summary>
    [HttpPost("{id:guid}/payments")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<SaleDetailDto>> RegisterPayment(
        Guid id, RegisterPaymentRequest request, CancellationToken ct)
        => Ok(await service.RegisterPaymentAsync(id, request, ct));

    /// <summary>Borra un abono mal capturado. Para devolver dinero se anula la venta.</summary>
    [HttpDelete("{id:guid}/payments/{paymentId:guid}")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<SaleDetailDto>> RemovePayment(
        Guid id, Guid paymentId, CancellationToken ct)
        => Ok(await service.RemovePaymentAsync(id, paymentId, ct));

    /// <summary>La factura en PDF. El Cliente puede bajar las suyas; el Técnico, ninguna.</summary>
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var sale = await service.GetAsync(id, ct);
        return File(await service.PdfAsync(id, ct), "application/pdf", $"{sale.Number}.pdf");
    }

    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = AppPolicies.OwnerOnly)]
    public async Task<ActionResult<SaleDetailDto>> Void(
        Guid id, VoidSaleRequest request, CancellationToken ct)
        => Ok(await service.VoidAsync(id, request, ct));
}

/// <summary>
/// Reportes de ingresos. El desglose entre repuestos y mano de obra sale del tipo de línea de
/// la venta, así que siempre cuadra con lo facturado.
/// </summary>
[ApiController]
[Authorize(Policy = AppPolicies.OwnerOnly)]
[Route("api/reports")]
public class ReportsController(IReportService service) : ControllerBase
{
    /// <summary>Ingresos por día, semana o mes. Sin rango, el mes en curso.</summary>
    [HttpGet("revenue")]
    public async Task<ActionResult<RevenueReportDto>> Revenue(
        [FromQuery] RevenueQuery query, CancellationToken ct)
        => Ok(await service.RevenueAsync(query, ct));

    /// <summary>Lo que el Dueño mira al abrir el sistema por la mañana.</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardDto>> Dashboard(
        [FromQuery] Guid? branchId, CancellationToken ct)
        => Ok(await service.DashboardAsync(branchId, ct));

    [HttpGet("revenue.csv")]
    public async Task<IActionResult> RevenueCsv([FromQuery] RevenueQuery query, CancellationToken ct)
        => File(await service.RevenueCsvAsync(query, ct), "text/csv", "ingresos.csv");
}

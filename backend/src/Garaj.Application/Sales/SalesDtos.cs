using Garaj.Application.Common;
using Garaj.Domain.Enums;

namespace Garaj.Application.Sales;

public record SaleLineDto(
    Guid Id,
    LineType LineType,
    Guid? PartId,
    Guid? LaborServiceId,
    string Description,
    int Sequence,
    decimal Quantity,
    decimal UnitPrice,
    decimal UnitCost,
    decimal Discount,
    decimal Total);

public record SaleListItemDto(
    Guid Id,
    string Number,
    Guid BranchId,
    string BranchName,
    Guid? CustomerId,
    string? CustomerName,
    Guid? WorkOrderId,
    string? WorkOrderNumber,
    DateTimeOffset SaleDate,
    PaymentMethod PaymentMethod,
    decimal Total,
    bool IsVoided);

public record SaleDetailDto(
    Guid Id,
    string Number,
    Guid BranchId,
    string BranchName,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    Guid? WorkOrderId,
    string? WorkOrderNumber,
    string? VehicleLabel,
    DateTimeOffset SaleDate,
    PaymentMethod PaymentMethod,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxRate,
    decimal TaxTotal,
    decimal Total,
    decimal CostTotal,
    // Total menos costo. Solo lo ve el Dueño.
    decimal Margin,
    string Currency,
    string? Notes,
    bool IsVoided,
    string? VoidReason,
    IReadOnlyList<SaleLineDto> Lines);

/// <param name="PartId">Para una línea de repuesto: descuenta de la bodega de la sucursal.</param>
public record SaleLineRequest(
    LineType LineType,
    Guid? PartId,
    Guid? LaborServiceId,
    string? Description,
    decimal Quantity,
    decimal? UnitPrice,
    decimal Discount = 0);

/// <summary>Venta directa de mostrador: alguien entra, compra un repuesto y se va.</summary>
public record CreateSaleRequest(
    Guid BranchId,
    Guid? CustomerId,
    PaymentMethod PaymentMethod,
    DateTimeOffset? SaleDate,
    string? Notes,
    decimal? TaxRate,
    IReadOnlyList<SaleLineRequest> Lines);

/// <summary>
/// Cierre de la orden: la entrega al cliente y genera la venta con lo que se le hizo.
/// </summary>
public record CloseWorkOrderRequest(
    Guid WorkOrderId,
    PaymentMethod PaymentMethod,
    string? Notes,
    decimal? TaxRate,
    // Añade la mano de obra de los pasos que tengan servicio del catálogo asignado.
    bool IncludeLabor = true,
    // Marca la orden como entregada. Falso si el vehículo todavía no se lo llevan.
    bool MarkAsDelivered = true);

public record VoidSaleRequest(string Reason);

public record SaleQuery : PageQuery
{
    public Guid? BranchId { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? WorkOrderId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public bool IncludeVoided { get; init; }
}

// ---------- Reportes ----------

public enum RevenueGrouping
{
    Day = 1,
    Week = 2,
    Month = 3
}

/// <param name="PeriodLabel">Ya formateado para pintarlo tal cual: "08/08", "sem. 32", "ago 2026".</param>
public record RevenuePointDto(
    DateTimeOffset PeriodStart,
    string PeriodLabel,
    decimal PartsRevenue,
    decimal LaborRevenue,
    decimal Total,
    decimal Cost,
    decimal Margin,
    int SaleCount);

public record RevenueReportDto(
    DateTimeOffset From,
    DateTimeOffset To,
    RevenueGrouping GroupBy,
    string Currency,
    decimal PartsRevenue,
    decimal LaborRevenue,
    decimal Total,
    decimal Cost,
    decimal Margin,
    // Margen sobre el total, en porcentaje. 0 si no hubo ventas.
    decimal MarginPercent,
    int SaleCount,
    IReadOnlyList<RevenuePointDto> Points,
    IReadOnlyList<BranchRevenueDto> Branches,
    IReadOnlyList<TopPartDto> TopParts);

public record BranchRevenueDto(
    Guid BranchId,
    string BranchName,
    decimal PartsRevenue,
    decimal LaborRevenue,
    decimal Total,
    int SaleCount);

public record TopPartDto(
    Guid PartId,
    string Sku,
    string Name,
    decimal Quantity,
    decimal Revenue,
    decimal Margin);

public record RevenueQuery
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public RevenueGrouping GroupBy { get; init; } = RevenueGrouping.Day;
    public Guid? BranchId { get; init; }
}

/// <summary>Lo que el Dueño quiere ver al abrir el sistema por la mañana.</summary>
public record DashboardDto(
    string Currency,
    decimal RevenueToday,
    decimal RevenueWeek,
    decimal RevenueMonth,
    decimal MarginMonth,
    int OpenWorkOrders,
    int PendingRequests,
    int LateWorkOrders,
    int QuotesAwaitingResponse,
    int PartsBelowMinimum,
    IReadOnlyList<StatusCountDto> WorkOrdersByStatus,
    IReadOnlyList<RevenuePointDto> LastDays);

public record StatusCountDto(WorkOrderStatus Status, int Count);

public interface ISaleService
{
    Task<PagedResult<SaleListItemDto>> ListAsync(SaleQuery query, CancellationToken ct = default);
    Task<SaleDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Venta directa de mostrador.</summary>
    Task<SaleDetailDto> CreateAsync(CreateSaleRequest request, CancellationToken ct = default);

    /// <summary>Cierra la orden y genera la venta con los repuestos consumidos y la mano de obra.</summary>
    Task<SaleDetailDto> CloseWorkOrderAsync(
        CloseWorkOrderRequest request, CancellationToken ct = default);

    /// <summary>Anula la venta y devuelve los repuestos a la bodega. No la borra.</summary>
    Task<SaleDetailDto> VoidAsync(Guid id, VoidSaleRequest request, CancellationToken ct = default);

    /// <summary>La factura en PDF, para imprimirla o mandarla por WhatsApp.</summary>
    Task<byte[]> PdfAsync(Guid id, CancellationToken ct = default);
}

public interface IReportService
{
    Task<RevenueReportDto> RevenueAsync(RevenueQuery query, CancellationToken ct = default);
    Task<DashboardDto> DashboardAsync(Guid? branchId, CancellationToken ct = default);

    /// <summary>Las ventas del rango en CSV, para abrirlo en Excel.</summary>
    Task<byte[]> RevenueCsvAsync(RevenueQuery query, CancellationToken ct = default);
}

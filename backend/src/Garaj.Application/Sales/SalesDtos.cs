using Garaj.Application.Common;
using Garaj.Application.Quotes;
using Garaj.Application.Tenants;
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

public record SalePaymentDto(
    Guid Id,
    decimal Amount,
    PaymentMethod Method,
    DateTimeOffset PaidAt,
    string? Reference,
    string? Notes,
    string? RegisteredByName);

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
    decimal AmountPaid,
    decimal Balance,
    DateTimeOffset? DueDate,
    bool IsOverdue,
    bool IsVoided);

public record SaleDetailDto(
    Guid Id,
    string Number,
    Guid BranchId,
    string BranchName,
    // Dirección del establecimiento: la factura fiscal tiene que decir desde dónde se emite.
    string? BranchAddress,
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
    // Lo cobrado hasta hoy y lo que falta. Salen de sumar los abonos, no de un campo que
    // haya que mantener al día.
    decimal AmountPaid,
    decimal Balance,
    DateTimeOffset? DueDate,
    bool IsOverdue,
    // Régimen de facturación: null en la venta que sale sin CAI, que es comprobante de
    // entrega y no documento fiscal.
    string? FiscalNumber,
    string? FiscalCai,
    string? FiscalRangeText,
    DateTimeOffset? FiscalIssueDeadline,
    string? CustomerTaxId,
    // Identidad del cliente. La factura al consumidor final la exige arriba de L 10,000.
    string? CustomerDocumentId,
    IReadOnlyList<SaleLineDto> Lines,
    IReadOnlyList<SalePaymentDto> Payments);

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
/// <param name="DueDate">Solo en ventas a crédito: la fecha en que se acordó terminar de pagar.</param>
/// <param name="InitialPayment">
/// Lo que el cliente deja en el momento. Si se omite se cobra el total —la venta de
/// mostrador normal—; si viene, la diferencia queda como saldo pendiente.
/// </param>
public record CreateSaleRequest(
    Guid BranchId,
    Guid? CustomerId,
    PaymentMethod PaymentMethod,
    DateTimeOffset? SaleDate,
    string? Notes,
    decimal? TaxRate,
    IReadOnlyList<SaleLineRequest> Lines,
    DateTimeOffset? DueDate = null,
    decimal? InitialPayment = null,
    // Consume un número del rango autorizado por el SAR. Falso por defecto: se marca cuando
    // el cliente pide la factura, para no quemar un correlativo en cada venta.
    bool Fiscal = false,
    // RTN del cliente para esta factura. Si va vacío se usa el de su ficha, y si no tiene
    // ninguno la factura sale a consumidor final.
    string? CustomerTaxId = null,
    // A nombre de quién sale la factura, cuando no es a nombre del cliente que la pide:
    // suele ser la empresa dueña del RTN. Vacío usa el de su ficha, y si tampoco tiene,
    // su propio nombre.
    string? CustomerName = null);

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
    // Cobra la mano de obra de esta cotización en lugar de la de los pasos. Es lo que el
    // cliente aprobó, así que la factura le cuadra con lo que se le mandó por WhatsApp.
    Guid? LaborFromQuoteId = null,
    // Marca la orden como entregada. Falso si el vehículo todavía no se lo llevan.
    bool MarkAsDelivered = true,
    // Fecha acordada de pago, en las que se entregan a crédito.
    DateTimeOffset? DueDate = null,
    // Lo que el cliente deja al recoger. Omitido significa que paga todo.
    decimal? InitialPayment = null,
    // Emite con CAI, consumiendo un número del rango de la sucursal. Ver CreateSaleRequest.
    bool Fiscal = false,
    string? CustomerTaxId = null,
    string? CustomerName = null);

/// <summary>Un abono a una venta con saldo.</summary>
public record RegisterPaymentRequest(
    decimal Amount,
    PaymentMethod Method,
    DateTimeOffset? PaidAt,
    string? Reference,
    string? Notes);

public record VoidSaleRequest(string Reason);

public record SaleQuery : PageQuery
{
    public Guid? BranchId { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? WorkOrderId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public bool IncludeVoided { get; init; }

    /// <summary>Solo las que tienen saldo: la lista de cuentas por cobrar.</summary>
    public bool OnlyUnpaid { get; init; }

    /// <summary>
    /// Busca por nombre o teléfono del cliente, a nombre de quién salió la factura, número de
    /// venta o número de la orden. Es como se busca a alguien cuando llama a preguntar por su
    /// saldo: con lo que tenga a mano, no con un identificador.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>
    /// Filtra por vencimiento: `true` solo las vencidas, `false` solo las que todavía no
    /// vencen —incluidas las que se entregaron sin fecha acordada—, null todas. Solo tiene
    /// sentido junto a <see cref="OnlyUnpaid"/>: una venta pagada no vence.
    /// </summary>
    public bool? Overdue { get; init; }
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
    IReadOnlyList<TechnicianRevenueDto> Technicians,
    IReadOnlyList<TopPartDto> TopParts);

/// <summary>
/// Cuánto facturó lo que pasó por cada técnico.
/// </summary>
/// <remarks>
/// Se atribuye por el <b>técnico responsable de la orden</b>, no por quién hizo cada paso:
/// es quien responde por el trabajo completo, y es la única atribución que reparte también
/// los repuestos —un paso no los tiene—. Las ventas de mostrador no pasaron por nadie y se
/// agrupan aparte; contarlas en el reparto premiaría a un técnico por una venta que no hizo.
/// </remarks>
/// <param name="TechnicianId">Null en las ventas de mostrador.</param>
public record TechnicianRevenueDto(
    Guid? TechnicianId,
    string TechnicianName,
    decimal PartsRevenue,
    decimal LaborRevenue,
    decimal Total,
    decimal Cost,
    decimal Margin,
    int SaleCount);

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

    /// <summary>Deja solo lo facturado en órdenes de ese técnico. Excluye el mostrador.</summary>
    public Guid? TechnicianId { get; init; }
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
    // Cuánto se ha facturado y todavía no se ha cobrado, y cuánto de eso ya venció.
    decimal Receivables,
    decimal OverdueReceivables,
    IReadOnlyList<StatusCountDto> WorkOrdersByStatus,
    IReadOnlyList<RevenuePointDto> LastDays);

public record StatusCountDto(WorkOrderStatus Status, int Count);

// ---------- Estado de cuenta ----------

/// <summary>
/// Lo que un cliente debe hoy, factura por factura y con los abonos de cada una.
///
/// Es la respuesta a «¿cuánto le debo?», que hasta ahora había que armar a mano abriendo cada
/// venta. Solo trae las que tienen saldo: una factura pagada ya no es una cuenta pendiente, y
/// meterlas todas convertiría una hoja en un historial que nadie lee.
/// </summary>
public record CustomerStatementDto(
    Guid CustomerId,
    string CustomerName,
    /// <summary>
    /// El taller, para el encabezado de la página que abre el cliente: lo primero que ve tiene
    /// que ser la marca de su taller. <c>TenantLogoUrl</c> es relativa a la base de la API.
    /// </summary>
    string TenantName,
    string? TenantLogoUrl,
    string? TenantPhone,
    /// <summary>A nombre de quién salen sus facturas, si no es a su propio nombre.</summary>
    string? BillingName,
    string? TaxId,
    string Phone,
    string Currency,
    /// <summary>El corte: un estado de cuenta es de un momento, no de siempre.</summary>
    DateTimeOffset AsOf,
    decimal Total,
    /// <summary>De lo anterior, cuánto ya venció.</summary>
    decimal Overdue,
    IReadOnlyList<StatementSaleDto> Sales);

public record StatementSaleDto(
    string Number,
    string? WorkOrderNumber,
    string BranchName,
    DateTimeOffset SaleDate,
    DateTimeOffset? DueDate,
    bool IsOverdue,
    decimal Total,
    decimal AmountPaid,
    decimal Balance,
    IReadOnlyList<StatementPaymentDto> Payments);

/// <summary>
/// Un abono como lo ve el cliente: cuándo, cómo y cuánto. Sin quién lo recibió, que es un dato
/// interno de quién estaba en caja.
/// </summary>
public record StatementPaymentDto(
    DateTimeOffset PaidAt,
    PaymentMethod Method,
    string? Reference,
    decimal Amount);

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

    /// <summary>Registra un abono. Nunca por encima del saldo.</summary>
    Task<SaleDetailDto> RegisterPaymentAsync(
        Guid id, RegisterPaymentRequest request, CancellationToken ct = default);

    /// <summary>Borra un abono mal capturado. Es una corrección, no una devolución.</summary>
    Task<SaleDetailDto> RemovePaymentAsync(Guid id, Guid paymentId, CancellationToken ct = default);

    /// <summary>Lo que un cliente debe hoy, factura por factura y con sus abonos.</summary>
    Task<CustomerStatementDto> StatementAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>El estado de cuenta en PDF, para imprimirlo o mandarlo.</summary>
    Task<byte[]> StatementPdfAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>El enlace de WhatsApp con el mensaje ya escrito y el enlace público dentro.</summary>
    Task<WhatsAppLinkDto> StatementLinkAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>El estado de cuenta desde el enlace público, sin sesión.</summary>
    Task<CustomerStatementDto> StatementPublicAsync(Guid token, CancellationToken ct = default);

    Task<byte[]> StatementPdfPublicAsync(Guid token, CancellationToken ct = default);

    /// <summary>El logo del taller detrás de un token de estado de cuenta, para la página pública.</summary>
    Task<TenantLogo?> StatementLogoPublicAsync(Guid token, CancellationToken ct = default);
}

public interface IReportService
{
    Task<RevenueReportDto> RevenueAsync(RevenueQuery query, CancellationToken ct = default);
    Task<DashboardDto> DashboardAsync(Guid? branchId, CancellationToken ct = default);

    /// <summary>Las ventas del rango en CSV, para abrirlo en Excel.</summary>
    Task<byte[]> RevenueCsvAsync(RevenueQuery query, CancellationToken ct = default);
}

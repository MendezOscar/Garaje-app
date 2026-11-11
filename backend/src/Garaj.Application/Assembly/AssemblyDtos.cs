using Garaj.Application.Common;
using Garaj.Domain.Enums;

namespace Garaj.Application.Assembly;

/// <summary>Un contratista con su tabla de modelos: lo que paga y lo que se le paga al técnico.</summary>
/// <param name="BranchId">
/// La sucursal del taller que lleva el contrato. De aquí sale la de cada moto armada, para
/// no preguntársela al técnico, que está en la bodega del contratista y no la sabe.
/// </param>
public record AssemblyContractorDto(
    Guid Id,
    string Name,
    string? Notes,
    bool IsActive,
    IReadOnlyList<AssemblyRateDto> Rates,
    Guid? BranchId = null);

/// <param name="Charge">Lo que el contratista paga por armar una. No lo ve el técnico.</param>
/// <param name="TechnicianPay">Lo que el taller le paga al técnico que la arma.</param>
public record AssemblyRateDto(
    Guid ModelId,
    string ModelName,
    decimal Charge,
    decimal TechnicianPay);

public record SaveAssemblyContractorRequest(
    string Name, string? Notes, bool IsActive = true, Guid? BranchId = null);

/// <summary>
/// La tabla entera de un contratista, de una vez.
/// </summary>
/// <remarks>
/// Se manda completa y reemplaza lo que había: es una tabla que se llena de un tirón al abrir
/// el contrato y se repasa cuando se renegocia. Guardar fila por fila obligaría a llevar el
/// estado de cuáles se tocaron, para resolver un problema que no existe.
/// </remarks>
public record SaveAssemblyRatesRequest(IReadOnlyList<SaveAssemblyRateLine> Rates);

public record SaveAssemblyRateLine(Guid ModelId, decimal Charge, decimal TechnicianPay);

public record AssemblyModelDto(Guid Id, string Name, bool IsActive, string? VinPrefix = null);

/// <param name="VinPrefix">
/// Los primeros caracteres del VIN de este modelo. Vacío se admite: entonces el modelo se
/// propone solo a partir de la segunda moto de cada lote, aprendido de lo ya registrado.
/// </param>
public record SaveAssemblyModelRequest(
    string Name, bool IsActive = true, string? VinPrefix = null);

public record AssemblyWarehouseDto(Guid Id, string Name, bool IsActive);

public record SaveAssemblyWarehouseRequest(string Name, bool IsActive = true);

/// <summary>
/// El catálogo del contrato: con quién se trabaja, qué modelos se arman y en qué bodegas.
/// </summary>
/// <remarks>
/// Es del Dueño entero. De aquí salen los dos precios, y uno de ellos —lo que se le cobra al
/// contratista— es justo lo que no puede ver el técnico.
/// </remarks>
/// <summary>
/// Los ajustes del armado que no son catálogo.
/// </summary>
/// <param name="ExpensePerMotorcycle">
/// Lo que al taller le cuesta armar una moto aparte del pago al técnico: transporte a la
/// bodega, herramienta, lo que el armado se lleve y no dependa del modelo. Un número para
/// todas, porque hoy lo es. Se copia a cada moto al registrarla.
/// </param>
public record AssemblySettingsDto(decimal ExpensePerMotorcycle);

public record SaveAssemblySettingsRequest(decimal ExpensePerMotorcycle);

public interface IAssemblyCatalogService
{
    Task<AssemblySettingsDto> SettingsAsync(CancellationToken ct = default);

    Task<AssemblySettingsDto> SaveSettingsAsync(
        SaveAssemblySettingsRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<AssemblyContractorDto>> ContractorsAsync(CancellationToken ct = default);

    Task<AssemblyContractorDto> SaveContractorAsync(
        Guid? id, SaveAssemblyContractorRequest request, CancellationToken ct = default);

    Task<AssemblyContractorDto> SaveRatesAsync(
        Guid contractorId, SaveAssemblyRatesRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<AssemblyModelDto>> ModelsAsync(CancellationToken ct = default);

    Task<AssemblyModelDto> SaveModelAsync(
        Guid? id, SaveAssemblyModelRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<AssemblyWarehouseDto>> WarehousesAsync(CancellationToken ct = default);

    Task<AssemblyWarehouseDto> SaveWarehouseAsync(
        Guid? id, SaveAssemblyWarehouseRequest request, CancellationToken ct = default);
}

/// <summary>Una moto armada, como la ve quien la registró o quien la factura.</summary>
/// <param name="Charge">
/// Lo que se le cobra al contratista. <b>Null para el técnico</b>: no se le esconde en la
/// pantalla, no sale del servidor.
/// </param>
public record MotorcycleAssemblyDto(
    Guid Id,
    DateOnly AssembledOn,
    string Serial,
    Guid ModelId,
    string ModelName,
    Guid ContractorId,
    string ContractorName,
    Guid WarehouseId,
    string WarehouseName,
    Guid TechnicianUserId,
    string TechnicianName,
    /// <summary>
    /// Lo que se le paga por armarla. <b>Null cuando pregunta un técnico</b>: lo que gana por
    /// moto es cosa del dueño y del corte de pago, y lo que no se manda no se puede mirar.
    /// </summary>
    decimal? TechnicianPay,
    string? Notes,
    decimal? Charge);

/// <param name="TechnicianUserId">
/// Quién la armó. Lo manda el panel, donde el dueño registra la moto que el técnico le
/// reportó por teléfono. Desde la app va vacío y se usa a quien está dentro: un técnico
/// solo registra lo suyo.
/// </param>
public record RegisterMotorcycleRequest(
    string Serial,
    Guid ModelId,
    Guid ContractorId,
    Guid WarehouseId,
    DateOnly? AssembledOn = null,
    Guid? BranchId = null,
    string? Notes = null,
    Guid? TechnicianUserId = null);

/// <summary>
/// Lo que el técnico necesita a mano para registrar sin teclear: con quién se trabaja, qué
/// modelos y en qué bodegas. Sin un solo precio.
/// </summary>
/// <param name="Technicians">
/// A quién se le puede apuntar la moto. <b>Vacía para el técnico</b>: él solo registra lo
/// suyo. Al dueño le llega la lista porque sigue recibiendo fotos por WhatsApp y registra él
/// mismo lo que armó otro.
/// </param>
public record AssemblyPickListsDto(
    IReadOnlyList<AssemblyNameDto> Contractors,
    IReadOnlyList<AssemblyNameDto> Models,
    IReadOnlyList<AssemblyNameDto> Warehouses,
    IReadOnlyList<AssemblyNameDto> Technicians);

public record AssemblyNameDto(Guid Id, string Name);

/// <summary>
/// Lo que lleva armado quien pregunta en un periodo, con lo que suma.
/// </summary>
/// <param name="Pay">Lo que se le debe por esas motos. Es su número, no el del taller.</param>
/// <param name="Pay">
/// Lo que lleva ganado en el periodo. Null para el propio técnico: su pantalla cuenta motos,
/// no lempiras. Lo lleva con valor solo cuando pregunta quien manda.
/// </param>
public record MyAssembliesDto(
    DateOnly From,
    DateOnly To,
    int Count,
    decimal? Pay,
    IReadOnlyList<MotorcycleAssemblyDto> Items);

/// <summary>
/// El modelo que probablemente le corresponde a una serie, aprendido de lo ya registrado.
/// </summary>
/// <remarks>
/// No hay tabla que comprar: los primeros ocho caracteres del VIN son el fabricante y la
/// descripción del vehículo, y lo que significan es la tabla interna de cada marca. Así que
/// se aprende de este taller: la primera moto de cada prefijo se teclea, y de la segunda en
/// adelante se propone. Es siempre una propuesta; la confirma quien tiene la moto delante.
/// </remarks>
public record ModelGuessDto(Guid? ModelId, string? ModelName, int SeenBefore);

public interface IMotorcycleAssemblyService
{
    Task<AssemblyPickListsDto> PickListsAsync(CancellationToken ct = default);

    Task<MotorcycleAssemblyDto> RegisterAsync(
        RegisterMotorcycleRequest request, CancellationToken ct = default);

    Task<MyAssembliesDto> MineAsync(
        DateOnly? from, DateOnly? to, CancellationToken ct = default);

    Task<ModelGuessDto> GuessModelAsync(string serial, CancellationToken ct = default);
}

/// <summary>Lo que el dueño filtra cuando va a facturar o a pagar.</summary>
public record AssemblyQuery : PageQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? ContractorId { get; init; }
    public Guid? TechnicianUserId { get; init; }
    public Guid? WarehouseId { get; init; }
    public Guid? ModelId { get; init; }

    /// <summary>Solo lo que todavía no entra en un corte de facturación.</summary>
    public bool OnlyUnbilled { get; init; }

    /// <summary>Solo lo que todavía no entra en un corte de pago.</summary>
    public bool OnlyUnpaid { get; init; }

    /// <summary>Parte de la serie, como se teclea de la calcomanía.</summary>
    public string? Serial { get; init; }

    /// <summary>
    /// Solo las motos que entraron en ese corte. Es lo que hace que el reporte de una factura
    /// se pueda volver a sacar tal cual meses después, sin depender de un filtro.
    /// </summary>
    public Guid? SettlementId { get; init; }
}

/// <summary>Una fila de la lista del dueño: la moto con su estado de corte.</summary>
public record AssemblyRowDto(
    Guid Id,
    DateOnly AssembledOn,
    string Serial,
    string ModelName,
    string ContractorName,
    string WarehouseName,
    string TechnicianName,
    decimal Charge,
    decimal TechnicianPay,
    string? Notes,
    bool Billed,
    bool Paid,
    /// <summary>Lo que le costó al taller, aparte del pago. Nunca viaja al contratista.</summary>
    decimal Expense = 0m,
    // Los ids, para poder precargar el formulario de corrección sin tener que adivinarlos
    // desde los nombres —hay modelos que se llaman casi igual—.
    Guid ModelId = default,
    Guid ContractorId = default,
    Guid WarehouseId = default,
    Guid TechnicianUserId = default);

/// <summary>La página, con lo que suma el filtro completo y no solo la página.</summary>
public record AssemblyPageDto(
    IReadOnlyList<AssemblyRowDto> Items,
    int Total,
    int Page,
    int PageSize,
    decimal TotalCharge,
    decimal TotalPay,
    decimal TotalExpense = 0m);

public record CorrectMotorcycleRequest(
    string Serial,
    Guid ModelId,
    Guid ContractorId,
    Guid WarehouseId,
    Guid TechnicianUserId,
    DateOnly AssembledOn,
    string? Notes = null,
    /// <summary>
    /// Importes a mano. Vacíos, se recalculan de la tarifa de hoy: es lo que se quiere cuando
    /// la moto se registró con el modelo equivocado.
    /// </summary>
    decimal? Charge = null,
    decimal? TechnicianPay = null);

/// <summary>Un corte ya cerrado.</summary>
public record AssemblySettlementDto(
    Guid Id,
    AssemblySettlementKind Kind,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    Guid? ContractorId,
    string? ContractorName,
    Guid? TechnicianUserId,
    string? TechnicianName,
    int Count,
    decimal Total,
    string? Reference,
    DateTimeOffset ClosedAt,
    string? ClosedByName,
    /// <summary>
    /// El día en que el contratista pagó esta factura, o null mientras siga por cobrar.
    /// Siempre null en un corte de pago: ahí cerrar ya es entregar la plata.
    /// </summary>
    DateOnly? CollectedOn = null,
    string? CollectedReference = null);

/// <param name="On">El día del pago. Vacío, el de hoy.</param>
/// <param name="Reference">El recibo, la transferencia o el cheque.</param>
public record CollectSettlementRequest(DateOnly? On = null, string? Reference = null);

/// <param name="Reference">El número de factura o de la nota de pago.</param>
/// <param name="Total">
/// Lo que de verdad se le paga, cuando no es la suma de sus motos. Solo en un corte de pago:
/// hay acuerdos que no salen ni de la tarifa del modelo ni del fijo por moto —un bono, un
/// descuento acordado, la quincena redondeada—. Vacío, se usa la suma.
/// </param>
public record CloseSettlementRequest(
    AssemblySettlementKind Kind,
    DateOnly From,
    DateOnly To,
    Guid? ContractorId = null,
    Guid? TechnicianUserId = null,
    string? Reference = null,
    decimal? Total = null);

/// <summary>Una fila del Excel histórico, ya leída y con su veredicto.</summary>
/// <param name="Problem">Por qué no entra, o null si entra.</param>
public record AssemblyImportRowDto(
    int Row,
    string Serial,
    string Model,
    string Technician,
    string Warehouse,
    DateOnly? AssembledOn,
    decimal? Charge,
    string? Problem);

/// <param name="WillImport">Cuántas entrarían. El resto queda enumerado con su motivo.</param>
public record AssemblyImportPreviewDto(
    int WillImport,
    int Rejected,
    IReadOnlyList<AssemblyImportRowDto> Rows);

public interface IAssemblyAdminService
{
    Task<AssemblyPageDto> ListAsync(AssemblyQuery query, CancellationToken ct = default);

    Task<AssemblyRowDto> CorrectAsync(
        Guid id, CorrectMotorcycleRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<AssemblySettlementDto>> SettlementsAsync(CancellationToken ct = default);

    Task<AssemblySettlementDto> CloseAsync(
        CloseSettlementRequest request, CancellationToken ct = default);

    Task ReopenAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Anotar que el contratista pagó esa factura. Solo en un corte de facturación.
    /// </summary>
    Task<AssemblySettlementDto> CollectAsync(
        Guid id, CollectSettlementRequest request, CancellationToken ct = default);

    /// <summary>Quitarle el cobro: se anotó por error, o el cheque rebotó.</summary>
    Task<AssemblySettlementDto> UncollectAsync(Guid id, CancellationToken ct = default);

    /// <summary>Quiénes han armado motos, para el filtro y para el reporte de pago.</summary>
    Task<IReadOnlyList<AssemblyNameDto>> TechniciansAsync(CancellationToken ct = default);
}

/// <summary>
/// El reporte de facturación: lo que se le cobra a un contratista por un periodo.
/// </summary>
/// <remarks>
/// Las columnas son las de la hoja que el contratista ya recibe hoy —fecha, serie, modelo,
/// técnico, cobro, bodega—, a propósito: el que lo tiene que aceptar es el que paga, y un
/// formato nuevo abre una conversación que nadie pidió.
/// </remarks>
public record AssemblyBillingReportDto(
    string TenantName,
    string ContractorName,
    DateOnly From,
    DateOnly To,
    int Count,
    decimal Total,
    IReadOnlyList<AssemblyRowDto> Rows,
    /// <summary>El número de factura, cuando la hoja sale de un corte ya cerrado.</summary>
    string? Reference = null);

/// <summary>Lo que se le debe a cada técnico por un periodo.</summary>
public record AssemblyPayReportDto(
    string TenantName,
    DateOnly From,
    DateOnly To,
    int Count,
    decimal Total,
    IReadOnlyList<AssemblyPayLineDto> Lines,
    /// <summary>La nota de pago, cuando la hoja sale de un corte ya cerrado.</summary>
    string? Reference = null);

public record AssemblyPayLineDto(
    Guid TechnicianUserId,
    string TechnicianName,
    int Count,
    decimal Pay);

/// <summary>
/// El reporte del taller: las cuatro columnas de plata de cada moto.
/// </summary>
/// <remarks>
/// Es el único de los tres que lleva el gasto y el margen, y por eso <b>no sale del taller</b>:
/// la hoja del contratista enseñaría lo que el taller gana con él, y la del técnico, lo mismo.
/// Esto se queda del lado de adentro, que es donde sirve.
/// </remarks>
public record AssemblyShopReportDto(
    string TenantName,
    DateOnly From,
    DateOnly To,
    int Count,
    decimal TotalCharge,
    decimal TotalPay,
    decimal TotalExpense,
    IReadOnlyList<AssemblyRowDto> Rows)
{
    /// <summary>Lo que de verdad le queda al taller en el periodo.</summary>
    public decimal TotalMargin => TotalCharge - TotalPay - TotalExpense;
}

public interface IAssemblyReportService
{
    Task<AssemblyShopReportDto> ShopAsync(AssemblyQuery query, CancellationToken ct = default);

    Task<byte[]> ShopExcelAsync(AssemblyQuery query, CancellationToken ct = default);
    Task<byte[]> ShopPdfAsync(AssemblyQuery query, CancellationToken ct = default);

    /// <summary>
    /// La hoja de un corte ya cerrado: exactamente las motos que entraron en él.
    /// </summary>
    /// <remarks>
    /// Es la que se le manda al contratista para que pague, y la que se vuelve a sacar el día
    /// que discuta la factura. No sale de un filtro —un filtro de hoy puede dar otra cosa que
    /// el de aquel día— sino del corte mismo, que ya congeló qué motos lo componen.
    /// </remarks>
    Task<byte[]> SettlementExcelAsync(Guid settlementId, CancellationToken ct = default);
    Task<byte[]> SettlementPdfAsync(Guid settlementId, CancellationToken ct = default);

    Task<AssemblyBillingReportDto> BillingAsync(
        AssemblyQuery query, CancellationToken ct = default);

    Task<AssemblyPayReportDto> PayAsync(AssemblyQuery query, CancellationToken ct = default);

    Task<byte[]> BillingExcelAsync(AssemblyQuery query, CancellationToken ct = default);
    Task<byte[]> BillingPdfAsync(AssemblyQuery query, CancellationToken ct = default);
    Task<byte[]> PayExcelAsync(AssemblyQuery query, CancellationToken ct = default);
    Task<byte[]> PayPdfAsync(AssemblyQuery query, CancellationToken ct = default);
}

/// <summary>
/// Traerse el Excel que el taller llevaba antes. De una sola vez y deliberadamente
/// conservador: no crea catálogo, y lo que no calza se rechaza con el motivo escrito.
/// </summary>
public interface IAssemblyImportService
{
    Task<AssemblyImportPreviewDto> PreviewAsync(
        Stream excel, Guid contractorId, CancellationToken ct = default);

    Task<AssemblyImportPreviewDto> CommitAsync(
        Stream excel, Guid contractorId, CancellationToken ct = default);
}

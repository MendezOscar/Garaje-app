using Garaj.Domain.Enums;

namespace Garaj.Application.Assistant;

/// <summary>
/// La lectura del negocio: una vez por semana, qué pasó y qué conviene hacer.
/// </summary>
/// <remarks>
/// No gasta créditos del taller. La mandamos nosotros, y cobrarle por algo que no pidió no se
/// puede explicar.
/// </remarks>
public interface IBusinessReadingService
{
    /// <summary>Las lecturas guardadas, la más reciente primero.</summary>
    Task<IReadOnlyList<BusinessReadingListItemDto>> ListAsync(CancellationToken ct = default);

    Task<BusinessReadingDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Arma el periodo, calcula los números y le pide al modelo que los lea. Si ya existe la
    /// de ese periodo, devuelve la que hay: pedirla dos veces el mismo lunes gastaría dos
    /// llamadas para decir lo mismo.
    /// </summary>
    Task<BusinessReadingDto> GenerateAsync(
        BusinessReadingKind kind, CancellationToken ct = default);

    Task<byte[]> PdfAsync(Guid id, CancellationToken ct = default);
}

public record BusinessReadingListItemDto(
    Guid Id,
    BusinessReadingKind Kind,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateTimeOffset CreatedAt);

/// <param name="Content">
/// La lectura tal como la escribió el modelo, en Markdown sencillo: títulos con <c>##</c>,
/// viñetas con <c>-</c> y negrita con <c>**</c>. El panel la pinta; nadie la edita.
/// </param>
/// <param name="Message">
/// Por qué no hay lectura, cuando no la hay. Sin volumen no se genera, y decirlo es parte de
/// la respuesta: inventar una tendencia con cuatro datos es peor que no decir nada.
/// </param>
public record BusinessReadingDto(
    Guid? Id,
    BusinessReadingKind Kind,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string? Content,
    string? Message,
    DateTimeOffset? CreatedAt);

/// <summary>
/// Los números del periodo, ya calculados. Es lo único que viaja al modelo.
/// </summary>
/// <remarks>
/// Todo lo de aquí sale de SQL. La IA recibe los números hechos y escribe la lectura; nunca se
/// le pide que sume, que divida ni que saque un porcentaje. Y no lleva un solo nombre de
/// cliente ni una sola factura: para leer un mes no hace falta saber quién pagó.
/// </remarks>
public record BusinessFiguresDto(
    string Currency,
    DateOnly From,
    DateOnly To,
    decimal Revenue,
    decimal PartsRevenue,
    decimal LaborRevenue,
    decimal Cost,
    decimal Margin,
    int SaleCount,
    int CounterSaleCount,
    int OrdersOpened,
    int OrdersDelivered,
    int OrdersLate,
    int QuotesSent,
    int QuotesApproved,
    int QuotesAwaiting,
    decimal Receivables,
    decimal OverdueReceivables,
    int PartsBelowMinimum,
    decimal Expenses,
    IReadOnlyList<BusinessBranchDto> Branches,
    IReadOnlyList<BusinessTechnicianDto> Technicians,
    IReadOnlyList<BusinessPartDto> TopParts);

public record BusinessBranchDto(string Name, decimal Revenue, int SaleCount);

/// <param name="AutoClosedEntries">
/// Tramos que cerró el sistema porque nadie los paró. Va en el paquete para que la lectura
/// pueda decir que ese tiempo es un techo y no el dato exacto.
/// </param>
public record BusinessTechnicianDto(
    string Name,
    int Orders,
    decimal Hours,
    decimal Revenue,
    decimal Margin,
    int AutoClosedEntries);

public record BusinessPartDto(string Name, decimal Quantity, decimal Revenue, decimal Margin);

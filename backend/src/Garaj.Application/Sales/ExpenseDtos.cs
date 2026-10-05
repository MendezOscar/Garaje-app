using Garaj.Application.Common;
using Garaj.Domain.Enums;

namespace Garaj.Application.Sales;

/// <param name="PhotoCount">Comprobantes adjuntos. Un gasto sin comprobante se puede discutir.</param>
public record ExpenseDto(
    Guid Id,
    Guid BranchId,
    string BranchName,
    ExpenseCategory Category,
    string Description,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string? SupplierName,
    DateTimeOffset ExpenseDate,
    string? Notes,
    string? CreatedByName,
    int PhotoCount);

public record SaveExpenseRequest(
    Guid BranchId,
    ExpenseCategory Category,
    string Description,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string? SupplierName,
    DateTimeOffset? ExpenseDate,
    string? Notes);

public record ExpenseQuery : PageQuery
{
    public Guid? BranchId { get; init; }
    public ExpenseCategory? Category { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }

    /// <summary>Por descripción o proveedor, que es como se busca un gasto que uno recuerda.</summary>
    public string? Search { get; init; }
}

// ---------- Estado de resultados ----------

/// <summary>
/// Qué dejó el taller en un periodo: lo que entró, lo que costó lo vendido, y lo que se gastó
/// en tenerlo abierto.
/// </summary>
/// <remarks>
/// Los ingresos y el costo de lo vendido salen de las ventas; los gastos, del registro de
/// gastos. La compra de repuestos no aparece como gasto a propósito: es inventario hasta que
/// se vende, y entonces entra como costo de ventas. Contarla en los dos lados restaría la
/// misma plata dos veces.
/// </remarks>
/// <param name="GrossProfit">Ingresos menos costo de lo vendido.</param>
/// <param name="NetProfit">Utilidad bruta menos gastos.</param>
public record IncomeStatementDto(
    DateTimeOffset From,
    DateTimeOffset To,
    string Currency,
    decimal Revenue,
    decimal PartsRevenue,
    decimal LaborRevenue,
    decimal CostOfSales,
    decimal GrossProfit,
    // Margen bruto sobre los ingresos, en porcentaje. 0 si no hubo ingresos.
    decimal GrossMarginPercent,
    decimal ExpenseTotal,
    decimal NetProfit,
    decimal NetMarginPercent,
    IReadOnlyList<ExpenseGroupDto> Expenses,
    // El mismo cálculo del periodo anterior, del mismo largo. Null si no se pidió comparar.
    IncomeStatementSummaryDto? Previous);

public record ExpenseGroupDto(ExpenseCategory Category, decimal Amount, int Count);

/// <summary>Lo mínimo del periodo anterior para poner al lado: contra qué se compara.</summary>
public record IncomeStatementSummaryDto(
    decimal Revenue,
    decimal GrossProfit,
    decimal ExpenseTotal,
    decimal NetProfit);

public record IncomeStatementQuery
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public Guid? BranchId { get; init; }

    /// <summary>Trae además el periodo anterior del mismo largo, para comparar.</summary>
    public bool ComparePrevious { get; init; } = true;
}

public interface IExpenseService
{
    Task<PagedResult<ExpenseDto>> ListAsync(ExpenseQuery query, CancellationToken ct = default);
    Task<ExpenseDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ExpenseDto> CreateAsync(SaveExpenseRequest request, CancellationToken ct = default);
    Task<ExpenseDto> UpdateAsync(Guid id, SaveExpenseRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>El estado de resultados del periodo.</summary>
    Task<IncomeStatementDto> IncomeStatementAsync(
        IncomeStatementQuery query, CancellationToken ct = default);
}

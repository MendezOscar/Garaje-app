using Garaj.Application.Common;
using Garaj.Application.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garaj.Api.Controllers;

/// <summary>
/// Gastos del taller y el estado de resultados. Solo el Dueño: lo que entra y lo que sale de
/// la caja es del negocio.
/// </summary>
[ApiController]
[Route("api/expenses")]
[Authorize]
public class ExpensesController(IExpenseService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ExpenseDto>>> List(
        [FromQuery] ExpenseQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    /// <summary>
    /// Qué dejó el taller en el periodo: ingresos, costo de lo vendido, utilidad bruta,
    /// gastos por categoría y utilidad neta. Sin fechas, el mes corriente.
    /// </summary>
    [HttpGet("income-statement")]
    public async Task<ActionResult<IncomeStatementDto>> IncomeStatement(
        [FromQuery] IncomeStatementQuery query, CancellationToken ct)
        => Ok(await service.IncomeStatementAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExpenseDto>> Get(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> Create(SaveExpenseRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ExpenseDto>> Update(
        Guid id, SaveExpenseRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}

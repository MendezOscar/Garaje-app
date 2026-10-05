using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Sales;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Gastos del taller y el estado de resultados que sale de ellos.
/// </summary>
/// <remarks>
/// Solo el Dueño. Lo que entra y lo que sale de la caja es del negocio, no del técnico.
/// </remarks>
public class ExpenseService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : IExpenseService
{
    public async Task<PagedResult<ExpenseDto>> ListAsync(
        ExpenseQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        var q = db.Expenses.AsNoTracking();

        if (query.Category is { } category) q = q.Where(e => e.Category == category);
        if (query.EmployeeUserId is { } employeeId) q = q.Where(e => e.EmployeeUserId == employeeId);
        if (query.From is { } from) q = q.Where(e => e.ExpenseDate >= from.ToUniversalTime());
        if (query.To is { } to) q = q.Where(e => e.ExpenseDate <= to.ToUniversalTime());

        if (query.BranchId is { } branchId)
        {
            scope.EnsureBranchAllowed(branchId);
            q = q.Where(e => e.BranchId == branchId);
        }

        if (query.Search?.Trim() is { Length: > 0 } search)
        {
            var texto = $"%{search}%";
            q = q.Where(e => EF.Functions.ILike(e.Description, texto)
                || (e.SupplierName != null && EF.Functions.ILike(e.SupplierName, texto)));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(e => e.ExpenseDate)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(e => new ExpenseDto(
                e.Id,
                e.BranchId,
                e.Branch.Name,
                e.Category,
                e.Description,
                e.Amount,
                e.PaymentMethod,
                e.SupplierName,
                e.ExpenseDate,
                e.Notes,
                db.Users.Where(u => u.Id == e.CreatedByUserId)
                    .Select(u => u.FullName).FirstOrDefault(),
                db.MediaAttachments.Count(m =>
                    m.OwnerType == MediaOwnerType.Expense && m.OwnerId == e.Id),
                e.EmployeeUserId,
                db.Users.Where(u => u.Id == e.EmployeeUserId)
                    .Select(u => u.FullName).FirstOrDefault()))
            .ToListAsync(ct);

        return new PagedResult<ExpenseDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<ExpenseDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        return await UnoAsync(id, ct) ?? throw new NotFoundException("El gasto no existe.");
    }

    public async Task<ExpenseDto> CreateAsync(
        SaveExpenseRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();
        scope.EnsureBranchAllowed(request.BranchId);

        Validar(request);

        var expense = new Expense
        {
            BranchId = request.BranchId,
            CreatedByUserId = scope.UserId
        };

        Aplicar(expense, request);

        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);

        return await UnoAsync(expense.Id, ct) ?? throw new NotFoundException("El gasto no existe.");
    }

    public async Task<ExpenseDto> UpdateAsync(
        Guid id, SaveExpenseRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();
        scope.EnsureBranchAllowed(request.BranchId);

        Validar(request);

        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("El gasto no existe.");

        expense.BranchId = request.BranchId;
        Aplicar(expense, request);

        await db.SaveChangesAsync(ct);

        return await UnoAsync(expense.Id, ct) ?? throw new NotFoundException("El gasto no existe.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("El gasto no existe.");

        db.Expenses.Remove(expense);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IncomeStatementDto> IncomeStatementAsync(
        IncomeStatementQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        if (query.BranchId is { } branchId) scope.EnsureBranchAllowed(branchId);

        // Por defecto, el mes corriente: es el periodo que el dueño mira.
        var hoy = clock.UtcNow;
        var desde = (query.From ?? new DateTimeOffset(hoy.Year, hoy.Month, 1, 0, 0, 0, TimeSpan.Zero))
            .ToUniversalTime();
        var hasta = (query.To ?? hoy).ToUniversalTime();

        if (hasta < desde) (desde, hasta) = (hasta, desde);

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct)
            ?? throw new NotFoundException("El taller no existe.");

        var actual = await CalcularAsync(desde, hasta, query.BranchId, ct);
        var gastos = await GastosPorCategoriaAsync(desde, hasta, query.BranchId, ct);

        var gastoTotal = gastos.Sum(g => g.Amount);
        var utilidadNeta = actual.GrossProfit - gastoTotal;

        // El periodo anterior del mismo largo, para poner al lado. Sin él, un número suelto no
        // dice nada: un millón de ingresos puede ser un buen mes o la mitad del anterior.
        IncomeStatementSummaryDto? anterior = null;
        if (query.ComparePrevious)
        {
            var largo = hasta - desde;
            var antesDesde = desde - largo;
            var antesHasta = desde;

            var previo = await CalcularAsync(antesDesde, antesHasta, query.BranchId, ct);
            var gastoPrevio = (await GastosPorCategoriaAsync(antesDesde, antesHasta, query.BranchId, ct))
                .Sum(g => g.Amount);

            anterior = new IncomeStatementSummaryDto(
                previo.Revenue,
                previo.GrossProfit,
                gastoPrevio,
                previo.GrossProfit - gastoPrevio);
        }

        return new IncomeStatementDto(
            desde,
            hasta,
            tenant.Currency,
            actual.Revenue,
            actual.PartsRevenue,
            actual.LaborRevenue,
            actual.CostOfSales,
            actual.GrossProfit,
            Porcentaje(actual.GrossProfit, actual.Revenue),
            gastoTotal,
            utilidadNeta,
            Porcentaje(utilidadNeta, actual.Revenue),
            gastos,
            anterior);
    }

    /// <summary>
    /// Lo que entró y lo que costó, del lado de las ventas. Las anuladas no cuentan: esa
    /// venta no existió, y dejarla inflaría los ingresos del mes.
    /// </summary>
    private async Task<(decimal Revenue, decimal PartsRevenue, decimal LaborRevenue,
        decimal CostOfSales, decimal GrossProfit)> CalcularAsync(
        DateTimeOffset desde, DateTimeOffset hasta, Guid? branchId, CancellationToken ct)
    {
        var ventas = db.Sales.AsNoTracking()
            .Where(s => !s.IsVoided && s.SaleDate >= desde && s.SaleDate <= hasta);

        if (branchId is { } id) ventas = ventas.Where(s => s.BranchId == id);

        var totales = await ventas
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Revenue = g.Sum(s => s.Total),
                Cost = g.Sum(s => s.CostTotal)
            })
            .FirstOrDefaultAsync(ct);

        var porTipo = await ventas
            .SelectMany(s => s.Lines)
            .GroupBy(l => l.LineType)
            .Select(g => new { Tipo = g.Key, Total = g.Sum(l => l.Total) })
            .ToListAsync(ct);

        var revenue = totales?.Revenue ?? 0;
        var cost = totales?.Cost ?? 0;

        return (
            revenue,
            porTipo.FirstOrDefault(x => x.Tipo == LineType.Part)?.Total ?? 0,
            porTipo.FirstOrDefault(x => x.Tipo == LineType.Labor)?.Total ?? 0,
            cost,
            revenue - cost);
    }

    private async Task<List<ExpenseGroupDto>> GastosPorCategoriaAsync(
        DateTimeOffset desde, DateTimeOffset hasta, Guid? branchId, CancellationToken ct)
    {
        var q = db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= desde && e.ExpenseDate <= hasta);

        if (branchId is { } id) q = q.Where(e => e.BranchId == id);

        // El orden se hace en memoria a propósito: ordenar por una propiedad del DTO ya
        // proyectado no se puede traducir a SQL, y son nueve filas como máximo —una por
        // categoría—, así que traerlas y ordenarlas aquí no cuesta nada.
        var grupos = await q
            .GroupBy(e => e.Category)
            .Select(g => new ExpenseGroupDto(g.Key, g.Sum(e => e.Amount), g.Count()))
            .ToListAsync(ct);

        return grupos.OrderByDescending(g => g.Amount).ToList();
    }

    private static decimal Porcentaje(decimal parte, decimal total) =>
        total == 0 ? 0 : Math.Round(parte / total * 100, 2);

    private static void Validar(SaveExpenseRequest request)
    {
        if (request.Amount <= 0)
            throw new AppException("El monto del gasto tiene que ser mayor que cero.");

        if (string.IsNullOrWhiteSpace(request.Description))
            throw new AppException("Escriba en qué se gastó.");
    }

    private void Aplicar(Expense expense, SaveExpenseRequest request)
    {
        expense.Category = request.Category;
        expense.Description = request.Description.Trim()[..Math.Min(request.Description.Trim().Length, 300)];
        expense.Amount = request.Amount;
        expense.PaymentMethod = request.PaymentMethod;
        expense.SupplierName = Recortar(request.SupplierName, 200);
        expense.Notes = Recortar(request.Notes, 1000);
        expense.ExpenseDate = (request.ExpenseDate ?? clock.UtcNow).ToUniversalTime();

        // Solo tiene sentido en un salario: en los demás gastos, a quién se le pagó es el
        // proveedor, que va por su nombre.
        expense.EmployeeUserId = request.Category == ExpenseCategory.Salaries
            ? request.EmployeeUserId
            : null;
    }

    private static string? Recortar(string? value, int max) =>
        value?.Trim() is { Length: > 0 } text ? text[..Math.Min(text.Length, max)] : null;

    private async Task<ExpenseDto?> UnoAsync(Guid id, CancellationToken ct) =>
        await db.Expenses.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new ExpenseDto(
                e.Id,
                e.BranchId,
                e.Branch.Name,
                e.Category,
                e.Description,
                e.Amount,
                e.PaymentMethod,
                e.SupplierName,
                e.ExpenseDate,
                e.Notes,
                db.Users.Where(u => u.Id == e.CreatedByUserId)
                    .Select(u => u.FullName).FirstOrDefault(),
                db.MediaAttachments.Count(m =>
                    m.OwnerType == MediaOwnerType.Expense && m.OwnerId == e.Id),
                e.EmployeeUserId,
                db.Users.Where(u => u.Id == e.EmployeeUserId)
                    .Select(u => u.FullName).FirstOrDefault()))
            .FirstOrDefaultAsync(ct);
}

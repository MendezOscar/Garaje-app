using Garaj.Application.Abstractions;
using Garaj.Application.Assembly;
using Garaj.Application.Common;
using Garaj.Infrastructure.Documents;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Assembly;

/// <summary>
/// Los tres reportes: el que se le manda al contratista, el que usa el dueño para pagarles a
/// los técnicos, y el del taller, que es el único que lleva el gasto y lo que queda.
/// </summary>
/// <remarks>
/// Los tres salen del mismo filtro que la lista del panel, a propósito: lo que el dueño ve en
/// pantalla y lo que baja tienen que ser la misma cosa. Un reporte que no cuadra con la
/// pantalla de la que salió es peor que no tener reporte.
///
/// Las cifras las suma el servidor. Nada de esto pasa por el modelo.
/// </remarks>
public class AssemblyReportService(
    GarajDbContext db,
    ITenantContext tenantContext,
    AssemblyAdminService admin,
    IDateTimeProvider clock) : IAssemblyReportService
{
    public async Task<AssemblyBillingReportDto> BillingAsync(
        AssemblyQuery query, CancellationToken ct = default)
    {
        var (taller, desde, hasta) = await CabeceraAsync(query, ct);

        // Sin contratista no hay reporte de facturación: se le factura a alguien, y mezclar
        // dos contratos en una hoja es el error que nadie detecta hasta que lo rechazan.
        if (query.ContractorId is not { } contratistaId)
            throw new AppException("Diga a qué contratista es el reporte.");

        var contratista = await db.AssemblyContractors.AsNoTracking()
            .Where(c => c.Id == contratistaId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("El contratista no existe.");

        var pagina = await admin.ListAsync(
            query with { Page = 1, PageSize = MaximoDeFilas }, ct);

        // Sin el gasto: esta hoja es la que recibe el contratista, y lo que al taller le
        // cuesta armar no es asunto suyo. No se esconde en el documento, no viaja.
        var filas = pagina.Items.Select(r => r with { Expense = 0m }).ToList();

        return new AssemblyBillingReportDto(
            taller, contratista, desde, hasta,
            pagina.Total, pagina.TotalCharge, filas);
    }

    public async Task<AssemblyPayReportDto> PayAsync(
        AssemblyQuery query, CancellationToken ct = default)
    {
        var (taller, desde, hasta) = await CabeceraAsync(query, ct);
        var scope = AccessScope.From(tenantContext);
        scope.EnsureManagement();

        var q = admin.Filtrar(query, scope);

        var sumas = await q
            .GroupBy(m => m.TechnicianUserId)
            .Select(g => new
            {
                TechnicianUserId = g.Key,
                Cuantas = g.Count(),
                Pago = g.Sum(m => m.TechnicianPay)
            })
            .ToListAsync(ct);

        var nombres = await admin.NombresAsync(sumas.Select(s => s.TechnicianUserId), ct);

        var lineas = sumas
            .Select(s => new AssemblyPayLineDto(
                s.TechnicianUserId,
                nombres.GetValueOrDefault(s.TechnicianUserId, "—"),
                s.Cuantas,
                s.Pago))
            .OrderByDescending(l => l.Pay)
            .ToList();

        return new AssemblyPayReportDto(
            taller, desde, hasta,
            lineas.Sum(l => l.Count), lineas.Sum(l => l.Pay), lineas);
    }

    public async Task<AssemblyShopReportDto> ShopAsync(
        AssemblyQuery query, CancellationToken ct = default)
    {
        var (taller, desde, hasta) = await CabeceraAsync(query, ct);

        var pagina = await admin.ListAsync(
            query with { Page = 1, PageSize = MaximoDeFilas }, ct);

        return new AssemblyShopReportDto(
            taller, desde, hasta, pagina.Total,
            pagina.TotalCharge, pagina.TotalPay, pagina.TotalExpense,
            pagina.Items);
    }

    public async Task<byte[]> ShopExcelAsync(AssemblyQuery query, CancellationToken ct = default)
        => AssemblyReports.ShopExcel(await ShopAsync(query, ct));

    public async Task<byte[]> ShopPdfAsync(AssemblyQuery query, CancellationToken ct = default)
        => AssemblyReports.ShopPdf(await ShopAsync(query, ct));

    public async Task<byte[]> BillingExcelAsync(AssemblyQuery query, CancellationToken ct = default)
        => AssemblyReports.BillingExcel(await BillingAsync(query, ct));

    public async Task<byte[]> BillingPdfAsync(AssemblyQuery query, CancellationToken ct = default)
        => AssemblyReports.BillingPdf(await BillingAsync(query, ct));

    public async Task<byte[]> PayExcelAsync(AssemblyQuery query, CancellationToken ct = default)
        => AssemblyReports.PayExcel(await PayAsync(query, ct));

    public async Task<byte[]> PayPdfAsync(AssemblyQuery query, CancellationToken ct = default)
        => AssemblyReports.PayPdf(await PayAsync(query, ct));

    /// <summary>
    /// Un reporte es un periodo cerrado, no una lista infinita. Si un mes pasa de aquí, es
    /// que el filtro está mal puesto.
    /// </summary>
    private const int MaximoDeFilas = 5000;

    private async Task<(string Taller, DateOnly Desde, DateOnly Hasta)> CabeceraAsync(
        AssemblyQuery query, CancellationToken ct)
    {
        var taller = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => new { t.Name, t.AssemblyEnabled })
            .FirstOrDefaultAsync(ct);

        if (taller is null || !taller.AssemblyEnabled) throw new NotFoundException();

        var hoy = clock.Today();

        return (
            taller.Name,
            query.From ?? new DateOnly(hoy.Year, hoy.Month, 1),
            query.To ?? hoy);
    }
}

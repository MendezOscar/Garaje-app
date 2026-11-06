using System.Text.Json;
using Garaj.Application.Abstractions;
using Garaj.Application.Assistant;
using Garaj.Application.Common;
using Garaj.Application.Sales;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Documents;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Assistant;

/// <summary>
/// La lectura del negocio. Arma el paquete de números, se lo da al modelo y guarda lo que
/// escribió.
/// </summary>
/// <remarks>
/// Todo lo que se le manda sale de los reportes que ya existen. Esta dirección no necesitaba
/// consultas nuevas; necesitaba juntar las que hay.
/// </remarks>
public class BusinessReadingService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IReportService reports,
    IAssistant assistant,
    IAssistantQuota quota,
    IDateTimeProvider clock) : IBusinessReadingService
{
    /// <summary>
    /// Debajo de esto no se genera. Inventar una tendencia con cuatro datos es peor que no
    /// decir nada, y un taller que viene empezando recibiría cada semana una página de humo.
    /// </summary>
    private const int MinimoDeVentas = 5;

    public async Task<IReadOnlyList<BusinessReadingListItemDto>> ListAsync(
        CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        return await db.BusinessReadings
            .AsNoTracking()
            .OrderByDescending(r => r.PeriodTo)
            .Take(52)
            .Select(r => new BusinessReadingListItemDto(
                r.Id, r.Kind, r.PeriodFrom, r.PeriodTo, r.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<BusinessReadingDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var fila = await db.BusinessReadings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Esa lectura no existe.");

        return Map(fila);
    }

    public async Task<BusinessReadingDto> GenerateAsync(
        BusinessReadingKind kind, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();
        await EnsureEnabledAsync(ct);

        var (desde, hasta) = Periodo(kind);

        // Si ya existe la de este periodo, es la que hay. Pedirla dos veces el mismo lunes
        // gastaría dos llamadas para decir lo mismo, y daría dos textos distintos.
        var yaEsta = await db.BusinessReadings
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Kind == kind && r.PeriodTo == hasta, ct);

        if (yaEsta is not null) return Map(yaEsta);

        var ahora = await CifrasAsync(desde, hasta, ct);

        if (ahora.SaleCount < MinimoDeVentas)
            return Sin(kind, desde, hasta,
                $"Con {ahora.SaleCount} "
                + (ahora.SaleCount == 1 ? "venta" : "ventas")
                + " en el periodo no hay lectura que dar. Sacar una conclusión de tan pocos "
                + "datos sería inventarla.");

        var largo = hasta.DayNumber - desde.DayNumber + 1;
        var antes = await CifrasAsync(desde.AddDays(-largo), desde.AddDays(-1), ct);

        var prompt = new AssistantPrompt(
            BusinessReadingPrompt.Instructions(kind),
            BusinessReadingPrompt.Context(ahora, antes),
            BusinessReadingPrompt.Question(ahora, kind),
            Schema: null,
            MaxTokens: 4000);

        var salida = await assistant.AskAsync(prompt, ct);

        // No gasta créditos del taller —la manda el sistema, no la pide él—, pero el costo se
        // cuenta igual: el techo en lempiras es de lo que se gasta, no de lo que se cobra.
        var llamadaId = await quota.RecordAsync(
            AssistantFeature.BusinessReading, salida, null, ct);

        if (!salida.Succeeded || string.IsNullOrWhiteSpace(salida.Json))
            return Sin(kind, desde, hasta,
                salida.Error ?? "No se pudo armar la lectura. Inténtelo más tarde.");

        var lectura = new BusinessReading
        {
            Kind = kind,
            PeriodFrom = desde,
            PeriodTo = hasta,
            Data = JsonSerializer.Serialize(new { ahora, antes }),
            Content = salida.Json,
            AssistantCallId = llamadaId
        };

        db.BusinessReadings.Add(lectura);
        await db.SaveChangesAsync(ct);

        return Map(lectura);
    }

    public async Task<byte[]> PdfAsync(Guid id, CancellationToken ct = default)
    {
        var lectura = await GetAsync(id, ct);

        var taller = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => t.Name)
            .FirstAsync(ct);

        return BusinessReadingPdf.Render(taller, lectura);
    }

    /// <summary>
    /// El periodo que toca: la semana pasada completa, o el mes pasado completo. Nunca el que
    /// está corriendo —leer media semana no dice nada— y siempre en días del taller.
    /// </summary>
    private (DateOnly Desde, DateOnly Hasta) Periodo(BusinessReadingKind kind)
    {
        var hoy = clock.Today();

        if (kind == BusinessReadingKind.Monthly)
        {
            var primeroDeEsteMes = new DateOnly(hoy.Year, hoy.Month, 1);
            var finDelPasado = primeroDeEsteMes.AddDays(-1);
            return (new DateOnly(finDelPasado.Year, finDelPasado.Month, 1), finDelPasado);
        }

        // La semana del taller empieza el lunes, que es como se habla de «la semana pasada».
        var lunesDeEsta = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));
        return (lunesDeEsta.AddDays(-7), lunesDeEsta.AddDays(-1));
    }

    /// <summary>
    /// Los números del periodo, todos de SQL. Es lo único que viaja al modelo, y no lleva un
    /// solo nombre de cliente.
    /// </summary>
    private async Task<BusinessFiguresDto> CifrasAsync(
        DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var inicio = Instante(desde);
        var fin = Instante(hasta.AddDays(1));

        var query = new RevenueQuery { From = inicio, To = fin, GroupBy = RevenueGrouping.Day };

        var ingresos = await reports.RevenueAsync(query, ct);
        var tiempos = await reports.TechnicianTimeAsync(query, ct);
        var tablero = await reports.DashboardAsync(null, ct);

        var abiertas = await db.WorkOrders.AsNoTracking()
            .CountAsync(o => o.OpenedAt >= inicio && o.OpenedAt < fin, ct);

        var entregadas = await db.WorkOrders.AsNoTracking()
            .CountAsync(o => o.ClosedAt >= inicio && o.ClosedAt < fin, ct);

        var cotizaciones = await db.Quotes.AsNoTracking()
            .Where(q => q.SentAt >= inicio && q.SentAt < fin)
            .GroupBy(q => 1)
            .Select(g => new
            {
                Mandadas = g.Count(),
                Aprobadas = g.Count(q => q.Status == QuoteStatus.Approved),
                Esperando = g.Count(q => q.Status == QuoteStatus.Sent)
            })
            .FirstOrDefaultAsync(ct);

        var gastos = await db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= inicio && e.ExpenseDate < fin)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        var porTecnico = tiempos.Rows.ToDictionary(r => r.UserId);

        return new BusinessFiguresDto(
            ingresos.Currency,
            desde,
            hasta,
            ingresos.Total,
            ingresos.PartsRevenue,
            ingresos.LaborRevenue,
            ingresos.Cost,
            ingresos.Margin,
            ingresos.SaleCount,
            ingresos.CounterSaleCount,
            abiertas,
            entregadas,
            tablero.LateWorkOrders,
            cotizaciones?.Mandadas ?? 0,
            cotizaciones?.Aprobadas ?? 0,
            cotizaciones?.Esperando ?? 0,
            tablero.Receivables,
            tablero.OverdueReceivables,
            tablero.PartsBelowMinimum,
            gastos,
            ingresos.Branches
                .Select(b => new BusinessBranchDto(b.BranchName, b.Total, b.SaleCount))
                .ToList(),
            ingresos.Technicians
                .Where(t => t.TechnicianId != null)
                .Select(t =>
                {
                    porTecnico.TryGetValue(t.TechnicianId!.Value, out var tiempo);

                    return new BusinessTechnicianDto(
                        t.TechnicianName,
                        tiempo?.Orders ?? t.SaleCount,
                        Math.Round((tiempo?.Minutes ?? 0) / 60m, 1),
                        t.Total,
                        t.Margin,
                        tiempo?.AutoClosed ?? 0);
                })
                .ToList(),
            ingresos.TopParts
                .Take(10)
                .Select(p => new BusinessPartDto(p.Name, p.Quantity, p.Revenue, p.Margin))
                .ToList());
    }

    /// <summary>
    /// La medianoche de ese día en Honduras, en UTC. Npgsql rechaza un desplazamiento distinto
    /// de cero contra una columna <c>timestamptz</c>.
    /// </summary>
    private static DateTimeOffset Instante(DateOnly dia) =>
        new DateTimeOffset(dia.Year, dia.Month, dia.Day, 0, 0, 0, TimeSpan.FromHours(-6))
            .ToUniversalTime();

    private async Task EnsureEnabledAsync(CancellationToken ct)
    {
        var encendido = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => t.AssistantEnabled)
            .FirstOrDefaultAsync(ct);

        if (!encendido) throw new NotFoundException();
    }

    private static BusinessReadingDto Map(BusinessReading r) =>
        new(r.Id, r.Kind, r.PeriodFrom, r.PeriodTo, r.Content, null, r.CreatedAt);

    private static BusinessReadingDto Sin(
        BusinessReadingKind kind, DateOnly desde, DateOnly hasta, string mensaje) =>
        new(null, kind, desde, hasta, null, mensaje, null);
}

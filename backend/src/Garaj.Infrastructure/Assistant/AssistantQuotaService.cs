using Garaj.Application.Abstractions;
using Garaj.Application.Assistant;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Garaj.Infrastructure.Assistant;

/// <summary>
/// Los tres techos: los créditos del día, los del mes y el gasto en lempiras del taller. Más
/// el cuarto, el de toda la plataforma.
/// </summary>
/// <remarks>
/// Cuenta siempre contra <see cref="AssistantCall"/>, que lleva una fila por llamada incluidas
/// las que fallaron. Al llegar a cualquiera de los techos la función se apaga con un mensaje
/// que lo dice, nunca con un error, y el resto de GarajApp sigue funcionando igual.
/// </remarks>
public class AssistantQuotaService(
    GarajDbContext db,
    ITenantContext tenant,
    IDateTimeProvider clock,
    IOptions<AssistantOptions> options) : IAssistantQuota
{
    private readonly AssistantOptions _options = options.Value;

    public async Task<AssistantAllowance> CheckAsync(
        AssistantFeature feature, CancellationToken ct = default)
    {
        var uso = await UsageAsync(ct);

        if (!uso.Enabled)
            return AssistantAllowance.No("El asistente no está activo en este taller.", uso);

        var cuesta = CreditosDelAsistente.De(feature);

        if (uso.CreditsToday + cuesta > uso.CreditsPerDay)
            return AssistantAllowance.No(
                "Se acabaron las consultas de hoy. Vuelven mañana.", uso);

        if (uso.CreditsThisMonth + cuesta > uso.CreditsPerMonth)
            return AssistantAllowance.No(
                "Se acabaron las consultas del mes. Se pueden comprar más o esperar al próximo mes.",
                uso);

        // El tope en lempiras corta aunque queden créditos: los créditos son una aproximación
        // —una propuesta en un taller con 400 repuestos cuesta más que en uno con 40— y este
        // es el número de verdad. El taller no lo ve, así que el mensaje no lo menciona.
        if (uso.CapHnl > 0 && uso.SpentHnl >= uso.CapHnl)
            return AssistantAllowance.No(
                "El asistente no está disponible en este momento. Vuelva más tarde.", uso);

        if (await PlataformaPasadaAsync(ct))
            return AssistantAllowance.No(
                "El asistente no está disponible en este momento. Vuelva más tarde.", uso);

        return AssistantAllowance.Yes(uso);
    }

    public async Task<Guid> RecordAsync(
        AssistantFeature feature,
        AssistantResult result,
        Guid? workOrderId = null,
        CancellationToken ct = default)
    {
        var llamada = new AssistantCall
        {
            Feature = feature,
            Model = result.Model,
            InputTokens = result.Usage.Input,
            OutputTokens = result.Usage.Output,
            CacheReadTokens = result.Usage.CacheRead,
            CacheWriteTokens = result.Usage.CacheWrite,
            CostUsd = result.CostUsd,

            // Una llamada que falló no le cuesta créditos al taller —no recibió nada— pero su
            // costo sí se cuenta, que es para lo que está el techo en dinero.
            Credits = result.Succeeded ? CreditosDelAsistente.De(feature) : 0,

            UserId = tenant.UserId,
            WorkOrderId = workOrderId,
            Succeeded = result.Succeeded,
            Error = result.Error is { Length: > 300 } largo ? largo[..300] : result.Error,
            ElapsedMs = result.ElapsedMs
        };

        db.AssistantCalls.Add(llamada);
        await db.SaveChangesAsync(ct);

        return llamada.Id;
    }

    public async Task<AssistantUsageDto> UsageAsync(CancellationToken ct = default)
    {
        var taller = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenant.TenantId)
            .Select(t => new
            {
                t.AssistantEnabled,
                t.AssistantCreditsPerDay,
                t.AssistantCreditsPerMonth,
                t.AssistantMonthlyCapHnl
            })
            .FirstOrDefaultAsync(ct);

        if (taller is null)
            return new AssistantUsageDto(false, 0, 0, 0, 0, 0m, 0m);

        // El día es el de Honduras, no el del servidor: el día UTC cambia a las seis de la
        // tarde de acá y le cortaría el asistente al taller justo mientras cierra el día.
        var desdeHoy = clock.StartOfToday();
        var hoy = clock.Today();
        var desdeElMes = new DateTimeOffset(
            hoy.Year, hoy.Month, 1, 0, 0, 0, TimeSpan.FromHours(-6)).ToUniversalTime();

        var mes = await db.AssistantCalls
            .AsNoTracking()
            .Where(c => c.CreatedAt >= desdeElMes)
            .GroupBy(c => 1)
            .Select(g => new
            {
                Creditos = g.Sum(c => c.Credits),
                CreditosHoy = g.Where(c => c.CreatedAt >= desdeHoy).Sum(c => c.Credits),
                Costo = g.Sum(c => c.CostUsd)
            })
            .FirstOrDefaultAsync(ct);

        return new AssistantUsageDto(
            taller.AssistantEnabled && !string.IsNullOrWhiteSpace(_options.ApiKey),
            mes?.CreditosHoy ?? 0,
            mes?.Creditos ?? 0,
            taller.AssistantCreditsPerDay,
            taller.AssistantCreditsPerMonth,
            Math.Round((mes?.Costo ?? 0m) * _options.HnlPerUsd, 2),
            taller.AssistantMonthlyCapHnl);
    }

    /// <summary>
    /// El techo de toda la plataforma. Mira el gasto de todos los talleres, así que salta el
    /// filtro por tenant: no hay un taller detrás de esta pregunta, y la respuesta es un
    /// número, no datos de nadie.
    /// </summary>
    private async Task<bool> PlataformaPasadaAsync(CancellationToken ct)
    {
        if (_options.MonthlyCapUsd <= 0) return false;

        var hoy = clock.Today();
        var desde = new DateTimeOffset(
            hoy.Year, hoy.Month, 1, 0, 0, 0, TimeSpan.FromHours(-6)).ToUniversalTime();

        var gastado = await db.AssistantCalls
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.CreatedAt >= desde)
            .SumAsync(c => (decimal?)c.CostUsd, ct) ?? 0m;

        return gastado >= _options.MonthlyCapUsd;
    }
}

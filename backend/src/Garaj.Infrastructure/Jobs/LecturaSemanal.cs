using Garaj.Application.Abstractions;
using Garaj.Application.Assistant;
using Garaj.Application.Common;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Garaj.Infrastructure.Jobs;

/// <summary>
/// Arma la lectura del negocio una vez por semana, de madrugada.
/// </summary>
/// <remarks>
/// No hay nadie esperando la respuesta, así que corre cuando el taller duerme. El lunes a las
/// cinco de la mañana la lectura de la semana pasada ya está hecha y el dueño la encuentra al
/// abrir, en vez de tener que pedirla.
///
/// Si falla, no pasa nada grave: el botón del panel la arma igual, y la semana siguiente se
/// vuelve a intentar. Por eso ningún error de aquí tumba el servicio.
/// </remarks>
public class LecturaSemanal(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<LecturaSemanal> logger) : BackgroundService
{
    private const int HusoHoras = -6;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!configuration.GetValue("Assistant:WeeklyReading", true))
        {
            logger.LogInformation("La lectura semanal del negocio está apagada por configuración.");
            return;
        }

        var hora = configuration.GetValue("Assistant:WeeklyReadingHour", 5);

        while (!ct.IsCancellationRequested)
        {
            var espera = HastaElProximoLunes(hora);
            logger.LogInformation("Próxima lectura del negocio en {Horas:0.#} h.", espera.TotalHours);

            try
            {
                await Task.Delay(espera, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await ArmarAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falló la lectura semanal del negocio.");
            }
        }
    }

    private static TimeSpan HastaElProximoLunes(int hora)
    {
        var aqui = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(HusoHoras));

        var proxima = new DateTimeOffset(
            aqui.Year, aqui.Month, aqui.Day, hora, 0, 0, TimeSpan.FromHours(HusoHoras));

        // Días hasta el lunes. Si hoy es lunes y la hora ya pasó, va el de la semana que viene.
        var faltan = ((int)DayOfWeek.Monday - (int)aqui.DayOfWeek + 7) % 7;
        if (faltan == 0 && proxima <= aqui) faltan = 7;

        return proxima.AddDays(faltan) - aqui;
    }

    /// <summary>
    /// Una lectura por taller con el asistente encendido, cada una en su propio ámbito.
    /// </summary>
    /// <remarks>
    /// Ámbito aparte por taller a propósito: cada uno trae su <c>DbContext</c> y su contexto de
    /// tenant, y si la lectura de uno revienta no se lleva por delante las de los demás.
    ///
    /// El contexto se inicializa como Dueño de ese taller porque no hay petición detrás —es lo
    /// mismo que hace el sembrador—, y así las consultas pasan por el filtro por tenant en vez
    /// de saltárselo.
    /// </remarks>
    private async Task ArmarAsync(CancellationToken ct)
    {
        List<Guid> talleres;

        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GarajDbContext>();
            db.TenantContext.BypassTenantFilter = true;

            talleres = await db.Tenants
                .AsNoTracking()
                .Where(t => t.IsActive && t.AssistantEnabled)
                .Select(t => t.Id)
                .ToListAsync(ct);
        }

        foreach (var taller in talleres)
        {
            if (ct.IsCancellationRequested) return;

            try
            {
                using var scope = scopes.CreateScope();

                scope.ServiceProvider
                    .GetRequiredService<ITenantContext>()
                    .Initialize(taller, null, AppRoles.Owner, [], null);

                var lectura = await scope.ServiceProvider
                    .GetRequiredService<IBusinessReadingService>()
                    .GenerateAsync(BusinessReadingKind.Weekly, ct);

                if (lectura.Message is { } motivo)
                    logger.LogInformation("Taller {Taller}: sin lectura. {Motivo}", taller, motivo);
            }
            catch (Exception ex)
            {
                // Un taller que falla no puede dejar sin lectura a los demás.
                logger.LogError(ex, "Falló la lectura del taller {Taller}.", taller);
            }
        }
    }
}

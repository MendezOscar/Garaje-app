using Garaj.Application.Abstractions;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Assistant;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Persistence.Interceptors;
using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Garaj.Tests;

/// <summary>
/// Los topes del asistente: que cuenten, que corten y que lo que falla también se pague.
/// </summary>
/// <remarks>
/// Es la única prueba que tiene esta parte, porque no produce ninguna pantalla. Si el conteo
/// está mal, el techo es una idea y no un techo, y eso no se nota hasta que llega la factura.
/// </remarks>
public class TopesDelAsistenteTests
{
    private const string CadenaLocal =
        "Host=localhost;Port=5434;Database=garaj;Username=garaj;Password=garaj-dev-secret";

    private static GarajDbContext Contexto(Guid taller)
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? CadenaLocal;

        var contexto = new TenantContext();
        contexto.Initialize(taller, Guid.NewGuid(), "Owner", [], null);

        var options = new DbContextOptionsBuilder<GarajDbContext>()
            .UseNpgsql(cadena)
            .AddInterceptors(new AuditableEntityInterceptor(contexto, new DateTimeProvider()))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GarajDbContext(options, contexto);
    }

    private static AssistantQuotaService Cuota(GarajDbContext db) =>
        new(db, db.TenantContext, new DateTimeProvider(),
            Options.Create(new AssistantOptions
            {
                // Con la llave vacía el asistente se da por apagado en todos lados: esa es la
                // llave de emergencia y aquí estorbaría.
                ApiKey = "prueba",
                MonthlyCapUsd = 0
            }));

    private static async Task<Guid> TallerConAsistenteAsync(int porDia, int porMes, decimal tope)
    {
        var id = Guid.NewGuid();

        await using var db = Contexto(id);
        db.Tenants.Add(new Tenant
        {
            Id = id,
            Name = $"Prueba {id:N}"[..20],
            Currency = "HNL",
            DefaultPhoneCountryCode = "504",
            AssistantEnabled = true,
            AssistantCreditsPerDay = porDia,
            AssistantCreditsPerMonth = porMes,
            AssistantMonthlyCapHnl = tope
        });
        await db.SaveChangesAsync();

        return id;
    }

    private static AssistantResult Salio(bool bien, decimal costoUsd) =>
        new(bien, bien ? "{}" : null, bien ? null : "falló",
            "claude-opus-5-5", new AssistantUsage(70, 1000, 3600, 0), costoUsd, 1200);

    [RequiereBaseLocal]
    public async Task Corta_al_llegar_al_tope_del_dia()
    {
        var taller = await TallerConAsistenteAsync(porDia: 2, porMes: 450, tope: 220m);

        await using var db = Contexto(taller);
        var cuota = Cuota(db);

        Assert.True((await cuota.CheckAsync(AssistantFeature.WorkSteps)).Allowed);

        // Dos propuestas, un crédito cada una: justo el tope del día.
        await cuota.RecordAsync(AssistantFeature.WorkSteps, Salio(true, 0.0177m));
        await cuota.RecordAsync(AssistantFeature.WorkSteps, Salio(true, 0.0177m));

        var tercera = await cuota.CheckAsync(AssistantFeature.WorkSteps);

        Assert.False(tercera.Allowed);
        Assert.Contains("hoy", tercera.Reason);
        Assert.Equal(2, tercera.Usage.CreditsToday);

        // Y no es un error: el motivo viene redactado para el taller.
        Assert.DoesNotContain("error", tercera.Reason!.ToLower());
    }

    [RequiereBaseLocal]
    public async Task Una_consulta_del_tecnico_cuesta_cinco_veces_mas_que_una_propuesta()
    {
        var taller = await TallerConAsistenteAsync(porDia: 80, porMes: 450, tope: 220m);

        await using var db = Contexto(taller);
        var cuota = Cuota(db);

        await cuota.RecordAsync(AssistantFeature.WorkSteps, Salio(true, 0.01m));
        await cuota.RecordAsync(AssistantFeature.TechnicianQuery, Salio(true, 0.05m));

        // Y la lectura del negocio no le cuesta créditos al taller: la mandamos nosotros.
        await cuota.RecordAsync(AssistantFeature.BusinessReading, Salio(true, 0.02m));

        var uso = await cuota.UsageAsync();

        Assert.Equal(6, uso.CreditsToday);
    }

    [RequiereBaseLocal]
    public async Task Lo_que_falla_no_cobra_creditos_pero_si_cuenta_el_costo()
    {
        var taller = await TallerConAsistenteAsync(porDia: 80, porMes: 450, tope: 220m);

        await using var db = Contexto(taller);
        var cuota = Cuota(db);

        // Un reintento que falla consumió tokens igual. Si solo se contaran las exitosas, un
        // error en bucle de madrugada no movería ningún techo.
        await cuota.RecordAsync(AssistantFeature.WorkSteps, Salio(false, 0.0177m));

        var uso = await cuota.UsageAsync();

        Assert.Equal(0, uso.CreditsToday);
        Assert.True(uso.SpentHnl > 0, "el costo de lo que falló tiene que contar igual");
    }

    [RequiereBaseLocal]
    public async Task El_tope_en_lempiras_corta_aunque_queden_creditos()
    {
        var taller = await TallerConAsistenteAsync(porDia: 80, porMes: 450, tope: 1m);

        await using var db = Contexto(taller);
        var cuota = Cuota(db);

        // Un dólar son 26 lempiras: con el tope en 1 lempira, una sola llamada lo pasa.
        await cuota.RecordAsync(AssistantFeature.WorkSteps, Salio(true, 1m));

        var despues = await cuota.CheckAsync(AssistantFeature.WorkSteps);

        Assert.False(despues.Allowed);

        // Quedan 79 créditos del día y 449 del mes, y aun así no deja: el techo de verdad es
        // el del dinero.
        Assert.Equal(1, despues.Usage.CreditsToday);
    }

    [RequiereBaseLocal]
    public async Task Un_taller_sin_asistente_no_pasa()
    {
        var taller = await TallerConAsistenteAsync(porDia: 80, porMes: 450, tope: 220m);

        await using (var apagar = Contexto(taller))
        {
            var fila = await apagar.Tenants.FirstAsync(t => t.Id == taller);
            fila.AssistantEnabled = false;
            await apagar.SaveChangesAsync();
        }

        await using var db = Contexto(taller);

        var permiso = await Cuota(db).CheckAsync(AssistantFeature.WorkSteps);

        Assert.False(permiso.Allowed);
        Assert.False(permiso.Usage.Enabled);
    }
}

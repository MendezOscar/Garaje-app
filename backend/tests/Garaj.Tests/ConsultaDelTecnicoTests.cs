using Garaj.Application.Abstractions;
using Garaj.Application.Assistant;
using Garaj.Application.Common;
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
/// La consulta del técnico: que se cierre sola y que la de uno no sea la de otro.
/// </summary>
/// <remarks>
/// Lo que no se puede probar aquí es que se niegue a dar un torque: eso depende del modelo de
/// verdad y se comprueba en el guion de pruebas, a mano. Lo que sí depende de este código es
/// el tope de turnos —que es lo que impide que una consulta se alargue sola hasta perder el
/// hilo y el presupuesto— y quién ve qué.
/// </remarks>
public class ConsultaDelTecnicoTests
{
    private const string CadenaLocal =
        "Host=localhost;Port=5434;Database=garaj;Username=garaj;Password=garaj-dev-secret";

    private sealed class AsistenteDeMentira : IAssistant
    {
        public Task<AssistantResult> AskAsync(AssistantPrompt prompt, CancellationToken ct = default)
        {
            Prompt = prompt;
            return Task.FromResult(new AssistantResult(
                true, "Eso se mide, no lo puedo saber de memoria.", null, "prueba",
                new AssistantUsage(80, 200, 4000, 0), 0.01m, 700));
        }

        public AssistantPrompt? Prompt { get; private set; }
    }

    private static GarajDbContext Contexto(
        Guid taller, Guid usuario, string rol, Guid? sucursal = null)
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? CadenaLocal;

        var contexto = new TenantContext();

        // El técnico solo ve las sucursales que tiene asignadas; sin ninguna, la orden le sale
        // como que no existe. El Dueño las ve todas y no necesita lista.
        contexto.Initialize(taller, usuario, rol, sucursal is { } s ? [s] : [], null);

        var options = new DbContextOptionsBuilder<GarajDbContext>()
            .UseNpgsql(cadena)
            .AddInterceptors(new AuditableEntityInterceptor(contexto, new DateTimeProvider()))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GarajDbContext(options, contexto);
    }

    private static TechnicianQueryService Servicio(GarajDbContext db, IAssistant modelo) =>
        new(db, db.TenantContext, modelo,
            new AssistantQuotaService(
                db, db.TenantContext, new DateTimeProvider(),
                Options.Create(new AssistantOptions { ApiKey = "prueba", MonthlyCapUsd = 0 })),
            new AssistantCatalog(db, db.TenantContext));

    /// <summary>Un taller con el asistente y una orden diagnosticada.</summary>
    private static async Task<(Guid Taller, Guid Orden, Guid Sucursal)> EscenarioAsync()
    {
        var taller = Guid.NewGuid();
        var sucursal = Guid.NewGuid();
        var cliente = Guid.NewGuid();
        var vehiculo = Guid.NewGuid();
        var orden = Guid.NewGuid();
        var corto = $"{taller:N}"[..8].ToUpperInvariant();

        await using var db = Contexto(taller, Guid.NewGuid(), AppRoles.Owner);

        db.Tenants.Add(new Tenant
        {
            Id = taller, Name = $"Prueba {corto}", Currency = "HNL",
            DefaultPhoneCountryCode = "504", WorksWith = WorkshopKind.Vehicles,
            AssistantEnabled = true,
            AssistantCreditsPerDay = 80, AssistantCreditsPerMonth = 450,
            AssistantMonthlyCapHnl = 220m
        });

        db.Branches.Add(new Branch
        {
            Id = sucursal, TenantId = taller, Name = "Matriz", Code = corto[..3], IsActive = true
        });

        db.Customers.Add(new Customer
        {
            Id = cliente, TenantId = taller, FullName = "Cliente", Phone = "50499990000"
        });

        db.Vehicles.Add(new Vehicle
        {
            Id = vehiculo, TenantId = taller, CustomerId = cliente, Type = VehicleType.Car,
            Brand = "Toyota", Model = "Hilux", Year = 2018, IsActive = true
        });

        db.WorkOrders.Add(new WorkOrder
        {
            Id = orden, TenantId = taller, BranchId = sucursal, VehicleId = vehiculo,
            Number = $"OT-{corto}", Description = "Ruido al frenar",
            Diagnosis = "Ruido metálico en el eje trasero",
            Status = WorkOrderStatus.InProgress, OpenedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        return (taller, orden, sucursal);
    }

    [RequiereBaseLocal]
    public async Task La_consulta_se_cierra_sola_a_las_seis_preguntas()
    {
        var (taller, orden, sucursal) = await EscenarioAsync();
        var tecnico = Guid.NewGuid();

        await using var db = Contexto(taller, tecnico, AppRoles.Technician, sucursal);
        var servicio = Servicio(db, new AsistenteDeMentira());

        var consulta = await servicio.AskAsync(orden, null, "¿Puede ser el rodamiento?");

        for (var i = 2; i <= 6; i++)
            consulta = await servicio.AskAsync(orden, consulta.Id, $"Pregunta {i}");

        Assert.True(consulta.IsClosed);
        Assert.Equal(12, consulta.Turns.Count);   // seis preguntas y seis respuestas
        Assert.Contains("abra una nueva", consulta.Message!.ToLower());

        // Y una séptima no entra: hay que abrir otra, que además obliga a replantearla.
        var septima = await servicio.AskAsync(orden, consulta.Id, "Una más");

        Assert.Equal(12, septima.Turns.Count);
    }

    [RequiereBaseLocal]
    public async Task La_consulta_de_un_tecnico_no_la_ve_otro_tecnico()
    {
        var (taller, orden, sucursal) = await EscenarioAsync();
        var kevin = Guid.NewGuid();
        var nelson = Guid.NewGuid();

        Guid consultaId;

        await using (var suya = Contexto(taller, kevin, AppRoles.Technician, sucursal))
        {
            var r = await Servicio(suya, new AsistenteDeMentira())
                .AskAsync(orden, null, "¿Qué le hicieron a este modelo?");
            consultaId = r.Id!.Value;
        }

        await using (var ajena = Contexto(taller, nelson, AppRoles.Technician, sucursal))
        {
            // 404 y no 403: decirle que existe ya sería decirle algo.
            await Assert.ThrowsAsync<NotFoundException>(
                () => Servicio(ajena, new AsistenteDeMentira()).GetAsync(consultaId));

            Assert.Empty(await Servicio(ajena, new AsistenteDeMentira()).ListAsync(orden));
        }

        // El Dueño sí: responde por el trabajo completo.
        await using var dueno = Contexto(taller, Guid.NewGuid(), AppRoles.Owner);

        Assert.Single(await Servicio(dueno, new AsistenteDeMentira()).ListAsync(orden));
    }

    [RequiereBaseLocal]
    public async Task El_historial_solo_trae_ordenes_entregadas_del_mismo_modelo()
    {
        var (taller, orden, sucursal) = await EscenarioAsync();

        await using (var historia = Contexto(taller, Guid.NewGuid(), AppRoles.Owner))
        {
            var cliente = await historia.Customers.Select(c => c.Id).FirstAsync();

            // Un Hilux entregado: este sí tiene que salir.
            var hilux = new Vehicle
            {
                TenantId = taller, CustomerId = cliente, Type = VehicleType.Car,
                Brand = "Toyota", Model = "Hilux", Year = 2017, IsActive = true
            };

            // Un Corolla entregado: mismo taller, otro modelo. No tiene nada que aportar.
            var corolla = new Vehicle
            {
                TenantId = taller, CustomerId = cliente, Type = VehicleType.Car,
                Brand = "Toyota", Model = "Corolla", Year = 2017, IsActive = true
            };

            historia.Vehicles.AddRange(hilux, corolla);

            historia.WorkOrders.Add(new WorkOrder
            {
                TenantId = taller, BranchId = sucursal, Vehicle = hilux,
                Number = "OT-HILUX", Description = "Ruido",
                Diagnosis = "Rodamiento de rueda trasera derecha",
                Status = WorkOrderStatus.Delivered,
                OpenedAt = DateTimeOffset.UtcNow.AddDays(-20),
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-19)
            });

            historia.WorkOrders.Add(new WorkOrder
            {
                TenantId = taller, BranchId = sucursal, Vehicle = corolla,
                Number = "OT-COROLLA", Description = "Otro",
                Diagnosis = "Bobina del cilindro 3",
                Status = WorkOrderStatus.Delivered,
                OpenedAt = DateTimeOffset.UtcNow.AddDays(-10),
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-9)
            });

            // Y un Hilux todavía abierto: todavía no enseñó nada.
            var abierto = new Vehicle
            {
                TenantId = taller, CustomerId = cliente, Type = VehicleType.Car,
                Brand = "Toyota", Model = "Hilux", Year = 2019, IsActive = true
            };

            historia.Vehicles.Add(abierto);
            historia.WorkOrders.Add(new WorkOrder
            {
                TenantId = taller, BranchId = sucursal, Vehicle = abierto,
                Number = "OT-ABIERTA", Description = "En curso",
                Diagnosis = "Todavía revisando",
                Status = WorkOrderStatus.InProgress,
                OpenedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });

            await historia.SaveChangesAsync();
        }

        await using var db = Contexto(taller, Guid.NewGuid(), AppRoles.Technician, sucursal);
        var modelo = new AsistenteDeMentira();

        await Servicio(db, modelo).AskAsync(orden, null, "¿Qué suele ser ese ruido?");

        var contexto = modelo.Prompt!.Context;

        Assert.Contains("OT-HILUX", contexto);
        Assert.DoesNotContain("OT-COROLLA", contexto);
        Assert.DoesNotContain("OT-ABIERTA", contexto);

        // Y la conversación va en la parte que cambia, no en el contexto: si se mezclaran, el
        // historial dejaría de leerse de caché en cada turno.
        Assert.Contains("ese ruido", modelo.Prompt.Question);
        Assert.DoesNotContain("ese ruido", contexto);
    }
}

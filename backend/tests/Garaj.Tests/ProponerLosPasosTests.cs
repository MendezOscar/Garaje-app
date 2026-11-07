using System.Text.Json;
using Garaj.Application.Abstractions;
using Garaj.Application.Assistant;
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
/// Lo que el modelo devuelve se trata como si viniera de un cliente hostil.
/// </summary>
/// <remarks>
/// La prueba contra casos reales no encontró ni un repuesto fuera del catálogo, pero eso no
/// quita la validación: es una respuesta de afuera, y basta un día en que el modelo cambie de
/// humor para que el taller vea en su propuesta un código que no tiene. Aquí se le dan a
/// propósito las respuestas que no deberían pasar.
///
/// El modelo no se llama de verdad: se le pone delante uno de mentira con la respuesta ya
/// escrita. Probar contra el modelo real costaría dinero y no probaría la validación, que es
/// lo que puede estar mal.
/// </remarks>
public class ProponerLosPasosTests
{
    private const string CadenaLocal =
        "Host=localhost;Port=5434;Database=garaj;Username=garaj;Password=garaj-dev-secret";

    /// <summary>Un asistente de mentira que devuelve lo que se le diga.</summary>
    private sealed class AsistenteDeMentira(string json) : IAssistant
    {
        public Task<AssistantResult> AskAsync(AssistantPrompt prompt, CancellationToken ct = default)
        {
            Prompt = prompt;
            return Task.FromResult(new AssistantResult(
                true, json, null, "prueba", new AssistantUsage(70, 900, 3600, 0), 0.018m, 900));
        }

        public AssistantPrompt? Prompt { get; private set; }
    }

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

    /// <summary>Un taller con una orden diagnosticada, dos repuestos y un servicio.</summary>
    private static async Task<(Guid Taller, Guid Orden)> EscenarioAsync()
    {
        var taller = Guid.NewGuid();
        var sucursal = Guid.NewGuid();
        var orden = Guid.NewGuid();
        var corto = $"{taller:N}"[..8].ToUpperInvariant();

        await using var db = Contexto(taller);

        db.Tenants.Add(new Tenant
        {
            Id = taller,
            Name = $"Prueba {corto}",
            Currency = "HNL",
            DefaultPhoneCountryCode = "504",
            WorksWith = WorkshopKind.Vehicles,
            AssistantEnabled = true,
            AssistantCreditsPerDay = 80,
            AssistantCreditsPerMonth = 450,
            AssistantMonthlyCapHnl = 220m
        });

        db.Branches.Add(new Branch
        {
            Id = sucursal, TenantId = taller, Name = "Matriz", Code = corto[..3], IsActive = true
        });

        db.Parts.Add(new Part
        {
            TenantId = taller, Sku = $"PAS-{corto}", Name = "Pastillas delanteras",
            Unit = "jgo", SalePrice = 1200m, IsActive = true
        });

        db.LaborServices.Add(new LaborService
        {
            TenantId = taller, Code = $"MO-{corto}", Name = "Cambio de pastillas",
            StandardHours = 1.5m, HourlyRate = 350m, IsActive = true
        });

        // La orden exige vehículo o equipo: hay un check en la base que lo impone.
        var cliente = Guid.NewGuid();
        var vehiculo = Guid.NewGuid();

        db.Customers.Add(new Customer
        {
            Id = cliente, TenantId = taller, FullName = "Cliente de prueba", Phone = "50499990000"
        });

        db.Vehicles.Add(new Vehicle
        {
            Id = vehiculo, TenantId = taller, CustomerId = cliente, Type = VehicleType.Car,
            Brand = "Toyota", Model = "Corolla", Year = 2015, IsActive = true,
            Plate = $"P{corto[..6]}", Vin = $"9BWZZZ377VT{corto[..6]}"
        });

        db.WorkOrders.Add(new WorkOrder
        {
            Id = orden, TenantId = taller, BranchId = sucursal, VehicleId = vehiculo,
            Number = $"OT-{corto}", Description = "Chilla al frenar",
            Diagnosis = "Pastillas delanteras gastadas, disco en buen estado.",
            Status = WorkOrderStatus.InProgress, OpenedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        return (taller, orden);
    }

    private static WorkStepsAssistant Servicio(GarajDbContext db, IAssistant modelo)
    {
        var opciones = Options.Create(new AssistantOptions { ApiKey = "prueba", MonthlyCapUsd = 0 });
        var reloj = new DateTimeProvider();

        return new WorkStepsAssistant(
            db, db.TenantContext, modelo,
            new AssistantQuotaService(db, db.TenantContext, reloj, opciones),
            new AssistantCatalog(db, db.TenantContext),
            // Solo lo usa aceptar, y esta prueba no acepta nada.
            workOrders: null!,
            reloj);
    }

    private static string Respuesta(object cuerpo) => JsonSerializer.Serialize(cuerpo);

    [RequiereBaseLocal]
    public async Task Un_repuesto_que_no_esta_en_el_catalogo_no_llega_a_la_propuesta()
    {
        var (taller, orden) = await EscenarioAsync();

        await using var db = Contexto(taller);
        var sku = await db.Parts.Select(p => p.Sku).FirstAsync();

        var modelo = new AsistenteDeMentira(Respuesta(new
        {
            pasos = new[]
            {
                new { titulo = "Cambiar pastillas", descripcion = "", codigoManoDeObra = (string?)null, horas = 1.5 }
            },
            repuestos = new[]
            {
                new { sku, cantidad = 1.0, paraElPaso = "Cambiar pastillas" },
                // Este no existe. En el taller se vería como un repuesto que sí tiene.
                new { sku = "INVENTADO-999", cantidad = 1.0, paraElPaso = "Cambiar pastillas" }
            },
            nota = (string?)null
        }));

        var propuesta = await Servicio(db, modelo).ProposeAsync(orden);

        Assert.True(propuesta.Available);
        Assert.Single(propuesta.Parts);
        Assert.Equal(sku, propuesta.Parts[0].Sku);
    }

    [RequiereBaseLocal]
    public async Task Las_horas_y_las_cantidades_absurdas_se_recortan()
    {
        var (taller, orden) = await EscenarioAsync();

        await using var db = Contexto(taller);
        var sku = await db.Parts.Select(p => p.Sku).FirstAsync();

        var modelo = new AsistenteDeMentira(Respuesta(new
        {
            pasos = new[]
            {
                new { titulo = "Paso largo", descripcion = "", codigoManoDeObra = (string?)null, horas = 9999.0 }
            },
            repuestos = new[]
            {
                new { sku, cantidad = -3.0, paraElPaso = "Paso largo" }
            },
            nota = (string?)null
        }));

        var propuesta = await Servicio(db, modelo).ProposeAsync(orden);

        Assert.Equal(40m, propuesta.Tasks[0].Hours);

        // Una cantidad negativa no se corrige a uno: se descarta. Corregirla sería inventar
        // cuánto lleva el trabajo.
        Assert.Empty(propuesta.Parts);
    }

    [RequiereBaseLocal]
    public async Task Un_codigo_de_mano_de_obra_que_no_existe_deja_el_paso_sin_servicio()
    {
        var (taller, orden) = await EscenarioAsync();

        await using var db = Contexto(taller);
        var codigo = await db.LaborServices.Select(s => s.Code).FirstAsync();

        var modelo = new AsistenteDeMentira(Respuesta(new
        {
            pasos = new[]
            {
                new { titulo = "Con servicio", descripcion = "", codigoManoDeObra = codigo, horas = 1.5 },
                new { titulo = "Sin servicio", descripcion = "", codigoManoDeObra = "MO-NO-EXISTE", horas = 1.0 }
            },
            repuestos = Array.Empty<object>(),
            nota = "Faltaría revisar el disco."
        }));

        var propuesta = await Servicio(db, modelo).ProposeAsync(orden);

        Assert.NotNull(propuesta.Tasks[0].LaborServiceId);

        // El paso entra igual: vale más un paso sin servicio que uno colgado del equivocado.
        Assert.Null(propuesta.Tasks[1].LaborServiceId);
        Assert.Equal("Faltaría revisar el disco.", propuesta.Note);
    }

    [RequiereBaseLocal]
    public async Task Sin_diagnostico_no_se_llama_al_modelo()
    {
        var (taller, orden) = await EscenarioAsync();

        await using (var borrar = Contexto(taller))
        {
            var fila = await borrar.WorkOrders.FirstAsync(o => o.Id == orden);
            fila.Diagnosis = null;
            await borrar.SaveChangesAsync();
        }

        await using var db = Contexto(taller);
        var modelo = new AsistenteDeMentira("{}");

        var propuesta = await Servicio(db, modelo).ProposeAsync(orden);

        Assert.False(propuesta.Available);
        Assert.Null(modelo.Prompt);

        // Y no gastó ningún crédito: no se llegó a llamar.
        Assert.Equal(0, await db.AssistantCalls.CountAsync());
    }

    [RequiereBaseLocal]
    public async Task El_taller_sin_asistente_recibe_404_y_no_403()
    {
        var (taller, orden) = await EscenarioAsync();

        await using (var apagar = Contexto(taller))
        {
            var fila = await apagar.Tenants.FirstAsync(t => t.Id == taller);
            fila.AssistantEnabled = false;
            await apagar.SaveChangesAsync();
        }

        await using var db = Contexto(taller);

        await Assert.ThrowsAsync<Garaj.Application.Common.NotFoundException>(
            () => Servicio(db, new AsistenteDeMentira("{}")).ProposeAsync(orden));
    }

    [RequiereBaseLocal]
    public async Task Lo_que_hace_falta_y_no_esta_en_catalogo_sale_aparte()
    {
        var (taller, orden) = await EscenarioAsync();

        await using var db = Contexto(taller);
        var sku = await db.Parts.Select(p => p.Sku).FirstAsync();
        var nombre = await db.Parts.Select(p => p.Name).FirstAsync();

        var modelo = new AsistenteDeMentira(Respuesta(new
        {
            pasos = new[]
            {
                new { titulo = "Cambiar pastillas", descripcion = "", codigoManoDeObra = (string?)null, horas = 1.5 }
            },
            repuestos = new[] { new { sku, cantidad = 1.0, paraElPaso = "Cambiar pastillas" } },
            faltantes = new[]
            {
                new { nombre = "Retenedor de cigüeñal delantero", cantidad = 1.0, paraElPaso = "Cambiar pastillas" },
                // Este sí está en el catálogo: verlo en las dos listas haría dudar de ambas.
                new { nombre, cantidad = 1.0, paraElPaso = "Cambiar pastillas" },
                // Y una cantidad imposible no se corrige, se descarta.
                new { nombre = "Empaque", cantidad = 0.0, paraElPaso = "Cambiar pastillas" }
            },
            nota = (string?)null
        }));

        var propuesta = await Servicio(db, modelo).ProposeAsync(orden);

        Assert.NotNull(propuesta.Missing);
        Assert.Single(propuesta.Missing!);
        Assert.Equal("Retenedor de cigüeñal delantero", propuesta.Missing![0].Name);

        // Y el del catálogo sigue estando donde le toca, con su existencia.
        Assert.Single(propuesta.Parts);
    }

    [RequiereBaseLocal]
    public async Task El_precio_de_un_paso_sale_de_las_ordenes_del_taller_no_del_modelo()
    {
        var (taller, orden) = await EscenarioAsync();

        Guid servicioId;

        // Dos trabajos iguales ya entregados: uno cobrado a mano y otro por catálogo.
        await using (var historia = Contexto(taller))
        {
            var servicio = await historia.LaborServices.FirstAsync();
            servicioId = servicio.Id;

            var sucursal = await historia.Branches.Select(b => b.Id).FirstAsync();
            var vehiculo = await historia.Vehicles.Select(v => v.Id).FirstAsync();

            for (var i = 0; i < 2; i++)
            {
                var vieja = new WorkOrder
                {
                    TenantId = taller, BranchId = sucursal, VehicleId = vehiculo,
                    Number = "OT-VIEJA-$i".Replace("$i", i.ToString()),
                    Description = "Trabajo anterior",
                    Status = WorkOrderStatus.Delivered,
                    OpenedAt = DateTimeOffset.UtcNow.AddDays(-30),
                    ClosedAt = DateTimeOffset.UtcNow.AddDays(-29)
                };

                historia.WorkOrders.Add(vieja);
                historia.WorkOrderTasks.Add(new WorkOrderTask
                {
                    TenantId = taller,
                    WorkOrder = vieja,
                    Title = "Cambio de pastillas",
                    LaborServiceId = servicioId,
                    ActualHours = 1.5m,
                    // El primero se cobró a mano; el segundo sale del catálogo: 1.5 h × 350.
                    ManualLaborPrice = i == 0 ? 800m : null
                });
            }

            await historia.SaveChangesAsync();
        }

        await using var db = Contexto(taller);
        var codigo = await db.LaborServices.Select(x => x.Code).FirstAsync();

        var modelo = new AsistenteDeMentira(Respuesta(new
        {
            pasos = new[]
            {
                new { titulo = "Cambiar pastillas", descripcion = "", codigoManoDeObra = codigo, horas = 1.5 }
            },
            repuestos = Array.Empty<object>(),
            faltantes = Array.Empty<object>(),
            nota = (string?)null
        }));

        var propuesta = await Servicio(db, modelo).ProposeAsync(orden);
        var historial = propuesta.Tasks[0].History;

        Assert.NotNull(historial);
        Assert.Equal(2, historial!.Times);
        Assert.Equal(525m, historial.PriceFrom);   // 1.5 h × 350 del catálogo
        Assert.Equal(800m, historial.PriceTo);     // el que se cobró a mano
        Assert.Equal(1.5m, historial.TypicalHours);
    }

    [RequiereBaseLocal]
    public async Task Un_trabajo_que_el_taller_nunca_ha_hecho_no_trae_precio_inventado()
    {
        var (taller, orden) = await EscenarioAsync();

        await using var db = Contexto(taller);
        var codigo = await db.LaborServices.Select(s => s.Code).FirstAsync();

        var modelo = new AsistenteDeMentira(Respuesta(new
        {
            pasos = new[]
            {
                new { titulo = "Cambiar pastillas", descripcion = "", codigoManoDeObra = codigo, horas = 1.5 }
            },
            repuestos = Array.Empty<object>(),
            faltantes = Array.Empty<object>(),
            nota = (string?)null
        }));

        var propuesta = await Servicio(db, modelo).ProposeAsync(orden);

        // Sin historia no se enseña ningún número. Es la diferencia entre callar y adivinar.
        Assert.Null(propuesta.Tasks[0].History);
    }

    [RequiereBaseLocal]
    public async Task El_catalogo_va_antes_que_la_pregunta_para_que_el_cache_sirva()
    {
        var (taller, orden) = await EscenarioAsync();

        await using var db = Contexto(taller);
        var modelo = new AsistenteDeMentira(Respuesta(new
        {
            pasos = Array.Empty<object>(), repuestos = Array.Empty<object>(), nota = (string?)null
        }));

        await Servicio(db, modelo).ProposeAsync(orden);

        Assert.NotNull(modelo.Prompt);

        // El catálogo es lo estable y va en el prefijo cacheado; el diagnóstico cambia en cada
        // llamada y va al final. Si se invierten, la entrada deja de leerse de caché y el costo
        // se multiplica sin que nada avise.
        Assert.Contains("Pastillas delanteras", modelo.Prompt!.Context);
        Assert.Contains("Pastillas delanteras gastadas", modelo.Prompt.Question);
        Assert.DoesNotContain("Pastillas delanteras gastadas", modelo.Prompt.Context);

        // Y nada que identifique al cliente ni al vehículo: ni nombre, ni teléfono, ni placa,
        // ni VIN. El VIN se agregó después y es de los que alguien mete «para dar contexto»:
        // no le sirve de nada al modelo y sí identifica un chasis.
        Assert.DoesNotContain("placa", modelo.Prompt.Question.ToLower());

        var enviado = modelo.Prompt.Instructions + modelo.Prompt.Context + modelo.Prompt.Question;

        Assert.DoesNotContain("Cliente de prueba", enviado);
        Assert.DoesNotContain("50499990000", enviado);
        Assert.DoesNotContain("9BWZZZ377VT", enviado);
    }

    /// <summary>
    /// El segundo clic sobre el mismo diagnóstico tiene que saber qué se aceptó en el primero.
    /// </summary>
    /// <remarks>
    /// Sin esto el modelo volvía a proponer los mismos pasos, y como al aceptar se anexan, la
    /// orden quedaba con el trabajo duplicado: seis pasos se volvían doce y las horas el doble.
    /// Pasó en producción la primera vez que alguien tocó el botón dos veces.
    /// </remarks>
    [RequiereBaseLocal]
    public async Task Los_pasos_que_la_orden_ya_tiene_viajan_para_que_no_se_repitan()
    {
        var (taller, orden) = await EscenarioAsync();

        await using var db = Contexto(taller);

        db.WorkOrderTasks.Add(new WorkOrderTask
        {
            TenantId = taller,
            WorkOrderId = orden,
            Title = "Cambiar pastillas delanteras",
            Sequence = 1
        });

        await db.SaveChangesAsync();

        var modelo = new AsistenteDeMentira(Respuesta(new
        {
            pasos = Array.Empty<object>(), repuestos = Array.Empty<object>(), nota = (string?)null
        }));

        await Servicio(db, modelo).ProposeAsync(orden);

        Assert.NotNull(modelo.Prompt);
        Assert.Contains("Cambiar pastillas delanteras", modelo.Prompt!.Question);

        // En la pregunta, no en el contexto: el contexto es el prefijo cacheado y esto cambia
        // cada vez que se aceptan pasos. Meterlo ahí tiraría el caché en cada llamada.
        Assert.DoesNotContain("Cambiar pastillas delanteras", modelo.Prompt.Context);
    }
}

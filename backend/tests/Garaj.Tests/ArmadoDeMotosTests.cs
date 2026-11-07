using Garaj.Application.Abstractions;
using Garaj.Application.Assembly;
using Garaj.Application.Common;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Assembly;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Persistence.Interceptors;
using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Tests;

/// <summary>
/// El cimiento del armado de motos: quién ve el catálogo y qué no puede quedar mal guardado.
/// </summary>
/// <remarks>
/// Lo que se prueba aquí no es el flujo —ese todavía no existe— sino las tres reglas que, si
/// ceden, se descubren cuando ya hay un mes facturado: que el taller sin el módulo no sepa
/// que existe, que el técnico no pueda leer lo que el taller cobra, y que no se pueda guardar
/// una tarifa donde el pago al técnico supere el cobro.
/// </remarks>
public class ArmadoDeMotosTests
{
    private const string CadenaLocal =
        "Host=localhost;Port=5434;Database=garaj;Username=garaj;Password=garaj-dev-secret";

    private static GarajDbContext Contexto(Guid taller, string rol, Guid? usuario = null)
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? CadenaLocal;

        var contexto = new TenantContext();
        contexto.Initialize(taller, usuario ?? Guid.NewGuid(), rol, [], null);

        var options = new DbContextOptionsBuilder<GarajDbContext>()
            .UseNpgsql(cadena)
            .AddInterceptors(new AuditableEntityInterceptor(contexto, new DateTimeProvider()))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GarajDbContext(options, contexto);
    }

    private static async Task<Guid> TallerAsync(bool conArmado)
    {
        var taller = Guid.NewGuid();

        await using var db = Contexto(taller, AppRoles.Owner);

        db.Tenants.Add(new Tenant
        {
            Id = taller,
            Name = $"Armado {taller:N}"[..20],
            Currency = "HNL",
            DefaultPhoneCountryCode = "504",
            WorksWith = WorkshopKind.Vehicles,
            AssemblyEnabled = conArmado
        });

        await db.SaveChangesAsync();

        return taller;
    }

    private static AssemblyCatalogService Servicio(GarajDbContext db) =>
        new(db, db.TenantContext);

    [RequiereBaseLocal]
    public async Task El_taller_sin_el_modulo_recibe_404_y_no_403()
    {
        var taller = await TallerAsync(conArmado: false);

        await using var db = Contexto(taller, AppRoles.Owner);

        // 404 y no 403: a quien no lo tiene contratado no se le dice que el módulo existe.
        await Assert.ThrowsAsync<NotFoundException>(() => Servicio(db).ContractorsAsync());
    }

    [RequiereBaseLocal]
    public async Task El_tecnico_no_puede_leer_la_tabla_de_precios()
    {
        var taller = await TallerAsync(conArmado: true);

        await using var db = Contexto(taller, AppRoles.Technician);

        // De los contratistas cuelga lo que el taller cobra. Es el número que el dueño pidió
        // que el técnico no viera, y no se esconde en la pantalla: no sale del servidor.
        await Assert.ThrowsAsync<ForbiddenException>(() => Servicio(db).ContractorsAsync());
    }

    [RequiereBaseLocal]
    public async Task El_encargado_si_ve_los_modelos_y_las_bodegas()
    {
        var taller = await TallerAsync(conArmado: true);

        await using var dueno = Contexto(taller, AppRoles.Owner);
        await Servicio(dueno).SaveModelAsync(null, new SaveAssemblyModelRequest("FT150"));
        await Servicio(dueno).SaveWarehouseAsync(null, new SaveAssemblyWarehouseRequest("Bodega Sur"));

        await using var db = Contexto(taller, AppRoles.Manager);

        // Son las listas que llena quien registra una moto, y no llevan ningún precio.
        Assert.Single(await Servicio(db).ModelsAsync());
        Assert.Single(await Servicio(db).WarehousesAsync());
    }

    [RequiereBaseLocal]
    public async Task No_se_guarda_una_tarifa_donde_el_tecnico_gana_mas_de_lo_que_se_cobra()
    {
        var taller = await TallerAsync(conArmado: true);

        await using var db = Contexto(taller, AppRoles.Owner);
        var servicio = Servicio(db);

        var modelo = await servicio.SaveModelAsync(null, new SaveAssemblyModelRequest("FT150"));
        var contratista = await servicio.SaveContractorAsync(
            null, new SaveAssemblyContractorRequest("Distribuidora del Norte", null));

        // Los números cambiados de columna: es el error de tecleo que de verdad pasa al
        // llenar una tabla de cuarenta modelos, y el que nadie nota hasta que paga.
        await Assert.ThrowsAsync<AppException>(() => servicio.SaveRatesAsync(
            contratista.Id,
            new SaveAssemblyRatesRequest([new SaveAssemblyRateLine(modelo.Id, 90m, 250m)])));
    }

    [RequiereBaseLocal]
    public async Task La_tabla_de_precios_se_reemplaza_entera_y_no_se_duplica()
    {
        var taller = await TallerAsync(conArmado: true);

        await using var db = Contexto(taller, AppRoles.Owner);
        var servicio = Servicio(db);

        var ft150 = await servicio.SaveModelAsync(null, new SaveAssemblyModelRequest("FT150"));
        var dt125 = await servicio.SaveModelAsync(null, new SaveAssemblyModelRequest("DT125"));

        var contratista = await servicio.SaveContractorAsync(
            null, new SaveAssemblyContractorRequest("Distribuidora del Norte", null));

        await servicio.SaveRatesAsync(contratista.Id, new SaveAssemblyRatesRequest([
            new SaveAssemblyRateLine(ft150.Id, 250m, 90m),
            new SaveAssemblyRateLine(dt125.Id, 300m, 110m)
        ]));

        // Se vuelve a guardar con una sola fila y otro precio: la que no viene se va, y la que
        // viene se corrige. Guardar dos veces no puede dejar cuatro filas.
        var despues = await servicio.SaveRatesAsync(contratista.Id, new SaveAssemblyRatesRequest([
            new SaveAssemblyRateLine(ft150.Id, 275m, 100m)
        ]));

        Assert.Single(despues.Rates);
        Assert.Equal("FT150", despues.Rates[0].ModelName);
        Assert.Equal(275m, despues.Rates[0].Charge);
        Assert.Equal(100m, despues.Rates[0].TechnicianPay);
    }

    /// <summary>
    /// Una serie, una moto. Es la regla que la hoja de cálculo nunca pudo imponer y la razón
    /// principal por la que esto deja de vivir en una hoja: repetida es una moto cobrada dos
    /// veces, y la restricción va en la base porque el chequeo en el servicio no sobrevive a
    /// dos técnicos registrando la misma a la vez desde dos bodegas.
    /// </summary>
    [RequiereBaseLocal]
    public async Task Dos_motos_no_pueden_llevar_la_misma_serie()
    {
        var taller = await TallerAsync(conArmado: true);

        await using var db = Contexto(taller, AppRoles.Owner);
        var servicio = Servicio(db);

        var modelo = await servicio.SaveModelAsync(null, new SaveAssemblyModelRequest("FT150"));
        var bodega = await servicio.SaveWarehouseAsync(
            null, new SaveAssemblyWarehouseRequest("Bodega Sur"));
        var contratista = await servicio.SaveContractorAsync(
            null, new SaveAssemblyContractorRequest("Distribuidora del Norte", null));

        var sucursal = Guid.NewGuid();
        db.Branches.Add(new Branch
        {
            Id = sucursal, TenantId = taller, Name = "Matriz",
            Code = $"{taller:N}"[..3].ToUpperInvariant(), IsActive = true
        });

        var tecnico = Guid.NewGuid();

        MotorcycleAssembly Moto() => new()
        {
            TenantId = taller,
            BranchId = sucursal,
            ContractorId = contratista.Id,
            ModelId = modelo.Id,
            WarehouseId = bodega.Id,
            TechnicianUserId = tecnico,
            AssembledOn = new DateOnly(2026, 11, 7),
            Serial = "3C1JA1234RT567890",
            Charge = 250m,
            TechnicianPay = 90m
        };

        db.MotorcycleAssemblies.Add(Moto());
        await db.SaveChangesAsync();

        db.MotorcycleAssemblies.Add(Moto());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}

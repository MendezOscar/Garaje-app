using Garaj.Application.Abstractions;
using Garaj.Application.Assembly;
using Garaj.Application.Common;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Assembly;
using Garaj.Infrastructure.Identity;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Persistence.Interceptors;
using Garaj.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
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

    private static GarajDbContext Contexto(
        Guid taller, string rol, Guid? usuario = null, Guid? sucursal = null)
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? CadenaLocal;

        var contexto = new TenantContext();

        contexto.Initialize(
            taller, usuario ?? Guid.NewGuid(), rol,
            sucursal is { } s ? [s] : [], null);

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

    /// <summary>Un taller listo para registrar: contratista, modelo con precio y bodega.</summary>
    private static async Task<(Guid Taller, Guid Sucursal, Guid Contratista, Guid Modelo, Guid Bodega)>
        ContratoAsync()
    {
        var taller = await TallerAsync(conArmado: true);
        var sucursal = Guid.NewGuid();

        await using var db = Contexto(taller, AppRoles.Owner);

        db.Branches.Add(new Branch
        {
            Id = sucursal, TenantId = taller, Name = "Matriz",
            Code = $"{taller:N}"[..3].ToUpperInvariant(), IsActive = true
        });

        await db.SaveChangesAsync();

        var servicio = Servicio(db);
        var modelo = await servicio.SaveModelAsync(null, new SaveAssemblyModelRequest("FT150"));
        var bodega = await servicio.SaveWarehouseAsync(
            null, new SaveAssemblyWarehouseRequest("Bodega Sur"));
        var contratista = await servicio.SaveContractorAsync(
            null, new SaveAssemblyContractorRequest("Distribuidora del Norte", null));

        await servicio.SaveRatesAsync(contratista.Id, new SaveAssemblyRatesRequest([
            new SaveAssemblyRateLine(modelo.Id, 250m, 90m)
        ]));

        return (taller, sucursal, contratista.Id, modelo.Id, bodega.Id);
    }

    private static MotorcycleAssemblyService Registro(GarajDbContext db) =>
        new(db, db.TenantContext, new DateTimeProvider());

    /// <summary>
    /// Un técnico de verdad: con su fila de usuario y con el rol puesto.
    /// </summary>
    /// <remarks>
    /// El rol importa: a quien manda se le exige decir quién armó la moto, y lo que mande
    /// tiene que ser un técnico de su taller. Sin el rol, una moto se le podía apuntar al
    /// dueño, que no arma ninguna, y el reporte de pago salía mal sin que nada avisara.
    /// </remarks>
    private static async Task<Guid> TecnicoAsync(GarajDbContext db, Guid taller, string nombre)
    {
        var id = Guid.NewGuid();

        db.Users.Add(new AppUser
        {
            Id = id, TenantId = taller, FullName = nombre,
            UserName = $"t{id:N}@prueba.hn", Email = $"t{id:N}@prueba.hn",
            IsActive = true
        });

        var rol = await db.Roles.FirstOrDefaultAsync(r => r.Name == AppRoles.Technician);

        if (rol is null)
        {
            rol = new AppRole
            {
                Id = Guid.NewGuid(),
                Name = AppRoles.Technician,
                NormalizedName = AppRoles.Technician.ToUpperInvariant()
            };

            db.Roles.Add(rol);
        }

        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = id, RoleId = rol.Id });

        await db.SaveChangesAsync();

        return id;
    }

    /// <summary>
    /// El técnico necesita las tres listas para registrar, y el catálogo del dueño no se las
    /// puede dar: de ahí cuelgan los precios. Por eso van por su propia puerta, sin un solo
    /// número.
    /// </summary>
    [RequiereBaseLocal]
    public async Task El_tecnico_lee_las_listas_para_registrar_sin_ver_precios()
    {
        var (taller, sucursal, _, _, _) = await ContratoAsync();

        await using var db = Contexto(taller, AppRoles.Technician, Guid.NewGuid(), sucursal);

        // La puerta del dueño se le cierra…
        await Assert.ThrowsAsync<ForbiddenException>(() => Servicio(db).ContractorsAsync());

        // …y la suya le da lo que necesita para registrar.
        var listas = await Registro(db).PickListsAsync();

        Assert.Single(listas.Contractors);
        Assert.Single(listas.Models);
        Assert.Single(listas.Warehouses);
        Assert.True(listas.Contractors.Count > 0);
    }

    [RequiereBaseLocal]
    public async Task El_tecnico_registra_y_no_recibe_lo_que_el_taller_cobra()
    {
        var (taller, sucursal, contratista, modelo, bodega) = await ContratoAsync();
        var tecnico = Guid.NewGuid();

        await using var db = Contexto(taller, AppRoles.Technician, tecnico, sucursal);

        var moto = await Registro(db).RegisterAsync(new RegisterMotorcycleRequest(
            "3c1-ja1234-rt-567890", modelo, contratista, bodega));

        // La serie queda normalizada como el VIN de un vehículo.
        Assert.Equal("3C1JA1234RT567890", moto.Serial);

        // Su pago sí; el cobro del taller no sale del servidor. No se esconde: no viaja.
        Assert.Equal(90m, moto.TechnicianPay);
        Assert.Null(moto.Charge);
    }

    /// <summary>
    /// La sucursal sale del contrato, no de quien registra.
    /// </summary>
    /// <remarks>
    /// Antes se le preguntaba a quien registraba y, al técnico asignado a dos sucursales, el
    /// registro se le caía con «Diga en qué sucursal se armó». No tenía cómo contestar: está
    /// parado en la bodega del contratista, que no es una sucursal del taller. Una moto que
    /// no se puede registrar vuelve al WhatsApp, y de ahí ya no sale.
    /// </remarks>
    [RequiereBaseLocal]
    public async Task El_tecnico_con_dos_sucursales_registra_y_la_moto_cae_en_la_del_contrato()
    {
        var (taller, sucursal, contratista, modelo, bodega) = await ContratoAsync();
        var otra = Guid.NewGuid();

        await using (var dueno = Contexto(taller, AppRoles.Owner))
        {
            dueno.Branches.Add(new Branch
            {
                Id = otra, TenantId = taller, Name = "Zona Sur",
                Code = $"{otra:N}"[..3].ToUpperInvariant(), IsActive = true
            });

            await dueno.SaveChangesAsync();
        }

        var contexto = new TenantContext();
        contexto.Initialize(taller, Guid.NewGuid(), AppRoles.Technician, [sucursal, otra], null);

        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? CadenaLocal;

        await using var db = new GarajDbContext(
            new DbContextOptionsBuilder<GarajDbContext>()
                .UseNpgsql(cadena)
                .AddInterceptors(new AuditableEntityInterceptor(contexto, new DateTimeProvider()))
                .UseSnakeCaseNamingConvention()
                .Options,
            contexto);

        // Sin decir en cuál: el técnico no manda BranchId, igual que desde el teléfono.
        var moto = await Registro(db).RegisterAsync(new RegisterMotorcycleRequest(
            "3C1JA1234RT111111", modelo, contratista, bodega));

        var guardada = await db.MotorcycleAssemblies.AsNoTracking()
            .FirstAsync(m => m.Id == moto.Id);

        // La del contrato, que es la que el dueño puso al darlo de alta.
        Assert.Equal(sucursal, guardada.BranchId);
    }

    [RequiereBaseLocal]
    public async Task El_dueno_si_ve_lo_que_se_cobra()
    {
        var (taller, sucursal, contratista, modelo, bodega) = await ContratoAsync();

        await using var db = Contexto(taller, AppRoles.Owner, Guid.NewGuid(), sucursal);
        var tecnico = await TecnicoAsync(db, taller, "Kevin Discua");

        var moto = await Registro(db).RegisterAsync(new RegisterMotorcycleRequest(
            "3C1JA1234RT567890", modelo, contratista, bodega,
            BranchId: sucursal, TechnicianUserId: tecnico));

        Assert.Equal(250m, moto.Charge);
    }

    [RequiereBaseLocal]
    public async Task Un_modelo_sin_precio_con_ese_contratista_no_se_registra()
    {
        var (taller, sucursal, contratista, _, bodega) = await ContratoAsync();

        await using var db = Contexto(taller, AppRoles.Owner, Guid.NewGuid(), sucursal);
        var servicio = Servicio(db);

        // Un modelo nuevo que todavía no se negoció con nadie.
        var sinPrecio = await servicio.SaveModelAsync(null, new SaveAssemblyModelRequest("DT125"));
        var tecnico = await TecnicoAsync(db, taller, "Kevin Discua");

        // Registrarlo en cero haría que esa moto se armara gratis y nadie lo notaría hasta
        // cuadrar el mes.
        await Assert.ThrowsAsync<AppException>(() => Registro(db).RegisterAsync(
            new RegisterMotorcycleRequest(
                "3C1JA1234RT567891", sinPrecio.Id, contratista, bodega,
                BranchId: sucursal, TechnicianUserId: tecnico)));
    }

    [RequiereBaseLocal]
    public async Task La_misma_serie_tecleada_distinto_rebota_igual()
    {
        var (taller, sucursal, contratista, modelo, bodega) = await ContratoAsync();

        await using var db = Contexto(taller, AppRoles.Owner, Guid.NewGuid(), sucursal);
        var registro = Registro(db);
        var tecnico = await TecnicoAsync(db, taller, "Kevin Discua");

        await registro.RegisterAsync(new RegisterMotorcycleRequest(
            "3C1JA1234RT567890", modelo, contratista, bodega,
            BranchId: sucursal, TechnicianUserId: tecnico));

        await Assert.ThrowsAsync<ConflictException>(() => registro.RegisterAsync(
            new RegisterMotorcycleRequest(
                "3c1 ja1234 rt 567890", modelo, contratista, bodega,
                BranchId: sucursal, TechnicianUserId: tecnico)));
    }

    [RequiereBaseLocal]
    public async Task El_tecnico_solo_ve_lo_suyo_y_lo_que_suma_para_el()
    {
        var (taller, sucursal, contratista, modelo, bodega) = await ContratoAsync();

        var uno = Guid.NewGuid();
        var otro = Guid.NewGuid();

        await using (var db = Contexto(taller, AppRoles.Technician, uno, sucursal))
        {
            await Registro(db).RegisterAsync(new RegisterMotorcycleRequest(
                "3C1JA1234RT000001", modelo, contratista, bodega));
            await Registro(db).RegisterAsync(new RegisterMotorcycleRequest(
                "3C1JA1234RT000002", modelo, contratista, bodega));
        }

        await using (var db = Contexto(taller, AppRoles.Technician, otro, sucursal))
        {
            await Registro(db).RegisterAsync(new RegisterMotorcycleRequest(
                "3C1JA1234RT000003", modelo, contratista, bodega));
        }

        await using var mias = Contexto(taller, AppRoles.Technician, uno, sucursal);
        var lo_mio = await Registro(mias).MineAsync(null, null);

        Assert.Equal(2, lo_mio.Count);
        Assert.Equal(180m, lo_mio.Pay);
        Assert.All(lo_mio.Items, m => Assert.Null(m.Charge));
    }

    /// <summary>
    /// El prefijo que el dueño escribe en el catálogo hace que el modelo se proponga desde
    /// la <b>primera</b> moto, sin esperar a que haya historia.
    /// </summary>
    /// <remarks>
    /// Los prefijos de la prueba salen de etiquetas de Italika de verdad: la 200Z lleva
    /// <c>LLCLPLPC</c> y la FT125 <c>LLCLPP20</c>.
    /// </remarks>
    [RequiereBaseLocal]
    public async Task El_prefijo_del_catalogo_propone_el_modelo_desde_la_primera_moto()
    {
        var taller = await TallerAsync(conArmado: true);

        await using var db = Contexto(taller, AppRoles.Owner, Guid.NewGuid());
        var catalogo = Servicio(db);

        await catalogo.SaveModelAsync(
            null, new SaveAssemblyModelRequest("200Z", VinPrefix: "LLCLPLPC"));

        var ft125 = await catalogo.SaveModelAsync(
            null, new SaveAssemblyModelRequest("FT125", VinPrefix: "llclpp20"));

        // Sin haber registrado ninguna moto todavía.
        var propuesta = await Registro(db).GuessModelAsync("LLCLPP200VE100410");

        Assert.Equal(ft125.Id, propuesta.ModelId);
        Assert.Equal("FT125", propuesta.ModelName);

        // El prefijo se guarda normalizado, como la serie: se escribió en minúsculas.
        var modelos = await catalogo.ModelsAsync();
        Assert.Equal("LLCLPP20", modelos.First(m => m.Name == "FT125").VinPrefix);

        // Y una serie de un prefijo que nadie declaró sigue sin propuesta.
        var ajena = await Registro(db).GuessModelAsync("9XYKK9999ZZ000001");
        Assert.Null(ajena.ModelId);
    }

    /// <summary>
    /// La tabla de prefijos que se aprende sola. Es lo que hace que registrar una moto sean
    /// tres toques: la primera de cada prefijo se teclea, de la segunda en adelante se
    /// propone. No hay tabla que comprar, y la que sale es la de los modelos que este taller
    /// de verdad arma.
    /// </summary>
    [RequiereBaseLocal]
    public async Task El_modelo_se_propone_desde_la_segunda_moto_del_mismo_prefijo()
    {
        var (taller, sucursal, contratista, modelo, bodega) = await ContratoAsync();

        await using var db = Contexto(taller, AppRoles.Owner, Guid.NewGuid(), sucursal);
        var registro = Registro(db);

        // Antes de la primera no hay nada que proponer, y no se inventa.
        var antes = await registro.GuessModelAsync("3C1JA1234RT999999");
        Assert.Null(antes.ModelId);

        await registro.RegisterAsync(new RegisterMotorcycleRequest(
            "3C1JA1234RT000001", modelo, contratista, bodega,
            BranchId: sucursal,
            TechnicianUserId: await TecnicoAsync(db, taller, "Kevin Discua")));

        // Otro chasis del mismo prefijo: mismos ocho primeros caracteres, distinto correlativo.
        var despues = await registro.GuessModelAsync("3C1JA1234RT000002");

        Assert.Equal(modelo, despues.ModelId);
        Assert.Equal("FT150", despues.ModelName);
        Assert.Equal(1, despues.SeenBefore);

        // Y un prefijo que nunca se ha visto sigue sin propuesta.
        var ajeno = await registro.GuessModelAsync("9XYKK9999ZZ000001");
        Assert.Null(ajeno.ModelId);
    }

    /// <summary>
    /// El dueño registra la moto que el técnico le reportó por teléfono, y queda a nombre
    /// del técnico. Un técnico, en cambio, solo puede registrar lo suyo: lo que mande en el
    /// campo se ignora, porque si no cualquiera se apunta motos a nombre de otro.
    /// </summary>
    [RequiereBaseLocal]
    public async Task El_dueno_registra_a_nombre_de_un_tecnico_y_el_tecnico_solo_a_su_nombre()
    {
        var (taller, sucursal, contratista, modelo, bodega) = await ContratoAsync();

        var otro = Guid.NewGuid();
        Guid tecnico;

        await using (var db = Contexto(taller, AppRoles.Owner, Guid.NewGuid(), sucursal))
        {
            tecnico = await TecnicoAsync(db, taller, "Kevin Discua");

            var moto = await Registro(db).RegisterAsync(new RegisterMotorcycleRequest(
                "3C1JA1234RT000001", modelo, contratista, bodega,
                BranchId: sucursal, TechnicianUserId: tecnico));

            Assert.Equal(tecnico, moto.TechnicianUserId);
        }

        await using (var db = Contexto(taller, AppRoles.Technician, otro, sucursal))
        {
            // Pide que se la apunten a otro: se ignora y queda a su nombre.
            var moto = await Registro(db).RegisterAsync(new RegisterMotorcycleRequest(
                "3C1JA1234RT000002", modelo, contratista, bodega,
                TechnicianUserId: tecnico));

            Assert.Equal(otro, moto.TechnicianUserId);
        }
    }

    /// <summary>
    /// El dueño no arma motos: tiene que decir cuál de sus técnicos la armó, y él mismo no
    /// está en la lista.
    /// </summary>
    /// <remarks>
    /// Callarlo se la apuntaba a él. No fallaba nada: la moto quedaba registrada, y el pago
    /// del técnico que de verdad la armó desaparecía del reporte del mes. De los errores que
    /// no avisan, este es de los caros.
    /// </remarks>
    [RequiereBaseLocal]
    public async Task El_dueno_no_sale_en_la_lista_y_tiene_que_decir_quien_armo()
    {
        var (taller, sucursal, contratista, modelo, bodega) = await ContratoAsync();
        var dueno = Guid.NewGuid();

        await using var db = Contexto(taller, AppRoles.Owner, dueno, sucursal);
        var tecnico = await TecnicoAsync(db, taller, "Kevin Discua");

        var listas = await Registro(db).PickListsAsync();

        // Están sus técnicos, y él no.
        Assert.Contains(listas.Technicians, t => t.Id == tecnico);
        Assert.DoesNotContain(listas.Technicians, t => t.Id == dueno);

        // Sin decir quién la armó, no se registra.
        await Assert.ThrowsAsync<AppException>(() => Registro(db).RegisterAsync(
            new RegisterMotorcycleRequest(
                "3C1JA1234RT222222", modelo, contratista, bodega, BranchId: sucursal)));

        var moto = await Registro(db).RegisterAsync(new RegisterMotorcycleRequest(
            "3C1JA1234RT222222", modelo, contratista, bodega,
            BranchId: sucursal, TechnicianUserId: tecnico));

        Assert.Equal(tecnico, moto.TechnicianUserId);
    }

    private static AssemblyAdminService Panel(GarajDbContext db) =>
        new(db, db.TenantContext, new DateTimeProvider());

    /// <summary>
    /// Cerrar un corte es lo que contesta «¿esto ya lo cobré?», que es la pregunta que la
    /// hoja de cálculo nunca pudo contestar. Lo que entra en el corte deja de estar
    /// pendiente, y cerrar dos veces el mismo periodo no vuelve a cobrarlo.
    /// </summary>
    [RequiereBaseLocal]
    public async Task Lo_facturado_deja_de_estar_pendiente_y_no_se_cobra_dos_veces()
    {
        var (taller, sucursal, contratista, modelo, bodega) = await ContratoAsync();

        await using var db = Contexto(taller, AppRoles.Owner, Guid.NewGuid(), sucursal);
        var registro = Registro(db);
        var panel = Panel(db);
        var tecnico = await TecnicoAsync(db, taller, "Kevin Discua");

        foreach (var serie in new[] { "3C1JA1234RT000001", "3C1JA1234RT000002" })
        {
            await registro.RegisterAsync(new RegisterMotorcycleRequest(
                serie, modelo, contratista, bodega,
                BranchId: sucursal, TechnicianUserId: tecnico));
        }

        var hoy = new DateTimeProvider().Today();
        var mes = new DateOnly(hoy.Year, hoy.Month, 1);

        var corte = await panel.CloseAsync(new CloseSettlementRequest(
            AssemblySettlementKind.Billing, mes, hoy, ContractorId: contratista,
            Reference: "FAC-001"));

        Assert.Equal(2, corte.Count);
        Assert.Equal(500m, corte.Total);

        // Lo facturado ya no aparece como pendiente.
        var pendiente = await panel.ListAsync(new AssemblyQuery { OnlyUnbilled = true });
        Assert.Equal(0, pendiente.Total);

        // Y cerrar otra vez el mismo periodo no encuentra nada que cobrar.
        await Assert.ThrowsAsync<AppException>(() => panel.CloseAsync(new CloseSettlementRequest(
            AssemblySettlementKind.Billing, mes, hoy, ContractorId: contratista)));
    }

    /// <summary>
    /// Una moto ya facturada no se corrige ni se borra: del otro lado hay una factura
    /// emitida. Reabrir el corte es la única manera honesta de arreglarlo.
    /// </summary>
    [RequiereBaseLocal]
    public async Task Una_moto_ya_facturada_se_congela_hasta_que_se_reabra_el_corte()
    {
        var (taller, sucursal, contratista, modelo, bodega) = await ContratoAsync();

        await using var db = Contexto(taller, AppRoles.Owner, Guid.NewGuid(), sucursal);
        var panel = Panel(db);

        var moto = await Registro(db).RegisterAsync(new RegisterMotorcycleRequest(
            "3C1JA1234RT000001", modelo, contratista, bodega,
            BranchId: sucursal,
            TechnicianUserId: await TecnicoAsync(db, taller, "Kevin Discua")));

        var hoy = new DateTimeProvider().Today();
        var mes = new DateOnly(hoy.Year, hoy.Month, 1);

        var corte = await panel.CloseAsync(new CloseSettlementRequest(
            AssemblySettlementKind.Billing, mes, hoy, ContractorId: contratista));

        var correccion = new CorrectMotorcycleRequest(
            "3C1JA1234RT000009", modelo, contratista, bodega,
            moto.TechnicianUserId, hoy);

        await Assert.ThrowsAsync<ConflictException>(
            () => panel.CorrectAsync(moto.Id, correccion));

        await Assert.ThrowsAsync<ConflictException>(() => panel.DeleteAsync(moto.Id));

        // Reabrir suelta la moto y vuelve a estar pendiente.
        await panel.ReopenAsync(corte.Id);

        var corregida = await panel.CorrectAsync(moto.Id, correccion);
        Assert.Equal("3C1JA1234RT000009", corregida.Serial);

        var pendiente = await panel.ListAsync(new AssemblyQuery { OnlyUnbilled = true });
        Assert.Equal(1, pendiente.Total);
    }

    /// <summary>
    /// Facturar y pagar son dos cortes distintos sobre las mismas motos: facturarle al
    /// contratista no le paga al técnico, y al revés tampoco.
    /// </summary>
    [RequiereBaseLocal]
    public async Task Facturar_no_paga_y_pagar_no_factura()
    {
        var (taller, sucursal, contratista, modelo, bodega) = await ContratoAsync();
        var tecnico = Guid.NewGuid();

        await using (var deElla = Contexto(taller, AppRoles.Technician, tecnico, sucursal))
        {
            await Registro(deElla).RegisterAsync(new RegisterMotorcycleRequest(
                "3C1JA1234RT000001", modelo, contratista, bodega));
        }

        await using var db = Contexto(taller, AppRoles.Owner, Guid.NewGuid(), sucursal);
        var panel = Panel(db);

        var hoy = new DateTimeProvider().Today();
        var mes = new DateOnly(hoy.Year, hoy.Month, 1);

        await panel.CloseAsync(new CloseSettlementRequest(
            AssemblySettlementKind.Billing, mes, hoy, ContractorId: contratista));

        // Facturada, pero todavía sin pagar.
        var porPagar = await panel.ListAsync(new AssemblyQuery { OnlyUnpaid = true });
        Assert.Equal(1, porPagar.Total);

        var pago = await panel.CloseAsync(new CloseSettlementRequest(
            AssemblySettlementKind.TechnicianPay, mes, hoy, TechnicianUserId: tecnico));

        Assert.Equal(90m, pago.Total);

        var yaNo = await panel.ListAsync(new AssemblyQuery { OnlyUnpaid = true });
        Assert.Equal(0, yaNo.Total);
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

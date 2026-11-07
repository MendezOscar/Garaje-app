using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Customers;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Persistence.Interceptors;
using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Tests;

/// <summary>
/// El VIN o número de serie del chasis: cómo se guarda, que no se repita y que se encuentre.
/// </summary>
/// <remarks>
/// Importa más de lo que parece. En la moto sin placa el VIN es el único nombre que tiene el
/// vehículo, así que si se guarda como lo teclean —con guiones, en minúsculas— el mismo
/// chasis termina registrado de tres maneras y la búsqueda, que es para lo que sirve, no
/// encuentra ninguna. Y un VIN repetido significa el mismo vehículo dado de alta dos veces,
/// con su historial partido en dos fichas.
/// </remarks>
public class VinDelVehiculoTests
{
    private const string CadenaLocal =
        "Host=localhost;Port=5434;Database=garaj;Username=garaj;Password=garaj-dev-secret";

    private static GarajDbContext Contexto(Guid taller, Guid usuario)
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? CadenaLocal;

        var contexto = new TenantContext();
        contexto.Initialize(taller, usuario, AppRoles.Owner, [], null);

        var options = new DbContextOptionsBuilder<GarajDbContext>()
            .UseNpgsql(cadena)
            .AddInterceptors(new AuditableEntityInterceptor(contexto, new DateTimeProvider()))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GarajDbContext(options, contexto);
    }

    /// <summary>Un taller con un cliente, para colgarle vehículos.</summary>
    private static async Task<(Guid Taller, Guid Cliente)> EscenarioAsync()
    {
        var taller = Guid.NewGuid();
        var cliente = Guid.NewGuid();

        await using var db = Contexto(taller, Guid.NewGuid());

        db.Tenants.Add(new Tenant
        {
            Id = taller,
            Name = $"Prueba {taller:N}"[..20],
            Currency = "HNL",
            DefaultPhoneCountryCode = "504",
            WorksWith = WorkshopKind.Vehicles
        });

        db.Customers.Add(new Customer
        {
            Id = cliente, TenantId = taller, FullName = "Cliente de prueba", Phone = "50499990000"
        });

        await db.SaveChangesAsync();

        return (taller, cliente);
    }

    private static VehicleService Servicio(GarajDbContext db) =>
        new(db, db.TenantContext);

    private static SaveVehicleRequest Moto(Guid cliente, string? vin, string? placa = null) =>
        new(cliente, VehicleType.Motorcycle, "Italika", "FT150", 2024, placa, vin, null, null, null);

    [RequiereBaseLocal]
    public async Task El_vin_se_guarda_en_mayusculas_y_sin_separadores()
    {
        var (taller, cliente) = await EscenarioAsync();

        await using var db = Contexto(taller, Guid.NewGuid());

        // Como lo teclea quien lo copia de la calcomanía: con guiones y en minúsculas.
        var creado = await Servicio(db).CreateAsync(Moto(cliente, "3c1-ja1234-rt-567890"));

        Assert.Equal("3C1JA1234RT567890", creado.Vin);
    }

    [RequiereBaseLocal]
    public async Task Dos_vehiculos_no_pueden_llevar_el_mismo_vin()
    {
        var (taller, cliente) = await EscenarioAsync();

        await using var db = Contexto(taller, Guid.NewGuid());
        var servicio = Servicio(db);

        await servicio.CreateAsync(Moto(cliente, "3C1JA1234RT567890"));

        // El mismo chasis, tecleado distinto. Tiene que chocar igual: lo que se compara es el
        // VIN normalizado, no el texto.
        await Assert.ThrowsAsync<ConflictException>(
            () => servicio.CreateAsync(Moto(cliente, "3c1 ja1234 rt 567890")));
    }

    [RequiereBaseLocal]
    public async Task La_moto_se_encuentra_por_los_ultimos_digitos_del_chasis()
    {
        var (taller, cliente) = await EscenarioAsync();

        await using var db = Contexto(taller, Guid.NewGuid());
        var servicio = Servicio(db);

        await servicio.CreateAsync(Moto(cliente, "3C1JA1234RT567890"));

        // Nadie teclea los diecisiete: se copian los últimos del chasis.
        var hallados = await servicio.ListAsync(new VehicleQuery { Search = "567890" });

        Assert.Single(hallados.Items);
        Assert.Equal("3C1JA1234RT567890", hallados.Items[0].Vin);
    }

    /// <summary>
    /// Tres caracteres no buscan por VIN. Si lo hicieran, teclear «BMW» traería todas las
    /// motos cuyo chasis lleve esas letras en medio, que no es lo que nadie quiso buscar.
    /// </summary>
    [RequiereBaseLocal]
    public async Task Un_termino_corto_no_dispara_la_busqueda_por_vin()
    {
        var (taller, cliente) = await EscenarioAsync();

        await using var db = Contexto(taller, Guid.NewGuid());
        var servicio = Servicio(db);

        await servicio.CreateAsync(Moto(cliente, "3C1JA1234RT567890"));

        var hallados = await servicio.ListAsync(new VehicleQuery { Search = "RT5" });

        Assert.Empty(hallados.Items);
    }

    [RequiereBaseLocal]
    public async Task El_vin_se_puede_llenar_despues_sin_tocar_el_resto_de_la_ficha()
    {
        var (taller, cliente) = await EscenarioAsync();

        await using var db = Contexto(taller, Guid.NewGuid());
        var servicio = Servicio(db);

        // Como están casi todos los vehículos de hoy: dados de alta sin VIN, porque ninguna
        // pantalla lo escribía.
        var creado = await servicio.CreateAsync(Moto(cliente, null, "AAA1234"));

        var conVin = await servicio.SetVinAsync(creado.Id, new SetVinRequest("3c1ja1234rt567890"));

        Assert.Equal("3C1JA1234RT567890", conVin.Vin);

        // Y lo demás sigue en pie: es el riesgo de mandar la ficha entera desde una pantalla
        // que no la tiene completa.
        Assert.Equal("AAA1234", conVin.Plate);
        Assert.Equal("Italika", conVin.Brand);
        Assert.Equal(2024, conVin.Year);
    }
}

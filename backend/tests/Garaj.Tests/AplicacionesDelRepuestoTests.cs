using Garaj.Application.Abstractions;
using Garaj.Application.Inventory;
using Garaj.Application.Validation;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Persistence.Interceptors;
using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Tests;

/// <summary>
/// Guardar «a qué vehículos les sirve» en un repuesto que ya las tenía.
/// </summary>
/// <remarks>
/// El taller reportó «Alguien más cambió esto mientras usted trabajaba» al corregir una
/// aplicación ya guardada, en el panel y en el teléfono. La causa era que <c>BaseEntity</c>
/// pone el Id al construir: EF veía una clave puesta bajo un padre que ya existía, lo tomaba
/// por edición y emitía UPDATE en vez de INSERT; ese UPDATE no encontraba nada porque la fila
/// vieja acababa de borrarse en el mismo guardado.
///
/// Corre contra la base local, que es donde se puede ver el SQL de verdad. Sin base no hay
/// prueba: el fallo solo aparece al escribir.
/// </remarks>
public class AplicacionesDelRepuestoTests
{
    private const string CadenaLocal = "Host=localhost;Port=5434;Database=garaj;Username=garaj;Password=garaj";

    private static GarajDbContext Contexto(Guid taller)
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? CadenaLocal;

        var contexto = new TenantContext();
        contexto.Initialize(taller, Guid.NewGuid(), "Owner", [], null);

        // El mismo interceptor que pone el DI: es el que rellena el taller y la auditoría.
        // Sin él las filas entran con tenant vacío y no las ve ni su propio dueño.
        var options = new DbContextOptionsBuilder<GarajDbContext>()
            .UseNpgsql(cadena)
            .AddInterceptors(new AuditableEntityInterceptor(contexto, new DateTimeProvider()))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GarajDbContext(options, contexto);
    }

    private static PartService Servicio(GarajDbContext db) =>
        new(db, db.TenantContext, new DateTimeProvider(), new SavePartValidator());

    [RequiereBaseLocal]
    public async Task Corregir_los_vehiculos_de_un_repuesto_que_ya_los_tenia()
    {
        var taller = Guid.NewGuid();
        Guid parteId;

        // Nace con un vehículo, que es como queda al crearlo desde la pantalla.
        await using (var db = Contexto(taller))
        {
            var creada = await Servicio(db).CreateAsync(new SavePartRequest(
                $"PRU-{taller:N}"[..12], "Repuesto de prueba", null, "KYB", "Suspensión", "u",
                1250m, 1900m, true,
                [new PartApplicationDto("Toyota", "RAV4", 2014, 2018, "2.4")]));

            parteId = creada.Id;
        }

        await using (var comprobar = Contexto(taller))
        {
            var filas = await comprobar.Parts.IgnoreQueryFilters().CountAsync(p => p.Id == parteId);
            var tenant = await comprobar.Parts.IgnoreQueryFilters()
                .Where(p => p.Id == parteId).Select(p => p.TenantId).FirstOrDefaultAsync();
            Assert.True(filas == 1, $"filas={filas} tenantGuardado={tenant} tenantEsperado={taller}");
        }

        // Y ahora se corrige el motor, que es lo que reventaba.
        await using (var db = Contexto(taller))
        {
            var guardada = await Servicio(db).UpdateAsync(parteId, new SavePartRequest(
                $"PRU-{taller:N}"[..12], "Repuesto de prueba", null, "KYB", "Suspensión", "u",
                1250m, 1900m, true,
                [new PartApplicationDto("Toyota", "RAV4", 2014, 2018, "2.5")]));

            var aplica = Assert.Single(guardada.Applications);
            Assert.Equal("2.5", aplica.Engine);
        }

        await using (var limpieza = Contexto(taller))
        {
            var parte = await limpieza.Parts.FirstAsync(p => p.Id == parteId);
            limpieza.Parts.Remove(parte);
            await limpieza.SaveChangesAsync();
        }
    }
}

/// <summary>
/// Como <see cref="FactAttribute"/>, pero se salta si no hay base a la que conectarse.
/// </summary>
/// <remarks>
/// Esta prueba escribe de verdad: el fallo que cuida —un UPDATE que no encuentra fila— solo
/// se ve contra Postgres, no contra un doble en memoria. Y quien clona el repo y corre
/// `dotnet test` sin levantar docker no tiene por qué ver una prueba roja que no es suya.
/// </remarks>
public sealed class RequiereBaseLocalAttribute : FactAttribute
{
    public RequiereBaseLocalAttribute()
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5434;Database=garaj;Username=garaj;Password=garaj";

        try
        {
            using var conexion = new Npgsql.NpgsqlConnection(cadena);
            conexion.Open();
        }
        catch
        {
            Skip = "Necesita la base local. Levántela con `docker compose up -d`.";
        }
    }
}

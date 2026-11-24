using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Persistence.Interceptors;
using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Tests;

/// <summary>
/// El número del documento que emitimos nosotros: el autorizado por el SAR y el correlativo
/// propio.
/// </summary>
/// <remarks>
/// Lo único que se prueba aquí es lo que no se puede arreglar después: que **dos emisiones a
/// la vez no se lleven el mismo número**. Mientras las facturas las emitía una persona de a
/// una no pasaba; con los portales de StockRuta y Profeapp pidiendo número por la API, sí.
///
/// Dos documentos con el mismo número del SAR no se distinguen, y uno de los dos no existe
/// para el SAR. Un hueco en la numeración, en cambio, se explica.
/// </remarks>
public class NumeracionDePlataformaTests
{
    private const string CadenaLocal =
        "Host=localhost;Port=5434;Database=garaj;Username=garaj;Password=garaj-dev-secret";

    private static string Cadena() =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? CadenaLocal;

    /// <summary>
    /// Cada tarea con su propia conexión, como en producción: dos peticiones no comparten
    /// DbContext.
    /// </summary>
    private static GarajDbContext Contexto()
    {
        var contexto = new TenantContext();
        contexto.Initialize(Guid.NewGuid(), Guid.NewGuid(), "Platform", [], null);

        var options = new DbContextOptionsBuilder<GarajDbContext>()
            .UseNpgsql(Cadena())
            .AddInterceptors(new AuditableEntityInterceptor(contexto, new DateTimeProvider()))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GarajDbContext(options, contexto);
    }

    /// <summary>Un rango autorizado con cien números por delante, vigente.</summary>
    private static async Task<Guid> RangoAsync(int desde, int hasta)
    {
        await using var db = Contexto();

        var rango = new PlatformFiscalRange
        {
            Id = Guid.NewGuid(),
            Cai = $"PRUEBA-{Guid.NewGuid():N}",
            EstablishmentCode = "000",
            PointOfSaleCode = "001",
            DocumentType = "01",
            RangeStart = desde,
            RangeEnd = hasta,
            NextNumber = desde,
            IssueDeadline = DateTimeOffset.UtcNow.AddMonths(6),
            // Inactivo a propósito: la pantalla busca «el activo más reciente» y una prueba no
            // tiene por qué cambiarle el rango vigente a nadie. Se toma por id.
            IsActive = false
        };

        db.PlatformFiscalRanges.Add(rango);
        await db.SaveChangesAsync();

        // Activo solo para la prueba, sin pasar por el camino que mira «el más reciente».
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE platform_fiscal_ranges SET is_active = true WHERE id = {0}", rango.Id);

        return rango.Id;
    }

    [RequiereBaseLocal]
    public async Task Diez_emisiones_a_la_vez_no_repiten_numero()
    {
        var rango = await RangoAsync(1, 100);
        var ahora = DateTimeOffset.UtcNow;

        var tareas = Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var db = Contexto();
            return await NumeracionDePlataforma.TomarAutorizadoAsync(db, rango, ahora, default);
        });

        var numeros = (await Task.WhenAll(tareas)).ToList();

        Assert.All(numeros, n => Assert.NotNull(n));

        // Diez distintos, y los diez primeros del rango: ni repetidos ni saltados.
        Assert.Equal(10, numeros.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 10), numeros.Select(n => n!.Value).OrderBy(n => n));
    }

    [RequiereBaseLocal]
    public async Task Cuando_se_acaba_el_rango_deja_de_dar_numeros()
    {
        // Tres números para cinco que los piden: dos se tienen que quedar sin.
        var rango = await RangoAsync(1, 3);
        var ahora = DateTimeOffset.UtcNow;

        var tareas = Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var db = Contexto();
            return await NumeracionDePlataforma.TomarAutorizadoAsync(db, rango, ahora, default);
        });

        var numeros = (await Task.WhenAll(tareas)).ToList();

        Assert.Equal(3, numeros.Count(n => n is not null));
        Assert.Equal(2, numeros.Count(n => n is null));

        // Y no se pasó del tope: nada de emitir el 4 de un rango que llega al 3.
        await using var db = Contexto();
        var sigue = await db.PlatformFiscalRanges.AsNoTracking().FirstAsync(r => r.Id == rango);
        Assert.Equal(4, sigue.NextNumber);
    }

    [RequiereBaseLocal]
    public async Task Un_rango_vencido_no_da_numeros()
    {
        var rango = await RangoAsync(1, 100);

        await using (var db = Contexto())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE platform_fiscal_ranges SET issue_deadline = now() - interval '1 day' "
                + "WHERE id = {0}", rango);
        }

        await using var otro = Contexto();

        Assert.Null(await NumeracionDePlataforma.TomarAutorizadoAsync(
            otro, rango, DateTimeOffset.UtcNow, default));
    }
}

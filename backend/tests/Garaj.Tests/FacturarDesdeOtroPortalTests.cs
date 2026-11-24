using Garaj.Application.Common;
using Garaj.Application.Platform;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Persistence.Interceptors;
using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Tests;

/// <summary>
/// La API con la que el portal de otro servicio —StockRuta, Profeapp— pide el número de su
/// documento, porque el CAI es uno solo para todo lo que cobramos.
/// </summary>
/// <remarks>
/// Dos cosas son las que no se pueden arreglar después y por eso se prueban:
///
/// - **La llave.** Sin ella, o con una que no vale, no se emite nada. Es la única credencial:
///   aquí no hay sesión ni taller.
/// - **La idempotencia.** Un reintento —internet caído, respuesta perdida— tiene que devolver
///   el documento que ya salió. Si no, el mismo cobro quema dos números del SAR y uno de los
///   dos documentos no existe para el SAR.
/// </remarks>
public class FacturarDesdeOtroPortalTests
{
    private const string CadenaLocal =
        "Host=localhost;Port=5434;Database=garaj;Username=garaj;Password=garaj-dev-secret";

    private static GarajDbContext Contexto(string rol = "Platform")
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? CadenaLocal;

        var contexto = new TenantContext();
        contexto.Initialize(Guid.NewGuid(), Guid.NewGuid(), rol, [], null);

        var options = new DbContextOptionsBuilder<GarajDbContext>()
            .UseNpgsql(cadena)
            .AddInterceptors(new AuditableEntityInterceptor(contexto, new DateTimeProvider()))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GarajDbContext(options, contexto);
    }

    private static PlatformBillingService Servicio(GarajDbContext db) =>
        new(db, db.TenantContext, new DateTimeProvider());

    /// <summary>Un servicio con llave, para poder emitir en su nombre.</summary>
    private static async Task<(Guid Id, string Llave)> ServicioConLlaveAsync()
    {
        await using var db = Contexto();

        var producto = await Servicio(db).SaveProductAsync(
            null, new SavePlatformProductRequest($"Prueba {Guid.NewGuid():N}"[..20]));

        var llave = await Servicio(db).RotateApiKeyAsync(producto.Id);

        return (producto.Id, llave.ApiKey);
    }

    private static IssueInvoiceRequest Peticion(string cliente = "Cliente de prueba") =>
        new(
            new InvoiceCustomerDto(cliente, "Cliente de prueba S. de R.L.", "08011999123456"),
            [new SaveInvoiceLineRequest("Mensualidad de noviembre", 1, 500m)],
            ExternalCustomerRef: "cli-0001",
            // Sin CAI: la prueba no tiene por qué consumir el rango autorizado de nadie.
            Fiscal: false,
            PaymentReference: "DEP-77123");

    [RequiereBaseLocal]
    public async Task Sin_llave_no_se_emite_nada()
    {
        await using var db = Contexto();

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => Servicio(db).IssueAsync("", "idem-1", Peticion()));

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => Servicio(db).IssueAsync("gbk_loquesea", "idem-1", Peticion()));
    }

    [RequiereBaseLocal]
    public async Task El_documento_sale_a_nombre_del_servicio_y_sin_taller()
    {
        var (productoId, llave) = await ServicioConLlaveAsync();

        await using var db = Contexto();

        var emitida = await Servicio(db).IssueAsync(llave, $"idem-{Guid.NewGuid():N}", Peticion());

        Assert.False(emitida.AlreadyIssued);
        Assert.Equal(500m, emitida.Subtotal);
        Assert.False(emitida.IsFiscal);

        var guardada = await db.SubscriptionInvoices.AsNoTracking()
            .FirstAsync(f => f.Id == emitida.Id);

        // Ni taller ni pago nuestro: el cobro lo hizo su portal.
        Assert.Null(guardada.TenantId);
        Assert.Null(guardada.SubscriptionPaymentId);
        Assert.Equal(productoId, guardada.ProductId);
        Assert.Equal("cli-0001", guardada.ExternalCustomerRef);
        Assert.Equal("DEP-77123", guardada.PaymentReference);

        // El nombre del servicio queda copiado, no leído por relación.
        Assert.NotNull(guardada.ProductName);
    }

    [RequiereBaseLocal]
    public async Task El_reintento_devuelve_el_mismo_documento()
    {
        var (_, llave) = await ServicioConLlaveAsync();
        var clave = $"idem-{Guid.NewGuid():N}";

        await using var db = Contexto();
        var servicio = Servicio(db);

        var primera = await servicio.IssueAsync(llave, clave, Peticion());

        // El portal reintenta porque no vio la respuesta. No puede salir otro número.
        var segunda = await servicio.IssueAsync(llave, clave, Peticion("Otro nombre"));

        Assert.True(segunda.AlreadyIssued);
        Assert.Equal(primera.Id, segunda.Id);
        Assert.Equal(primera.Number, segunda.Number);

        var cuantas = await db.SubscriptionInvoices.AsNoTracking()
            .CountAsync(f => f.IdempotencyKey == clave);

        Assert.Equal(1, cuantas);
    }

    [RequiereBaseLocal]
    public async Task Dos_reintentos_a_la_vez_tampoco_duplican()
    {
        var (_, llave) = await ServicioConLlaveAsync();
        var clave = $"idem-{Guid.NewGuid():N}";

        // Cada uno con su conexión, como dos peticiones de verdad.
        var tareas = Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = Contexto();
            return await Servicio(db).IssueAsync(llave, clave, Peticion());
        });

        var salidas = await Task.WhenAll(tareas);

        Assert.Single(salidas.Select(s => s.Id).Distinct());

        await using var db = Contexto();
        Assert.Equal(1, await db.SubscriptionInvoices.AsNoTracking()
            .CountAsync(f => f.IdempotencyKey == clave));
    }

    [RequiereBaseLocal]
    public async Task Sin_conceptos_no_hay_documento()
    {
        var (_, llave) = await ServicioConLlaveAsync();

        await using var db = Contexto();

        await Assert.ThrowsAnyAsync<AppException>(
            () => Servicio(db).IssueAsync(
                llave,
                $"idem-{Guid.NewGuid():N}",
                new IssueInvoiceRequest(new InvoiceCustomerDto("Cliente"), [], Fiscal: false)));
    }

    [RequiereBaseLocal]
    public async Task La_llave_vieja_deja_de_valer_al_rotarla()
    {
        var (productoId, vieja) = await ServicioConLlaveAsync();

        await using var db = Contexto();
        var servicio = Servicio(db);

        var nueva = await servicio.RotateApiKeyAsync(productoId);

        Assert.NotEqual(vieja, nueva.ApiKey);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => servicio.IssueAsync(vieja, $"idem-{Guid.NewGuid():N}", Peticion()));

        var conLaNueva = await servicio.IssueAsync(
            nueva.ApiKey, $"idem-{Guid.NewGuid():N}", Peticion());

        Assert.False(conLaNueva.AlreadyIssued);
    }

    [RequiereBaseLocal]
    public async Task El_servicio_apagado_no_emite()
    {
        var (productoId, llave) = await ServicioConLlaveAsync();

        await using var db = Contexto();
        var servicio = Servicio(db);

        var producto = await db.PlatformProducts.AsNoTracking().FirstAsync(p => p.Id == productoId);

        // Apagarlo es la forma de cortarle la emisión sin borrarle el historial.
        await servicio.SaveProductAsync(
            productoId, new SavePlatformProductRequest(producto.Name, IsActive: false));

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => servicio.IssueAsync(llave, $"idem-{Guid.NewGuid():N}", Peticion()));
    }

    [RequiereBaseLocal]
    public async Task El_documento_de_otro_servicio_no_existe_para_este()
    {
        var (_, unaLlave) = await ServicioConLlaveAsync();
        var (_, otraLlave) = await ServicioConLlaveAsync();

        await using var db = Contexto();
        var servicio = Servicio(db);

        var emitida = await servicio.IssueAsync(unaLlave, $"idem-{Guid.NewGuid():N}", Peticion());

        // 404 y no 403: al otro portal no se le confirma ni que ese documento existe.
        await Assert.ThrowsAsync<NotFoundException>(
            () => servicio.InvoicePdfForProductAsync(otraLlave, emitida.Id));
    }
}

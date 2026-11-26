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

    /// <summary>La misma, pero con CAI: es la única que entra al libro de ventas.</summary>
    private static IssueInvoiceRequest ConCai(string cliente = "Cliente de prueba") =>
        Peticion(cliente) with { Fiscal = true };

    /// <summary>
    /// Un rango autorizado vivo, para las pruebas que necesitan emitir con CAI. Se registra
    /// uno nuevo cada vez: registrar un rango da de baja el anterior, así que las pruebas no
    /// se pisan entre ellas.
    /// </summary>
    private static async Task ConRangoVigenteAsync()
    {
        await using var db = Contexto();

        await Servicio(db).SaveFiscalRangeAsync(new SavePlatformFiscalRangeRequest(
            Cai: $"PRUEBA-{Guid.NewGuid():N}",
            EstablishmentCode: "000",
            PointOfSaleCode: "001",
            DocumentType: "01",
            RangeStart: 1,
            RangeEnd: 1_000_000,
            IssueDeadline: DateTimeOffset.UtcNow.AddYears(1)));
    }

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

    /// <summary>
    /// El libro de ventas es la cuenta de los números autorizados, que es lo que pide el SAR.
    /// El comprobante lleva numeración propia y no ampara nada: meterlo infla el libro y
    /// confunde a quien lo revisa. Se mira en Ingresos, que es el control propio.
    /// </summary>
    [RequiereBaseLocal]
    public async Task El_comprobante_no_entra_al_libro_de_ventas()
    {
        var (productoId, llave) = await ServicioConLlaveAsync();

        await using var db = Contexto();
        var servicio = Servicio(db);

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        var comprobante = await servicio.IssueAsync(llave, $"idem-{Guid.NewGuid():N}", Peticion());
        Assert.False(comprobante.IsFiscal);

        var libro = await servicio.SalesBookAsync(hoy.Year, hoy.Month, productoId);

        Assert.Equal(0, libro.Count);
        Assert.Equal(0m, libro.Total);

        // Pero la venta existe y se cuenta: el control propio la tiene.
        var ingresos = await servicio.IncomeAsync(hoy.Year);
        var suyas = ingresos.Rows.Where(r => r.ProductId == productoId).ToList();

        Assert.Single(suyas);
        Assert.False(suyas[0].Fiscal);
        Assert.Equal(comprobante.Total - comprobante.TaxTotal, suyas[0].Net);
    }

    /// <summary>
    /// El ISV solo entra en la factura con CAI. El comprobante no desglosa impuesto: se cobra
    /// el precio pelado, y quien quiere la factura paga el 15% encima.
    /// </summary>
    [RequiereBaseLocal]
    public async Task El_comprobante_no_lleva_isv_y_la_factura_si()
    {
        var (_, llave) = await ServicioConLlaveAsync();
        await ConRangoVigenteAsync();

        await using var db = Contexto();
        var servicio = Servicio(db);

        var comprobante = await servicio.IssueAsync(llave, $"idem-{Guid.NewGuid():N}", Peticion());

        Assert.Equal(0m, comprobante.TaxRate);
        Assert.Equal(0m, comprobante.TaxTotal);
        Assert.Equal(comprobante.Subtotal, comprobante.Total);

        var factura = await servicio.IssueAsync(llave, $"idem-{Guid.NewGuid():N}", ConCai());

        Assert.True(factura.IsFiscal);
        Assert.True(factura.TaxTotal > 0m);

        // El mismo precio de lista en los dos: lo que cambia es lo que se cobra encima.
        Assert.Equal(comprobante.Subtotal, factura.Subtotal);

        // Cómo se cobra el ISV —encima o adentro— es un ajuste del emisor, así que se mira y
        // no se escribe aquí. Cobrándolo encima, que es como cobramos, la factura sale más
        // cara que el comprobante por el mismo servicio.
        var ajustes = await db.PlatformSettings.AsNoTracking().FirstAsync();

        if (!ajustes.PricesIncludeTax)
        {
            Assert.Equal(factura.Subtotal + factura.TaxTotal, factura.Total);
            Assert.True(factura.Total > comprobante.Total);
        }
    }

    /// <summary>
    /// Lo anulado no es ingreso: el número existió, la venta no. En el libro sigue saliendo
    /// —tachado y en cero—, pero acá no suma.
    /// </summary>
    [RequiereBaseLocal]
    public async Task Lo_anulado_no_cuenta_como_ingreso()
    {
        var (productoId, llave) = await ServicioConLlaveAsync();

        await using var db = Contexto();
        var servicio = Servicio(db);

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        var buena = await servicio.IssueAsync(llave, $"idem-{Guid.NewGuid():N}", Peticion());
        var mala = await servicio.IssueAsync(llave, $"idem-{Guid.NewGuid():N}", Peticion());

        await servicio.VoidInvoiceAsync(mala.Id, new VoidInvoiceRequest("Se cobró de más"));

        var ingresos = await servicio.IncomeAsync(hoy.Year);
        var suyas = ingresos.Rows.Where(r => r.ProductId == productoId).ToList();

        Assert.Single(suyas);
        Assert.Equal(1, suyas[0].Count);
        Assert.Equal(buena.Total - buena.TaxTotal, suyas[0].Net);
    }

    [RequiereBaseLocal]
    public async Task El_libro_suma_lo_emitido_y_deja_fuera_lo_anulado()
    {
        var (productoId, llave) = await ServicioConLlaveAsync();

        await using var db = Contexto();
        var servicio = Servicio(db);

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        await ConRangoVigenteAsync();

        var una = await servicio.IssueAsync(llave, $"idem-{Guid.NewGuid():N}", ConCai());
        var otra = await servicio.IssueAsync(llave, $"idem-{Guid.NewGuid():N}", ConCai());

        await servicio.VoidInvoiceAsync(otra.Id, new VoidInvoiceRequest("Se cobró de más"));

        var libro = await servicio.SalesBookAsync(hoy.Year, hoy.Month, productoId);

        // Las dos salen en el libro: el número existió aunque el dinero no.
        Assert.Equal(2, libro.Count);
        Assert.Equal(1, libro.VoidedCount);

        // Pero solo una suma, y suma exactamente lo que dice ese documento. Se compara contra
        // el emitido y no contra un número escrito aquí: cómo cobramos el ISV —adentro o
        // aparte— es un ajuste del emisor, y el libro tiene que cuadrar con los dos.
        Assert.Equal(una.Total, libro.Total);
        Assert.Equal(una.TaxTotal, libro.Tax);
        Assert.Equal(una.Total, libro.Taxable + libro.Exempt + libro.Tax);

        if (una.TaxRate > 0)
        {
            Assert.Equal(una.Total - una.TaxTotal, libro.Taxable);
            Assert.Equal(0m, libro.Exempt);
        }
        else
        {
            Assert.Equal(una.Total, libro.Exempt);
            Assert.Equal(0m, libro.Taxable);
        }

        Assert.Contains(libro.Rows, r => r.Number == una.Number && !r.IsVoided);
        Assert.Contains(libro.Rows, r => r.Number == otra.Number && r.IsVoided);
    }

    [RequiereBaseLocal]
    public async Task El_libro_sale_en_hoja_de_calculo()
    {
        var (productoId, llave) = await ServicioConLlaveAsync();

        await using var db = Contexto();
        var servicio = Servicio(db);

        await servicio.IssueAsync(llave, $"idem-{Guid.NewGuid():N}", Peticion());

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var hoja = await servicio.SalesBookSheetAsync(hoy.Year, hoy.Month, productoId);

        // Un .xlsx es un zip: los dos primeros bytes lo dicen.
        Assert.True(hoja.Length > 1000);
        Assert.Equal((byte)'P', hoja[0]);
        Assert.Equal((byte)'K', hoja[1]);
    }

    [RequiereBaseLocal]
    public async Task Al_mismo_documento_no_se_le_emite_otro()
    {
        var (_, llave) = await ServicioConLlaveAsync();

        await using var db = Contexto();
        var servicio = Servicio(db);

        var una = await servicio.IssueAsync(llave, $"idem-{Guid.NewGuid():N}", Peticion());

        // Anularlo no habilita emitir otro con la misma clave: la clave es del cobro, no del
        // documento, y el número anulado ya se gastó.
        await servicio.VoidInvoiceAsync(una.Id, new VoidInvoiceRequest("Mal emitido"));

        var otra = await servicio.IssueAsync(llave, $"idem-{Guid.NewGuid():N}", Peticion());

        Assert.NotEqual(una.Number, otra.Number);
    }

    [RequiereBaseLocal]
    public async Task Un_mes_que_no_existe_no_tiene_libro()
    {
        await using var db = Contexto();

        await Assert.ThrowsAnyAsync<AppException>(
            () => Servicio(db).SalesBookAsync(2026, 13));
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

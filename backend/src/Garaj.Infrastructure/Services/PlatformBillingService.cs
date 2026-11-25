using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Platform;
using Garaj.Domain.Entities;
using Garaj.Domain.Rules;
using Garaj.Infrastructure.Documents;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// El papel de nuestra cobranza: quiénes somos en el documento, nuestro CAI y las facturas
/// que ya se emitieron.
/// </summary>
/// <remarks>
/// La emisión no está aquí, sino en <see cref="PlatformService"/>: nace con el pago y en su
/// misma transacción. Aquí vive lo que se configura una vez y lo que se consulta después.
/// </remarks>
public class PlatformBillingService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : IPlatformBillingService
{
    public async Task<PlatformSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var settings = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? new PlatformSettings();

        return await MapAsync(settings, ct);
    }

    public async Task<PlatformSettingsDto> SaveSettingsAsync(
        SavePlatformSettingsRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        if (request.DefaultTaxRate is < 0 or > 100)
            throw new AppException("El ISV tiene que ir entre 0 y 100.");

        var settings = await db.PlatformSettings.FirstOrDefaultAsync(ct);

        if (settings is null)
        {
            settings = new PlatformSettings();
            db.PlatformSettings.Add(settings);
        }

        settings.Name = request.Name.Trim();
        settings.LegalName = Trim(request.LegalName);
        settings.TaxId = Trim(request.TaxId);
        settings.Phone = Trim(request.Phone);
        settings.Email = Trim(request.Email);
        settings.Address = Trim(request.Address);
        settings.DefaultTaxRate = request.DefaultTaxRate;
        settings.PricesIncludeTax = request.PricesIncludeTax;

        // El prefijo manda sobre números ya emitidos: cambiarlo a media numeración deja dos
        // series conviviendo, así que se limpia y se deja en mayúsculas.
        var prefijo = Trim(request.InvoicePrefix)?.ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(prefijo)) settings.InvoicePrefix = prefijo;

        await db.SaveChangesAsync(ct);

        return await MapAsync(settings, ct);
    }

    public async Task<PlatformSettingsDto> SaveFiscalRangeAsync(
        SavePlatformFiscalRangeRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        if (request.RangeEnd < request.RangeStart)
            throw new AppException("El final del rango no puede ser menor que el inicio.");

        if (request.IssueDeadline < clock.UtcNow)
            throw new AppException("La fecha límite de emisión ya pasó: revise el CAI.");

        // Uno vivo a la vez. Con dos rangos activos, el siguiente número depende de cuál se
        // leyó primero, y así es como se salta un correlativo sin enterarse.
        var anteriores = await db.PlatformFiscalRanges.Where(r => r.IsActive).ToListAsync(ct);
        foreach (var anterior in anteriores) anterior.IsActive = false;

        db.PlatformFiscalRanges.Add(new PlatformFiscalRange
        {
            Cai = request.Cai.Trim(),
            EstablishmentCode = request.EstablishmentCode.Trim(),
            PointOfSaleCode = request.PointOfSaleCode.Trim(),
            DocumentType = request.DocumentType.Trim(),
            RangeStart = request.RangeStart,
            RangeEnd = request.RangeEnd,
            NextNumber = request.RangeStart,
            IssueDeadline = request.IssueDeadline,
            IsActive = true
        });

        await db.SaveChangesAsync(ct);

        return await GetSettingsAsync(ct);
    }

    public async Task<SubscriptionInvoiceDto> GetInvoiceAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        return Map(await FindAsync(id, ct));
    }

    public async Task<byte[]> InvoicePdfAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var factura = await FindAsync(id, ct);
        var settings = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? new PlatformSettings();

        return SubscriptionInvoicePdf.Render(Map(factura), await MapAsync(settings, ct));
    }

    public async Task<SubscriptionInvoiceDto> VoidInvoiceAsync(
        Guid id, VoidInvoiceRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new AppException("Diga por qué se anula: es lo que explica el número que falta.");

        var factura = await db.SubscriptionInvoices
            .Include(f => f.Lines)
            .FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new NotFoundException("El documento no existe.");

        if (factura.IsVoided) throw new ConflictException("Ese documento ya está anulado.");

        factura.IsVoided = true;
        factura.VoidReason = request.Reason.Trim();
        factura.VoidedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        return Map(factura);
    }

    // ---------- Buscar un documento suelto ----------

    public async Task<PagedResult<InvoiceListItemDto>> InvoicesAsync(
        InvoiceQuery query, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var q = db.SubscriptionInvoices.AsNoTracking().AsQueryable();

        if (query.ProductId is { } producto) q = q.Where(f => f.ProductId == producto);
        if (query.From is { } desde) q = q.Where(f => f.IssuedOn >= desde);
        if (query.To is { } hasta) q = q.Where(f => f.IssuedOn <= hasta);
        if (query.OnlyVoided) q = q.Where(f => f.IsVoided);

        // Se busca como se busca una factura en papel: por su número, por el nombre o por el
        // RTN. El tope de largo es del validador; aquí se recorta para que un texto absurdo no
        // se convierta en un LIKE absurdo.
        if (query.Search?.Trim() is { Length: > 0 } texto)
        {
            var buscado = texto[..Math.Min(texto.Length, 100)];

            q = q.Where(f =>
                EF.Functions.ILike(f.Number, $"%{buscado}%")
                || EF.Functions.ILike(f.CustomerName, $"%{buscado}%")
                || (f.CustomerTaxId != null && EF.Functions.ILike(f.CustomerTaxId, $"%{buscado}%")));
        }

        var total = await q.CountAsync(ct);

        // Lo último emitido arriba: lo que se busca casi siempre es reciente.
        var items = await q
            .OrderByDescending(f => f.IssuedOn)
            .ThenByDescending(f => f.Number)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(f => new InvoiceListItemDto(
                f.Id, f.Number, f.IssuedOn, f.ProductName, f.CustomerName, f.CustomerTaxId,
                f.Total, f.Currency, f.FiscalCai != null, f.IsVoided))
            .ToListAsync(ct);

        return new PagedResult<InvoiceListItemDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<SubscriptionInvoiceDto> IssueFromConsoleAsync(
        IssueFromConsoleRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var producto = await db.PlatformProducts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
            ?? throw new NotFoundException("El servicio no existe.");

        var lineas = (request.Lines ?? [])
            .Where(l => !string.IsNullOrWhiteSpace(l.Description))
            .ToList();

        if (lineas.Count == 0)
            throw new AppException("El documento necesita al menos un concepto.");

        if (lineas.Any(l => l.Quantity <= 0 || l.UnitPrice < 0))
            throw new AppException("Cantidad y precio tienen que ser válidos en cada concepto.");

        var cliente = request.Customer?.Name?.Trim();

        if (string.IsNullOrWhiteSpace(cliente))
            throw new AppException("Diga a nombre de quién va el documento.");

        var settings = await db.PlatformSettings.FirstOrDefaultAsync(ct);

        if (settings is null)
        {
            settings = new PlatformSettings();
            db.PlatformSettings.Add(settings);
        }

        var tasa = settings.DefaultTaxRate;
        var suma = lineas.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
        var (total, impuesto) = Isv.Totales(suma, tasa, settings.PricesIncludeTax);

        var factura = new SubscriptionInvoice
        {
            TenantId = null,
            SubscriptionPaymentId = null,
            ProductId = producto.Id,
            ProductName = producto.Name,
            ExternalCustomerRef = Recortar(request.ExternalCustomerRef, 100),
            PaymentReference = Recortar(request.PaymentReference, 80),
            IssuedOn = request.IssuedOn ?? clock.Today(),
            CustomerName = cliente,
            CustomerLegalName = Recortar(request.Customer?.LegalName, 200),
            CustomerTaxId = Recortar(request.Customer?.TaxId, 20),
            Subtotal = suma,
            TaxRate = tasa,
            TaxTotal = impuesto,
            Total = total,
            PricesIncludedTax = settings.PricesIncludeTax,
            Currency = settings.Currency,
            Note = Recortar(request.Note, 300),
            IssuedByUserId = tenantContext.UserId
        };

        foreach (var linea in lineas)
        {
            factura.Lines.Add(new SubscriptionInvoiceLine
            {
                Description = linea.Description.Trim(),
                Quantity = linea.Quantity,
                UnitPrice = linea.UnitPrice,
                Total = Math.Round(linea.Quantity * linea.UnitPrice, 2, MidpointRounding.AwayFromZero)
            });
        }

        factura.Number = request.Fiscal
            ? await NumeracionDePlataforma.AutorizadoAsync(db, factura, clock.UtcNow, ct)
            : await NumeracionDePlataforma.PropioAsync(db, settings, ct);

        db.SubscriptionInvoices.Add(factura);
        await db.SaveChangesAsync(ct);

        return Map(factura);
    }

    /// <summary>
    /// Lo que entró por los tres servicios en un año, mes a mes y por servicio.
    /// </summary>
    /// <remarks>
    /// No es el libro de ventas y no se le parece: el libro es de los números autorizados y
    /// lo pide el SAR; esto es el control propio, así que entran las facturas y también los
    /// comprobantes, que son venta igual aunque no amparen nada.
    ///
    /// Lo que se suma es el <b>neto</b>, no el total: el ISV se cobra y se entrega, así que
    /// contarlo como ingreso es contar plata que no es nuestra. El impuesto va aparte, que es
    /// justo lo que hay que tener a mano para declararlo.
    ///
    /// Los anulados no cuentan: el número existió, la venta no.
    /// </remarks>
    public async Task<IncomeDto> IncomeAsync(int year, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        if (year is < 2020 or > 2100) throw new AppException("El año no existe.");

        var desde = new DateOnly(year, 1, 1);
        var hasta = new DateOnly(year, 12, 31);

        var filas = await db.SubscriptionInvoices
            .AsNoTracking()
            .Where(f => !f.IsVoided && f.IssuedOn >= desde && f.IssuedOn <= hasta)
            .GroupBy(f => new { f.IssuedOn.Month, f.ProductId, f.ProductName, Fiscal = f.FiscalCai != null })
            .Select(g => new IncomeRowDto(
                g.Key.Month,
                g.Key.ProductId,
                g.Key.ProductName ?? "Sin servicio",
                g.Key.Fiscal,
                g.Sum(f => f.Total - f.TaxTotal),
                g.Sum(f => f.TaxTotal),
                g.Sum(f => f.Total),
                g.Count()))
            .ToListAsync(ct);

        return new IncomeDto(year, filas);
    }

    // ---------- El libro de ventas ----------

    /// <remarks>
    /// Se ordena por número y no por fecha: el libro es la cuenta de los números autorizados,
    /// y es así como se coteja con el rango del SAR.
    ///
    /// «Gravado» se saca como total menos impuesto y no del subtotal guardado, porque el
    /// subtotal significa una cosa u otra según cómo se cobrara —con el ISV adentro o aparte—
    /// y eso se congela por documento. Restar es la única forma que vale para los dos.
    /// </remarks>
    public async Task<SalesBookDto> SalesBookAsync(
        int year, int month, Guid? productId = null, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        if (month is < 1 or > 12) throw new AppException("El mes no existe.");
        if (year is < 2020 or > 2100) throw new AppException("El año no existe.");

        var desde = new DateOnly(year, month, 1);
        var hasta = desde.AddMonths(1).AddDays(-1);

        var facturas = await db.SubscriptionInvoices
            .AsNoTracking()
            .Where(f => f.IssuedOn >= desde && f.IssuedOn <= hasta)
            // Solo lo amparado por el CAI: el libro de ventas es la cuenta de los números
            // autorizados, que es lo que pide el SAR. El comprobante lleva numeración propia
            // y no ampara nada, así que mezclarlo infla el libro y confunde al contador. Los
            // comprobantes se miran en Documentos emitidos y en Ingresos.
            .Where(f => f.FiscalCai != null)
            .Where(f => productId == null || f.ProductId == productId)
            .OrderBy(f => f.Number)
            .ToListAsync(ct);

        var filas = facturas
            .Select(f =>
            {
                var gravado = f.TaxRate > 0 ? f.Total - f.TaxTotal : 0m;
                var exento = f.TaxRate > 0 ? 0m : f.Total;

                return new SalesBookRowDto(
                    f.IssuedOn, f.Number, f.FiscalCai, f.ProductName, f.CustomerName,
                    f.CustomerTaxId, gravado, exento, f.TaxTotal, f.Total,
                    f.IsVoided, f.VoidReason);
            })
            .ToList();

        // Los anulados no suman: el número existió, el dinero no.
        var buenas = filas.Where(r => !r.IsVoided).ToList();

        var settings = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(ct);

        return new SalesBookDto(
            desde,
            hasta,
            filas,
            await FaltantesAsync(facturas, ct),
            buenas.Sum(r => r.Taxable),
            buenas.Sum(r => r.Exempt),
            buenas.Sum(r => r.Tax),
            buenas.Sum(r => r.Total),
            filas.Count,
            filas.Count(r => r.IsVoided),
            settings?.Currency ?? "HNL");
    }

    public async Task<byte[]> SalesBookSheetAsync(
        int year, int month, Guid? productId = null, CancellationToken ct = default)
    {
        var libro = await SalesBookAsync(year, month, productId, ct);

        var settings = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? new PlatformSettings();

        return SalesBookSheet.Render(libro, await MapAsync(settings, ct));
    }

    /// <summary>
    /// Los números autorizados que se tomaron y no quedaron en ningún documento, entre el
    /// primero y el último del periodo.
    /// </summary>
    /// <remarks>
    /// Pasa cuando se toma el número y después falla el guardado: es a propósito que el número
    /// no se devuelva —dos documentos con el mismo número no se pueden explicar, un hueco sí—,
    /// pero el hueco hay que poder verlo y nombrarlo.
    ///
    /// Solo se miran los del rango autorizado: el correlativo propio del comprobante no es del
    /// SAR y su hueco no le interesa a nadie.
    /// </remarks>
    private async Task<IReadOnlyList<string>> FaltantesAsync(
        List<SubscriptionInvoice> facturas, CancellationToken ct)
    {
        var fiscales = facturas.Where(f => f.PlatformFiscalRangeId is not null).ToList();

        if (fiscales.Count == 0) return [];

        var rangoId = fiscales[0].PlatformFiscalRangeId!.Value;

        // Con documentos de dos rangos en el mismo mes —el CAI se acabó a mitad— no se cuentan
        // huecos: la cuenta se hace rango por rango, y mezclarlos inventaría los que no son.
        if (fiscales.Any(f => f.PlatformFiscalRangeId != rangoId)) return [];

        var rango = await db.PlatformFiscalRanges.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == rangoId, ct);

        if (rango is null) return [];

        var emitidos = new HashSet<int>();

        foreach (var factura in fiscales)
            if (Correlativo(factura.Number) is { } numero)
                emitidos.Add(numero);

        if (emitidos.Count == 0) return [];

        var faltantes = new List<string>();

        for (var numero = emitidos.Min(); numero <= emitidos.Max(); numero++)
            if (!emitidos.Contains(numero))
                faltantes.Add(rango.Format(numero));

        return faltantes;
    }

    /// <summary>El correlativo de «000-001-01-00000042». Null si el número no tiene esa forma.</summary>
    private static int? Correlativo(string numero)
    {
        var ultimo = numero.Split('-')[^1];

        return int.TryParse(ultimo, out var valor) ? valor : null;
    }

    // ---------- Los servicios que cobramos ----------

    public async Task<IReadOnlyList<PlatformProductDto>> ProductsAsync(CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var productos = await db.PlatformProducts.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);

        var emitidos = await db.SubscriptionInvoices
            .AsNoTracking()
            .Where(f => f.ProductId != null)
            .GroupBy(f => f.ProductId!.Value)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.Count, ct);

        return productos
            .Select(p => new PlatformProductDto(
                p.Id, p.Name, p.IsActive, p.ApiKeyHint, p.ApiKeyIssuedOn,
                emitidos.GetValueOrDefault(p.Id),
                p.Id == PlatformProducts.GarajApp))
            .ToList();
    }

    public async Task<PlatformProductDto> SaveProductAsync(
        Guid? id, SavePlatformProductRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var nombre = request.Name.Trim();

        if (nombre.Length == 0) throw new AppException("El servicio necesita un nombre.");

        var repetido = await db.PlatformProducts
            .AnyAsync(p => p.Name == nombre && (id == null || p.Id != id), ct);

        if (repetido)
            throw new ConflictException($"Ya hay un servicio que se llama «{nombre}».");

        PlatformProduct producto;

        if (id is { } existente)
        {
            producto = await db.PlatformProducts.FirstOrDefaultAsync(p => p.Id == existente, ct)
                ?? throw new NotFoundException("El servicio no existe.");
        }
        else
        {
            producto = new PlatformProduct();
            db.PlatformProducts.Add(producto);
        }

        producto.Name = nombre;
        producto.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);

        return (await ProductsAsync(ct)).First(p => p.Id == producto.Id);
    }

    public async Task<NewApiKeyDto> RotateApiKeyAsync(Guid productId, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        // Nosotros no pedimos número por la API: lo tomamos aquí dentro, con el pago del
        // taller. Una llave para GarajApp sería una credencial repartida que nadie usa.
        if (productId == PlatformProducts.GarajApp)
            throw new AppException(
                "GarajApp factura desde esta misma consola: no necesita llave.");

        var producto = await db.PlatformProducts.FirstOrDefaultAsync(p => p.Id == productId, ct)
            ?? throw new NotFoundException("El servicio no existe.");

        var (llave, hash, pista) = LlavesDeProducto.Nueva();

        // La anterior queda sin valor en el mismo acto: dos llaves vivas es una que nadie
        // recuerda haber repartido.
        producto.ApiKeyHash = hash;
        producto.ApiKeyHint = pista;
        producto.ApiKeyIssuedOn = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        return new NewApiKeyDto(producto.Id, producto.Name, llave);
    }

    // ---------- Emitir desde el portal de otro servicio ----------

    /// <remarks>
    /// Aquí no hay sesión ni taller: la llave identifica al servicio. Por eso lo primero es
    /// resolverla, y todo lo que sigue cuelga del producto que salió de ella —nunca de algo que
    /// venga en el cuerpo—.
    /// </remarks>
    public async Task<IssuedInvoiceDto> IssueAsync(
        string apiKey, string idempotencyKey, IssueInvoiceRequest request,
        CancellationToken ct = default)
    {
        var clave = idempotencyKey.Trim();

        if (clave.Length is 0 or > 100)
            throw new AppException("Falta la cabecera Idempotency-Key, o es demasiado larga.");

        var producto = await ProductoDeLaLlaveAsync(apiKey, ct);

        // El reintento devuelve lo que ya salió. Es lo que impide que una respuesta perdida
        // queme un segundo número del SAR por el mismo cobro.
        var yaEmitida = await db.SubscriptionInvoices
            .AsNoTracking()
            .Include(f => f.Lines)
            .FirstOrDefaultAsync(f => f.ProductId == producto.Id && f.IdempotencyKey == clave, ct);

        if (yaEmitida is not null) return Emitida(yaEmitida, true);

        var lineas = (request.Lines ?? [])
            .Where(l => !string.IsNullOrWhiteSpace(l.Description))
            .ToList();

        if (lineas.Count == 0)
            throw new AppException("El documento necesita al menos un concepto.");

        if (lineas.Count > 50)
            throw new AppException("Son demasiados conceptos para un documento.");

        if (lineas.Any(l => l.Quantity <= 0 || l.UnitPrice < 0))
            throw new AppException("Cantidad y precio tienen que ser válidos en cada concepto.");

        var cliente = request.Customer?.Name?.Trim();

        if (string.IsNullOrWhiteSpace(cliente))
            throw new AppException("Diga a nombre de quién va el documento.");

        var settings = await db.PlatformSettings.FirstOrDefaultAsync(ct);

        if (settings is null)
        {
            settings = new PlatformSettings();
            db.PlatformSettings.Add(settings);
        }

        var tasa = settings.DefaultTaxRate;
        var suma = lineas.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
        var (total, impuesto) = Isv.Totales(suma, tasa, settings.PricesIncludeTax);

        var factura = new SubscriptionInvoice
        {
            // Sin taller y sin pago nuestro: el cobro lo hizo su portal.
            TenantId = null,
            SubscriptionPaymentId = null,
            ProductId = producto.Id,
            ProductName = producto.Name,
            ExternalCustomerRef = Recortar(request.ExternalCustomerRef, 100),
            PaymentReference = Recortar(request.PaymentReference, 80),
            IdempotencyKey = clave,
            IssuedOn = request.PaidOn ?? clock.Today(),
            CustomerName = cliente,
            CustomerLegalName = Recortar(request.Customer?.LegalName, 200),
            CustomerTaxId = Recortar(request.Customer?.TaxId, 20),
            Subtotal = suma,
            TaxRate = tasa,
            TaxTotal = impuesto,
            Total = total,
            PricesIncludedTax = settings.PricesIncludeTax,
            Currency = settings.Currency,
            Note = Recortar(request.Note, 300)
        };

        foreach (var linea in lineas)
        {
            factura.Lines.Add(new SubscriptionInvoiceLine
            {
                Description = linea.Description.Trim(),
                Quantity = linea.Quantity,
                UnitPrice = linea.UnitPrice,
                Total = Math.Round(linea.Quantity * linea.UnitPrice, 2, MidpointRounding.AwayFromZero)
            });
        }

        factura.Number = request.Fiscal
            ? await NumeracionDePlataforma.AutorizadoAsync(db, factura, clock.UtcNow, ct)
            : await NumeracionDePlataforma.PropioAsync(db, settings, ct);

        db.SubscriptionInvoices.Add(factura);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dos reintentos a la vez: el índice único cazó al segundo. Lo que importa es que
            // el portal reciba el documento, no cuál de los dos llegó primero.
            db.ChangeTracker.Clear();

            var carrera = await YaEstabaAsync(producto.Id, clave, ct);

            if (carrera is null) throw;

            return Emitida(carrera, true);
        }

        return Emitida(factura, false);
    }

    public async Task<byte[]> InvoicePdfForProductAsync(
        string apiKey, Guid invoiceId, CancellationToken ct = default)
    {
        var producto = await ProductoDeLaLlaveAsync(apiKey, ct);

        var factura = await db.SubscriptionInvoices
            .AsNoTracking()
            .Include(f => f.Lines)
            .FirstOrDefaultAsync(f => f.Id == invoiceId && f.ProductId == producto.Id, ct)
            ?? throw new NotFoundException("El documento no existe.");

        var settings = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? new PlatformSettings();

        return SubscriptionInvoicePdf.Render(Map(factura), await MapAsync(settings, ct));
    }

    /// <summary>
    /// El servicio al que pertenece la llave. La comparación es por hash, y el servicio tiene
    /// que estar activo: apagarlo es la forma de cortarle la emisión sin borrar su historial.
    /// </summary>
    private async Task<PlatformProduct> ProductoDeLaLlaveAsync(string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new UnauthorizedException("Falta la llave del servicio.");

        var hash = LlavesDeProducto.Hash(apiKey);

        return await db.PlatformProducts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ApiKeyHash == hash && p.IsActive, ct)
            ?? throw new UnauthorizedException("La llave no vale.");
    }

    private async Task<SubscriptionInvoice?> YaEstabaAsync(
        Guid productId, string clave, CancellationToken ct) =>
        await db.SubscriptionInvoices
            .AsNoTracking()
            .Include(f => f.Lines)
            .FirstOrDefaultAsync(f => f.ProductId == productId && f.IdempotencyKey == clave, ct);

    private static string? Recortar(string? texto, int largo) =>
        texto?.Trim() is { Length: > 0 } limpio ? limpio[..Math.Min(limpio.Length, largo)] : null;

    private static IssuedInvoiceDto Emitida(SubscriptionInvoice f, bool reintento) =>
        new(f.Id, f.Number, f.IssuedOn, f.IsFiscal, f.FiscalCai, f.FiscalRangeText,
            f.FiscalIssueDeadline, f.Subtotal, f.TaxRate, f.TaxTotal, f.Total, f.Currency,
            $"/api/billing/invoices/{f.Id}.pdf", reintento);

    private async Task<SubscriptionInvoice> FindAsync(Guid id, CancellationToken ct) =>
        await db.SubscriptionInvoices
            .AsNoTracking()
            .Include(f => f.Lines)
            .FirstOrDefaultAsync(f => f.Id == id, ct)
        ?? throw new NotFoundException("El documento no existe.");

    private async Task<PlatformSettingsDto> MapAsync(PlatformSettings settings, CancellationToken ct)
    {
        var rango = await db.PlatformFiscalRanges
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return new PlatformSettingsDto(
            settings.Name, settings.LegalName, settings.TaxId, settings.Phone, settings.Email,
            settings.Address, settings.DefaultTaxRate, settings.PricesIncludeTax,
            settings.InvoicePrefix, settings.NextInvoiceNumber, settings.Currency,
            rango is null ? null : Map(rango, clock.UtcNow));
    }

    private static PlatformFiscalRangeDto Map(PlatformFiscalRange r, DateTimeOffset ahora) =>
        new(r.Id, r.Cai, r.EstablishmentCode, r.PointOfSaleCode, r.DocumentType,
            r.RangeStart, r.RangeEnd, r.NextNumber, r.Format(r.NextNumber), r.RangeText,
            r.IssueDeadline, r.IsActive, r.Remaining,
            r.IssueDeadline < ahora, r.NextNumber > r.RangeEnd);

    private static SubscriptionInvoiceDto Map(SubscriptionInvoice f) =>
        new(f.Id, f.TenantId, f.Number, f.IssuedOn, f.CustomerName, f.CustomerLegalName,
            f.CustomerTaxId, f.Subtotal, f.TaxRate, f.TaxTotal, f.Total, f.PricesIncludedTax,
            f.Currency, f.Note, f.IsFiscal, f.FiscalCai, f.FiscalRangeText,
            f.FiscalIssueDeadline, f.IsVoided, f.VoidReason,
            f.Lines.Select(l => new SubscriptionInvoiceLineDto(
                l.Description, l.Quantity, l.UnitPrice, l.Total)).ToList(),
            f.ProductName, f.ExternalCustomerRef, f.PaymentReference);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

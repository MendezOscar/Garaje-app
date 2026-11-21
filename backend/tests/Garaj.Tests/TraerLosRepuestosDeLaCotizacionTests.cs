using Garaj.Application.Common;
using Garaj.Application.Inventory;
using Garaj.Application.Notifications;
using Garaj.Application.WorkOrders;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Persistence.Interceptors;
using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Tests;

/// <summary>
/// Traer a la orden los repuestos que ya se le cotizaron al cliente.
/// </summary>
/// <remarks>
/// Lo que se prueba es lo que puede salir caro: que traerlos no descuente la bodega —lo
/// cotizado es una propuesta, puede que ni haya existencia—, que traerlos dos veces no
/// duplique la línea —sería cobrárselo dos veces al cliente— y que una cotización de otra
/// orden no se pueda traer.
/// </remarks>
public class TraerLosRepuestosDeLaCotizacionTests
{
    private const string CadenaLocal =
        "Host=localhost;Port=5434;Database=garaj;Username=garaj;Password=garaj-dev-secret";

    private static GarajDbContext Contexto(
        Guid taller, Guid usuario, string rol = "Owner", Guid? sucursal = null)
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? CadenaLocal;

        var contexto = new TenantContext();
        contexto.Initialize(taller, usuario, rol, sucursal is { } s ? [s] : [], null);

        var options = new DbContextOptionsBuilder<GarajDbContext>()
            .UseNpgsql(cadena)
            .AddInterceptors(new AuditableEntityInterceptor(contexto, new DateTimeProvider()))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GarajDbContext(options, contexto);
    }

    private sealed record Escenario(
        Guid Taller, Guid Usuario, Guid Sucursal, Guid Orden, Guid Repuesto, Guid Cotizacion);

    /// <summary>
    /// Un taller con una orden abierta, un repuesto con tres en bodega y una cotización
    /// aprobada de esa orden: dos juegos de pastillas y una mano de obra.
    /// </summary>
    private static async Task<Escenario> EscenarioAsync(decimal descuento = 0m)
    {
        var taller = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        var sucursal = Guid.NewGuid();
        var orden = Guid.NewGuid();
        var repuesto = Guid.NewGuid();
        var cotizacion = Guid.NewGuid();
        var corto = $"{taller:N}"[..8].ToUpperInvariant();

        await using var db = Contexto(taller, usuario);

        db.Tenants.Add(new Tenant
        {
            Id = taller,
            Name = $"Prueba {corto}",
            Currency = "HNL",
            DefaultPhoneCountryCode = "504",
            WorksWith = WorkshopKind.Vehicles
        });

        db.Branches.Add(new Branch
        {
            Id = sucursal, TenantId = taller, Name = "Matriz", Code = corto[..3], IsActive = true
        });

        db.Parts.Add(new Part
        {
            Id = repuesto, TenantId = taller, Sku = $"PAS-{corto}", Name = "Pastillas delanteras",
            Unit = "jgo", CostPrice = 600m, SalePrice = 1200m, IsActive = true
        });

        db.StockItems.Add(new StockItem
        {
            TenantId = taller, BranchId = sucursal, PartId = repuesto, Quantity = 3m,
            MinQuantity = 1m
        });

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
            Status = WorkOrderStatus.Diagnosing, OpenedAt = DateTimeOffset.UtcNow
        });

        var total = 2m * 1200m - descuento;

        db.Quotes.Add(new Quote
        {
            Id = cotizacion, TenantId = taller, BranchId = sucursal, CustomerId = cliente,
            VehicleId = vehiculo, WorkOrderId = orden, Number = $"COT-{corto}",
            Status = QuoteStatus.Approved, TaxRate = 15m, PricesIncludedTax = true,
            Subtotal = total, DiscountTotal = descuento, Total = total + 900m,
            Lines =
            [
                new QuoteLine
                {
                    TenantId = taller, LineType = LineType.Part, PartId = repuesto,
                    Description = "Pastillas delanteras", Sequence = 1, Quantity = 2m,
                    UnitPrice = 1200m, Discount = descuento, Total = total
                },
                new QuoteLine
                {
                    TenantId = taller, LineType = LineType.Labor,
                    Description = "Cambio de pastillas", Sequence = 2, Quantity = 1m,
                    UnitPrice = 900m, Total = 900m
                }
            ]
        });

        await db.SaveChangesAsync();

        return new Escenario(taller, usuario, sucursal, orden, repuesto, cotizacion);
    }

    /// <summary>Los avisos no son lo que se prueba aquí: se tragan.</summary>
    private sealed class SinAvisos : INotificationPublisher
    {
        public Task NotifyUserAsync(
            Guid tenantId, Guid userId, NotificationDraft draft, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyOwnersAsync(
            Guid tenantId, NotificationDraft draft, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyBranchManagementAsync(
            Guid tenantId, Guid branchId, NotificationDraft draft, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyCustomerAsync(
            Guid tenantId, Guid customerId, NotificationDraft draft, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static WorkOrderService Servicio(GarajDbContext db)
    {
        var reloj = new DateTimeProvider();

        return new WorkOrderService(
            db, db.TenantContext, reloj,
            configuration: null!, tenants: null!, media: null!,
            stock: new StockService(db, db.TenantContext, reloj),
            storage: null!, receptionDocuments: null!, notifications: new SinAvisos());
    }

    private static async Task<decimal> ExistenciaAsync(GarajDbContext db, Guid repuesto) =>
        await db.StockItems.AsNoTracking()
            .Where(s => s.PartId == repuesto)
            .Select(s => s.Quantity)
            .FirstAsync();

    [RequiereBaseLocal]
    public async Task Traerlos_los_deja_por_confirmar_y_no_toca_la_bodega()
    {
        var caso = await EscenarioAsync();

        await using var db = Contexto(caso.Taller, caso.Usuario);

        var traidos = await Servicio(db).ImportQuotePartsAsync(
            caso.Orden, new ImportQuotePartsRequest(caso.Cotizacion));

        // La mano de obra no viene: esa se elige al facturar.
        var linea = Assert.Single(traidos.Added);
        Assert.Equal(caso.Repuesto, linea.PartId);
        Assert.Equal(2m, linea.Quantity);
        Assert.Equal(1200m, linea.UnitPrice);
        Assert.True(linea.PorConfirmar);

        Assert.Equal(3m, await ExistenciaAsync(db, caso.Repuesto));
    }

    [RequiereBaseLocal]
    public async Task El_descuento_de_la_cotizacion_queda_en_el_precio()
    {
        // 2 × 1200 con 400 de descuento son 2000: el cliente aprobó 1000 cada uno.
        var caso = await EscenarioAsync(descuento: 400m);

        await using var db = Contexto(caso.Taller, caso.Usuario);

        var traidos = await Servicio(db).ImportQuotePartsAsync(
            caso.Orden, new ImportQuotePartsRequest(caso.Cotizacion));

        Assert.Equal(1000m, Assert.Single(traidos.Added).UnitPrice);
    }

    [RequiereBaseLocal]
    public async Task Traerlos_dos_veces_no_duplica_la_linea()
    {
        var caso = await EscenarioAsync();

        await using var db = Contexto(caso.Taller, caso.Usuario);
        var servicio = Servicio(db);

        await servicio.ImportQuotePartsAsync(caso.Orden, new ImportQuotePartsRequest(caso.Cotizacion));

        var otra = await servicio.ImportQuotePartsAsync(
            caso.Orden, new ImportQuotePartsRequest(caso.Cotizacion));

        Assert.Empty(otra.Added);
        Assert.Equal(1, otra.Skipped);

        var cuantas = await db.WorkOrderParts.AsNoTracking()
            .CountAsync(p => p.WorkOrderId == caso.Orden);

        Assert.Equal(1, cuantas);
    }

    [RequiereBaseLocal]
    public async Task La_cotizacion_de_otra_orden_no_existe_para_esta()
    {
        var caso = await EscenarioAsync();
        var otroCaso = await EscenarioAsync();

        await using var db = Contexto(caso.Taller, caso.Usuario);

        // La de otro taller ni se ve; la de otra orden del mismo taller, tampoco: 404.
        await Assert.ThrowsAsync<NotFoundException>(
            () => Servicio(db).ImportQuotePartsAsync(
                caso.Orden, new ImportQuotePartsRequest(otroCaso.Cotizacion)));
    }

    // ---------- Corregir el precio de lo que se trajo ----------

    [RequiereBaseLocal]
    public async Task Corregir_el_precio_puede_dejarlo_tambien_en_el_catalogo()
    {
        var caso = await EscenarioAsync();

        await using var db = Contexto(caso.Taller, caso.Usuario);
        var servicio = Servicio(db);

        var traidos = await servicio.ImportQuotePartsAsync(
            caso.Orden, new ImportQuotePartsRequest(caso.Cotizacion));

        var linea = traidos.Added.Single();

        var corregida = await servicio.SetPartPriceAsync(
            caso.Orden, linea.Id, new SetPartPriceRequest(1500m, null, UpdateCatalog: true));

        Assert.Equal(1500m, corregida.UnitPrice);

        var ficha = await db.Parts.AsNoTracking().FirstAsync(p => p.Id == caso.Repuesto);
        Assert.Equal(1500m, ficha.SalePrice);
    }

    [RequiereBaseLocal]
    public async Task Sin_pedirlo_el_catalogo_no_se_toca()
    {
        var caso = await EscenarioAsync();

        await using var db = Contexto(caso.Taller, caso.Usuario);
        var servicio = Servicio(db);

        var traidos = await servicio.ImportQuotePartsAsync(
            caso.Orden, new ImportQuotePartsRequest(caso.Cotizacion));

        await servicio.SetPartPriceAsync(
            caso.Orden, traidos.Added.Single().Id, new SetPartPriceRequest(1500m, null));

        var ficha = await db.Parts.AsNoTracking().FirstAsync(p => p.Id == caso.Repuesto);
        Assert.Equal(1200m, ficha.SalePrice);
    }

    [RequiereBaseLocal]
    public async Task Bajar_la_cantidad_de_una_linea_confirmada_devuelve_a_bodega()
    {
        var caso = await EscenarioAsync();

        await using var db = Contexto(caso.Taller, caso.Usuario);
        var servicio = Servicio(db);

        // Dos juegos salen de bodega: quedan uno.
        var linea = await servicio.AddPartAsync(
            caso.Orden, new AddWorkOrderPartRequest(caso.Repuesto, 2m, null, null));

        Assert.Equal(1m, await ExistenciaAsync(db, caso.Repuesto));

        // Era uno solo: el otro vuelve.
        var corregida = await servicio.SetPartPriceAsync(
            caso.Orden, linea.Id, new SetPartPriceRequest(1200m, null, Quantity: 1m));

        Assert.Equal(1m, corregida.Quantity);
        Assert.Equal(2m, await ExistenciaAsync(db, caso.Repuesto));
    }

    [RequiereBaseLocal]
    public async Task Subirla_descuenta_y_sin_existencia_no_pasa()
    {
        var caso = await EscenarioAsync();

        await using var db = Contexto(caso.Taller, caso.Usuario);
        var servicio = Servicio(db);

        var linea = await servicio.AddPartAsync(
            caso.Orden, new AddWorkOrderPartRequest(caso.Repuesto, 1m, null, null));

        await servicio.SetPartPriceAsync(
            caso.Orden, linea.Id, new SetPartPriceRequest(1200m, null, Quantity: 3m));

        Assert.Equal(0m, await ExistenciaAsync(db, caso.Repuesto));

        // Ya no hay de dónde sacar el cuarto.
        await Assert.ThrowsAnyAsync<AppException>(
            () => servicio.SetPartPriceAsync(
                caso.Orden, linea.Id, new SetPartPriceRequest(1200m, null, Quantity: 4m)));

        Assert.Equal(0m, await ExistenciaAsync(db, caso.Repuesto));
    }

    [RequiereBaseLocal]
    public async Task La_linea_por_confirmar_cambia_de_cantidad_sin_tocar_la_bodega()
    {
        var caso = await EscenarioAsync();

        await using var db = Contexto(caso.Taller, caso.Usuario);
        var servicio = Servicio(db);

        var traidos = await servicio.ImportQuotePartsAsync(
            caso.Orden, new ImportQuotePartsRequest(caso.Cotizacion));

        var corregida = await servicio.SetPartPriceAsync(
            caso.Orden, traidos.Added.Single().Id,
            new SetPartPriceRequest(1200m, null, Quantity: 5m));

        Assert.Equal(5m, corregida.Quantity);
        Assert.True(corregida.PorConfirmar);
        Assert.Equal(3m, await ExistenciaAsync(db, caso.Repuesto));
    }

    [RequiereBaseLocal]
    public async Task El_catalogo_del_taller_solo_lo_mueve_el_Dueno()
    {
        var caso = await EscenarioAsync();

        Guid lineaId;

        await using (var db = Contexto(caso.Taller, caso.Usuario))
        {
            var traidos = await Servicio(db).ImportQuotePartsAsync(
                caso.Orden, new ImportQuotePartsRequest(caso.Cotizacion));

            lineaId = traidos.Added.Single().Id;
        }

        // El Encargado corrige el precio de su orden, pero no el catálogo del taller.
        await using var comoEncargado =
            Contexto(caso.Taller, caso.Usuario, "Manager", caso.Sucursal);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Servicio(comoEncargado).SetPartPriceAsync(
                caso.Orden, lineaId, new SetPartPriceRequest(1500m, null, UpdateCatalog: true)));

        var ficha = await comoEncargado.Parts.AsNoTracking().FirstAsync(p => p.Id == caso.Repuesto);
        Assert.Equal(1200m, ficha.SalePrice);
    }
}

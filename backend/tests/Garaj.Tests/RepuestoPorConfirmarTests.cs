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
/// El repuesto que está en la orden pero no ha salido de bodega.
/// </summary>
/// <remarks>
/// Es la pieza que permite dejar puestos los repuestos que el asistente propuso sin descontar
/// de la bodega un trabajo que todavía no se hizo. Lo que se prueba aquí es lo único que puede
/// salir caro: que poner no descuente, que confirmar sí, y que una orden no se entregue con
/// líneas puestas y nunca descontadas —eso desfasa el inventario en silencio—.
/// </remarks>
public class RepuestoPorConfirmarTests
{
    private const string CadenaLocal =
        "Host=localhost;Port=5434;Database=garaj;Username=garaj;Password=garaj-dev-secret";

    private static GarajDbContext Contexto(Guid taller, Guid usuario)
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? CadenaLocal;

        var contexto = new TenantContext();
        contexto.Initialize(taller, usuario, "Owner", [], null);

        var options = new DbContextOptionsBuilder<GarajDbContext>()
            .UseNpgsql(cadena)
            .AddInterceptors(new AuditableEntityInterceptor(contexto, new DateTimeProvider()))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GarajDbContext(options, contexto);
    }

    /// <summary>
    /// Un taller con una orden abierta y un repuesto con tres en bodega.
    /// </summary>
    private static async Task<(Guid Taller, Guid Usuario, Guid Orden, Guid Repuesto)> EscenarioAsync()
    {
        var taller = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        var sucursal = Guid.NewGuid();
        var orden = Guid.NewGuid();
        var repuesto = Guid.NewGuid();
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
            Status = WorkOrderStatus.Ready, OpenedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        return (taller, usuario, orden, repuesto);
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

    /// <summary>
    /// Las dependencias que estas rutas no tocan van en null a propósito: la orden de la prueba
    /// no tiene técnico asignado, así que el aviso de cambios se sale en la primera línea.
    /// </summary>
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
    public async Task Poner_un_repuesto_por_confirmar_no_descuenta_la_bodega()
    {
        var (taller, usuario, orden, repuesto) = await EscenarioAsync();

        await using var db = Contexto(taller, usuario);

        var linea = await Servicio(db).AddPartAsync(
            orden, new AddWorkOrderPartRequest(repuesto, 2m, null, null, Pending: true));

        Assert.True(linea.PorConfirmar);
        Assert.Equal(3m, await ExistenciaAsync(db, repuesto));

        // Y cuenta en lo que la orden suma: es lo que se le cotiza al cliente.
        Assert.Equal(2400m, linea.Total);
    }

    [RequiereBaseLocal]
    public async Task Confirmarlo_es_lo_que_lo_saca_de_la_bodega()
    {
        var (taller, usuario, orden, repuesto) = await EscenarioAsync();

        await using var db = Contexto(taller, usuario);
        var servicio = Servicio(db);

        var linea = await servicio.AddPartAsync(
            orden, new AddWorkOrderPartRequest(repuesto, 2m, null, null, Pending: true));

        var confirmada = await servicio.ConfirmPartAsync(orden, linea.Id);

        Assert.False(confirmada.PorConfirmar);
        Assert.Equal(1m, await ExistenciaAsync(db, repuesto));

        // Dos veces no: la segunda descontaría la bodega otra vez por el mismo repuesto.
        await Assert.ThrowsAsync<ConflictException>(
            () => servicio.ConfirmPartAsync(orden, linea.Id));
    }

    [RequiereBaseLocal]
    public async Task Si_entre_tanto_se_acabo_la_existencia_no_se_confirma()
    {
        var (taller, usuario, orden, repuesto) = await EscenarioAsync();

        await using var db = Contexto(taller, usuario);
        var servicio = Servicio(db);

        var linea = await servicio.AddPartAsync(
            orden, new AddWorkOrderPartRequest(repuesto, 3m, null, null, Pending: true));

        // Se fueron en otra orden mientras esta esperaba aprobación.
        var item = await db.StockItems.FirstAsync(s => s.PartId == repuesto);
        item.Quantity = 1m;
        await db.SaveChangesAsync();

        await Assert.ThrowsAnyAsync<AppException>(() => servicio.ConfirmPartAsync(orden, linea.Id));

        // La línea sigue puesta y por confirmar: hay que comprar, no perder el dato.
        var sigue = await db.WorkOrderParts.AsNoTracking().FirstAsync(p => p.Id == linea.Id);
        Assert.Null(sigue.ConfirmedOn);
    }

    [RequiereBaseLocal]
    public async Task Quitar_una_linea_por_confirmar_no_devuelve_nada_a_la_bodega()
    {
        var (taller, usuario, orden, repuesto) = await EscenarioAsync();

        await using var db = Contexto(taller, usuario);
        var servicio = Servicio(db);

        var linea = await servicio.AddPartAsync(
            orden, new AddWorkOrderPartRequest(repuesto, 2m, null, null, Pending: true));

        await servicio.RemovePartAsync(orden, linea.Id);

        // Si devolviera, la bodega quedaría en cinco: dos de regalo por una línea que nunca
        // salió. Es el error que más caro sale de todos los de este cambio.
        Assert.Equal(3m, await ExistenciaAsync(db, repuesto));
    }

    [RequiereBaseLocal]
    public async Task Una_orden_no_se_entrega_con_repuestos_por_confirmar()
    {
        var (taller, usuario, orden, repuesto) = await EscenarioAsync();

        await using var db = Contexto(taller, usuario);
        var servicio = Servicio(db);

        var linea = await servicio.AddPartAsync(
            orden, new AddWorkOrderPartRequest(repuesto, 2m, null, null, Pending: true));

        await Assert.ThrowsAsync<ConflictException>(
            () => servicio.ChangeStatusAsync(
                orden, new ChangeStatusRequest(WorkOrderStatus.Delivered, null)));

        // Confirmado, se entrega.
        await servicio.ConfirmPartAsync(orden, linea.Id);

        var entregada = await servicio.ChangeStatusAsync(
            orden, new ChangeStatusRequest(WorkOrderStatus.Delivered, null));

        Assert.Equal(WorkOrderStatus.Delivered, entregada.Status);
    }

    [RequiereBaseLocal]
    public async Task Lo_que_no_esta_en_el_catalogo_entra_sin_precio_y_no_se_confirma_asi()
    {
        var (taller, usuario, orden, _) = await EscenarioAsync();

        await using var db = Contexto(taller, usuario);
        var servicio = Servicio(db);

        // Así entra un «faltante» del asistente: con el nombre con el que se pide, sin precio.
        var linea = await servicio.AddPartAsync(
            orden,
            new AddWorkOrderPartRequest(
                null, 1m, null, null,
                Description: "Empaque de tapa de válvulas", Pending: true));

        Assert.True(linea.PorConfirmar);
        Assert.Equal(0m, linea.UnitPrice);

        // Confirmarlo en cero lo metería regalado en la factura.
        await Assert.ThrowsAnyAsync<AppException>(() => servicio.ConfirmPartAsync(orden, linea.Id));

        await servicio.SetPartPriceAsync(orden, linea.Id, new SetPartPriceRequest(450m, null));

        var confirmada = await servicio.ConfirmPartAsync(orden, linea.Id);
        Assert.False(confirmada.PorConfirmar);
    }
}

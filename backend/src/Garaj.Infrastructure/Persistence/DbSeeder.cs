using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Garaj.Infrastructure.Persistence;

/// <summary>
/// Datos de demostración para desarrollo. Sin esto el web y el móvil no tienen contra qué
/// trabajar. Es idempotente: si el taller demo ya existe no hace nada.
/// </summary>
public class DbSeeder(
    GarajDbContext db,
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<DbSeeder> logger)
{
    private const string DemoPassword = "Garaj123!";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // El seeder corre fuera de una petición: sin esto el filtro por tenant ocultaría
        // todo lo que acaba de insertar y crearía duplicados en cada arranque.
        tenantContext.BypassTenantFilter = true;

        await SeedRolesAsync();

        if (await db.Tenants.AnyAsync(ct))
        {
            logger.LogInformation("La base ya tiene datos, se omite el seed.");
            return;
        }

        var now = clock.UtcNow;

        var tenant = new Tenant
        {
            Name = "Taller Garaj",
            LegalName = "Garaj Servicios Automotrices S.A.",
            Phone = "50499001111",
            Email = "contacto@garaj.test",
            // Lempira e ISV del 15%, que es la tasa general en Honduras.
            Currency = "HNL",
            DefaultTaxRate = 15m,
            DefaultPhoneCountryCode = "504"
        };
        db.Tenants.Add(tenant);
        tenantContext.SetTenant(tenant.Id);

        var matriz = new Branch { Name = "Matriz", Code = "MTZ", City = "Tegucigalpa", Address = "Bulevar Morazán, frente a Plaza Criolla", Phone = "50499001111" };
        var norte = new Branch { Name = "Sucursal Norte", Code = "SPS", City = "San Pedro Sula", Address = "Avenida Circunvalación, 12 calle NO", Phone = "50499002222" };
        db.Branches.AddRange(matriz, norte);

        await db.SaveChangesAsync(ct);

        var owner = await CreateUserAsync("owner@garaj.test", "Óscar Méndez", AppRoles.Owner, tenant.Id);
        var tech1 = await CreateUserAsync("tecnico1@garaj.test", "Luis Cabrera", AppRoles.Technician, tenant.Id, [matriz.Id]);
        var tech2 = await CreateUserAsync("tecnico2@garaj.test", "Andrea Salas", AppRoles.Technician, tenant.Id, [norte.Id]);

        var vehicles = await SeedCustomersAndVehiclesAsync(tenant.Id, ct);
        var parts = await SeedPartsAndStockAsync([matriz, norte], owner.Id, now, ct);
        await SeedLaborServicesAsync(ct);
        await SeedOperationsAsync(matriz, norte, vehicles, parts, owner.Id, tech1.Id, tech2.Id, now, ct);

        logger.LogInformation(
            "Seed completo. Usuarios demo: owner@garaj.test / tecnico1@garaj.test / tecnico2@garaj.test / cliente@garaj.test — contraseña {Password}",
            DemoPassword);
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new AppRole(role));
        }
    }

    private async Task<AppUser> CreateUserAsync(
        string email, string fullName, string role, Guid tenantId,
        Guid[]? branchIds = null, Guid? customerId = null)
    {
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            TenantId = tenantId,
            CustomerId = customerId,
            CreatedAt = clock.UtcNow
        };

        var result = await userManager.CreateAsync(user, DemoPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"No se pudo crear el usuario {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        await userManager.AddToRoleAsync(user, role);

        foreach (var branchId in branchIds ?? [])
            db.UserBranches.Add(new UserBranch { TenantId = tenantId, UserId = user.Id, BranchId = branchId });

        await db.SaveChangesAsync();
        return user;
    }

    private async Task<List<Vehicle>> SeedCustomersAndVehiclesAsync(
        Guid tenantId, CancellationToken ct)
    {
        var customers = new List<Customer>
        {
            new() { FullName = "María Torres", Phone = "50498881111", Email = "cliente@garaj.test", DocumentId = "0801199012345" },
            new() { FullName = "Jorge Paredes", Phone = "50498882222", DocumentId = "0501198567890" },
            new() { FullName = "Transportes Catracho S. de R.L.", Phone = "50498883333", Email = "flota@catracho.test", DocumentId = "08019995123456" }
        };
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync(ct);

        // Solo la primera clienta tiene acceso a la app; las demás se manejan por WhatsApp.
        await CreateUserAsync("cliente@garaj.test", "María Torres", AppRoles.Customer, tenantId, customerId: customers[0].Id);
        customers[0].AppUserId = (await db.Users.FirstAsync(u => u.Email == "cliente@garaj.test", ct)).Id;

        var vehicles = new List<Vehicle>
        {
            new() { CustomerId = customers[0].Id, Type = VehicleType.Car, Brand = "Chevrolet", Model = "Sail", Year = 2019, Plate = "PBH1234", Color = "Plata", Mileage = 78500 },
            new() { CustomerId = customers[0].Id, Type = VehicleType.Motorcycle, Brand = "Suzuki", Model = "GN125", Year = 2021, Plate = "MAB4521", Color = "Negro", Mileage = 14200 },
            new() { CustomerId = customers[1].Id, Type = VehicleType.Motorcycle, Brand = "Yamaha", Model = "FZ 2.0", Year = 2022, Plate = "MCD7788", Color = "Azul", Mileage = 9800 },
            new() { CustomerId = customers[2].Id, Type = VehicleType.Car, Brand = "Hyundai", Model = "H100", Year = 2018, Plate = "PDF9042", Color = "Blanco", Mileage = 162000 }
        };
        db.Vehicles.AddRange(vehicles);
        await db.SaveChangesAsync(ct);

        return vehicles;
    }

    private async Task<List<Part>> SeedPartsAndStockAsync(
        Branch[] branches, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var parts = new List<Part>
        {
            new() { Sku = "ACE-15W40", Name = "Aceite motor 15W40 mineral", Brand = "Valvoline", Category = "Lubricantes", Unit = "lt", CostPrice = 118m, SalePrice = 205m },
            new() { Sku = "FIL-ACE-01", Name = "Filtro de aceite", Brand = "Fram", Category = "Filtros", Unit = "u", CostPrice = 83m, SalePrice = 170m },
            new() { Sku = "FIL-AIR-01", Name = "Filtro de aire", Brand = "Fram", Category = "Filtros", Unit = "u", CostPrice = 151m, SalePrice = 286m },
            new() { Sku = "PAS-FRE-DEL", Name = "Pastillas de freno delanteras", Brand = "Bosch", Category = "Frenos", Unit = "jgo", CostPrice = 468m, SalePrice = 832m },
            new() { Sku = "BUJ-NGK-01", Name = "Bujía NGK estándar", Brand = "NGK", Category = "Encendido", Unit = "u", CostPrice = 62m, SalePrice = 130m },
            new() { Sku = "CAD-MOT-125", Name = "Kit cadena y piñones 125cc", Brand = "DID", Category = "Transmisión", Unit = "jgo", CostPrice = 572m, SalePrice = 1066m },
            new() { Sku = "BAT-12V-60", Name = "Batería 12V 60Ah", Brand = "Bosch", Category = "Eléctrico", Unit = "u", CostPrice = 1768m, SalePrice = 2730m },
            new() { Sku = "LIQ-FRE-DOT4", Name = "Líquido de frenos DOT4", Brand = "Wagner", Category = "Lubricantes", Unit = "lt", CostPrice = 133m, SalePrice = 247m }
        };
        db.Parts.AddRange(parts);
        await db.SaveChangesAsync(ct);

        // Existencia inicial vía movimiento de entrada: el stock nunca se escribe a mano.
        foreach (var branch in branches)
        {
            foreach (var part in parts)
            {
                // Las baterías quedan justo en el mínimo a propósito: así la alerta de
                // reposición tiene algo real que mostrar desde el primer arranque.
                var quantity = part.Sku.StartsWith("BAT") ? 2m : 12m;

                db.StockItems.Add(new StockItem
                {
                    BranchId = branch.Id,
                    PartId = part.Id,
                    Quantity = quantity,
                    MinQuantity = part.Sku.StartsWith("BAT") ? 2m : 5m,
                    Location = $"Estante {(part.Category ?? "General")[..1]}"
                });

                db.StockMovements.Add(new StockMovement
                {
                    BranchId = branch.Id,
                    PartId = part.Id,
                    Type = StockMovementType.In,
                    Quantity = quantity,
                    UnitCost = part.CostPrice,
                    ResultingQuantity = quantity,
                    Reference = "Inventario inicial",
                    MovedAt = now.AddDays(-30),
                    MovedByUserId = userId
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return parts;
    }

    private async Task SeedLaborServicesAsync(CancellationToken ct)
    {
        db.LaborServices.AddRange(
            new LaborService { Code = "MO-ACE", Name = "Cambio de aceite y filtro", Category = "Mantenimiento", StandardHours = 0.5m, HourlyRate = 520m },
            new LaborService { Code = "MO-ABC", Name = "ABC de motor", Category = "Mantenimiento", StandardHours = 3m, HourlyRate = 520m },
            new LaborService { Code = "MO-FRE", Name = "Cambio de pastillas y rectificado de discos", Category = "Frenos", StandardHours = 2m, HourlyRate = 570m },
            new LaborService { Code = "MO-DIA", Name = "Diagnóstico electrónico", Category = "Diagnóstico", StandardHours = 1m, HourlyRate = 650m, IsFixedPrice = true, FixedPrice = 650m },
            new LaborService { Code = "MO-CAD", Name = "Cambio de kit de arrastre (moto)", Category = "Transmisión", StandardHours = 1.5m, HourlyRate = 470m });

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedOperationsAsync(
        Branch matriz, Branch norte, List<Vehicle> vehicles, List<Part> parts,
        Guid ownerId, Guid tech1Id, Guid tech2Id, DateTimeOffset now, CancellationToken ct)
    {
        // 1. Requerimiento pendiente: lo que el Dueño ve en su bandeja al abrir la app.
        db.ServiceRequests.Add(new ServiceRequest
        {
            BranchId = norte.Id,
            VehicleId = vehicles[2].Id,
            Description = "La moto se apaga en ralentí",
            ReportedSymptoms = "Desde hace una semana, sobre todo en frío. Ya le cambié la bujía.",
            Status = ServiceRequestStatus.Pending,
            PreferredDate = now.AddDays(2),
            Mileage = 9800
        });

        // 2. Orden en curso con pasos y evidencia: alimenta el kanban y la vista del técnico.
        var request = new ServiceRequest
        {
            BranchId = matriz.Id,
            VehicleId = vehicles[0].Id,
            Description = "Mantenimiento de 80.000 km + ruido en frenos",
            ReportedSymptoms = "Chirrido al frenar en bajada.",
            Status = ServiceRequestStatus.Converted,
            Mileage = 78500
        };
        db.ServiceRequests.Add(request);
        await db.SaveChangesAsync(ct);

        matriz.WorkOrderSequence++;
        var workOrder = new WorkOrder
        {
            BranchId = matriz.Id,
            VehicleId = vehicles[0].Id,
            ServiceRequestId = request.Id,
            Number = $"{matriz.Code}-{matriz.WorkOrderSequence:D6}",
            Status = WorkOrderStatus.InProgress,
            AssignedTechnicianId = tech1Id,
            Description = request.Description,
            Diagnosis = "Pastillas delanteras al límite. Discos en tolerancia, solo rectificar.",
            MileageIn = 78500,
            OpenedAt = now.AddDays(-1),
            PromisedAt = now.AddDays(1)
        };
        db.WorkOrders.Add(workOrder);
        request.WorkOrderId = workOrder.Id;
        await db.SaveChangesAsync(ct);

        // Los dos pasos que se cobran llevan su servicio del catálogo; el de recepción no,
        // que es trabajo del taller y no se factura. Así la orden de ejemplo enseña las dos
        // caras: lo que entra en la factura y lo que no.
        var aceite = await db.LaborServices.FirstAsync(s => s.Code == "MO-ACE", ct);
        var frenos = await db.LaborServices.FirstAsync(s => s.Code == "MO-FRE", ct);

        db.WorkOrderTasks.AddRange(
            new WorkOrderTask { WorkOrderId = workOrder.Id, Sequence = 1, Title = "Recepción e inspección visual", IsDone = true, AssignedTechnicianId = tech1Id, StartedAt = now.AddDays(-1), CompletedAt = now.AddDays(-1).AddHours(1), ActualHours = 0.5m },
            new WorkOrderTask { WorkOrderId = workOrder.Id, Sequence = 2, Title = "Cambio de aceite y filtro", IsDone = true, AssignedTechnicianId = tech1Id, LaborServiceId = aceite.Id, EstimatedHours = 0.5m, ActualHours = 0.6m, StartedAt = now.AddHours(-6), CompletedAt = now.AddHours(-5) },
            new WorkOrderTask { WorkOrderId = workOrder.Id, Sequence = 3, Title = "Cambio de pastillas delanteras", IsDone = false, AssignedTechnicianId = tech1Id, LaborServiceId = frenos.Id, EstimatedHours = 2m, StartedAt = now.AddHours(-2), TechnicianNotes = "Esperando rectificado de discos." });

        db.WorkOrderParts.AddRange(
            new WorkOrderPart { WorkOrderId = workOrder.Id, PartId = parts[0].Id, Quantity = 4m, UnitPrice = parts[0].SalePrice, UnitCost = parts[0].CostPrice },
            new WorkOrderPart { WorkOrderId = workOrder.Id, PartId = parts[1].Id, Quantity = 1m, UnitPrice = parts[1].SalePrice, UnitCost = parts[1].CostPrice });

        db.WorkOrderStatusHistory.AddRange(
            new WorkOrderStatusHistory { WorkOrderId = workOrder.Id, FromStatus = null, ToStatus = WorkOrderStatus.Received, ChangedAt = now.AddDays(-1), ChangedByUserId = ownerId, Note = "Vehículo recibido en Matriz." },
            new WorkOrderStatusHistory { WorkOrderId = workOrder.Id, FromStatus = WorkOrderStatus.Received, ToStatus = WorkOrderStatus.Diagnosing, ChangedAt = now.AddDays(-1).AddHours(1), ChangedByUserId = tech1Id, Note = "Inspección inicial." },
            new WorkOrderStatusHistory { WorkOrderId = workOrder.Id, FromStatus = WorkOrderStatus.Diagnosing, ToStatus = WorkOrderStatus.InProgress, ChangedAt = now.AddHours(-6), ChangedByUserId = tech1Id, Note = "Trabajo aprobado, iniciando mantenimiento." });

        // 3. Orden lista para entregar en la otra sucursal, para que el kanban no salga vacío.
        norte.WorkOrderSequence++;
        db.WorkOrders.Add(new WorkOrder
        {
            BranchId = norte.Id,
            VehicleId = vehicles[3].Id,
            Number = $"{norte.Code}-{norte.WorkOrderSequence:D6}",
            Status = WorkOrderStatus.Ready,
            AssignedTechnicianId = tech2Id,
            Description = "Revisión de frenos y cambio de batería",
            MileageIn = 162000,
            OpenedAt = now.AddDays(-3),
            ClosedAt = null
        });

        await db.SaveChangesAsync(ct);
    }
}

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
/// Siembra un taller con varias semanas de historia, para presentar la aplicación.
/// </summary>
/// <remarks>
/// No es el <see cref="DbSeeder"/> de desarrollo. Aquel deja cuatro órdenes para tener contra
/// qué programar; este simula un taller que lleva tiempo operando: ingresos que suben y bajan
/// según el día, órdenes en todos los estados, cotizaciones aprobadas y rechazadas, kardex con
/// movimiento y repuestos que ya cayeron bajo mínimo. Sin eso, la gráfica de reportes es una
/// sola barra y no se entiende para qué sirve.
///
/// <b>Borra todo antes de sembrar.</b> Está pensado para una base de demostración, no para una
/// con datos reales; por eso el endpoint que lo dispara exige una bandera de configuración y
/// una frase de confirmación.
///
/// Las fechas se escriben hacia atrás. El interceptor de auditoría respeta la fecha que traiga
/// la entidad, así que la historia queda repartida en el calendario en vez de sellada hoy.
/// </remarks>
public class DemoSeeder(
    GarajDbContext db,
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<DemoSeeder> logger)
{
    private const string Password = "Garaj123!";

    /// <summary>Honduras no cambia de hora, así que el día del taller es UTC−6 todo el año.</summary>
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(-6);

    // Semilla fija: dos ejecuciones dan el mismo taller. Si en una presentación hay que
    // repetir la siembra, los números que ya se enseñaron siguen siendo los mismos.
    private readonly Random _rnd = new(20260808);

    private readonly List<Notification> _notifications = [];

    public async Task<DemoSeedSummary> RunAsync(int weeks = 6, CancellationToken ct = default)
    {
        if (weeks is < 1 or > 26)
            throw new AppException("Las semanas de historia deben estar entre 1 y 26.");

        // Fuera de una petición HTTP: sin esto el filtro por tenant escondería todo lo que se
        // acaba de insertar y el borrado no alcanzaría nada.
        tenantContext.BypassTenantFilter = true;

        await WipeAsync(ct);
        await EnsureRolesAsync();

        var today = clock.UtcNow;
        var start = today.AddDays(-weeks * 7);

        var tenant = await SeedTenantAsync(ct);
        var (centro, sps) = await SeedBranchesAsync(ct);
        var staff = await SeedStaffAsync(tenant.Id, centro, sps);
        var vehicles = await SeedCustomersAsync(tenant.Id, ct);
        var labor = await SeedLaborServicesAsync(ct);
        var parts = await SeedPartsAsync(ct);

        var world = new World(tenant, [centro, sps], staff, vehicles, parts, labor);

        await SeedInitialStockAsync(world, start.AddDays(-3), ct);
        var closed = await SeedHistoryAsync(world, start, today, ct);
        await SeedOpenWorkAsync(world, today, ct);

        db.Notifications.AddRange(_notifications);
        await db.SaveChangesAsync(ct);

        var summary = new DemoSeedSummary(
            tenant.Name,
            weeks,
            await db.Customers.CountAsync(ct),
            await db.WorkOrders.CountAsync(ct),
            closed,
            await db.Quotes.CountAsync(ct),
            await db.Sales.SumAsync(s => s.Total, ct),
            staff.Owner.Email!,
            Password);

        logger.LogInformation(
            "Demostración sembrada: {Orders} órdenes, {Sales} facturadas, {Total:N2} HNL en {Weeks} semanas.",
            summary.WorkOrders, summary.ClosedWorkOrders, summary.Revenue, weeks);

        return summary;
    }

    // ------------------------------------------------------------------ borrado

    /// <summary>
    /// Vacía la base en orden de dependencia. Las fotos que ya estén en el bucket quedan
    /// huérfanas: borrarlas exigiría recorrer el almacenamiento y no vale la pena en una
    /// base de demostración, donde de todos modos se van a pisar.
    /// </summary>
    private async Task WipeAsync(CancellationToken ct)
    {
        await db.Notifications.ExecuteDeleteAsync(ct);
        await db.DeviceTokens.ExecuteDeleteAsync(ct);
        await db.MediaAttachments.ExecuteDeleteAsync(ct);
        await db.SaleLines.ExecuteDeleteAsync(ct);
        await db.Sales.ExecuteDeleteAsync(ct);
        await db.QuoteLines.ExecuteDeleteAsync(ct);
        await db.Quotes.ExecuteDeleteAsync(ct);
        await db.StockMovements.ExecuteDeleteAsync(ct);
        await db.StockItems.ExecuteDeleteAsync(ct);
        await db.WorkOrderParts.ExecuteDeleteAsync(ct);
        await db.WorkOrderStatusHistory.ExecuteDeleteAsync(ct);
        await db.WorkOrderTasks.ExecuteDeleteAsync(ct);
        await db.ServiceRequests.ExecuteDeleteAsync(ct);
        await db.WorkOrders.ExecuteDeleteAsync(ct);
        await db.Vehicles.ExecuteDeleteAsync(ct);
        await db.Customers.ExecuteDeleteAsync(ct);
        await db.Parts.ExecuteDeleteAsync(ct);
        await db.LaborServices.ExecuteDeleteAsync(ct);
        await db.RefreshTokens.ExecuteDeleteAsync(ct);
        await db.UserBranches.ExecuteDeleteAsync(ct);
        await db.Users.ExecuteDeleteAsync(ct);
        await db.Branches.ExecuteDeleteAsync(ct);
        await db.Tenants.ExecuteDeleteAsync(ct);
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new AppRole(role));
        }
    }

    // ------------------------------------------------------------------ catálogos

    private async Task<Tenant> SeedTenantAsync(CancellationToken ct)
    {
        var tenant = new Tenant
        {
            Name = "Taller Mecánico Maradiaga",
            LegalName = "Inversiones Maradiaga S. de R.L.",
            TaxId = "08019012345678",
            Phone = "50422340011",
            Email = "contacto@tallermaradiaga.hn",
            Currency = "HNL",
            DefaultTaxRate = 15m,
            DefaultPhoneCountryCode = "504"
        };

        db.Tenants.Add(tenant);
        tenantContext.SetTenant(tenant.Id);
        await db.SaveChangesAsync(ct);

        return tenant;
    }

    private async Task<(Branch Centro, Branch Sps)> SeedBranchesAsync(CancellationToken ct)
    {
        var centro = new Branch
        {
            Name = "Maradiaga Comayagüela",
            Code = "TGU",
            City = "Tegucigalpa",
            Address = "6 avenida, entre 11 y 12 calle, Comayagüela",
            Phone = "50422340011"
        };

        var sps = new Branch
        {
            Name = "Maradiaga San Pedro",
            Code = "SPS",
            City = "San Pedro Sula",
            Address = "Barrio Guamilito, 8 calle NO entre 9 y 10 avenida",
            Phone = "50425520022"
        };

        db.Branches.AddRange(centro, sps);
        await db.SaveChangesAsync(ct);

        return (centro, sps);
    }

    private async Task<Staff> SeedStaffAsync(Guid tenantId, Branch centro, Branch sps)
    {
        var owner = await CreateUserAsync("dueno@maradiaga.hn", "Wilmer Maradiaga", AppRoles.Owner, tenantId);

        // Dos técnicos por sucursal: así la bandeja de cada uno tiene contenido propio y se
        // ve que un técnico no alcanza el trabajo del otro.
        var tgu1 = await CreateUserAsync("tecnico1@maradiaga.hn", "Luis Cabrera", AppRoles.Technician, tenantId, [centro.Id]);
        var tgu2 = await CreateUserAsync("tecnico2@maradiaga.hn", "Nery Zelaya", AppRoles.Technician, tenantId, [centro.Id]);
        var sps1 = await CreateUserAsync("tecnico3@maradiaga.hn", "Andrea Salas", AppRoles.Technician, tenantId, [sps.Id]);

        return new Staff(owner, new Dictionary<Guid, AppUser[]>
        {
            [centro.Id] = [tgu1, tgu2],
            [sps.Id] = [sps1]
        });
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

        var result = await userManager.CreateAsync(user, Password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"No se pudo crear {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        await userManager.AddToRoleAsync(user, role);

        foreach (var branchId in branchIds ?? [])
            db.UserBranches.Add(new UserBranch { TenantId = tenantId, UserId = user.Id, BranchId = branchId });

        await db.SaveChangesAsync();
        return user;
    }

    private async Task<List<Vehicle>> SeedCustomersAsync(Guid tenantId, CancellationToken ct)
    {
        // Nombres, teléfonos y placas con forma hondureña: en una demostración el detalle que
        // delata que son datos inventados es siempre la placa o el número de teléfono.
        (string Name, string Phone, string? Email)[] people =
        [
            ("María Fernanda Torres", "50497451122", "mfa.torres@gmail.com"),
            ("José Ramón Bustillo", "50488230145", null),
            ("Karla Vanessa Discua", "50432117788", "kdiscua@hotmail.com"),
            ("Óscar Adalid Rivera", "50499874512", null),
            ("Transportes La Ceiba S. de R.L.", "50425509911", "flota@translaceiba.hn"),
            ("Digna Esperanza Núñez", "50496320014", null),
            ("Elvin Josué Padilla", "50433458890", "elvinpadilla@yahoo.com"),
            ("Sandra Yolany Mejía", "50487740023", null),
            ("Farmacias del Valle", "50422456600", "compras@farmaciasdelvalle.hn"),
            ("Roberto Antonio Cálix", "50498112345", null),
            ("Gladys Marleny Hernández", "50494567712", null),
            ("Jorge Alberto Ordóñez", "50489900456", "jaordonez@gmail.com"),
        ];

        var customers = people
            .Select(p => new Customer { FullName = p.Name, Phone = p.Phone, Email = p.Email })
            .ToList();

        db.Customers.AddRange(customers);
        await db.SaveChangesAsync(ct);

        // El primero tiene cuenta en la app: es el cliente que se enseña en la demostración.
        await CreateUserAsync("cliente@maradiaga.hn", customers[0].FullName, AppRoles.Customer,
            tenantId, customerId: customers[0].Id);
        await CreateUserAsync("cliente2@maradiaga.hn", customers[4].FullName, AppRoles.Customer,
            tenantId, customerId: customers[4].Id);

        (int Owner, VehicleType Type, string Brand, string Model, int Year, string Plate, string Color, int Km)[] fleet =
        [
            (0, VehicleType.Car, "Toyota", "Corolla", 2018, "PBH1234", "Plata", 96400),
            (0, VehicleType.Motorcycle, "Honda", "CB125F", 2022, "MAB4521", "Rojo", 11200),
            (1, VehicleType.Car, "Nissan", "Frontier", 2016, "PCK8890", "Blanco", 184300),
            (2, VehicleType.Car, "Hyundai", "Accent", 2020, "PDL2277", "Azul", 52100),
            (3, VehicleType.Motorcycle, "Yamaha", "YBR125", 2021, "MCD7788", "Negro", 23800),
            (4, VehicleType.Car, "Toyota", "Hiace", 2015, "PEF3311", "Blanco", 312500),
            (4, VehicleType.Car, "Toyota", "Hilux", 2019, "PEF3312", "Gris", 148700),
            (4, VehicleType.Car, "Mitsubishi", "L200", 2017, "PEF3313", "Blanco", 201400),
            (5, VehicleType.Car, "Kia", "Rio", 2019, "PGH5544", "Rojo", 71900),
            (6, VehicleType.Motorcycle, "Bajaj", "Boxer CT100", 2023, "MEF1109", "Azul", 8400),
            (7, VehicleType.Car, "Honda", "CR-V", 2017, "PJK9902", "Negro", 129600),
            (8, VehicleType.Car, "Toyota", "Yaris", 2021, "PLM4407", "Blanco", 43200),
            (8, VehicleType.Motorcycle, "Italika", "FT150", 2022, "MGH2231", "Negro", 15600),
            (9, VehicleType.Car, "Ford", "Ranger", 2014, "PNP7715", "Verde", 246800),
            (10, VehicleType.Car, "Suzuki", "Swift", 2020, "PQR3348", "Plata", 61300),
            (11, VehicleType.Motorcycle, "Suzuki", "GN125", 2019, "MJK5580", "Negro", 34700),
        ];

        var vehicles = fleet
            .Select(v => new Vehicle
            {
                CustomerId = customers[v.Owner].Id,
                Type = v.Type,
                Brand = v.Brand,
                Model = v.Model,
                Year = v.Year,
                Plate = v.Plate,
                Color = v.Color,
                Mileage = v.Km
            })
            .ToList();

        db.Vehicles.AddRange(vehicles);
        await db.SaveChangesAsync(ct);

        return vehicles;
    }

    private async Task<List<LaborService>> SeedLaborServicesAsync(CancellationToken ct)
    {
        // Tarifa de 350 L/hora para autos y algo menos para moto: es el orden de magnitud de
        // un taller de barrio en Tegucigalpa, y los totales tienen que sonar creíbles.
        (string Code, string Name, decimal Hours, decimal Rate, string Category)[] catalog =
        [
            ("MO-001", "Cambio de aceite y filtro", 0.5m, 350m, "Mantenimiento"),
            ("MO-002", "Afinamiento mayor", 3m, 350m, "Mantenimiento"),
            ("MO-003", "Cambio de pastillas de freno", 1.5m, 350m, "Frenos"),
            ("MO-004", "Rectificado de discos", 2m, 350m, "Frenos"),
            ("MO-005", "Diagnóstico electrónico", 1m, 400m, "Diagnóstico"),
            ("MO-006", "Cambio de batería", 0.3m, 350m, "Eléctrico"),
            ("MO-007", "Cambio de correa de distribución", 4m, 400m, "Motor"),
            ("MO-008", "Reparación de sistema de enfriamiento", 3m, 350m, "Motor"),
            ("MO-009", "Alineación y balanceo", 1m, 300m, "Suspensión"),
            ("MO-010", "Cambio de amortiguadores", 2.5m, 350m, "Suspensión"),
            ("MO-011", "Servicio mayor de motocicleta", 2m, 250m, "Motocicletas"),
            ("MO-012", "Cambio de kit de arrastre", 1.5m, 250m, "Motocicletas"),
            ("MO-013", "Revisión general prediagnóstico", 0.5m, 300m, "Diagnóstico"),
        ];

        var services = catalog
            .Select(s => new LaborService
            {
                Code = s.Code,
                Name = s.Name,
                Category = s.Category,
                StandardHours = s.Hours,
                HourlyRate = s.Rate
            })
            .ToList();

        db.LaborServices.AddRange(services);
        await db.SaveChangesAsync(ct);

        return services;
    }

    private async Task<List<Part>> SeedPartsAsync(CancellationToken ct)
    {
        (string Sku, string Name, string Brand, string Category, string Unit, decimal Cost, decimal Sale)[] catalog =
        [
            ("ACE-15W40", "Aceite motor 15W40 mineral", "Castrol", "Lubricantes", "gal", 380m, 520m),
            ("ACE-20W50", "Aceite motor 20W50 semisintético", "Mobil", "Lubricantes", "gal", 420m, 590m),
            ("ACE-4T", "Aceite motocicleta 4T 20W50", "Motul", "Lubricantes", "lt", 165m, 260m),
            ("FIL-ACE-01", "Filtro de aceite", "Toyota", "Filtros", "u", 95m, 165m),
            ("FIL-AIR-01", "Filtro de aire", "Fram", "Filtros", "u", 140m, 240m),
            ("FIL-COM-01", "Filtro de combustible", "Bosch", "Filtros", "u", 180m, 310m),
            ("BUJ-NGK-01", "Bujía NGK estándar", "NGK", "Encendido", "u", 85m, 150m),
            ("PAS-FRE-DEL", "Pastillas de freno delanteras", "Brembo", "Frenos", "jgo", 620m, 980m),
            ("PAS-FRE-TRA", "Pastillas de freno traseras", "Brembo", "Frenos", "jgo", 540m, 860m),
            ("DIS-FRE-01", "Disco de freno ventilado", "Brembo", "Frenos", "u", 890m, 1350m),
            ("LIQ-DOT4", "Líquido de frenos DOT4", "Bosch", "Frenos", "lt", 95m, 175m),
            ("BAT-65AH", "Batería 12V 65Ah", "Tudor", "Eléctrico", "u", 1850m, 2600m),
            ("AMO-DEL-01", "Amortiguador delantero", "Monroe", "Suspensión", "u", 1250m, 1890m),
            ("COR-DIS-01", "Correa de distribución", "Gates", "Motor", "u", 780m, 1180m),
            ("BOM-AGU-01", "Bomba de agua", "Aisin", "Motor", "u", 950m, 1450m),
            ("RAD-01", "Radiador", "Denso", "Motor", "u", 2400m, 3450m),
            ("LLA-185-65", "Llanta 185/65 R15", "Yokohama", "Llantas", "u", 1450m, 2150m),
            ("KIT-ARR-125", "Kit de arrastre 125cc", "DID", "Motocicletas", "jgo", 780m, 1180m),
            ("PAS-MOT-01", "Pastilla de freno motocicleta", "Galfer", "Motocicletas", "jgo", 210m, 360m),
            ("REF-VERDE", "Refrigerante verde concentrado", "Prestone", "Lubricantes", "gal", 260m, 420m),
        ];

        var parts = catalog
            .Select(p => new Part
            {
                Sku = p.Sku,
                Name = p.Name,
                Brand = p.Brand,
                Category = p.Category,
                Unit = p.Unit,
                CostPrice = p.Cost,
                SalePrice = p.Sale
            })
            .ToList();

        db.Parts.AddRange(parts);
        await db.SaveChangesAsync(ct);

        return parts;
    }

    // ------------------------------------------------------------------ inventario

    private async Task SeedInitialStockAsync(World world, DateTimeOffset when, CancellationToken ct)
    {
        foreach (var branch in world.Branches)
        {
            foreach (var part in world.Parts)
            {
                // Lo barato se compra por docenas y lo caro de a uno o dos: un taller no
                // tiene cuatro radiadores en la bodega.
                var quantity = part.CostPrice switch
                {
                    < 200m => _rnd.Next(14, 30),
                    < 800m => _rnd.Next(6, 14),
                    < 1500m => _rnd.Next(3, 7),
                    _ => _rnd.Next(1, 4)
                };

                var minimum = Math.Max(1, (int)Math.Round(quantity * 0.25));

                var item = new StockItem
                {
                    BranchId = branch.Id,
                    PartId = part.Id,
                    Quantity = quantity,
                    MinQuantity = minimum,
                    Location = $"Estante {(char)('A' + _rnd.Next(0, 5))}{_rnd.Next(1, 6)}"
                };

                db.StockItems.Add(item);
                world.Stock[(branch.Id, part.Id)] = item;

                db.StockMovements.Add(new StockMovement
                {
                    BranchId = branch.Id,
                    PartId = part.Id,
                    Type = StockMovementType.In,
                    Quantity = quantity,
                    UnitCost = part.CostPrice,
                    ResultingQuantity = quantity,
                    Reference = "Inventario inicial",
                    MovedAt = when,
                    MovedByUserId = world.Staff.Owner.Id,
                    CreatedAt = when,
                    CreatedByUserId = world.Staff.Owner.Id
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Descuenta de la bodega y deja el movimiento. Devuelve false si no alcanza.</summary>
    private bool Consume(World world, Guid branchId, Part part, decimal quantity, Guid workOrderId,
        DateTimeOffset when, Guid userId)
    {
        var item = world.Stock[(branchId, part.Id)];
        if (item.Quantity < quantity) return false;

        item.Quantity -= quantity;

        db.StockMovements.Add(new StockMovement
        {
            BranchId = branchId,
            PartId = part.Id,
            Type = StockMovementType.Out,
            Quantity = quantity,
            ResultingQuantity = item.Quantity,
            WorkOrderId = workOrderId,
            MovedAt = when,
            MovedByUserId = userId,
            CreatedAt = when,
            CreatedByUserId = userId
        });

        return true;
    }

    // ------------------------------------------------------------------ historia

    /// <summary>
    /// Recorre el calendario día por día. El sábado se trabaja medio día y el domingo el
    /// taller cierra: sin esa forma, la gráfica de ingresos sale plana y no se parece a nada.
    /// </summary>
    private async Task<int> SeedHistoryAsync(
        World world, DateTimeOffset start, DateTimeOffset today, CancellationToken ct)
    {
        var closed = 0;
        var lastRestock = start;

        for (var day = start; day.Date <= today.Date; day = day.AddDays(1))
        {
            var local = day.ToOffset(LocalOffset);

            if (local.DayOfWeek == DayOfWeek.Sunday) continue;

            var jobs = local.DayOfWeek == DayOfWeek.Saturday ? _rnd.Next(1, 3) : _rnd.Next(2, 6);

            // El día en curso va a medias: son las entregas de la mañana. Sin esto la tarjeta
            // de "hoy" del tablero abre en cero, que es justo lo que no se quiere enseñar.
            if (day.Date == today.Date) jobs = Math.Max(1, jobs / 2);

            for (var i = 0; i < jobs; i++)
            {
                if (CloseOneJob(world, local, i)) closed++;
            }

            // Compra de reposición cada diez días, como haría cualquier taller. Sin ella, seis
            // semanas de consumo dejan media bodega bajo mínimo y la alerta pierde sentido.
            if ((day - lastRestock).TotalDays >= 10)
            {
                Restock(world, day);
                lastRestock = day;
            }

            // Guardar por día mantiene el rastreador pequeño y hace que un fallo diga en qué
            // fecha ocurrió, en vez de reventar al final con miles de entidades encima.
            await db.SaveChangesAsync(ct);
        }

        return closed;
    }

    /// <summary>Repone lo que bajó del doble de su mínimo, con su factura de compra.</summary>
    private void Restock(World world, DateTimeOffset when)
    {
        var invoice = $"FAC-{_rnd.Next(10000, 99999)}";

        foreach (var item in world.Stock.Values)
        {
            if (item.Quantity > item.MinQuantity * 2) continue;

            var part = world.Parts.First(p => p.Id == item.PartId);
            var quantity = Math.Max(2, Math.Round(item.MinQuantity * 3 - item.Quantity));

            item.Quantity += quantity;

            db.StockMovements.Add(new StockMovement
            {
                BranchId = item.BranchId,
                PartId = part.Id,
                Type = StockMovementType.In,
                Quantity = quantity,
                UnitCost = part.CostPrice,
                ResultingQuantity = item.Quantity,
                Reference = invoice,
                MovedAt = when,
                MovedByUserId = world.Staff.Owner.Id,
                CreatedAt = when,
                CreatedByUserId = world.Staff.Owner.Id
            });
        }
    }

    private bool CloseOneJob(World world, DateTimeOffset localDay, int index)
    {
        var branch = world.Branches[_rnd.Next(world.Branches.Count)];
        var technicians = world.Staff.Technicians[branch.Id];
        var technician = technicians[_rnd.Next(technicians.Length)];
        var vehicle = world.Vehicles[_rnd.Next(world.Vehicles.Count)];
        var isBike = vehicle.Type == VehicleType.Motorcycle;

        var job = Jobs.Pick(_rnd, isBike);

        // La hora se decide en el reloj del taller —abre a las ocho— pero se guarda en UTC:
        // Npgsql rechaza cualquier otro desfase en `timestamp with time zone`.
        var opened = localDay.Date.AddHours(8).AddMinutes(index * 45 + _rnd.Next(0, 30));
        var openedAt = new DateTimeOffset(opened, LocalOffset).ToUniversalTime();

        var request = new ServiceRequest
        {
            BranchId = branch.Id,
            VehicleId = vehicle.Id,
            Description = job.Reason,
            ReportedSymptoms = job.Symptom,
            Status = ServiceRequestStatus.Converted,
            Mileage = vehicle.Mileage,
            CreatedAt = openedAt.AddHours(-16),
            CreatedByUserId = world.Staff.Owner.Id
        };
        db.ServiceRequests.Add(request);

        branch.WorkOrderSequence++;
        var order = new WorkOrder
        {
            BranchId = branch.Id,
            VehicleId = vehicle.Id,
            ServiceRequestId = request.Id,
            Number = $"{branch.Code}-{branch.WorkOrderSequence:D6}",
            Status = WorkOrderStatus.Delivered,
            AssignedTechnicianId = technician.Id,
            Description = job.Reason,
            Diagnosis = job.Diagnosis,
            MileageIn = vehicle.Mileage,
            OpenedAt = openedAt,
            CreatedAt = openedAt,
            CreatedByUserId = world.Staff.Owner.Id
        };
        db.WorkOrders.Add(order);
        request.WorkOrderId = order.Id;

        // El trabajo dura entre dos horas y día y medio, según el tamaño del servicio.
        var hours = job.LaborCodes.Length * 2 + _rnd.Next(1, 4);
        var closedAt = openedAt.AddHours(hours);

        Timeline(order, world.Staff.Owner.Id, technician.Id, openedAt, closedAt);

        var sequence = 1;
        var laborLines = new List<(LaborService Service, decimal Hours, decimal Total)>();

        foreach (var code in job.LaborCodes)
        {
            var service = world.Labor.First(l => l.Code == code);
            // Las horas reales se desvían de las estándar: es lo que pasa en un taller y es
            // justo lo que el reporte de margen tiene que reflejar.
            var actual = Math.Round(service.StandardHours * (decimal)(0.85 + _rnd.NextDouble() * 0.45), 1);

            db.WorkOrderTasks.Add(new WorkOrderTask
            {
                WorkOrderId = order.Id,
                Title = service.Name,
                Sequence = sequence++,
                IsDone = true,
                StartedAt = openedAt.AddHours(1),
                CompletedAt = closedAt.AddHours(-1),
                AssignedTechnicianId = technician.Id,
                LaborServiceId = service.Id,
                EstimatedHours = service.StandardHours,
                ActualHours = actual,
                TechnicianNotes = job.TechnicianNote,
                CreatedAt = openedAt,
                CreatedByUserId = technician.Id
            });

            laborLines.Add((service, actual, Math.Round(actual * service.HourlyRate, 2)));
        }

        var partLines = new List<(Part Part, decimal Quantity)>();

        foreach (var sku in job.PartSkus)
        {
            var part = world.Parts.First(p => p.Sku == sku);
            var quantity = sku.StartsWith("ACE-") ? _rnd.Next(1, 3) : 1;

            if (!Consume(world, branch.Id, part, quantity, order.Id, closedAt, technician.Id))
                continue;

            db.WorkOrderParts.Add(new WorkOrderPart
            {
                WorkOrderId = order.Id,
                PartId = part.Id,
                Quantity = quantity,
                UnitPrice = part.SalePrice,
                UnitCost = part.CostPrice,
                CreatedAt = closedAt,
                CreatedByUserId = technician.Id
            });

            partLines.Add((part, quantity));
        }

        // Uno de cada tres pasó por cotización antes de autorizarse. El resto son trabajos de
        // mostrador que el cliente aprueba de palabra: fingir que todos se cotizan sería falso.
        if (_rnd.Next(3) == 0)
            AddQuote(world, branch, vehicle, order, partLines, laborLines, openedAt, QuoteStatus.Approved);

        BillOut(world, branch, vehicle, order, partLines, laborLines, closedAt);
        return true;
    }

    private void Timeline(WorkOrder order, Guid ownerId, Guid technicianId,
        DateTimeOffset openedAt, DateTimeOffset closedAt)
    {
        (WorkOrderStatus? From, WorkOrderStatus To, double Fraction, string Note)[] steps =
        [
            (null, WorkOrderStatus.Received, 0, "Vehículo recibido en el taller."),
            (WorkOrderStatus.Received, WorkOrderStatus.Diagnosing, 0.15, "En revisión."),
            (WorkOrderStatus.Diagnosing, WorkOrderStatus.InProgress, 0.35, "Trabajo autorizado, manos a la obra."),
            (WorkOrderStatus.InProgress, WorkOrderStatus.Testing, 0.75, "Prueba de ruta."),
            (WorkOrderStatus.Testing, WorkOrderStatus.Ready, 0.9, "Listo para retirar."),
            (WorkOrderStatus.Ready, WorkOrderStatus.Delivered, 1, "Entregado al cliente.")
        ];

        var span = closedAt - openedAt;

        foreach (var step in steps)
        {
            var at = openedAt + span * step.Fraction;

            db.WorkOrderStatusHistory.Add(new WorkOrderStatusHistory
            {
                WorkOrderId = order.Id,
                FromStatus = step.From,
                ToStatus = step.To,
                ChangedAt = at,
                ChangedByUserId = step.To is WorkOrderStatus.Received or WorkOrderStatus.Delivered
                    ? ownerId
                    : technicianId,
                Note = step.Note,
                CreatedAt = at,
                CreatedByUserId = ownerId
            });
        }

        order.ClosedAt = closedAt;
    }

    private Quote AddQuote(
        World world, Branch branch, Vehicle vehicle, WorkOrder? order,
        List<(Part Part, decimal Quantity)> parts,
        List<(LaborService Service, decimal Hours, decimal Total)> labor,
        DateTimeOffset when, QuoteStatus status)
    {
        branch.QuoteSequence++;

        var quote = new Quote
        {
            BranchId = branch.Id,
            CustomerId = vehicle.CustomerId,
            VehicleId = vehicle.Id,
            WorkOrderId = order?.Id,
            Number = $"COT-{branch.Code}-{branch.QuoteSequence:D6}",
            Status = status,
            ValidUntil = when.AddDays(15),
            SentAt = status == QuoteStatus.Draft ? null : when.AddHours(2),
            TaxRate = world.Tenant.DefaultTaxRate,
            CreatedAt = when,
            CreatedByUserId = world.Staff.Owner.Id
        };

        var sequence = 1;
        decimal subtotal = 0;

        foreach (var (part, quantity) in parts)
        {
            var total = Math.Round(quantity * part.SalePrice, 2);
            subtotal += total;

            quote.Lines.Add(new QuoteLine
            {
                LineType = LineType.Part,
                PartId = part.Id,
                Description = part.Name,
                Sequence = sequence++,
                Quantity = quantity,
                UnitPrice = part.SalePrice,
                Total = total,
                CreatedAt = when,
                CreatedByUserId = world.Staff.Owner.Id
            });
        }

        foreach (var (service, hours, total) in labor)
        {
            subtotal += total;

            quote.Lines.Add(new QuoteLine
            {
                LineType = LineType.Labor,
                LaborServiceId = service.Id,
                Description = service.Name,
                Sequence = sequence++,
                Quantity = hours,
                UnitPrice = service.HourlyRate,
                Total = total,
                CreatedAt = when,
                CreatedByUserId = world.Staff.Owner.Id
            });
        }

        quote.Subtotal = subtotal;
        quote.TaxTotal = Math.Round(subtotal * quote.TaxRate / 100m, 2);
        quote.Total = quote.Subtotal + quote.TaxTotal;

        if (status is QuoteStatus.Approved or QuoteStatus.Rejected)
        {
            quote.RespondedAt = when.AddHours(_rnd.Next(3, 30));
            quote.CustomerResponseNote = status == QuoteStatus.Approved
                ? "De acuerdo, procedan."
                : "Por ahora no, gracias. Lo veo el próximo mes.";
        }

        db.Quotes.Add(quote);
        return quote;
    }

    private void BillOut(
        World world, Branch branch, Vehicle vehicle, WorkOrder order,
        List<(Part Part, decimal Quantity)> parts,
        List<(LaborService Service, decimal Hours, decimal Total)> labor,
        DateTimeOffset when)
    {
        branch.SaleSequence++;

        var sale = new Sale
        {
            BranchId = branch.Id,
            CustomerId = vehicle.CustomerId,
            WorkOrderId = order.Id,
            Number = $"VTA-{branch.Code}-{branch.SaleSequence:D6}",
            SaleDate = when,
            PaymentMethod = _rnd.Next(10) switch
            {
                < 6 => PaymentMethod.Cash,
                < 8 => PaymentMethod.Card,
                _ => PaymentMethod.Transfer
            },
            TaxRate = world.Tenant.DefaultTaxRate,
            CreatedAt = when,
            CreatedByUserId = world.Staff.Owner.Id
        };

        var sequence = 1;
        decimal subtotal = 0, cost = 0;

        foreach (var (part, quantity) in parts)
        {
            var total = Math.Round(quantity * part.SalePrice, 2);
            subtotal += total;
            cost += quantity * part.CostPrice;

            sale.Lines.Add(new SaleLine
            {
                LineType = LineType.Part,
                PartId = part.Id,
                Description = part.Name,
                Sequence = sequence++,
                Quantity = quantity,
                UnitPrice = part.SalePrice,
                UnitCost = part.CostPrice,
                Total = total,
                CreatedAt = when,
                CreatedByUserId = world.Staff.Owner.Id
            });
        }

        foreach (var (service, hours, total) in labor)
        {
            subtotal += total;

            sale.Lines.Add(new SaleLine
            {
                LineType = LineType.Labor,
                LaborServiceId = service.Id,
                Description = service.Name,
                Sequence = sequence++,
                Quantity = hours,
                UnitPrice = service.HourlyRate,
                Total = total,
                CreatedAt = when,
                CreatedByUserId = world.Staff.Owner.Id
            });
        }

        sale.Subtotal = subtotal;
        sale.CostTotal = Math.Round(cost, 2);
        sale.TaxTotal = Math.Round(subtotal * sale.TaxRate / 100m, 2);
        sale.Total = sale.Subtotal + sale.TaxTotal;

        db.Sales.Add(sale);
        order.SaleId = sale.Id;
    }

    // ------------------------------------------------------------------ el día de hoy

    /// <summary>
    /// Lo que la demostración necesita tener vivo al abrirla: órdenes en cada estado,
    /// requerimientos sin atender, cotizaciones esperando respuesta y avisos sin leer.
    /// </summary>
    private async Task SeedOpenWorkAsync(World world, DateTimeOffset today, CancellationToken ct)
    {
        WorkOrderStatus[] open =
        [
            WorkOrderStatus.Received,
            WorkOrderStatus.Diagnosing,
            WorkOrderStatus.WaitingApproval,
            WorkOrderStatus.WaitingParts,
            WorkOrderStatus.InProgress,
            WorkOrderStatus.InProgress,
            WorkOrderStatus.Testing,
            WorkOrderStatus.Ready
        ];

        for (var i = 0; i < open.Length; i++)
        {
            var branch = world.Branches[i % world.Branches.Count];
            var technicians = world.Staff.Technicians[branch.Id];
            var technician = technicians[_rnd.Next(technicians.Length)];
            var vehicle = world.Vehicles[_rnd.Next(world.Vehicles.Count)];
            var job = Jobs.Pick(_rnd, vehicle.Type == VehicleType.Motorcycle);

            // Escalonadas hacia atrás: las de arriba entraron hoy, las de abajo llevan días,
            // y así el tablero muestra órdenes atrasadas de verdad y no todas de esta mañana.
            var openedAt = today.AddDays(-(open.Length - i) * 0.6).AddHours(-2);

            branch.WorkOrderSequence++;
            var order = new WorkOrder
            {
                BranchId = branch.Id,
                VehicleId = vehicle.Id,
                Number = $"{branch.Code}-{branch.WorkOrderSequence:D6}",
                Status = open[i],
                AssignedTechnicianId = open[i] == WorkOrderStatus.Received ? null : technician.Id,
                Description = job.Reason,
                Diagnosis = open[i] >= WorkOrderStatus.WaitingApproval ? job.Diagnosis : null,
                MileageIn = vehicle.Mileage,
                OpenedAt = openedAt,
                PromisedAt = openedAt.AddDays(2),
                CreatedAt = openedAt,
                CreatedByUserId = world.Staff.Owner.Id
            };
            db.WorkOrders.Add(order);

            db.WorkOrderStatusHistory.Add(new WorkOrderStatusHistory
            {
                WorkOrderId = order.Id,
                ToStatus = WorkOrderStatus.Received,
                ChangedAt = openedAt,
                ChangedByUserId = world.Staff.Owner.Id,
                Note = $"Vehículo recibido en {branch.Name}.",
                CreatedAt = openedAt,
                CreatedByUserId = world.Staff.Owner.Id
            });

            if (open[i] != WorkOrderStatus.Received)
            {
                db.WorkOrderStatusHistory.Add(new WorkOrderStatusHistory
                {
                    WorkOrderId = order.Id,
                    FromStatus = WorkOrderStatus.Received,
                    ToStatus = open[i],
                    ChangedAt = openedAt.AddHours(3),
                    ChangedByUserId = technician.Id,
                    Note = job.TechnicianNote,
                    CreatedAt = openedAt.AddHours(3),
                    CreatedByUserId = technician.Id
                });

                var sequence = 1;
                foreach (var code in job.LaborCodes)
                {
                    var service = world.Labor.First(l => l.Code == code);
                    var done = open[i] >= WorkOrderStatus.Testing;

                    db.WorkOrderTasks.Add(new WorkOrderTask
                    {
                        WorkOrderId = order.Id,
                        Title = service.Name,
                        Sequence = sequence++,
                        IsDone = done,
                        StartedAt = openedAt.AddHours(4),
                        CompletedAt = done ? openedAt.AddHours(7) : null,
                        AssignedTechnicianId = technician.Id,
                        LaborServiceId = service.Id,
                        EstimatedHours = service.StandardHours,
                        ActualHours = done ? service.StandardHours : null,
                        CreatedAt = openedAt.AddHours(3),
                        CreatedByUserId = technician.Id
                    });
                }

                _notifications.Add(new Notification
                {
                    RecipientUserId = technician.Id,
                    Type = NotificationType.WorkOrderAssigned,
                    Title = $"Nueva orden asignada · {order.Number}",
                    Body = $"{vehicle.Brand} {vehicle.Model} · {vehicle.Plate}",
                    WorkOrderId = order.Id,
                    CreatedAt = openedAt,
                    CreatedByUserId = world.Staff.Owner.Id
                });
            }

            // La que espera aprobación tiene su cotización enviada y sin responder: es el
            // estado en el que se enseña el link de WhatsApp durante la demostración.
            if (open[i] == WorkOrderStatus.WaitingApproval)
            {
                var service = world.Labor.First(l => l.Code == job.LaborCodes[0]);
                var part = world.Parts.First(p => p.Sku == job.PartSkus[0]);

                AddQuote(world, branch, vehicle, order,
                    [(part, 1)],
                    [(service, service.StandardHours, service.StandardHours * service.HourlyRate)],
                    openedAt.AddHours(4), QuoteStatus.Sent);
            }
        }

        // Requerimientos sin atender: la bandeja de entrada del Dueño no puede estar vacía.
        (int Vehicle, string Reason, string Symptom)[] pending =
        [
            (0, "Chequeo general antes de viaje a la costa", "Se siente floja la dirección en carretera"),
            (5, "Revisión de frenos de la Hiace", "Chilla al frenar con pasajeros"),
            (9, "Servicio de la moto", "Ya toca el cambio de aceite, van 4 meses"),
        ];

        for (var i = 0; i < pending.Length; i++)
        {
            var vehicle = world.Vehicles[pending[i].Vehicle];
            var at = today.AddHours(-(i * 5 + 2));

            var request = new ServiceRequest
            {
                BranchId = world.Branches[i % world.Branches.Count].Id,
                VehicleId = vehicle.Id,
                Description = pending[i].Reason,
                ReportedSymptoms = pending[i].Symptom,
                Status = ServiceRequestStatus.Pending,
                Mileage = vehicle.Mileage,
                PreferredDate = today.AddDays(i + 1),
                CreatedAt = at,
                CreatedByUserId = world.Staff.Owner.Id
            };

            db.ServiceRequests.Add(request);

            _notifications.Add(new Notification
            {
                RecipientUserId = world.Staff.Owner.Id,
                Type = NotificationType.ServiceRequestCreated,
                Title = "Nuevo requerimiento de un cliente",
                Body = $"{vehicle.Brand} {vehicle.Model} · {vehicle.Plate}: {pending[i].Reason}",
                ServiceRequestId = request.Id,
                CreatedAt = at,
                CreatedByUserId = world.Staff.Owner.Id
            });
        }

        // Dos repuestos por debajo del mínimo, para que la alerta del tablero tenga contenido
        // real y no haya que provocarla a mano durante la presentación.
        foreach (var sku in new[] { "PAS-FRE-DEL", "BAT-65AH" })
        {
            var part = world.Parts.First(p => p.Sku == sku);
            var item = world.Stock[(world.Branches[0].Id, part.Id)];
            var target = Math.Max(0, item.MinQuantity - 1);

            if (item.Quantity <= target) continue;

            var difference = target - item.Quantity;
            item.Quantity = target;

            db.StockMovements.Add(new StockMovement
            {
                BranchId = item.BranchId,
                PartId = part.Id,
                Type = StockMovementType.Adjustment,
                Quantity = difference,
                ResultingQuantity = target,
                Reference = "Conteo físico de fin de mes",
                Notes = "Faltante detectado en el conteo.",
                MovedAt = today.AddDays(-1),
                MovedByUserId = world.Staff.Owner.Id,
                CreatedAt = today.AddDays(-1),
                CreatedByUserId = world.Staff.Owner.Id
            });
        }

        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ tipos internos

    private sealed record Staff(AppUser Owner, Dictionary<Guid, AppUser[]> Technicians);

    private sealed record World(
        Tenant Tenant,
        List<Branch> Branches,
        Staff Staff,
        List<Vehicle> Vehicles,
        List<Part> Parts,
        List<LaborService> Labor)
    {
        /// <summary>Saldo en memoria por (sucursal, repuesto): evita releer la bodega en cada consumo.</summary>
        public Dictionary<(Guid Branch, Guid Part), StockItem> Stock { get; } = [];
    }

    /// <summary>
    /// Los trabajos que de verdad entran a un taller de barrio, con su motivo, su diagnóstico
    /// y los repuestos que consumen. Que el motivo, el diagnóstico y las piezas concuerden es
    /// lo que hace que la demostración se lea como un taller y no como relleno.
    /// </summary>
    private sealed record Job(
        string Reason,
        string Symptom,
        string Diagnosis,
        string TechnicianNote,
        string[] LaborCodes,
        string[] PartSkus);

    private static class Jobs
    {
        private static readonly Job[] Cars =
        [
            new("Cambio de aceite y afinamiento", "Le cuesta arrancar en frío",
                "Bujías desgastadas y filtro de aire saturado.",
                "Se cambió aceite, filtros y las cuatro bujías. Compresión normal.",
                ["MO-001", "MO-002"], ["ACE-15W40", "FIL-ACE-01", "FIL-AIR-01", "BUJ-NGK-01"]),

            new("Servicio de frenos", "Chilla al frenar y el pedal se va al fondo",
                "Pastillas delanteras al límite y discos rayados.",
                "Pastillas nuevas, discos rectificados y purga del sistema.",
                ["MO-003", "MO-004"], ["PAS-FRE-DEL", "DIS-FRE-01", "LIQ-DOT4"]),

            new("No enciende", "Amaneció sin batería otra vez",
                "Batería sin carga, con dos celdas muertas.",
                "Batería reemplazada. Alternador cargando 14.2 V, está bien.",
                ["MO-006", "MO-005"], ["BAT-65AH"]),

            new("Se recalienta", "Sube la aguja en el tráfico de la mañana",
                "Bomba de agua con juego y refrigerante contaminado.",
                "Bomba y refrigerante cambiados. Probado 40 minutos sin subir de temperatura.",
                ["MO-008"], ["BOM-AGU-01", "REF-VERDE"]),

            new("Mantenimiento de 100 mil kilómetros", "Toca el servicio completo",
                "Correa de distribución en su límite de vida.",
                "Correa, bomba y refrigerante cambiados. Puesta a punto verificada.",
                ["MO-007", "MO-001"], ["COR-DIS-01", "BOM-AGU-01", "ACE-20W50", "FIL-ACE-01"]),

            new("Ruido en la suspensión", "Golpea al pasar los túmulos",
                "Amortiguadores delanteros vencidos.",
                "Amortiguadores cambiados y alineación corregida.",
                ["MO-010", "MO-009"], ["AMO-DEL-01"]),

            new("Cambio de aceite", "Mantenimiento de rutina",
                "Sin novedad. Aceite en su intervalo.",
                "Aceite y filtro cambiados. Revisados niveles y presión de llantas.",
                ["MO-001"], ["ACE-15W40", "FIL-ACE-01"]),

            new("Revisión antes de comprar", "Quiero saber en qué estado está",
                "Vehículo en buen estado general; filtro de combustible sucio.",
                "Revisión completa. Se recomendó cambiar el filtro de combustible, y se hizo.",
                ["MO-013", "MO-005"], ["FIL-COM-01"]),
        ];

        private static readonly Job[] Bikes =
        [
            new("Servicio de motocicleta", "Ya van cuatro meses del último cambio",
                "Mantenimiento de rutina, cadena floja.",
                "Aceite cambiado, cadena tensada y lubricada, frenos revisados.",
                ["MO-011"], ["ACE-4T"]),

            new("Cambio de kit de arrastre", "Salta la cadena al acelerar",
                "Kit de arrastre desgastado, con dientes puntiagudos.",
                "Kit completo cambiado. Tensión y alineación ajustadas.",
                ["MO-012"], ["KIT-ARR-125"]),

            new("Frenos de la moto", "No frena igual que antes",
                "Pastillas delanteras gastadas.",
                "Pastillas cambiadas y purga del freno delantero.",
                ["MO-003"], ["PAS-MOT-01", "LIQ-DOT4"]),
        ];

        public static Job Pick(Random rnd, bool isBike) =>
            isBike ? Bikes[rnd.Next(Bikes.Length)] : Cars[rnd.Next(Cars.Length)];
    }
}

/// <summary>Lo que hay que enseñarle a quien dispara la siembra.</summary>
public record DemoSeedSummary(
    string Tenant,
    int Weeks,
    int Customers,
    int WorkOrders,
    int ClosedWorkOrders,
    int Quotes,
    decimal Revenue,
    string OwnerEmail,
    string Password);

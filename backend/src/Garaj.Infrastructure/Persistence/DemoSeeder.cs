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
/// Siembra el Taller Demo con varias semanas de historia, para presentar la aplicación.
/// </summary>
/// <remarks>
/// No es el <see cref="DbSeeder"/> de desarrollo. Aquel deja cuatro órdenes para tener contra
/// qué programar; este simula un taller que lleva tiempo operando: ingresos que suben y bajan
/// según el día, órdenes en todos los estados, cotizaciones aprobadas y rechazadas, kardex con
/// movimiento y repuestos que ya cayeron bajo mínimo. Sin eso, la gráfica de reportes es una
/// sola barra y no se entiende para qué sirve.
///
/// <b>Solo motocicletas.</b> El sistema maneja autos igual de bien, pero el taller de la
/// demostración trabaja motos y un Corolla en medio se nota postizo. Para volver a incluir autos hay
/// que añadir vehículos de tipo <see cref="VehicleType.Car"/> y entradas al catálogo de
/// <see cref="Jobs"/>; el resto del sembrador no distingue.
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
        var branches = await SeedBranchesAsync(ct);
        var staff = await SeedStaffAsync(tenant.Id, branches);
        var vehicles = await SeedCustomersAsync(tenant.Id, ct);
        var labor = await SeedLaborServicesAsync(ct);
        var parts = await SeedPartsAsync(ct);

        var world = new World(tenant, branches, staff, vehicles, parts, labor);

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
    /// Vacía la base entera, hijos antes que padres: el orden es el que exigen las llaves
    /// foráneas y no es decorativo —al agregar una tabla nueva hay que agregarla aquí, o la
    /// siembra estalla en cuanto alguien haya usado esa parte del sistema—. Las fotos que ya
    /// estén en el bucket quedan huérfanas: borrarlas exigiría recorrer el almacenamiento y no
    /// vale la pena en una base de demostración, donde de todos modos se van a pisar.
    /// </summary>
    private async Task WipeAsync(CancellationToken ct)
    {
        await db.Notifications.ExecuteDeleteAsync(ct);
        await db.DeviceTokens.ExecuteDeleteAsync(ct);
        await db.MediaAttachments.ExecuteDeleteAsync(ct);
        await db.SalePayments.ExecuteDeleteAsync(ct);
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
        await db.JobTemplateParts.ExecuteDeleteAsync(ct);
        await db.JobTemplateTasks.ExecuteDeleteAsync(ct);
        await db.JobTemplates.ExecuteDeleteAsync(ct);
        await db.Parts.ExecuteDeleteAsync(ct);
        await db.LaborServices.ExecuteDeleteAsync(ct);
        await db.RefreshTokens.ExecuteDeleteAsync(ct);
        await db.UserBranches.ExecuteDeleteAsync(ct);
        await db.Users.ExecuteDeleteAsync(ct);
        await db.FiscalRanges.ExecuteDeleteAsync(ct);
        await db.Branches.ExecuteDeleteAsync(ct);
        await db.SubscriptionPayments.ExecuteDeleteAsync(ct);
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
            Name = "Taller Demo",
            // Todo es inventado a propósito, y se nota: este taller se le enseña a un
            // cliente que todavía no ha comprado, así que ningún dato de aquí puede
            // parecerse al de un taller de verdad. Los teléfonos van a un bloque que no
            // existe (9000 xxxx) para que un enlace de WhatsApp no le caiga a nadie.
            LegalName = "Taller Demo S. de R.L.",
            TaxId = "05019012345678",
            Phone = "50490000100",
            Email = "contacto@tallerdemo.hn",
            Currency = "HNL",
            DefaultTaxRate = 15m,
            DefaultPhoneCountryCode = "504"
        };

        db.Tenants.Add(tenant);
        tenantContext.SetTenant(tenant.Id);
        await db.SaveChangesAsync(ct);

        return tenant;
    }

    private async Task<List<Branch>> SeedBranchesAsync(CancellationToken ct)
    {
        // El código prefija los correlativos: CEN-000123, SUR-000045, PTO-000012.
        var branches = new List<Branch>
        {
            new()
            {
                Name = "Sucursal Centro",
                Code = "CEN",
                City = "San Pedro Sula",
                Address = "3 calle, entre 8 y 9 avenida",
                Phone = "50490000100"
            },
            new()
            {
                Name = "Sucursal Sur",
                Code = "SUR",
                City = "San Pedro Sula",
                Address = "Boulevard del sur, salida a La Lima",
                Phone = "50490000200"
            },
            new()
            {
                Name = "Sucursal Puerto",
                Code = "PTO",
                City = "Puerto Cortés",
                Address = "Calle principal, contiguo al mercado",
                Phone = "50490000300"
            }
        };

        db.Branches.AddRange(branches);
        await db.SaveChangesAsync(ct);

        return branches;
    }

    private async Task<Staff> SeedStaffAsync(Guid tenantId, List<Branch> branches)
    {
        var (centro, sur, puerto) = (branches[0], branches[1], branches[2]);

        var owner = await CreateUserAsync(
            "dueno@tallerdemo.hn", "Mario Alvarado", AppRoles.Owner, tenantId);

        // El primero cubre las dos sucursales de San Pedro: un técnico puede estar asignado
        // a varias, y en la práctica se mueve entre ellas.
        var tecnico1 = await CreateUserAsync(
            "tecnico1@tallerdemo.hn", "Kevin Discua", AppRoles.Technician, tenantId,
            [centro.Id, sur.Id]);

        // Con tres sucursales y doscientos trabajos, un solo técnico no da la talla y la
        // demostración se vuelve inverosímil.
        var tecnico2 = await CreateUserAsync(
            "tecnico2@tallerdemo.hn", "Nelson Aguilar", AppRoles.Technician, tenantId, [sur.Id]);
        var tecnico3 = await CreateUserAsync(
            "tecnico3@tallerdemo.hn", "Wilmer Castellanos", AppRoles.Technician, tenantId,
            [puerto.Id]);

        return new Staff(owner, new Dictionary<Guid, AppUser[]>
        {
            [centro.Id] = [tecnico1],
            [sur.Id] = [tecnico1, tecnico2],
            [puerto.Id] = [tecnico3]
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
        // Los dos primeros son los clientes con cuenta en la app, los que se enseñan en la
        // demostración. El resto rellena el padrón: nombres, teléfonos y placas con forma
        // hondureña, porque el detalle que delata un dato inventado es siempre la placa.
        (string Name, string Phone, string? Email)[] people =
        [
            ("Ana Lucía Fajardo", "50490001001", "cliente1@tallerdemo.hn"),
            ("Marvin Alexis Portillo", "50490001002", "cliente2@tallerdemo.hn"),
            ("Karla Vanessa Discua", "50490001003", null),
            ("José Ramón Bustillo", "50490001004", null),
            ("Mensajería Rápida Demo", "50490001005", null),
            ("Digna Esperanza Núñez", "50490001006", null),
            ("Elvin Josué Padilla", "50490001007", null),
            ("Sandra Yolany Mejía", "50490001008", null),
            ("Pollos El Buen Sabor Demo", "50490001009", null),
            ("Roberto Antonio Cálix", "50490001010", null),
            ("Gladys Marleny Hernández", "50490001011", null),
            ("Jorge Alberto Ordóñez", "50490001012", null),
        ];

        var customers = people
            .Select(p => new Customer { FullName = p.Name, Phone = p.Phone, Email = p.Email })
            .ToList();

        db.Customers.AddRange(customers);
        await db.SaveChangesAsync(ct);

        await CreateUserAsync("cliente1@tallerdemo.hn", customers[0].FullName, AppRoles.Customer,
            tenantId, customerId: customers[0].Id);
        await CreateUserAsync("cliente2@tallerdemo.hn", customers[1].FullName, AppRoles.Customer,
            tenantId, customerId: customers[1].Id);

        // Marcas y cilindradas del mercado hondureño: lo que de verdad entra a un taller de
        // motos en San Pedro. Las de las dos empresas son flotas de reparto, que es de donde
        // sale el trabajo repetido.
        (int Owner, string Brand, string Model, int Year, string Plate, string Color, int Km)[] fleet =
        [
            (0, "Honda", "CB125F", 2022, "MAB4521", "Rojo", 18400),
            (0, "Yamaha", "Crypton FI", 2023, "MAB9087", "Azul", 9200),
            (1, "Bajaj", "Pulsar NS160", 2021, "MCD7788", "Negro", 31700),
            (1, "Suzuki", "GN125", 2018, "MCD1145", "Negro", 62300),
            (2, "Italika", "FT150", 2022, "MEF1109", "Azul", 15600),
            (3, "Honda", "XR150L", 2020, "MGH2231", "Rojo", 44800),
            (4, "Bajaj", "Boxer CT100", 2023, "MJK5580", "Negro", 27500),
            (4, "Bajaj", "Boxer CT100", 2023, "MJK5581", "Negro", 29100),
            (4, "Honda", "CB125F", 2022, "MJK5582", "Rojo", 33800),
            (5, "Yamaha", "YBR125", 2019, "MLM4407", "Azul", 51200),
            (6, "Keeway", "RKV125", 2021, "MNP7715", "Negro", 24600),
            (7, "Italika", "DT150", 2020, "MQR3348", "Blanco", 38900),
            (8, "Freedom", "Rayo 150", 2023, "MST8812", "Rojo", 12400),
            (8, "Freedom", "Rayo 150", 2023, "MST8813", "Rojo", 14100),
            (9, "TVS", "Apache RTR 160", 2022, "MUV2290", "Negro", 21300),
            (10, "Suzuki", "EN125", 2017, "MWX6634", "Plata", 74500),
            (11, "Serpento", "Storm 150", 2021, "MYZ1178", "Verde", 29800),
        ];

        var vehicles = fleet
            .Select(v => new Vehicle
            {
                CustomerId = customers[v.Owner].Id,
                Type = VehicleType.Motorcycle,
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
        // 250 L/hora en lo corriente y 300 en trabajo de motor: es el orden de magnitud de un
        // taller de motos en San Pedro, y los totales tienen que sonar creíbles.
        (string Code, string Name, decimal Hours, decimal Rate, string Category)[] catalog =
        [
            ("MO-001", "Cambio de aceite y filtro", 0.4m, 250m, "Mantenimiento"),
            ("MO-002", "Servicio de mantenimiento", 1.5m, 250m, "Mantenimiento"),
            ("MO-003", "Ajuste de válvulas", 1.5m, 300m, "Motor"),
            ("MO-004", "Cambio de kit de arrastre", 1m, 250m, "Transmisión"),
            ("MO-005", "Frenos delanteros: pastillas y purga", 0.8m, 250m, "Frenos"),
            ("MO-006", "Frenos traseros: zapatas y ajuste", 0.6m, 250m, "Frenos"),
            ("MO-007", "Cambio de llanta y balanceo", 0.6m, 250m, "Llantas"),
            ("MO-008", "Limpieza y sincronización de carburador", 1.5m, 250m, "Motor"),
            ("MO-009", "Cambio de balineras de dirección", 1.5m, 250m, "Dirección"),
            ("MO-010", "Revisión del sistema eléctrico", 1m, 250m, "Eléctrico"),
            ("MO-011", "Cambio de embrague", 2m, 300m, "Transmisión"),
            ("MO-012", "Reparación de suspensión delantera", 2m, 250m, "Suspensión"),
            ("MO-013", "Diagnóstico general", 0.5m, 250m, "Diagnóstico"),
            ("MO-014", "Cambio de cadena de distribución", 3m, 300m, "Motor"),
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
            ("ACE-4T-20W50", "Aceite 4T 20W50", "Motul", "Lubricantes", "lt", 165m, 260m),
            ("ACE-4T-10W40", "Aceite 4T sintético 10W40", "Motul", "Lubricantes", "lt", 240m, 380m),
            ("ACE-BARRA", "Aceite de barra de suspensión", "Bel-Ray", "Lubricantes", "lt", 145m, 240m),
            ("FIL-ACE", "Filtro de aceite", "Honda", "Filtros", "u", 85m, 150m),
            ("FIL-AIR", "Filtro de aire", "Fram", "Filtros", "u", 130m, 230m),
            ("BUJ-NGK", "Bujía NGK", "NGK", "Encendido", "u", 75m, 135m),
            ("KIT-ARR-125", "Kit de arrastre 125cc", "DID", "Transmisión", "jgo", 780m, 1180m),
            ("KIT-ARR-150", "Kit de arrastre 150cc", "DID", "Transmisión", "jgo", 890m, 1350m),
            ("KIT-CLU", "Kit de discos de embrague", "Ferodo", "Transmisión", "jgo", 520m, 820m),
            ("CAD-DIS", "Cadena de distribución", "DID", "Motor", "u", 320m, 520m),
            ("EMP-CULATA", "Empaque de culata", "Athena", "Motor", "u", 140m, 250m),
            ("CAR-REP", "Kit de reparación de carburador", "Keihin", "Motor", "jgo", 260m, 430m),
            ("PAS-FRE", "Pastillas de freno delanteras", "Galfer", "Frenos", "jgo", 210m, 360m),
            ("ZAP-FRE", "Zapatas de freno traseras", "Galfer", "Frenos", "jgo", 180m, 310m),
            ("DIS-FRE", "Disco de freno delantero", "Galfer", "Frenos", "u", 620m, 950m),
            ("LIQ-DOT4", "Líquido de frenos DOT4", "Bosch", "Frenos", "u", 85m, 150m),
            ("BAT-12V7", "Batería 12V 7Ah", "Yuasa", "Eléctrico", "u", 620m, 950m),
            ("BOM-LED", "Bombillo de faro LED", "Philips", "Eléctrico", "u", 180m, 320m),
            ("FAR-INT", "Foco intermitente", "Philips", "Eléctrico", "u", 85m, 150m),
            ("CAB-EMB", "Cable de embrague", "Venhill", "Controles", "u", 95m, 175m),
            ("CAB-ACE", "Cable de acelerador", "Venhill", "Controles", "u", 90m, 165m),
            ("LLA-DEL", "Llanta delantera 2.75-18", "Kenda", "Llantas", "u", 780m, 1180m),
            ("LLA-TRA", "Llanta trasera 3.00-18", "Kenda", "Llantas", "u", 890m, 1320m),
            ("TUB-LLA", "Tubo de llanta 18\"", "Kenda", "Llantas", "u", 160m, 280m),
            ("RET-BARRA", "Retenedores de barra (par)", "Athena", "Suspensión", "jgo", 210m, 360m),
            ("BAL-DIR", "Balineras de dirección", "SKF", "Dirección", "jgo", 240m, 420m),
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
            // Cuyamel es un local pequeño: lleva alrededor de la mitad de existencia que las
            // dos de San Pedro. Sembrar las tres iguales haría que el inventario por sucursal
            // no dijera nada.
            var scale = branch.Code == "CUY" ? 0.5 : 1.0;

            foreach (var part in world.Parts)
            {
                // Lo barato se compra por docenas y lo caro de a pocos: un taller de motos no
                // tiene diez baterías en la bodega.
                var baseQuantity = part.CostPrice switch
                {
                    < 150m => _rnd.Next(18, 40),
                    < 350m => _rnd.Next(10, 22),
                    < 700m => _rnd.Next(5, 12),
                    _ => _rnd.Next(2, 6)
                };

                var quantity = Math.Max(1, (int)Math.Round(baseQuantity * scale));
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
    ///
    /// El día en curso siempre trabaja, aunque caiga domingo: la demostración se abre el día
    /// que se abre, y enseñarla con todo en cero no le sirve a nadie.
    /// </summary>
    private async Task<int> SeedHistoryAsync(
        World world, DateTimeOffset start, DateTimeOffset today, CancellationToken ct)
    {
        var closed = 0;
        var lastRestock = start;

        for (var day = start; day.Date <= today.Date; day = day.AddDays(1))
        {
            var local = day.ToOffset(LocalOffset);

            if (local.DayOfWeek == DayOfWeek.Sunday && day.Date != today.Date) continue;

            // Una moto entra y sale el mismo día casi siempre, así que el volumen diario es
            // bastante mayor que el de un taller de autos.
            var jobs = local.DayOfWeek == DayOfWeek.Saturday ? _rnd.Next(3, 7) : _rnd.Next(5, 11);

            // El día en curso va a medias: son las entregas de la mañana. Sin esto la tarjeta
            // de "hoy" del tablero abre en cero, que es justo lo que no se quiere enseñar.
            if (day.Date == today.Date) jobs = Math.Max(2, jobs / 2);

            for (var i = 0; i < jobs; i++)
            {
                if (CloseOneJob(world, local, i)) closed++;
            }

            // Y las ventas de mostrador: gente que entra por un filtro o un litro de aceite y
            // se va sin dejar la moto. En un taller de barrio son pan de cada día, y sin ellas
            // la cifra de «solo venta» del reporte sale en cero y parece que la función no
            // sirve para nada.
            for (var i = 0; i < _rnd.Next(0, 4); i++) SellOverTheCounter(world, local, i);

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

    /// <summary>
    /// Las dos sucursales de San Pedro mueven la mayor parte del trabajo; Cuyamel es un
    /// pueblo y factura menos. Repartir por igual haría que el desglose por sucursal del
    /// reporte fuera tres barras idénticas.
    /// </summary>
    private Branch PickBranch(World world)
    {
        var draw = _rnd.Next(100);
        return draw < 40 ? world.Branches[0] : draw < 75 ? world.Branches[1] : world.Branches[2];
    }

    private bool CloseOneJob(World world, DateTimeOffset localDay, int index)
    {
        var branch = PickBranch(world);
        var technicians = world.Staff.Technicians[branch.Id];
        var technician = technicians[_rnd.Next(technicians.Length)];
        var vehicle = world.Vehicles[_rnd.Next(world.Vehicles.Count)];
        var job = Jobs.Pick(_rnd);

        // La hora se decide en el reloj del taller —abre a las ocho— pero se guarda en UTC:
        // Npgsql rechaza cualquier otro desfase en `timestamp with time zone`.
        var opened = localDay.Date.AddHours(8).AddMinutes(index * 35 + _rnd.Next(0, 25));
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

        // Una moto se entrega el mismo día salvo que haya que abrir el motor.
        var hours = job.LaborCodes.Length * 1.5 + _rnd.Next(1, 4);
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
                StartedAt = openedAt.AddHours(0.5),
                CompletedAt = closedAt.AddHours(-0.5),
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

        // Uno de cada cuatro pasó por cotización. En motos casi todo se autoriza de palabra
        // en el mostrador; se cotiza cuando el trabajo es grande y el dueño quiere pensarlo.
        if (_rnd.Next(4) == 0)
            AddQuote(world, branch, vehicle, order, partLines, laborLines, openedAt, QuoteStatus.Approved);

        BillOut(world, branch, vehicle, order, partLines, laborLines, closedAt);
        return true;
    }

    private void Timeline(WorkOrder order, Guid ownerId, Guid technicianId,
        DateTimeOffset openedAt, DateTimeOffset closedAt)
    {
        (WorkOrderStatus? From, WorkOrderStatus To, double Fraction, string Note)[] steps =
        [
            (null, WorkOrderStatus.Received, 0, "Motocicleta recibida en el taller."),
            (WorkOrderStatus.Received, WorkOrderStatus.Diagnosing, 0.15, "En revisión."),
            (WorkOrderStatus.Diagnosing, WorkOrderStatus.InProgress, 0.35, "Trabajo autorizado, manos a la obra."),
            (WorkOrderStatus.InProgress, WorkOrderStatus.Testing, 0.75, "Prueba de ruta."),
            (WorkOrderStatus.Testing, WorkOrderStatus.Ready, 0.9, "Lista para retirar."),
            (WorkOrderStatus.Ready, WorkOrderStatus.Delivered, 1, "Entregada al cliente.")
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
            // Sin ISV, como nace una cotización de verdad: al cotizar nadie sabe todavía si el
            // cliente va a pedir factura con CAI, y el impuesto solo lo lleva esa factura.
            TaxRate = 0m,
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
            // En un taller de motos casi todo se paga en efectivo.
            PaymentMethod = _rnd.Next(10) switch
            {
                < 8 => PaymentMethod.Cash,
                < 9 => PaymentMethod.Card,
                _ => PaymentMethod.Transfer
            },
            // Sin CAI no hay ISV, igual que en el sistema de verdad: estas ventas de la
            // demostración son comprobantes de entrega, no facturas fiscales.
            TaxRate = 0m,
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

        // Una de cada ocho se entrega a crédito: es lo que pasa en un taller de barrio con
        // los clientes de siempre. Sin ninguna, la pantalla de cuentas por cobrar aparece
        // vacía en la demostración y no se entiende para qué está.
        var onCredit = _rnd.Next(8) == 0;

        if (onCredit)
        {
            // Deja algo y queda debiendo. La fecha acordada es a dos semanas: algunas ya
            // vencieron y otras no, que es justo lo que hace útil la lista.
            var down = Math.Round(sale.Total * (_rnd.Next(20, 61) / 100m), 2);

            sale.DueDate = when.AddDays(14);

            if (down > 0)
            {
                sale.Payments.Add(new SalePayment
                {
                    Amount = down,
                    Method = sale.PaymentMethod,
                    PaidAt = when,
                    Notes = "Pago inicial",
                    CreatedAt = when,
                    CreatedByUserId = world.Staff.Owner.Id
                });
            }
        }
        else
        {
            sale.Payments.Add(new SalePayment
            {
                Amount = sale.Total,
                Method = sale.PaymentMethod,
                PaidAt = when,
                CreatedAt = when,
                CreatedByUserId = world.Staff.Owner.Id
            });
        }

        db.Sales.Add(sale);
        order.SaleId = sale.Id;
    }

    /// <summary>
    /// Una venta de mostrador: uno o dos repuestos, pagados de contado y sin orden de trabajo.
    /// </summary>
    /// <remarks>
    /// Va sin CAI —como la mayoría en un taller— y por eso sin ISV, igual que en el sistema.
    /// La mitad quedan a nombre de un cliente del padrón y la otra mitad a nombre de nadie,
    /// que es lo que pasa de verdad: al que compra un empaque no se le abre ficha.
    /// </remarks>
    private void SellOverTheCounter(World world, DateTimeOffset localDay, int index)
    {
        var branch = PickBranch(world);

        var when = new DateTimeOffset(
            localDay.Date.AddHours(9).AddMinutes(index * 97 + _rnd.Next(0, 40)),
            LocalOffset).ToUniversalTime();

        branch.SaleSequence++;
        var sale = new Sale
        {
            BranchId = branch.Id,
            // La mitad sin cliente: el que compra de paso no está en el padrón.
            CustomerId = _rnd.Next(2) == 0
                ? world.Vehicles[_rnd.Next(world.Vehicles.Count)].CustomerId
                : null,
            Number = $"VTA-{branch.Code}-{branch.SaleSequence:D6}",
            SaleDate = when,
            PaymentMethod = _rnd.Next(10) < 8 ? PaymentMethod.Cash : PaymentMethod.Transfer,
            // Sin CAI no hay ISV, igual que en el sistema de verdad.
            TaxRate = 0m,
            CreatedAt = when,
            CreatedByUserId = world.Staff.Owner.Id
        };

        var sequence = 1;
        decimal subtotal = 0, cost = 0;

        foreach (var part in Enumerable.Range(0, _rnd.Next(1, 3))
                     .Select(_ => world.Parts[_rnd.Next(world.Parts.Count)])
                     .Distinct())
        {
            var quantity = _rnd.Next(1, 3);

            // Si no alcanza, no se inventa: la venta se queda con lo que sí había.
            if (!Consume(world, branch.Id, part, quantity, Guid.Empty, when, world.Staff.Owner.Id))
                continue;

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

        if (sale.Lines.Count == 0) return;

        sale.Subtotal = subtotal;
        sale.CostTotal = Math.Round(cost, 2);
        sale.Total = subtotal;

        // De contado, que es como se paga en el mostrador.
        sale.Payments.Add(new SalePayment
        {
            Amount = sale.Total,
            Method = sale.PaymentMethod,
            PaidAt = when,
            CreatedAt = when,
            CreatedByUserId = world.Staff.Owner.Id
        });

        db.Sales.Add(sale);
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
            WorkOrderStatus.Ready,
            WorkOrderStatus.Ready
        ];

        for (var i = 0; i < open.Length; i++)
        {
            // Recorre las tres sucursales por turno para que ninguna quede sin trabajo vivo.
            var branch = world.Branches[i % world.Branches.Count];
            var technicians = world.Staff.Technicians[branch.Id];
            var technician = technicians[_rnd.Next(technicians.Length)];
            var vehicle = world.Vehicles[_rnd.Next(world.Vehicles.Count)];
            var job = Jobs.Pick(_rnd);

            // Escalonadas hacia atrás: las de arriba entraron hoy, las de abajo llevan días.
            // Con dos días de plazo, las tres o cuatro más viejas salen atrasadas y la alerta
            // del tablero tiene contenido; escalonarlas más haría ver al taller como un
            // desastre, que tampoco es lo que se quiere enseñar.
            var openedAt = today.AddDays(-(open.Length - i) * 0.35).AddHours(-2);

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
                Note = $"Motocicleta recibida en {branch.Name}.",
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
                    ChangedAt = openedAt.AddHours(2),
                    ChangedByUserId = technician.Id,
                    Note = job.TechnicianNote,
                    CreatedAt = openedAt.AddHours(2),
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
                        StartedAt = openedAt.AddHours(3),
                        CompletedAt = done ? openedAt.AddHours(5) : null,
                        AssignedTechnicianId = technician.Id,
                        LaborServiceId = service.Id,
                        EstimatedHours = service.StandardHours,
                        ActualHours = done ? service.StandardHours : null,
                        CreatedAt = openedAt.AddHours(2),
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
                    openedAt.AddHours(3), QuoteStatus.Sent);
            }
        }

        // Requerimientos sin atender: la bandeja de entrada del Dueño no puede estar vacía.
        // Los dos primeros son de los clientes con cuenta en la app, para poder enseñar de
        // dónde salió el aviso.
        (int Vehicle, string Reason, string Symptom)[] pending =
        [
            (0, "Servicio de los 20 mil", "Ya toca, y siento que le falta fuerza en subida"),
            (2, "Revisión de frenos", "Chilla al frenar y hay que apretar mucho"),
            (7, "Cambio de llanta trasera", "Está lisa y se me poncha seguido"),
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
        foreach (var sku in new[] { "PAS-FRE", "BAT-12V7" })
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
    /// Los trabajos que de verdad entran a un taller de motos, con su motivo, su diagnóstico y
    /// los repuestos que consumen. Que el motivo, el diagnóstico y las piezas concuerden es lo
    /// que hace que la demostración se lea como un taller y no como relleno.
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
        private static readonly Job[] All =
        [
            new("Servicio de mantenimiento", "Ya van cinco mil kilómetros del último",
                "Mantenimiento de rutina. Filtro de aire saturado por el polvo.",
                "Aceite, filtros y bujía cambiados. Cadena tensada y lubricada.",
                ["MO-001", "MO-002"], ["ACE-4T-20W50", "FIL-ACE", "FIL-AIR", "BUJ-NGK"]),

            new("Cambio de aceite", "Mantenimiento de rutina",
                "Sin novedad. Aceite en su intervalo.",
                "Aceite y filtro cambiados. Revisada la tensión de la cadena y la presión de llantas.",
                ["MO-001"], ["ACE-4T-20W50", "FIL-ACE"]),

            new("Cambio de kit de arrastre", "Salta la cadena al acelerar",
                "Kit de arrastre desgastado, con los dientes en punta.",
                "Kit completo cambiado. Tensión y alineación ajustadas.",
                ["MO-004"], ["KIT-ARR-125"]),

            new("Frenos delanteros", "No frena igual y chilla",
                "Pastillas delanteras al límite y líquido contaminado.",
                "Pastillas cambiadas y purga del freno delantero.",
                ["MO-005"], ["PAS-FRE", "LIQ-DOT4"]),

            new("Frenos traseros", "El pedal se va hasta abajo",
                "Zapatas gastadas y varilla desajustada.",
                "Zapatas cambiadas y freno reajustado.",
                ["MO-006"], ["ZAP-FRE"]),

            new("Cambio de llanta trasera", "Está lisa y se poncha seguido",
                "Llanta trasera sin dibujo y tubo parchado tres veces.",
                "Llanta y tubo nuevos. Balanceada y probada.",
                ["MO-007"], ["LLA-TRA", "TUB-LLA"]),

            new("Cambio de llanta delantera", "Se siente inestable en curva",
                "Llanta delantera con el hombro comido.",
                "Llanta y tubo cambiados. Presión ajustada.",
                ["MO-007"], ["LLA-DEL", "TUB-LLA"]),

            new("Cuesta arrancar", "Se apaga en ralentí y en frío no pega",
                "Carburador sucio y filtro de aire saturado.",
                "Carburador desarmado y limpiado, kit nuevo, ralentí sincronizado.",
                ["MO-008"], ["CAR-REP", "FIL-AIR"]),

            new("Bota aceite la suspensión", "Se ve mojada la barra",
                "Retenedores de barra vencidos.",
                "Retenedores y aceite de barra cambiados. Sin fuga tras la prueba.",
                ["MO-012"], ["RET-BARRA", "ACE-BARRA"]),

            new("No le da marcha", "Amaneció sin nada, ni las luces",
                "Batería sin carga, con dos celdas muertas.",
                "Batería reemplazada. Regulador cargando bien, 14.1 V.",
                ["MO-010"], ["BAT-12V7"]),

            new("Patina el embrague", "En tercera sube revoluciones y no avanza",
                "Discos de embrague quemados.",
                "Discos cambiados y juego del cable ajustado.",
                ["MO-011"], ["KIT-CLU", "CAB-EMB"]),

            new("Cabecea al frenar", "Se siente juego en el manubrio",
                "Balineras de dirección picadas.",
                "Balineras cambiadas y dirección reapretada a torque.",
                ["MO-009"], ["BAL-DIR"]),

            new("Suena el motor arriba", "Un tableteo cuando acelera",
                "Válvulas fuera de luz.",
                "Válvulas ajustadas y empaque de culata cambiado. Ruido eliminado.",
                ["MO-003"], ["EMP-CULATA"]),

            new("Ruido metálico del motor", "Como una cadena suelta adentro",
                "Cadena de distribución estirada.",
                "Cadena de distribución y tensor cambiados.",
                ["MO-014"], ["CAD-DIS"]),

            new("No enciende la luz", "El faro no prende y un direccional tampoco",
                "Bombillo del faro quemado y foco intermitente fundido.",
                "Bombillo LED y foco cambiados. Revisado el arnés, sin falsos contactos.",
                ["MO-010"], ["BOM-LED", "FAR-INT"]),

            new("Se reventó el cable", "El acelerador quedó suelto",
                "Cable de acelerador cortado en la funda.",
                "Cable cambiado y juego ajustado.",
                ["MO-013"], ["CAB-ACE"]),
        ];

        public static Job Pick(Random rnd) => All[rnd.Next(All.Length)];
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

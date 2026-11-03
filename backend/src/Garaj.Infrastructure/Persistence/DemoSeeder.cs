using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
/// <b>Carros y pick-ups.</b> Lo que de verdad entra a un taller en Honduras: Corollas con
/// doscientos mil kilómetros, Hilux de empresa, el Accent de la señora. El catálogo de mano de
/// obra y el de repuestos son los de ese taller —aceite 15W40, pastillas, amortiguadores,
/// rótulas— y no un inventario genérico: en una capacitación, el que mira reconoce su trabajo
/// o no reconoce nada. Para volver a motos hay que cambiar los tres catálogos y
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
    IConfiguration configuration,
    ILogger<DemoSeeder> logger)
{
    /// <summary>
    /// La contraseña con la que nacen los usuarios de la demostración. Por omisión la de
    /// siempre, que sirve para la base local; en un ambiente publicado se pone otra por
    /// <c>Demo__Password</c> y así no queda escrita en un repositorio público.
    /// </summary>
    private string Password => configuration["Demo:Password"] is { Length: > 0 } clave
        ? clave
        : "Garaj123!";

    /// <summary>Honduras no cambia de hora, así que el día del taller es UTC−6 todo el año.</summary>
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(-6);

    // Semilla fija: dos ejecuciones dan el mismo taller. Si en una presentación hay que
    // repetir la siembra, los números que ya se enseñaron siguen siendo los mismos.
    private readonly Random _rnd = new(20260808);

    private readonly List<Notification> _notifications = [];

    /// <param name="wipe">
    /// Vaciar la base entera antes de sembrar. Es lo que hace idempotente a la siembra local,
    /// y es exactamente lo que no se puede hacer donde hay talleres de verdad: borra todos,
    /// no solo el de la demostración. Contra una base con clientes se siembra con
    /// <c>wipe: false</c>, después de haber borrado el taller de la demostración con
    /// <c>delete-tenant</c>, que sí sabe borrar uno solo.
    /// </param>
    public async Task<DemoSeedSummary> RunAsync(
        int weeks = 6, bool wipe = true, CancellationToken ct = default)
    {
        if (weeks is < 1 or > 26)
            throw new AppException("Las semanas de historia deben estar entre 1 y 26.");

        // Fuera de una petición HTTP: sin esto el filtro por tenant escondería todo lo que se
        // acaba de insertar y el borrado no alcanzaría nada.
        tenantContext.BypassTenantFilter = true;

        if (wipe) await WipeAsync(ct);
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

        // Contado por taller y no sobre la base entera: sin vaciado hay otros talleres, y un
        // resumen que sumara los suyos mentiría sobre lo que se acaba de sembrar.
        var summary = new DemoSeedSummary(
            tenant.Name,
            weeks,
            await db.Customers.CountAsync(c => c.TenantId == tenant.Id, ct),
            await db.WorkOrders.CountAsync(w => w.TenantId == tenant.Id, ct),
            closed,
            await db.Quotes.CountAsync(q => q.TenantId == tenant.Id, ct),
            await db.Sales.Where(s => s.TenantId == tenant.Id).SumAsync(s => s.Total, ct),
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
        await db.Claims.ExecuteDeleteAsync(ct);
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
        await db.Appointments.ExecuteDeleteAsync(ct);
        await db.VehicleReceptions.ExecuteDeleteAsync(ct);
        await db.WorkOrders.ExecuteDeleteAsync(ct);
        await db.Vehicles.ExecuteDeleteAsync(ct);
        await db.Equipment.ExecuteDeleteAsync(ct);
        await db.EquipmentTypes.ExecuteDeleteAsync(ct);
        await db.Customers.ExecuteDeleteAsync(ct);
        await db.JobTemplateParts.ExecuteDeleteAsync(ct);
        await db.JobTemplateTasks.ExecuteDeleteAsync(ct);
        await db.JobTemplates.ExecuteDeleteAsync(ct);
        await db.Parts.ExecuteDeleteAsync(ct);
        await db.LaborServices.ExecuteDeleteAsync(ct);
        await db.Expenses.ExecuteDeleteAsync(ct);
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

        // Un encargado de la sucursal del Puerto: es la que está lejos del Dueño, que es
        // justo para lo que sirve el perfil. Entrando con él se ve el taller como lo ve un
        // encargado —solo su sucursal, y sin ajustes ni catálogos—.
        await CreateUserAsync(
            "encargado@tallerdemo.hn", "Sandra Interiano", AppRoles.Manager, tenantId,
            [puerto.Id]);

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

        // Marcas y modelos del parque hondureño: Toyota y Nissan mandan, la pick-up es de
        // trabajo y los kilometrajes son altos porque aquí los carros no se jubilan. La placa
        // delata al dato inventado, así que llevan la forma de verdad: P de particular,
        // C de carga en las de empresa.
        //
        // Los años están repartidos a propósito —de 2013 a 2021— porque un taller no atiende
        // una sola generación, y las dos flotas repiten modelo, que es de donde sale el
        // trabajo repetido que hace ver para qué sirven los trabajos frecuentes.
        (int Owner, string Brand, string Model, int Year, string Plate, string Color, int Km)[] fleet =
        [
            (0, "Toyota", "Corolla", 2018, "PAB4521", "Plata", 128400),
            (0, "Toyota", "Yaris", 2021, "PAB9087", "Blanco", 54200),
            (1, "Nissan", "Frontier", 2016, "PCD7788", "Gris", 187300),
            (1, "Honda", "Civic", 2014, "PCD1145", "Negro", 212600),
            (2, "Hyundai", "Accent", 2019, "PEF1109", "Azul", 96500),
            (3, "Mitsubishi", "L200", 2017, "PGH2231", "Blanco", 164800),
            (4, "Toyota", "Hilux", 2020, "CJK5580", "Blanco", 88500),
            (4, "Toyota", "Hilux", 2020, "CJK5581", "Blanco", 91200),
            (4, "Isuzu", "D-Max", 2019, "CJK5582", "Blanco", 112300),
            (5, "Kia", "Rio", 2018, "PLM4407", "Rojo", 102400),
            (6, "Suzuki", "Swift", 2017, "PNP7715", "Gris", 134900),
            (7, "Nissan", "Sentra", 2015, "PQR3348", "Plata", 198700),
            (8, "Toyota", "Hiace", 2019, "CST8812", "Blanco", 143500),
            (8, "Toyota", "Hiace", 2018, "CST8813", "Blanco", 151800),
            (9, "Honda", "CR-V", 2016, "PUV2290", "Negro", 176200),
            (10, "Mitsubishi", "Montero Sport", 2013, "PWX6634", "Verde", 243100),
            (11, "Hyundai", "Tucson", 2020, "PYZ1178", "Blanco", 71600),
        ];

        var vehicles = fleet
            .Select(v => new Vehicle
            {
                CustomerId = customers[v.Owner].Id,
                Type = VehicleType.Car,
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
        // 350 L/hora en lo corriente y 450 en motor y caja: es el orden de magnitud de un
        // taller de carros en San Pedro, y los totales tienen que sonar creíbles. El código
        // dice de qué es el trabajo —MAN, FRE, SUS— para que el catálogo se pueda leer
        // ordenado, que es como lo va a recorrer quien aprende a usarlo.
        (string Code, string Name, decimal Hours, decimal Rate, string Category)[] catalog =
        [
            ("MAN-001", "Cambio de aceite y filtro", 0.5m, 350m, "Mantenimiento"),
            ("MAN-002", "Servicio mayor de mantenimiento", 2m, 350m, "Mantenimiento"),
            ("MAN-003", "Afinamiento: bujías y filtros", 1.5m, 350m, "Mantenimiento"),
            ("MOT-001", "Cambio de banda de accesorios", 1m, 350m, "Motor"),
            ("MOT-002", "Cambio de banda de tiempo", 6m, 450m, "Motor"),
            ("MOT-003", "Cambio de bomba de agua y refrigerante", 3m, 450m, "Motor"),
            ("MOT-004", "Cambio de radiador", 2.5m, 450m, "Motor"),
            ("FRE-001", "Frenos delanteros: pastillas y rectificado", 1.5m, 350m, "Frenos"),
            ("FRE-002", "Frenos traseros: zapatas y ajuste", 1.5m, 350m, "Frenos"),
            ("SUS-001", "Cambio de amortiguadores (par)", 2m, 350m, "Suspensión"),
            ("SUS-002", "Cambio de rótulas y terminales", 2.5m, 350m, "Dirección"),
            ("SUS-003", "Alineado y balanceo", 1m, 350m, "Llantas"),
            ("TRA-001", "Cambio de embrague", 5m, 450m, "Transmisión"),
            ("ELE-001", "Cambio de batería y revisión de carga", 0.5m, 350m, "Eléctrico"),
            ("ELE-002", "Revisión del sistema eléctrico", 1.5m, 350m, "Eléctrico"),
            ("AIR-001", "Recarga y revisión de aire acondicionado", 1.5m, 350m, "Aire acondicionado"),
            ("DIA-001", "Diagnóstico por escáner", 1m, 450m, "Diagnóstico"),
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
        // Precios de casa de repuestos de San Pedro, en lempiras: el costo es lo que se paga y
        // la venta lo que se cobra, con el margen que de verdad se usa —más grueso en lo
        // barato que se mueve rápido, más fino en lo caro—. Las marcas son las que se piden
        // por nombre en el mostrador.
        (string Sku, string Name, string Brand, string Category, string Unit, decimal Cost, decimal Sale)[] catalog =
        [
            ("ACE-15W40", "Aceite de motor 15W40", "Castrol", "Lubricantes", "lt", 125m, 195m),
            ("ACE-5W30", "Aceite de motor sintético 5W30", "Mobil", "Lubricantes", "lt", 210m, 330m),
            ("ACE-ATF", "Aceite de transmisión automática ATF", "Castrol", "Lubricantes", "lt", 150m, 245m),
            ("REF-PRE", "Refrigerante prediluido", "Prestone", "Lubricantes", "lt", 95m, 165m),
            ("FIL-ACE", "Filtro de aceite", "Fram", "Filtros", "u", 110m, 195m),
            ("FIL-AIR", "Filtro de aire", "Fram", "Filtros", "u", 180m, 310m),
            ("FIL-COM", "Filtro de combustible", "Fram", "Filtros", "u", 220m, 380m),
            ("FIL-CAB", "Filtro de cabina", "Fram", "Filtros", "u", 190m, 330m),
            ("BUJ-NGK", "Bujía NGK", "NGK", "Encendido", "u", 95m, 165m),
            ("BUJ-IRI", "Bujía de iridio", "NGK", "Encendido", "u", 290m, 450m),
            ("BAN-ACC", "Banda de accesorios", "Gates", "Motor", "u", 420m, 680m),
            ("KIT-TIE", "Kit de banda de tiempo", "Gates", "Motor", "jgo", 2400m, 3650m),
            ("BOM-AGU", "Bomba de agua", "Aisin", "Motor", "u", 1450m, 2200m),
            ("RAD-ALU", "Radiador", "Koyo", "Motor", "u", 2800m, 4200m),
            ("MAN-RAD", "Manguera superior de radiador", "Gates", "Motor", "u", 240m, 420m),
            ("EMP-CUL", "Empaque de culata", "Victor Reinz", "Motor", "u", 850m, 1350m),
            ("PAS-FRE-D", "Pastillas de freno delanteras", "Bosch", "Frenos", "jgo", 480m, 780m),
            ("ZAP-FRE-T", "Zapatas de freno traseras", "Bosch", "Frenos", "jgo", 420m, 690m),
            ("DIS-FRE", "Disco de freno delantero", "Brembo", "Frenos", "u", 980m, 1500m),
            ("LIQ-DOT4", "Líquido de frenos DOT4", "Bosch", "Frenos", "u", 95m, 165m),
            ("AMO-DEL", "Amortiguador delantero", "KYB", "Suspensión", "u", 1250m, 1900m),
            ("AMO-TRA", "Amortiguador trasero", "KYB", "Suspensión", "u", 1100m, 1700m),
            ("ROT-INF", "Rótula inferior", "555", "Dirección", "u", 650m, 1050m),
            ("TER-DIR", "Terminal de dirección", "555", "Dirección", "u", 480m, 790m),
            ("BAT-12V", "Batería 12V 65Ah", "Yuasa", "Eléctrico", "u", 2200m, 3200m),
            ("ALT-REC", "Alternador reconstruido", "Bosch", "Eléctrico", "u", 2600m, 3900m),
            ("BOM-FAR", "Bombillo de faro H4", "Philips", "Eléctrico", "u", 160m, 280m),
            ("LLA-185", "Llanta 185/65 R15", "Kenda", "Llantas", "u", 1450m, 2100m),
            ("LLA-265", "Llanta 265/65 R17", "Kenda", "Llantas", "u", 2900m, 4200m),
            ("GAS-134", "Gas refrigerante R134a", "Chemours", "Aire acondicionado", "lb", 180m, 320m),
            ("FIL-SEC", "Filtro secador de aire acondicionado", "Denso", "Aire acondicionado", "u", 620m, 980m),
            ("KIT-EMB", "Kit de embrague", "Exedy", "Transmisión", "jgo", 3800m, 5600m),
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
            // Puerto Cortés es un local pequeño: lleva alrededor de la mitad de existencia
            // que las dos de San Pedro. Sembrar las tres iguales haría que el inventario por
            // sucursal no dijera nada.
            var scale = branch.Code == "PTO" ? 0.5 : 1.0;

            foreach (var part in world.Parts)
            {
                // Lo barato se compra por docenas y lo caro de a pocos: ningún taller tiene
                // diez radiadores en la bodega. Lo que se vende por litro va aparte, porque un
                // carro se lleva cuatro de un golpe: con existencia de repuesto, el aceite se
                // acababa a los tres días y las órdenes salían sin la línea que todo el mundo
                // espera ver.
                var baseQuantity = part.Unit == "lt"
                    ? _rnd.Next(80, 160)
                    : part.CostPrice switch
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

            // Un carro ocupa la rampa más que una moto, así que salen menos trabajos al día:
            // tres bahías y un trabajo de frenos se lleva la mañana entera.
            var jobs = local.DayOfWeek == DayOfWeek.Saturday ? _rnd.Next(2, 5) : _rnd.Next(3, 7);

            // El día en curso va a medias: son las entregas de la mañana. Sin esto la tarjeta
            // de "hoy" del tablero abre en cero, que es justo lo que no se quiere enseñar.
            if (day.Date == today.Date) jobs = Math.Max(2, jobs / 2);

            for (var i = 0; i < jobs; i++)
            {
                if (CloseOneJob(world, local, i)) closed++;
            }

            // Y las ventas de mostrador: gente que entra por un filtro o un litro de aceite y
            // se va sin dejar el carro. En un taller de barrio son pan de cada día, y sin ellas
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

        // Casi todo se entrega el mismo día; lo que toca motor o caja se va a la tarde.
        var hours = job.LaborCodes.Length * 2.0 + _rnd.Next(1, 4);
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

            // La cantidad es la del carro, no la de la línea: el aceite va por litros, las
            // bujías de a cuatro y lo que existe por pares se cambia por pares. Una orden con
            // «1 amortiguador» la descarta de un vistazo cualquiera que sepa del oficio.
            var quantity = sku switch
            {
                "ACE-15W40" or "ACE-5W30" => _rnd.Next(4, 6),
                "ACE-ATF" or "REF-PRE" => _rnd.Next(2, 5),
                "BUJ-NGK" or "BUJ-IRI" => 4,
                "LLA-185" or "LLA-265" => 2,
                "AMO-DEL" or "AMO-TRA" => 2,
                "ROT-INF" or "TER-DIR" => 2,
                "GAS-134" => _rnd.Next(2, 4),
                _ => 1
            };

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

        // Uno de cada tres pasó por cotización. En carros el trabajo grande —embrague, banda
        // de tiempo— no se autoriza de palabra: el cliente quiere el número por escrito.
        //
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
            // Todavía manda el efectivo, pero en carros la tarjeta pesa más que en motos:
            // los montos son otros y nadie anda cinco mil lempiras en la bolsa.
            PaymentMethod = _rnd.Next(10) switch
            {
                < 6 => PaymentMethod.Cash,
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

                // Hay trabajo que no lleva repuesto —un alineado, un diagnóstico— y entonces
                // la cotización va de pura mano de obra, que también hay que poder enseñar.
                var partLines = job.PartSkus.Length > 0
                    ? new List<(Part, decimal)> { (world.Parts.First(p => p.Sku == job.PartSkus[0]), 1) }
                    : [];

                AddQuote(world, branch, vehicle, order,
                    partLines,
                    [(service, service.StandardHours, service.StandardHours * service.HourlyRate)],
                    openedAt.AddHours(3), QuoteStatus.Sent);
            }
        }

        // Requerimientos sin atender: la bandeja de entrada del Dueño no puede estar vacía.
        // Los dos primeros son de los clientes con cuenta en la app, para poder enseñar de
        // dónde salió el aviso.
        (int Vehicle, string Reason, string Symptom)[] pending =
        [
            (0, "Servicio de los 130 mil", "Ya toca, y siento que le falta fuerza en subida"),
            (2, "Revisión de frenos", "Chilla al frenar y hay que pisar hasta el fondo"),
            (7, "No enfría el aire", "Sopla, pero sale caliente desde la semana pasada"),
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

        // Citas: una pedida por el enlace que espera respuesta, una confirmada para hoy y una
        // que pidió desde la app un cliente con cuenta. Sin esto, la agenda abre vacía y no se
        // entiende de qué va la pantalla.
        var manana = DateOnly.FromDateTime(today.UtcDateTime.AddHours(-6)).AddDays(1);
        var hoyDia = DateOnly.FromDateTime(today.UtcDateTime.AddHours(-6));

        db.Appointments.AddRange(
            new Appointment
            {
                BranchId = world.Branches[0].Id,
                ContactName = "Marvin Zelaya",
                ContactPhone = "50499112233",
                VehicleNote = "Hilux 2016, placa HAE4455",
                Reason = "Hace un ruido al frenar y quiere saber cuánto cuesta",
                Day = manana,
                Slot = AppointmentSlot.Morning,
                Source = AppointmentSource.PublicLink,
                Status = AppointmentStatus.Requested,
                CreatedAt = today.AddHours(-3)
            },
            new Appointment
            {
                BranchId = world.Branches[0].Id,
                CustomerId = world.Vehicles[0].CustomerId,
                VehicleId = world.Vehicles[0].Id,
                ContactName = "Cita desde la app",
                ContactPhone = "50498881111",
                Reason = "Servicio de los 40 mil",
                Day = hoyDia,
                Slot = AppointmentSlot.Afternoon,
                Source = AppointmentSource.CustomerApp,
                Status = AppointmentStatus.Confirmed,
                ConfirmedAt = today.AddHours(-20),
                CreatedAt = today.AddDays(-2)
            },
            new Appointment
            {
                BranchId = world.Branches[world.Branches.Count - 1].Id,
                ContactName = "Doña Rosa",
                ContactPhone = "50497776655",
                VehicleNote = "Un Yaris blanco, no sé el año",
                Reason = "Cambio de aceite",
                Day = manana.AddDays(2),
                Slot = AppointmentSlot.Morning,
                Source = AppointmentSource.PublicLink,
                Status = AppointmentStatus.Requested,
                CreatedAt = today.AddHours(-1)
            });

        // Dos repuestos por debajo del mínimo, para que la alerta del tablero tenga contenido
        // real y no haya que provocarla a mano durante la presentación.
        foreach (var sku in new[] { "PAS-FRE-D", "BAT-12V" })
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
        string[] PartSkus,
        /// <summary>
        /// Cuánto se repite este trabajo. Sin peso, el sembrador sacaba tantos cambios de
        /// banda de tiempo como cambios de aceite, y un taller así no existe: el reporte
        /// quedaba plano y el catálogo de trabajos frecuentes no se entendía para qué sirve.
        /// </summary>
        int Weight);

    private static class Jobs
    {
        /// <summary>
        /// El trabajo como llega: lo que dijo el cliente, lo que encontró el técnico y lo que
        /// se hizo. Los tres textos importan —son los que se leen en la orden, en la
        /// cotización y en el historial del vehículo— y por eso están escritos como los
        /// escribe un taller y no como los escribiría un catálogo.
        /// </summary>
        private static readonly Job[] All =
        [
            new("Servicio de mantenimiento", "Ya toca el servicio",
                "Mantenimiento de rutina. Filtro de aire saturado por el polvo de la calle.",
                "Aceite y filtros cambiados. Revisados niveles, frenos y presión de llantas.",
                ["MAN-001", "MAN-002"], ["ACE-15W40", "FIL-ACE", "FIL-AIR", "FIL-CAB"], 10),

            new("Cambio de aceite", "Mantenimiento de rutina",
                "Sin novedad. Aceite en su intervalo.",
                "Aceite y filtro cambiados. Revisados niveles y presión de llantas.",
                ["MAN-001"], ["ACE-15W40", "FIL-ACE"], 12),

            new("Afinamiento", "Le falta fuerza en subida y gasta más",
                "Bujías gastadas y filtro de combustible sucio.",
                "Bujías y filtros cambiados. Probado en ruta, recuperó respuesta.",
                ["MAN-003"], ["BUJ-NGK", "FIL-COM", "FIL-AIR"], 6),

            new("Frenos delanteros", "Chilla al frenar y vibra el volante",
                "Pastillas delanteras al límite y discos con labio.",
                "Pastillas cambiadas y discos rectificados. Purga del sistema.",
                ["FRE-001"], ["PAS-FRE-D", "LIQ-DOT4"], 8),

            new("Frenos traseros", "El pedal se va hasta el fondo",
                "Zapatas gastadas y freno de mano desajustado.",
                "Zapatas cambiadas, tambores limpiados y freno de mano reajustado.",
                ["FRE-002"], ["ZAP-FRE-T"], 6),

            new("Cambio de discos y pastillas", "Se siente que no agarra al frenar fuerte",
                "Discos delanteros por debajo del espesor mínimo.",
                "Discos y pastillas nuevos. Probado en ruta, frena derecho.",
                ["FRE-001"], ["DIS-FRE", "PAS-FRE-D"], 4),

            new("Suena la suspensión", "Golpetea en los topes y se siente flojo",
                "Amortiguadores delanteros vencidos, con fuga de aceite.",
                "Amortiguadores delanteros cambiados. Alineado después del trabajo.",
                ["SUS-001", "SUS-003"], ["AMO-DEL"], 5),

            new("Se come las llantas", "Las delanteras se gastan por dentro",
                "Rótulas con juego y alineación fuera de especificación.",
                "Rótulas y terminales cambiados, alineado y balanceado.",
                ["SUS-002", "SUS-003"], ["ROT-INF", "TER-DIR"], 4),

            new("Alineado y balanceo", "Se va a la derecha y vibra en carretera",
                "Alineación fuera de especificación y una llanta desbalanceada.",
                "Alineado y balanceado. Probado en ruta, va derecho.",
                ["SUS-003"], [], 6),

            new("Cambio de llantas", "Están lisas y se me ponchan",
                "Dos llantas delanteras sin dibujo.",
                "Llantas nuevas, balanceadas, y alineado.",
                ["SUS-003"], ["LLA-185"], 5),

            new("Se calienta", "Sube la aguja en el tráfico",
                "Bomba de agua con fuga por el sello y manguera superior reventada.",
                "Bomba de agua y manguera cambiadas, sistema purgado. Sin fuga tras la prueba.",
                ["MOT-003"], ["BOM-AGU", "REF-PRE", "MAN-RAD"], 4),

            new("Bota agua", "Amanece un charco verde debajo",
                "Radiador picado por los panales.",
                "Radiador cambiado y sistema purgado. Presión probada, sin fuga.",
                ["MOT-004"], ["RAD-ALU", "REF-PRE"], 2),

            new("Chilla la banda", "Un chillido al arrancar en frío",
                "Banda de accesorios cuarteada y floja.",
                "Banda cambiada y tensor revisado. Chillido eliminado.",
                ["MOT-001"], ["BAN-ACC"], 4),

            new("Cambio de banda de tiempo", "Me dijeron que ya toca por kilometraje",
                "Banda de tiempo en el límite de su intervalo.",
                "Kit de banda de tiempo cambiado con bomba de agua. Puesto a tiempo y probado.",
                ["MOT-002", "MOT-003"], ["KIT-TIE", "BOM-AGU", "REF-PRE"], 2),

            new("No le da marcha", "Amaneció sin nada, ni las luces",
                "Batería sin carga, con dos celdas muertas.",
                "Batería reemplazada. Alternador cargando bien, 14.2 V.",
                ["ELE-001"], ["BAT-12V"], 5),

            new("Se descarga la batería", "Cada dos días hay que darle corriente",
                "Alternador no carga: 12.1 V con el motor andando.",
                "Alternador reconstruido instalado. Carga estable a 14.1 V.",
                ["ELE-002"], ["ALT-REC"], 3),

            new("No enfría el aire", "Sopla, pero sale caliente",
                "Sistema de aire sin gas y filtro secador saturado.",
                "Filtro secador cambiado, sistema al vacío y cargado. Enfría a 7 °C en salida.",
                ["AIR-001"], ["GAS-134", "FIL-SEC"], 5),

            new("Patina el embrague", "En cuarta sube revoluciones y no avanza",
                "Disco de embrague quemado.",
                "Kit de embrague cambiado y volante rectificado. Probado en ruta.",
                ["TRA-001"], ["KIT-EMB"], 2),

            new("Luz del motor encendida", "Se prendió el chequeo y no se apaga",
                "Falla por encendido en el cilindro 3: bujía en mal estado.",
                "Bujías cambiadas, código borrado. Probado en ruta, no volvió a encender.",
                ["DIA-001", "MAN-003"], ["BUJ-IRI"], 5),

            new("No enciende el faro", "Un faro no prende de noche",
                "Bombillo del faro derecho quemado.",
                "Bombillo cambiado y revisado el arnés, sin falsos contactos.",
                ["ELE-002"], ["BOM-FAR"], 3),
        ];

        private static readonly int Total = All.Sum(j => j.Weight);

        /// <summary>Sorteo con peso: sale más lo que más entra al taller.</summary>
        public static Job Pick(Random rnd)
        {
            var punto = rnd.Next(Total);

            foreach (var job in All)
            {
                punto -= job.Weight;
                if (punto < 0) return job;
            }

            return All[^1];
        }
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

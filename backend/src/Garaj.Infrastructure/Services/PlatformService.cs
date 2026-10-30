using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Platform;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Domain.Rules;
using Garaj.Infrastructure.Identity;
using Garaj.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Nuestro lado del negocio: dar de alta talleres y cobrarles la mensualidad.
/// </summary>
/// <remarks>
/// Todo lo que hay aquí toca <c>Tenant</c> y <c>SubscriptionPayment</c>, y nada más. No es una
/// coincidencia: el usuario de plataforma no tiene taller, así que el global query filter le
/// devuelve cero filas de órdenes, clientes e inventario aunque alguien escribiera la consulta.
/// La llave maestra abre la puerta del cobro, no la del taller.
/// </remarks>
public class PlatformService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    TenantProvisioner provisioner,
    UserManager<AppUser> userManager) : IPlatformService
{
    public async Task<IReadOnlyList<PlatformTenantDto>> ListAsync(CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var tenants = await db.Tenants.AsNoTracking().ToListAsync(ct);

        var branchCounts = await db.Branches
            .IgnoreQueryFilters()
            .GroupBy(b => b.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var lastPayments = await db.SubscriptionPayments
            .GroupBy(p => p.TenantId)
            .Select(g => new { TenantId = g.Key, PaidOn = g.Max(p => p.PaidOn) })
            .ToDictionaryAsync(x => x.TenantId, x => x.PaidOn, ct);

        var today = clock.Today();

        return tenants
            .Select(t => Map(
                t,
                branchCounts.GetValueOrDefault(t.Id),
                lastPayments.TryGetValue(t.Id, out var paidOn) ? paidOn : null,
                today))
            // Lo que vence primero, arriba: es la lista de a quién hay que cobrarle.
            .OrderBy(t => t.PaidThrough ?? DateOnly.MaxValue)
            .ThenBy(t => t.Name)
            .ToList();
    }

    public async Task<PlatformTenantDetailDto> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();
        return await BuildDetailAsync(await FindAsync(tenantId, ct), ct);
    }

    public async Task<CreatedTenantDto> CreateTenantAsync(
        CreateTenantRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsurePlatform();

        TenantProvisioner.Result result;

        try
        {
            // El mismo camino que el comando de consola, no una copia: dar de alta un taller es
            // la operación más difícil de deshacer del sistema y no debe tener dos versiones.
            result = await provisioner.RunAsync(
                new TenantProvisioner.Request(
                    request.Name,
                    request.OwnerEmail,
                    request.OwnerName,
                    request.BranchName,
                    request.BranchCode,
                    request.City,
                    request.Address,
                    request.LegalName,
                    request.TaxId,
                    request.Phone,
                    request.Email,
                    // Si viene vacía, el provisioner genera una y la devuelve, como siempre.
                    Password: request.Password,
                    PlanName: request.PlanName,
                    MonthlyFee: request.MonthlyFee,
                    PaidThrough: request.PaidThrough,
                    GraceDays: request.GraceDays,
                    WorksWith: request.WorksWith),
                ct);
        }
        finally
        {
            // El provisioner deja el contexto apuntando al taller recién creado y con el filtro
            // desactivado —lo necesita para verse a sí mismo—. Restaurarlo evita que lo que
            // quede de la petición escriba o lea como si fuéramos ese taller.
            tenantContext.Initialize(null, scope.UserId, scope.Role, [], null);
            tenantContext.BypassTenantFilter = false;
        }

        return new CreatedTenantDto(result.TenantId, result.BranchId, result.OwnerEmail, result.Password);
    }

    public async Task<CreatedTenantDto> CreateOwnerAsync(
        Guid tenantId, CreateOwnerRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var tenant = await FindAsync(tenantId, ct);

        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        var nombre = (request.FullName ?? "").Trim();

        if (email.Length == 0 || nombre.Length == 0)
            throw new AppException("Hacen falta el correo y el nombre del Dueño.");

        if (await userManager.FindByEmailAsync(email) is not null)
            throw new ConflictException($"El correo {email} ya tiene usuario.");

        var owner = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = nombre,
            TenantId = tenant.Id,
            CreatedAt = clock.UtcNow
        };

        var password = string.IsNullOrWhiteSpace(request.Password)
            ? TenantProvisioner.GeneratePassword()
            : request.Password;

        var created = await userManager.CreateAsync(owner, password);
        if (!created.Succeeded)
            throw new AppException(
                $"No se pudo crear el Dueño: {string.Join("; ", created.Errors.Select(e => e.Description))}");

        await userManager.AddToRoleAsync(owner, AppRoles.Owner);

        // El Dueño no se asigna a sucursales: las ve todas por rol.
        return new CreatedTenantDto(tenant.Id, Guid.Empty, email, password);
    }

    public async Task<PlatformTenantDetailDto> RegisterPaymentAsync(
        Guid tenantId, RegisterPaymentRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        if (request.Amount <= 0)
            throw new AppException("El monto del pago debe ser mayor que cero.");

        var tenant = await FindAsync(tenantId, ct);
        var today = clock.Today();
        var paidOn = request.PaidOn ?? today;

        // El número de depósito identifica el pago en el banco: si ya está registrado, es el
        // mismo dinero. Sin esto, el doble clic —o el reintento cuando se cae el internet—
        // corre la fecha dos meses y quema dos números del CAI.
        var referencia = Trim(request.Reference);

        if (referencia is not null && await db.SubscriptionPayments
                .AnyAsync(p => p.TenantId == tenant.Id && p.Reference == referencia && !p.IsVoided, ct))
        {
            throw new ConflictException(
                $"El pago con referencia «{referencia}» ya está registrado para este taller.");
        }
        var months = Math.Clamp(request.Months, 1, 24);

        // Se corre desde donde estaba, no desde hoy: quien paga con una semana de atraso no
        // pierde esa semana. Si arrastra varios meses, la fecha resultante sigue en el pasado
        // —y el taller sigue bloqueado—, que es exactamente lo que corresponde.
        var desde = tenant.PaidThrough ?? paidOn;
        var coversThrough = desde.AddMonths(months);

        var payment = new SubscriptionPayment
        {
            TenantId = tenant.Id,
            PaidOn = paidOn,
            Amount = request.Amount,
            Currency = tenant.Currency,
            Method = Trim(request.Method),
            Reference = referencia,
            CoversThrough = coversThrough,
            // Desde dónde se corrió: es lo que deja deshacerlo sin adivinar.
            PreviousPaidThrough = tenant.PaidThrough,
            Note = Trim(request.Note),
            RegisteredByUserId = tenantContext.UserId
        };

        db.SubscriptionPayments.Add(payment);

        if (request.Invoice)
            await EmitirAsync(tenant, payment, request, paidOn, ct);

        tenant.PaidThrough = coversThrough;

        // El acuerdo era para llegar hasta el pago. Ya llegó.
        tenant.UnblockedThrough = null;
        tenant.UnblockNote = null;

        await db.SaveChangesAsync(ct);

        return await BuildDetailAsync(tenant, ct);
    }

    public async Task<PlatformTenantDto> SetAgreementAsync(
        Guid tenantId, PaymentAgreementRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var today = clock.Today();

        if (request.UnblockedThrough < today)
            throw new AppException("El acuerdo de pago tiene que ser hasta hoy o más adelante.");

        var tenant = await FindAsync(tenantId, ct);
        tenant.UnblockedThrough = request.UnblockedThrough;
        tenant.UnblockNote = Trim(request.Note);

        await db.SaveChangesAsync(ct);

        return await MapAsync(tenant, ct);
    }

    public async Task<PlatformTenantDto> ClearAgreementAsync(Guid tenantId, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var tenant = await FindAsync(tenantId, ct);
        tenant.UnblockedThrough = null;
        tenant.UnblockNote = null;

        await db.SaveChangesAsync(ct);

        return await MapAsync(tenant, ct);
    }

    public async Task<PlatformTenantDto> UpdateTenantAsync(
        Guid tenantId, UpdateTenantRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new AppException("El taller necesita un nombre.");

        var tenant = await FindAsync(tenantId, ct);

        tenant.Name = request.Name.Trim();
        tenant.LegalName = Trim(request.LegalName);
        tenant.TaxId = Trim(request.TaxId);
        tenant.Phone = Trim(request.Phone);
        tenant.Email = Trim(request.Email);
        tenant.Address = Trim(request.Address);

        await db.SaveChangesAsync(ct);

        return await MapAsync(tenant, ct);
    }

    public async Task<PlatformTenantDto> UpdateSubscriptionAsync(
        Guid tenantId, UpdateSubscriptionRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var tenant = await FindAsync(tenantId, ct);
        tenant.PlanName = Trim(request.PlanName);
        tenant.MonthlyFee = request.MonthlyFee;
        tenant.PaidThrough = request.PaidThrough;
        tenant.GraceDays = NormalizeGraceDays(request.GraceDays);

        await db.SaveChangesAsync(ct);

        return await MapAsync(tenant, ct);
    }

    public async Task<PlatformTenantDto> SetWorksWithAsync(
        Guid tenantId, WorkshopKind worksWith, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var tenant = await FindAsync(tenantId, ct);

        if (tenant.WorksWith != worksWith)
        {
            tenant.WorksWith = worksWith;
            await db.SaveChangesAsync(ct);

            // Para sembrar las categorías hay que mirar las del taller, y el filtro por tenant
            // va por la identidad de la petición, que aquí es la nuestra.
            if (worksWith == WorkshopKind.Equipment)
            {
                var antes = tenantContext.TenantId;
                tenantContext.SetTenant(tenantId);

                try
                {
                    await EquipmentTypeSeeder.EnsureAsync(db, ct);
                }
                finally
                {
                    if (antes is { } propio) tenantContext.SetTenant(propio);
                }
            }
        }

        return await MapAsync(tenant, ct);
    }

    public async Task<PlatformTenantDto> SetActiveAsync(
        Guid tenantId, bool active, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        var tenant = await FindAsync(tenantId, ct);
        tenant.IsActive = active;

        await db.SaveChangesAsync(ct);

        return await MapAsync(tenant, ct);
    }

    private async Task<Tenant> FindAsync(Guid tenantId, CancellationToken ct) =>
        await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
        ?? throw new NotFoundException("El taller no existe.");

    public async Task<PlatformTenantDetailDto> VoidPaymentAsync(
        Guid tenantId, Guid paymentId, VoidPaymentRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new AppException("Diga por qué se anula el pago.");

        var tenant = await FindAsync(tenantId, ct);

        var pago = await db.SubscriptionPayments
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.TenantId == tenantId, ct)
            ?? throw new NotFoundException("El pago no existe.");

        if (pago.IsVoided) throw new ConflictException("Ese pago ya está anulado.");

        pago.IsVoided = true;
        pago.VoidReason = request.Reason.Trim();
        pago.VoidedAt = clock.UtcNow;

        // El documento se va con él: cobrarle al taller por un pago que no existió es
        // exactamente lo que el SAR viene a preguntar.
        var factura = await db.SubscriptionInvoices
            .FirstOrDefaultAsync(f => f.SubscriptionPaymentId == pago.Id, ct);

        if (factura is not null && !factura.IsVoided)
        {
            factura.IsVoided = true;
            factura.VoidReason = $"Pago anulado: {pago.VoidReason}";
            factura.VoidedAt = clock.UtcNow;
        }

        // Devolver el vencimiento solo si este pago es el que lo dejó donde está. Con pagos
        // posteriores encima, restaurar la fecha vieja le quitaría al taller meses que sí
        // pagó; ahí se anula el pago y la fecha se corrige a mano, que es lo que hay.
        var hayPosteriores = await db.SubscriptionPayments
            .AnyAsync(p => p.TenantId == tenantId && !p.IsVoided && p.Id != pago.Id
                && p.CreatedAt > pago.CreatedAt, ct);

        // Y solo si sabemos desde dónde se corrió: en un pago viejo —de antes de que esto se
        // guardara— poner null diría «nunca pagó» y le cortaría el sistema al taller.
        if (!hayPosteriores
            && tenant.PaidThrough == pago.CoversThrough
            && pago.PreviousPaidThrough is { } anterior)
        {
            tenant.PaidThrough = anterior;
        }

        await db.SaveChangesAsync(ct);

        return await BuildDetailAsync(tenant, ct);
    }

    /// <summary>
    /// Arma el documento del pago: los conceptos, el impuesto y el número.
    /// </summary>
    /// <remarks>
    /// Va en la misma transacción que el pago a propósito. Un pago sin su documento se queda
    /// esperando a que alguien se acuerde, y un documento sin su pago es un número quemado.
    ///
    /// El monto que se recibió manda sobre todo lo demás: si no se escriben conceptos, se
    /// factura uno solo por lo que entró, con el impuesto sacado hacia atrás o hacia adelante
    /// según cómo cobremos. Así el documento siempre cuadra con el depósito del banco.
    /// </remarks>
    private async Task EmitirAsync(
        Tenant tenant, SubscriptionPayment payment, RegisterPaymentRequest request,
        DateOnly paidOn, CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);
        var tasa = settings.DefaultTaxRate;
        var conImpuestoAdentro = settings.PricesIncludeTax;

        var lineas = (request.Lines ?? []).Where(l => !string.IsNullOrWhiteSpace(l.Description)).ToList();

        // Sin conceptos escritos: una línea por lo que entró. El neto depende de cómo
        // cobremos —con el ISV adentro el total ya es el neto más el impuesto; con el ISV
        // aparte hay que sacarlo del monto recibido para no cobrarlo dos veces—.
        if (lineas.Count == 0)
        {
            var neto = tasa <= 0 || conImpuestoAdentro
                ? request.Amount
                : Math.Round(request.Amount / (1 + tasa / 100), 2, MidpointRounding.AwayFromZero);

            var meses = Math.Clamp(request.Months, 1, 24);

            lineas =
            [
                new SaveInvoiceLineRequest(
                    meses == 1 ? "Mensualidad GarajApp" : $"Mensualidad GarajApp ({meses} meses)",
                    1, neto)
            ];
        }

        var suma = lineas.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
        var (total, impuesto) = Isv.Totales(suma, tasa, conImpuestoAdentro);

        var factura = new SubscriptionInvoice
        {
            TenantId = tenant.Id,
            SubscriptionPaymentId = payment.Id,
            IssuedOn = paidOn,
            // Copiados, no leídos por relación: el RTN que corrija el taller el mes que viene
            // no puede reescribir el documento de este mes.
            CustomerName = tenant.Name,
            CustomerLegalName = tenant.LegalName,
            CustomerTaxId = tenant.TaxId,
            Subtotal = suma,
            TaxRate = tasa,
            TaxTotal = impuesto,
            Total = total,
            PricesIncludedTax = conImpuestoAdentro,
            Currency = settings.Currency,
            Note = Trim(request.Note),
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
            ? await NumeroAutorizadoAsync(factura, ct)
            : NumeroPropio(settings);

        db.SubscriptionInvoices.Add(factura);
    }

    /// <summary>
    /// Toma el siguiente número del rango autorizado y lo avanza. Se niega —en vez de emitir
    /// un comprobante por su cuenta— cuando no hay rango, venció o se acabó: quien pidió
    /// factura con CAI tiene que enterarse ahí, no al imprimir.
    /// </summary>
    private async Task<string> NumeroAutorizadoAsync(SubscriptionInvoice factura, CancellationToken ct)
    {
        var rango = await db.PlatformFiscalRanges
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new AppException(
                "Todavía no hay un CAI registrado. Regístrelo en Ajustes de facturación, o "
                + "emita el comprobante sin CAI.");

        if (rango.IssueDeadline < clock.UtcNow)
            throw new AppException("El CAI registrado venció. Registre el rango nuevo del SAR.");

        if (rango.NextNumber > rango.RangeEnd)
            throw new AppException("Se agotó el rango autorizado. Registre el rango nuevo del SAR.");

        var numero = rango.Format(rango.NextNumber);

        factura.PlatformFiscalRangeId = rango.Id;
        factura.FiscalCai = rango.Cai;
        factura.FiscalRangeText = rango.RangeText;
        factura.FiscalIssueDeadline = rango.IssueDeadline;

        rango.NextNumber++;

        return numero;
    }

    /// <summary>El correlativo nuestro, el del comprobante sin CAI: «C-000007».</summary>
    private static string NumeroPropio(PlatformSettings settings)
    {
        var numero = $"{settings.InvoicePrefix}-{settings.NextInvoiceNumber:D6}";
        settings.NextInvoiceNumber++;
        return numero;
    }

    /// <summary>
    /// Nuestros datos de emisor. La fila nace sola la primera vez que se necesita: pedirle al
    /// instalador que la cree antes de poder cobrar es una trampa para el primer día.
    /// </summary>
    private async Task<PlatformSettings> SettingsAsync(CancellationToken ct)
    {
        var settings = await db.PlatformSettings.FirstOrDefaultAsync(ct);
        if (settings is not null) return settings;

        settings = new PlatformSettings();
        db.PlatformSettings.Add(settings);

        return settings;
    }

    private async Task<PlatformTenantDetailDto> BuildDetailAsync(Tenant tenant, CancellationToken ct)
    {
        // El documento se trae con el pago, de un solo viaje: la fila del historial enseña su
        // número y el botón para bajarlo, y sin esto haría falta una consulta por fila.
        var payments = await db.SubscriptionPayments
            .AsNoTracking()
            .Where(p => p.TenantId == tenant.Id)
            .OrderByDescending(p => p.PaidOn)
            .ThenByDescending(p => p.CreatedAt)
            .GroupJoin(
                db.SubscriptionInvoices.AsNoTracking(),
                p => p.Id,
                f => f.SubscriptionPaymentId,
                (p, facturas) => new { Pago = p, Factura = facturas.FirstOrDefault() })
            .Select(x => new SubscriptionPaymentDto(
                x.Pago.Id, x.Pago.PaidOn, x.Pago.Amount, x.Pago.Currency, x.Pago.Method,
                x.Pago.Reference, x.Pago.CoversThrough, x.Pago.Note, x.Pago.CreatedAt,
                x.Factura == null ? null : x.Factura.Id,
                x.Factura == null ? null : x.Factura.Number,
                x.Factura != null && x.Factura.FiscalCai != null,
                x.Factura != null && x.Factura.IsVoided,
                x.Pago.IsVoided, x.Pago.VoidReason))
            .ToListAsync(ct);

        return new PlatformTenantDetailDto(
            Map(tenant, await BranchCountAsync(tenant.Id, ct), payments.FirstOrDefault()?.PaidOn, clock.Today()),
            payments);
    }

    private async Task<PlatformTenantDto> MapAsync(Tenant tenant, CancellationToken ct)
    {
        var lastPayment = await db.SubscriptionPayments
            .Where(p => p.TenantId == tenant.Id && !p.IsVoided)
            .OrderByDescending(p => p.PaidOn)
            .Select(p => (DateOnly?)p.PaidOn)
            .FirstOrDefaultAsync(ct);

        return Map(tenant, await BranchCountAsync(tenant.Id, ct), lastPayment, clock.Today());
    }

    private async Task<int> BranchCountAsync(Guid tenantId, CancellationToken ct) =>
        await db.Branches.IgnoreQueryFilters().CountAsync(b => b.TenantId == tenantId, ct);

    private static PlatformTenantDto Map(
        Tenant tenant, int branchCount, DateOnly? lastPaymentOn, DateOnly today)
    {
        var status = SubscriptionRules.For(tenant, today);

        return new PlatformTenantDto(
            tenant.Id,
            tenant.Name,
            tenant.LegalName,
            tenant.Phone,
            tenant.Email,
            tenant.PlanName,
            tenant.MonthlyFee,
            tenant.Currency,
            tenant.PaidThrough,
            tenant.GraceDays,
            tenant.UnblockedThrough,
            tenant.UnblockNote,
            tenant.IsActive,
            status.State.ToString(),
            status.DaysLeft,
            status.ReadOnlyOn,
            lastPaymentOn,
            branchCount,
            tenant.CreatedAt,
            tenant.WorksWith,
            tenant.TaxId,
            tenant.Address);
    }

    /// <summary>Sin tope los días de gracia se vuelven «gratis para siempre» por un cero de más.</summary>
    private static int NormalizeGraceDays(int days) => Math.Clamp(days, 0, 60);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

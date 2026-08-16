using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Platform;
using Garaj.Domain.Entities;
using Garaj.Domain.Rules;
using Garaj.Infrastructure.Persistence;
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
    TenantProvisioner provisioner) : IPlatformService
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
                    GraceDays: request.GraceDays),
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

    public async Task<PlatformTenantDetailDto> RegisterPaymentAsync(
        Guid tenantId, RegisterPaymentRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsurePlatform();

        if (request.Amount <= 0)
            throw new AppException("El monto del pago debe ser mayor que cero.");

        var tenant = await FindAsync(tenantId, ct);
        var today = clock.Today();
        var paidOn = request.PaidOn ?? today;
        var months = Math.Clamp(request.Months, 1, 24);

        // Se corre desde donde estaba, no desde hoy: quien paga con una semana de atraso no
        // pierde esa semana. Si arrastra varios meses, la fecha resultante sigue en el pasado
        // —y el taller sigue bloqueado—, que es exactamente lo que corresponde.
        var desde = tenant.PaidThrough ?? paidOn;
        var coversThrough = desde.AddMonths(months);

        db.SubscriptionPayments.Add(new SubscriptionPayment
        {
            TenantId = tenant.Id,
            PaidOn = paidOn,
            Amount = request.Amount,
            Currency = tenant.Currency,
            Method = Trim(request.Method),
            Reference = Trim(request.Reference),
            CoversThrough = coversThrough,
            Note = Trim(request.Note),
            RegisteredByUserId = tenantContext.UserId
        });

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

    private async Task<PlatformTenantDetailDto> BuildDetailAsync(Tenant tenant, CancellationToken ct)
    {
        var payments = await db.SubscriptionPayments
            .AsNoTracking()
            .Where(p => p.TenantId == tenant.Id)
            .OrderByDescending(p => p.PaidOn)
            .ThenByDescending(p => p.CreatedAt)
            .Select(p => new SubscriptionPaymentDto(
                p.Id, p.PaidOn, p.Amount, p.Currency, p.Method, p.Reference,
                p.CoversThrough, p.Note, p.CreatedAt))
            .ToListAsync(ct);

        return new PlatformTenantDetailDto(
            Map(tenant, await BranchCountAsync(tenant.Id, ct), payments.FirstOrDefault()?.PaidOn, clock.Today()),
            payments);
    }

    private async Task<PlatformTenantDto> MapAsync(Tenant tenant, CancellationToken ct)
    {
        var lastPayment = await db.SubscriptionPayments
            .Where(p => p.TenantId == tenant.Id)
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
            tenant.CreatedAt);
    }

    /// <summary>Sin tope los días de gracia se vuelven «gratis para siempre» por un cero de más.</summary>
    private static int NormalizeGraceDays(int days) => Math.Clamp(days, 0, 60);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

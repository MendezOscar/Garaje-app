using System.Reflection;
using Garaj.Application.Abstractions;
using Garaj.Domain.Common;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Persistence;

public class GarajDbContext(DbContextOptions<GarajDbContext> options, ITenantContext tenantContext)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    private readonly ITenantContext _tenantContext = tenantContext;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderTask> WorkOrderTasks => Set<WorkOrderTask>();
    public DbSet<WorkOrderPart> WorkOrderParts => Set<WorkOrderPart>();
    public DbSet<WorkOrderStatusHistory> WorkOrderStatusHistory => Set<WorkOrderStatusHistory>();

    public DbSet<MediaAttachment> MediaAttachments => Set<MediaAttachment>();

    public DbSet<Part> Parts => Set<Part>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<LaborService> LaborServices => Set<LaborService>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteLine> QuoteLines => Set<QuoteLine>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    /// <summary>
    /// Usuarios del taller actual. <see cref="AppUser"/> no lleva global query filter porque el
    /// login necesita buscar por email antes de conocer el tenant, así que este es el único
    /// punto de entrada seguro para listar o buscar usuarios dentro de una petición.
    /// </summary>
    public IQueryable<AppUser> UsersInTenant =>
        Users.Where(u => u.TenantId == _tenantContext.TenantId);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(GarajDbContext).Assembly);

        // Nombres de tabla de Identity sin el prefijo "AspNet", por coherencia con el resto.
        builder.Entity<AppUser>().ToTable("users");
        builder.Entity<AppRole>().ToTable("roles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("role_claims");

        ApplyTenantFilters(builder);
        ApplyDecimalPrecision(builder);
    }

    /// <summary>
    /// Aplica el filtro por tenant a toda entidad ITenantEntity. Si <c>TenantId</c> es null
    /// (petición anónima) el filtro no coincide con nada: la falla es hacia el lado seguro.
    /// Los endpoints públicos, como la cotización por token, deben usar IgnoreQueryFilters
    /// explícitamente y resolver el tenant a partir del propio token.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder builder)
    {
        var method = typeof(GarajDbContext)
            .GetMethod(nameof(SetTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            // AppUser queda fuera a propósito: ver el comentario de UsersInTenant.
            if (entityType.ClrType == typeof(AppUser))
                continue;

            method.MakeGenericMethod(entityType.ClrType).Invoke(this, [builder]);
        }
    }

    /// <summary>
    /// El lambda accede a <see cref="TenantContext"/> como miembro de instancia del DbContext.
    /// EF Core reconoce ese patrón y lo traduce a un parámetro que se reevalúa en cada consulta,
    /// en vez de hornear el valor en el modelo cacheado.
    /// </summary>
    private void SetTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, ITenantEntity
    {
        builder.Entity<TEntity>().HasQueryFilter(
            e => TenantContext.BypassTenantFilter || e.TenantId == TenantContext.TenantId);
    }

    /// <summary>Expuesto para que el árbol de expresión del filtro lo lea en cada consulta.</summary>
    public ITenantContext TenantContext => _tenantContext;

    /// <summary>
    /// Postgres rechaza decimal sin precisión explícita en agregaciones monetarias, y los
    /// reportes suman importes: 18,2 para dinero, 18,3 para cantidades y horas.
    /// </summary>
    private static void ApplyDecimalPrecision(ModelBuilder builder)
    {
        foreach (var property in builder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            var isQuantity = property.Name is "Quantity" or "MinQuantity" or "ResultingQuantity"
                or "EstimatedHours" or "ActualHours" or "StandardHours";

            property.SetPrecision(18);
            property.SetScale(isQuantity ? 3 : 2);
        }
    }
}

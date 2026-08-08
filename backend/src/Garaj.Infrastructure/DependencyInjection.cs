using Garaj.Application.Abstractions;
using Garaj.Application.Auth;
using Garaj.Application.Branches;
using Garaj.Application.Customers;
using Garaj.Application.Inventory;
using Garaj.Application.Media;
using Garaj.Application.Notifications;
using Garaj.Application.Quotes;
using Garaj.Application.Sales;
using Garaj.Application.ServiceRequests;
using Garaj.Application.Users;
using Garaj.Application.WorkOrders;
using Garaj.Infrastructure.Auth;
using Garaj.Infrastructure.Identity;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Persistence.Interceptors;
using Garaj.Infrastructure.Push;
using Garaj.Infrastructure.Services;
using Garaj.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Garaj.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // QuestPDF exige declarar la licencia antes de generar el primer documento. La
        // Community es gratuita para empresas por debajo de 1 M USD de facturación anual.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<PushOptions>(configuration.GetSection(PushOptions.SectionName));

        services.AddScoped<ITenantContext, TenantContext>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<AuditableEntityInterceptor>();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Falta la cadena de conexión 'Default'. Configúrela en appsettings o en la variable " +
                "de entorno ConnectionStrings__Default.");

        services.AddDbContext<GarajDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(GarajDbContext).Assembly.FullName);
                    // Neon y Supabase cortan conexiones ociosas; sin reintentos el primer
                    // request tras un rato de inactividad falla.
                    npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 10;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<GarajDbContext>();

        services.AddScoped<JwtTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<DbSeeder>();
        services.AddScoped<DemoSeeder>();

        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IVehicleService, VehicleService>();
        services.AddScoped<IWorkOrderService, WorkOrderService>();
        services.AddScoped<IServiceRequestService, ServiceRequestService>();
        services.AddScoped<IPartService, PartService>();
        services.AddScoped<ILaborServiceCatalog, LaborServiceCatalog>();
        services.AddScoped<IQuoteService, QuoteService>();

        // Registrado también por su tipo concreto y resuelto a la misma instancia: la orden
        // de trabajo consume stock dentro de su propia transacción, y para eso necesita los
        // métodos internos que no están en la interfaz.
        services.AddScoped<StockService>();
        services.AddScoped<IStockService>(sp => sp.GetRequiredService<StockService>());

        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<IReportService, ReportService>();

        // Singleton: el cliente de S3 mantiene su pool de conexiones HTTP y crearlo por
        // petición desperdicia handshakes TLS contra el bucket.
        services.AddSingleton<IStorageService, S3StorageService>();
        services.AddScoped<IMediaService, MediaService>();

        // Una sola instancia por petición para las dos caras: la que lee la campana y la que
        // emite los avisos comparten el mismo DbContext y la misma transacción implícita.
        services.AddScoped<NotificationService>();
        services.AddScoped<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        services.AddScoped<INotificationPublisher>(sp => sp.GetRequiredService<NotificationService>());

        services.AddHttpClient();
        // Singleton: la credencial de Google cachea y renueva su access token, y rehacerla
        // en cada aviso significaría un viaje extra a los servidores de OAuth cada vez.
        services.AddSingleton<IPushSender, FcmPushSender>();

        return services;
    }
}

using Garaj.Application.Abstractions;
using Garaj.Application.Auth;
using Garaj.Application.Branches;
using Garaj.Application.Customers;
using Garaj.Application.ServiceRequests;
using Garaj.Application.Users;
using Garaj.Application.WorkOrders;
using Garaj.Infrastructure.Auth;
using Garaj.Infrastructure.Identity;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Persistence.Interceptors;
using Garaj.Infrastructure.Services;
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
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

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

        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IVehicleService, VehicleService>();
        services.AddScoped<IWorkOrderService, WorkOrderService>();
        services.AddScoped<IServiceRequestService, ServiceRequestService>();

        return services;
    }
}

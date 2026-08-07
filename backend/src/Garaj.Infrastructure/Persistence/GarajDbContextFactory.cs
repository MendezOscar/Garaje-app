using Garaj.Application.Abstractions;
using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Garaj.Infrastructure.Persistence;

/// <summary>
/// Fábrica que usa <c>dotnet ef</c> al generar o aplicar migraciones. Sin ella, las
/// herramientas arrancarían la API completa —incluido el seed— solo para leer el modelo.
/// Para generar una migración basta con el proveedor; no necesita una base real.
/// </summary>
public class GarajDbContextFactory : IDesignTimeDbContextFactory<GarajDbContext>
{
    public GarajDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5434;Database=garaj;Username=garaj;Password=garaj-dev-secret";

        var options = new DbContextOptionsBuilder<GarajDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(GarajDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

        // Fuera de una petición no hay tenant; el bypass evita que el filtro afecte al modelo.
        var tenantContext = new TenantContext { BypassTenantFilter = true };

        return new GarajDbContext(options, tenantContext);
    }
}

using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Garaj.Infrastructure.Persistence;

/// <summary>
/// Fábrica que usa <c>dotnet ef</c> al generar o aplicar migraciones. Sin ella, las
/// herramientas arrancarían la API completa —incluido el seed— solo para leer el modelo:
/// el código entre <c>builder.Build()</c> y <c>app.Run()</c> sí se ejecuta.
/// </summary>
public class GarajDbContextFactory : IDesignTimeDbContextFactory<GarajDbContext>
{
    /// <summary>
    /// UserSecretsId de Garaj.Api. Se repite aquí porque esta fábrica no arranca el host y
    /// por tanto no hereda su configuración; si cambia en el .csproj, hay que cambiarlo aquí.
    /// </summary>
    private const string ApiUserSecretsId = "97d64a31-cdc0-4db0-8d2f-c681158d0e0e";

    public GarajDbContext CreateDbContext(string[] args)
    {
        // Mismo orden de precedencia que la API: entorno gana sobre secretos, y estos sobre
        // appsettings. `dotnet ef` corre con el directorio del proyecto de arranque.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(ApiUserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "No se encontró la cadena de conexión 'Default'. Configúrela con " +
                "`dotnet user-secrets set \"ConnectionStrings:Default\" \"...\" --project src/Garaj.Api` " +
                "o en la variable de entorno ConnectionStrings__Default.");

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

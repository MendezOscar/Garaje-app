using System.Text;
using Garaj.Api.Middleware;
using Garaj.Api.Services;
using Garaj.Application.Common;
using Garaj.Infrastructure;
using Garaj.Infrastructure.Auth;
using Garaj.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IHttpContextAccessorAdapter, HttpRequestInfo>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Garaj API", Version = "v1" });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Pegue solo el token, sin el prefijo 'Bearer'.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };

    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
});

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Falta la sección de configuración 'Jwt'.");

if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
    throw new InvalidOperationException(
        "Jwt:SigningKey debe tener al menos 32 caracteres. En producción configúrela por " +
        "variable de entorno Jwt__SigningKey, nunca en appsettings.json.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            // Sin esto el token sigue aceptándose hasta 5 minutos después de expirar.
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AppPolicies.OwnerOnly, p => p.RequireRole(AppRoles.Owner))
    .AddPolicy(AppPolicies.TechnicianOrOwner, p => p.RequireRole(AppRoles.Owner, AppRoles.Technician))
    .AddPolicy(AppPolicies.StaffOnly, p => p.RequireRole(AppRoles.Owner, AppRoles.Technician))
    .AddPolicy(AppPolicies.PlatformOnly, p => p.RequireRole(AppRoles.Platform));

const string CorsPolicy = "garaj-clients";
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddHealthChecks().AddDbContextCheck<GarajDbContext>();

var app = builder.Build();

// `dotnet run -- provision-tenant …` da de alta el taller de un cliente y sale sin montar el
// servidor. A propósito no hay endpoint equivalente: sería una puerta permanente para crear
// talleres en producción, y esto se hace una vez por cliente desde la máquina de quien instala.
if (args.FirstOrDefault() == "provision-tenant")
{
    return await ProvisionTenantAsync(app, args);
}

// El usuario de plataforma —el nuestro, el que da de alta talleres y les cobra— tampoco tiene
// endpoint que lo cree, y aquí la razón es más fuerte: si el panel pudiera crear otro, una
// sesión robada bastaría para fabricarse llaves maestras nuevas.
if (args.FirstOrDefault() == "create-platform-user")
{
    return await CreatePlatformUserAsync(app, args);
}

// Serilog va por fuera del manejo de errores a propósito. Al revés, el middleware de
// request logging ve la excepción antes de que se traduzca y registra un 500 aunque al
// cliente le llegue un 401 o un 404: los logs de producción mentirían.
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// En Render no hay un paso de release aparte, así que las migraciones se aplican al
// arrancar. El seed de demostración solo corre en Development: nunca en producción.
if (builder.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
{
    await MigrateAsync(app);
}

if (app.Environment.IsDevelopment())
{
    await SeedAsync(app);
}

app.UseCors(CorsPolicy);
app.UseAuthentication();
// Después de UseAuthentication: necesita los claims ya resueltos para fijar el tenant.
app.UseMiddleware<TenantContextMiddleware>();
// Después del anterior: necesita saber de qué taller es la petición para mirar su mensualidad.
app.UseMiddleware<SubscriptionGuardMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
return 0;

static async Task MigrateAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<GarajDbContext>().Database.MigrateAsync();
}

static async Task SeedAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync();
}

/// <summary>
/// Alta del taller de un cliente. Los argumentos se leen a mano —son diez y se usan una vez
/// por cliente— y el resultado se imprime en pantalla: la contraseña del Dueño no se guarda
/// en ninguna parte, así que esta es la única vez que se ve.
/// </summary>
static async Task<int> ProvisionTenantAsync(WebApplication app, string[] args)
{
    var values = ParseArguments(args);

    if (values.ContainsKey("help") || values.Count == 0)
    {
        Console.WriteLine(
            """
            Uso: dotnet run --project src/Garaj.Api -- provision-tenant [argumentos]

              --name          Nombre del taller (obligatorio)
              --owner-email   Correo del Dueño (obligatorio)
              --owner-name    Nombre completo del Dueño (obligatorio)
              --branch        Nombre de la primera sucursal (obligatorio)
              --branch-code   Código corto de la sucursal, ej. MTZ
              --city          Ciudad de la sucursal
              --address       Dirección de la sucursal
              --legal-name    Razón social, como sale en la cotización
              --tax-id        RTN
              --phone         Teléfono del taller
              --email         Correo del taller
              --logo          Ruta a un PNG o JPEG con el logo
              --password      Contraseña del Dueño; si se omite, se genera y se imprime
              --plan          Nombre del plan contratado
              --fee           Cuota mensual acordada
              --paid-through  Hasta cuándo queda pagado (aaaa-mm-dd); por omisión, un mes
              --grace-days    Días de tolerancia tras el vencimiento (por omisión 5)
            """);

        return values.Count == 0 ? 1 : 0;
    }

    using var scope = app.Services.CreateScope();
    var provisioner = scope.ServiceProvider.GetRequiredService<TenantProvisioner>();

    try
    {
        var result = await provisioner.RunAsync(new TenantProvisioner.Request(
            Name: values.GetValueOrDefault("name") ?? "",
            OwnerEmail: values.GetValueOrDefault("owner-email") ?? "",
            OwnerName: values.GetValueOrDefault("owner-name") ?? "",
            BranchName: values.GetValueOrDefault("branch") ?? "",
            BranchCode: values.GetValueOrDefault("branch-code"),
            City: values.GetValueOrDefault("city"),
            Address: values.GetValueOrDefault("address"),
            LegalName: values.GetValueOrDefault("legal-name"),
            TaxId: values.GetValueOrDefault("tax-id"),
            Phone: values.GetValueOrDefault("phone"),
            Email: values.GetValueOrDefault("email"),
            Password: values.GetValueOrDefault("password"),
            LogoPath: values.GetValueOrDefault("logo"),
            PlanName: values.GetValueOrDefault("plan"),
            MonthlyFee: decimal.TryParse(values.GetValueOrDefault("fee"), out var fee) ? fee : 0,
            PaidThrough: DateOnly.TryParse(values.GetValueOrDefault("paid-through"), out var hasta)
                ? hasta
                : null,
            GraceDays: int.TryParse(values.GetValueOrDefault("grace-days"), out var gracia)
                ? gracia
                : null));

        Console.WriteLine();
        Console.WriteLine($"Taller creado: {values["name"]}");
        Console.WriteLine($"  Id del taller : {result.TenantId}");
        Console.WriteLine($"  Sucursal      : {result.BranchId}");
        Console.WriteLine($"  Dueño         : {result.OwnerEmail}");
        Console.WriteLine($"  Contraseña    : {result.Password}");
        Console.WriteLine();
        Console.WriteLine("Anótela ahora: no queda guardada. Cámbiela al entrar la primera vez.");

        return 0;
    }
    catch (AppException e)
    {
        Console.Error.WriteLine($"No se dio de alta el taller: {e.Message}");
        return 1;
    }
}

/// <summary>
/// Alta del usuario de plataforma: el nuestro. Se corre una vez al montar el ambiente, y de
/// nuevo solo si hay que darle acceso a otra persona del equipo.
/// </summary>
static async Task<int> CreatePlatformUserAsync(WebApplication app, string[] args)
{
    var values = ParseArguments(args);

    if (values.ContainsKey("help") || values.Count == 0)
    {
        Console.WriteLine(
            """
            Uso: dotnet run --project src/Garaj.Api -- create-platform-user [argumentos]

              --email     Correo de quien administra GarajApp (obligatorio)
              --name      Nombre completo (obligatorio)
              --password  Contraseña; si se omite, se genera y se imprime
            """);

        return values.Count == 0 ? 1 : 0;
    }

    using var scope = app.Services.CreateScope();
    var provisioner = scope.ServiceProvider.GetRequiredService<PlatformUserProvisioner>();

    try
    {
        var result = await provisioner.RunAsync(
            values.GetValueOrDefault("email") ?? "",
            values.GetValueOrDefault("name") ?? "",
            values.GetValueOrDefault("password"));

        Console.WriteLine();
        Console.WriteLine("Usuario de plataforma creado.");
        Console.WriteLine($"  Correo     : {result.Email}");
        Console.WriteLine($"  Contraseña : {result.Password}");
        Console.WriteLine();
        Console.WriteLine("Anótela ahora: no queda guardada. Esta cuenta administra el cobro de");
        Console.WriteLine("todos los talleres, así que trátela como lo que es.");

        return 0;
    }
    catch (AppException e)
    {
        Console.Error.WriteLine($"No se creó el usuario: {e.Message}");
        return 1;
    }
}

/// <summary>Convierte `--clave valor` en un diccionario. El nombre del comando ya se consumió.</summary>
static Dictionary<string, string> ParseArguments(string[] args)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 1; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--")) continue;

        var key = args[i][2..];
        var value = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "";

        values[key] = value;
    }

    return values;
}

/// <summary>Necesario para que WebApplicationFactory pueda arrancar la API en los tests.</summary>
public partial class Program;

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
    .AddPolicy(AppPolicies.StaffOnly, p => p.RequireRole(AppRoles.Owner, AppRoles.Technician));

const string CorsPolicy = "garaj-clients";
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddHealthChecks().AddDbContextCheck<GarajDbContext>();

var app = builder.Build();

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
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

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

/// <summary>Necesario para que WebApplicationFactory pueda arrancar la API en los tests.</summary>
public partial class Program;

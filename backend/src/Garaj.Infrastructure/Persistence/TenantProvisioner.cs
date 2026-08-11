using System.Security.Cryptography;
using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Tenants;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Persistence;

/// <summary>
/// Alta de un taller real: el taller, su primera sucursal y el usuario Dueño.
///
/// No hay endpoint para esto a propósito. Un endpoint protegido por una clave sería una
/// puerta permanente a la creación de talleres en producción, y esto se hace una vez por
/// cliente: lo corre <c>dotnet run --project src/Garaj.Api -- provision-tenant …</c> desde la
/// máquina de quien instala, con la cadena de Supabase en user secrets.
///
/// El catálogo de repuestos y la mano de obra no se siembran: eso lo carga el Dueño desde el
/// panel, que es donde ya se administra.
/// </summary>
public class TenantProvisioner(
    GarajDbContext db,
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    ITenantContext tenantContext,
    ITenantService tenantService,
    IDateTimeProvider clock)
{
    public record Request(
        string Name,
        string OwnerEmail,
        string OwnerName,
        string BranchName,
        string? BranchCode = null,
        string? City = null,
        string? Address = null,
        string? LegalName = null,
        string? TaxId = null,
        string? Phone = null,
        string? Email = null,
        string? Password = null,
        string? LogoPath = null);

    public record Result(Guid TenantId, Guid BranchId, Guid OwnerUserId, string OwnerEmail, string Password);

    public async Task<Result> RunAsync(Request request, CancellationToken ct = default)
    {
        // Corre fuera de una petición: sin esto el filtro por tenant esconde lo que se acaba
        // de insertar y las comprobaciones de duplicados no verían nada.
        tenantContext.BypassTenantFilter = true;

        var name = Required(request.Name, "--name");
        var branchName = Required(request.BranchName, "--branch");
        var ownerEmail = Required(request.OwnerEmail, "--owner-email").ToLowerInvariant();
        var ownerName = Required(request.OwnerName, "--owner-name");

        // Antes de escribir nada: un alta a medias deja un taller sin dueño, que no se puede
        // arreglar desde ninguna pantalla.
        if (await db.Tenants.AnyAsync(t => t.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException($"Ya existe un taller llamado «{name}».");

        if (await userManager.FindByEmailAsync(ownerEmail) is not null)
            throw new ConflictException($"El correo {ownerEmail} ya tiene usuario.");

        await RoleSeeder.EnsureAsync(roleManager);

        var tenant = new Tenant
        {
            Name = name,
            LegalName = Trim(request.LegalName),
            TaxId = Trim(request.TaxId),
            Phone = Trim(request.Phone),
            Email = Trim(request.Email),
            // La casa matriz arranca en la dirección de la primera sucursal: al instalar es la
            // misma, y el Dueño la corrige en «Taller» si el SAR le registró otra.
            Address = Trim(request.Address),
            // Honduras: lempira, ISV general del 15% y el 504 al frente de los links de WhatsApp.
            Currency = "HNL",
            DefaultTaxRate = 15m,
            DefaultPhoneCountryCode = "504"
        };
        db.Tenants.Add(tenant);
        tenantContext.SetTenant(tenant.Id);

        var branch = new Branch
        {
            Name = branchName,
            Code = Trim(request.BranchCode),
            City = Trim(request.City),
            Address = Trim(request.Address),
            Phone = Trim(request.Phone)
        };
        db.Branches.Add(branch);

        await db.SaveChangesAsync(ct);

        var password = Trim(request.Password) ?? GeneratePassword();

        var owner = new AppUser
        {
            UserName = ownerEmail,
            Email = ownerEmail,
            EmailConfirmed = true,
            FullName = ownerName,
            TenantId = tenant.Id,
            CreatedAt = clock.UtcNow
        };

        var created = await userManager.CreateAsync(owner, password);
        if (!created.Succeeded)
            throw new AppException(
                $"No se pudo crear el Dueño: {string.Join("; ", created.Errors.Select(e => e.Description))}");

        await userManager.AddToRoleAsync(owner, AppRoles.Owner);

        // El Dueño no se asigna a sucursales: las ve todas por rol.
        if (Trim(request.LogoPath) is { } logoPath)
        {
            // Actúa como el Dueño recién creado para reutilizar la misma validación y
            // normalización que usa la pantalla del panel.
            tenantContext.Initialize(tenant.Id, owner.Id, AppRoles.Owner, [], null);

            await using var file = File.OpenRead(logoPath);
            await tenantService.SetLogoAsync(file, ContentTypeOf(logoPath), ct);
        }

        return new Result(tenant.Id, branch.Id, owner.Id, ownerEmail, password);
    }

    private static string ContentTypeOf(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" ? "image/jpeg" : "image/png";

    /// <summary>
    /// Contraseña de un solo uso, para que el Dueño la cambie al entrar. Se imprime una vez y
    /// no queda guardada en ninguna parte.
    /// </summary>
    private static string GeneratePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var chars = RandomNumberGenerator.GetItems<char>(alphabet, 12);

        // Identity exige dígito, minúscula y mayúscula: se garantizan al final en lugar de
        // sortear hasta que salga una válida.
        return new string(chars) + "9aA";
    }

    private static string Required(string? value, string argument) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new AppException($"Falta el argumento {argument}.")
            : value.Trim();

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

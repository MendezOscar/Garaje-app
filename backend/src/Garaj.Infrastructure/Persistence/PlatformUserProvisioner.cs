using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Garaj.Infrastructure.Persistence;

/// <summary>
/// Alta de un usuario nuestro, el que administra los talleres y sus cobros.
///
/// Solo por línea de comandos, igual que el alta de un taller y por una razón más fuerte: si el
/// panel pudiera crear otro usuario de plataforma, una sola sesión robada bastaría para
/// fabricarse llaves maestras nuevas. Desde adentro no se escala.
///
/// El usuario no pertenece a ningún taller (<c>TenantId</c> vacío), y eso no es un hueco sino la
/// barrera: el global query filter compara el taller de cada fila con el de la petición, así que
/// las órdenes, los clientes y el inventario de todos los talleres le salen vacíos.
/// </summary>
public class PlatformUserProvisioner(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IDateTimeProvider clock)
{
    public record Result(Guid UserId, string Email, string Password);

    public async Task<Result> RunAsync(string email, string fullName, string? password)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new AppException("Falta el argumento --email.");
        if (string.IsNullOrWhiteSpace(fullName)) throw new AppException("Falta el argumento --name.");

        var normalized = email.Trim().ToLowerInvariant();

        if (await userManager.FindByEmailAsync(normalized) is not null)
            throw new ConflictException($"El correo {normalized} ya tiene usuario.");

        await RoleSeeder.EnsureAsync(roleManager);

        var user = new AppUser
        {
            UserName = normalized,
            Email = normalized,
            EmailConfirmed = true,
            FullName = fullName.Trim(),
            TenantId = Guid.Empty,
            CreatedAt = clock.UtcNow
        };

        var pass = string.IsNullOrWhiteSpace(password) ? TenantProvisioner.GeneratePassword() : password;

        var created = await userManager.CreateAsync(user, pass);
        if (!created.Succeeded)
            throw new AppException(
                $"No se pudo crear el usuario: {string.Join("; ", created.Errors.Select(e => e.Description))}");

        await userManager.AddToRoleAsync(user, AppRoles.Platform);

        return new Result(user.Id, normalized, pass);
    }
}

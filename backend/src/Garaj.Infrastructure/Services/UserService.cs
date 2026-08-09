using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Users;
using Garaj.Infrastructure.Identity;
using Garaj.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

public class UserService(
    GarajDbContext db,
    UserManager<AppUser> userManager,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : IUserService
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(string? role, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        // UsersInTenant en vez de db.Users: AppUser no lleva global query filter, así que
        // esta es la única forma segura de no listar usuarios de otro taller.
        var users = await db.UsersInTenant
            .AsNoTracking()
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        var result = new List<UserDto>(users.Count);

        foreach (var user in users)
        {
            var userRole = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;
            if (role is not null && !string.Equals(userRole, role, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(await MapAsync(user, userRole, ct));
        }

        return result;
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var user = await FindInTenantAsync(id, ct);
        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;

        return await MapAsync(user, role, ct);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        if (!AppRoles.All.Contains(request.Role))
            throw new AppException($"El perfil '{request.Role}' no existe.");

        if (request.Role == AppRoles.Customer && request.CustomerId is null)
            throw new AppException("Un usuario con perfil Cliente necesita un cliente asociado.");

        var tenantId = tenantContext.TenantId!.Value;
        var email = request.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), ct))
            throw new ConflictException("Ya existe un usuario con ese correo.");

        if (request.CustomerId is { } customerId)
        {
            var linked = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct)
                ?? throw new NotFoundException("El cliente asociado no existe.");

            // Un segundo usuario para el mismo cliente dejaría dos accesos a los mismos
            // vehículos y solo uno visible en la ficha: el otro no habría cómo quitarlo.
            if (linked.AppUserId is not null)
                throw new ConflictException("Ese cliente ya tiene acceso a la app.");
        }

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = request.FullName.Trim(),
            TenantId = tenantId,
            CustomerId = request.CustomerId,
            CreatedAt = clock.UtcNow
        };

        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            throw new AppException(string.Join(" ", created.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, request.Role);
        await ReplaceBranchesAsync(user, request.Role, request.BranchIds, tenantId, ct);

        // Enlaza el cliente con su usuario para que la app pueda resolver "mis vehículos".
        if (request.CustomerId is { } linkedCustomerId)
        {
            var customer = await db.Customers.FirstAsync(c => c.Id == linkedCustomerId, ct);
            customer.AppUserId = user.Id;
        }

        await db.SaveChangesAsync(ct);

        return await MapAsync(user, request.Role, ct);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        var user = await FindInTenantAsync(id, ct);
        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;

        // Sin esto, el Dueño podría desactivarse a sí mismo y dejar el taller sin quien
        // administre, sin forma de revertirlo desde la aplicación.
        if (user.Id == scope.UserId && !request.IsActive)
            throw new AppException("No puede desactivar su propio usuario.");

        user.FullName = request.FullName.Trim();
        user.IsActive = request.IsActive;

        await ReplaceBranchesAsync(user, role, request.BranchIds, user.TenantId, ct);
        await db.SaveChangesAsync(ct);

        return await MapAsync(user, role, ct);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var user = await FindInTenantAsync(id, ct);

        // El Dueño no conoce la contraseña actual, así que se reemplaza en vez de cambiarla.
        // No se usa el token de restablecimiento a propósito: exige registrar los proveedores
        // de tokens de Identity, que solo harían falta para esto.
        await userManager.RemovePasswordAsync(user);
        var result = await userManager.AddPasswordAsync(user, request.NewPassword);

        if (!result.Succeeded)
            throw new AppException(string.Join(" ", result.Errors.Select(e => e.Description)));

        // Cambiar la contraseña cierra las sesiones abiertas de ese usuario.
        var now = clock.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private async Task<AppUser> FindInTenantAsync(Guid id, CancellationToken ct) =>
        await db.UsersInTenant.FirstOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new NotFoundException("El usuario no existe.");

    /// <summary>El Dueño ve todo el taller, así que no lleva filas de asignación a sucursal.</summary>
    private async Task ReplaceBranchesAsync(
        AppUser user, string role, IReadOnlyList<Guid>? branchIds, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.UserBranches.Where(ub => ub.UserId == user.Id).ToListAsync(ct);
        db.UserBranches.RemoveRange(existing);

        if (role == AppRoles.Owner || branchIds is null || branchIds.Count == 0) return;

        var valid = await db.Branches
            .Where(b => branchIds.Contains(b.Id))
            .Select(b => b.Id)
            .ToListAsync(ct);

        foreach (var branchId in valid)
            db.UserBranches.Add(new UserBranch { TenantId = tenantId, UserId = user.Id, BranchId = branchId });
    }

    private async Task<UserDto> MapAsync(AppUser user, string role, CancellationToken ct)
    {
        var branchIds = await db.UserBranches
            .Where(ub => ub.UserId == user.Id)
            .Select(ub => ub.BranchId)
            .ToListAsync(ct);

        return new UserDto(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            role,
            user.IsActive,
            user.CustomerId,
            branchIds,
            user.LastLoginAt);
    }
}

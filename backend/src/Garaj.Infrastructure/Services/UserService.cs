using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Domain.Enums;
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

        // Roles y sucursales de todos, en dos consultas. Antes se preguntaban usuario por
        // usuario —el rol con UserManager y las sucursales en el mapeo—, así que un taller con
        // quince empleados hacía treinta y un viajes a la base para pintar una pantalla.
        var ids = users.Select(u => u.Id).ToList();

        var roles = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where ids.Contains(ur.UserId)
            select new { ur.UserId, r.Name }).ToListAsync(ct);

        var rolePorUsuario = roles
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.First().Name ?? string.Empty);

        var sucursales = await db.UserBranches
            .Where(ub => ids.Contains(ub.UserId))
            .Select(ub => new { ub.UserId, ub.BranchId })
            .ToListAsync(ct);

        var sucursalesPorUsuario = sucursales
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.BranchId).ToList());

        var result = new List<UserDto>(users.Count);

        foreach (var user in users)
        {
            var userRole = rolePorUsuario.GetValueOrDefault(user.Id, string.Empty);
            if (role is not null && !string.Equals(userRole, role, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new UserDto(
                user.Id,
                user.Email ?? string.Empty,
                user.FullName,
                userRole,
                user.IsActive,
                user.CustomerId,
                sucursalesPorUsuario.GetValueOrDefault(user.Id, []),
                user.LastLoginAt,
                user.PayMode,
                user.PayAmount));
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

        // Cómo se le paga, si ya viene definido al crearlo. Igual que al editar: solo técnicos.
        if (request.Role == AppRoles.Technician)
        {
            if (request.PayAmount < 0)
                throw new AppException("El monto del pago no puede ser negativo.");

            if (request.PayMode == TechnicianPayMode.Percentage && request.PayAmount > 100)
                throw new AppException("El porcentaje va entre 0 y 100.");

            user.PayMode = request.PayMode;
            user.PayAmount = request.PayAmount;
        }

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

        // Cómo se le paga. Solo en técnicos: al Dueño no se le paga desde aquí, y un Cliente
        // no es empleado de nadie.
        if (string.Equals(role, AppRoles.Technician, StringComparison.OrdinalIgnoreCase))
        {
            if (request.PayAmount < 0)
                throw new AppException("El monto del pago no puede ser negativo.");

            if (request.PayMode == TechnicianPayMode.Percentage && request.PayAmount > 100)
                throw new AppException("El porcentaje va entre 0 y 100.");

            user.PayMode = request.PayMode;
            user.PayAmount = request.PayAmount;
        }

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

    public async Task<TechnicianPayProposalDto> PayProposalAsync(
        Guid id, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var user = await FindInTenantAsync(id, ct);
        var desde = from.ToUniversalTime();
        var hasta = to.ToUniversalTime();

        if (hasta < desde) (desde, hasta) = (hasta, desde);

        // La mano de obra que facturaron sus órdenes. Se atribuye por el técnico responsable
        // de la orden y no por quién hizo cada paso: es quien responde por el trabajo
        // completo, el mismo criterio del reporte de ingresos.
        var laborRevenue = await db.SaleLines.AsNoTracking()
            .Where(l => l.LineType == LineType.Labor
                && db.Sales.Any(s => s.Id == l.SaleId
                    && !s.IsVoided
                    && s.SaleDate >= desde
                    && s.SaleDate <= hasta
                    && db.WorkOrders.Any(w => w.Id == s.WorkOrderId
                        && w.AssignedTechnicianId == user.Id)))
            .SumAsync(l => (decimal?)l.Total, ct) ?? 0;

        // Las horas que registró. Las reales, no las estimadas: pagar por lo estimado es
        // pagar por lo que se calculó, no por lo que se trabajó.
        var hours = await db.WorkOrderTasks.AsNoTracking()
            .Where(t => t.AssignedTechnicianId == user.Id
                && t.IsDone
                && t.CompletedAt != null
                && t.CompletedAt >= desde
                && t.CompletedAt <= hasta)
            .SumAsync(t => (decimal?)t.ActualHours, ct) ?? 0;

        var propuesta = user.PayMode switch
        {
            TechnicianPayMode.Fixed => user.PayAmount,
            TechnicianPayMode.Percentage => Math.Round(laborRevenue * user.PayAmount / 100, 2),
            TechnicianPayMode.Hourly => Math.Round(hours * user.PayAmount, 2),
            _ => 0
        };

        // Lo que ya se le pagó en el periodo. El pago es un gasto, así que sale de ahí: no hay
        // un segundo registro que pueda decir otra cosa.
        var pagado = await db.Expenses.AsNoTracking()
            .Where(e => e.EmployeeUserId == user.Id
                && e.ExpenseDate >= desde
                && e.ExpenseDate <= hasta)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0;

        return new TechnicianPayProposalDto(
            user.Id,
            user.FullName,
            user.PayMode,
            user.PayAmount,
            desde,
            hasta,
            laborRevenue,
            hours,
            propuesta,
            pagado);
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
            user.LastLoginAt,
            user.PayMode,
            user.PayAmount);
    }
}

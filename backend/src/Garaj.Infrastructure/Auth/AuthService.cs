using Garaj.Application.Abstractions;
using Garaj.Application.Auth;
using Garaj.Application.Common;
using Garaj.Application.Tenants;
using Garaj.Infrastructure.Identity;
using Garaj.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Garaj.Infrastructure.Auth;

public class AuthService(
    GarajDbContext db,
    UserManager<AppUser> userManager,
    JwtTokenGenerator tokenGenerator,
    IOptions<JwtOptions> jwtOptions,
    IDateTimeProvider clock,
    IHttpContextAccessorAdapter requestInfo) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var normalizedEmail = userManager.NormalizeEmail(request.Email);

        // Sin filtro de tenant: en el login todavía no sabemos a qué taller pertenece el usuario.
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        // El mismo mensaje para usuario inexistente y contraseña incorrecta: no revelamos
        // qué emails están registrados.
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
            throw new UnauthorizedException();

        if (!user.IsActive)
            throw new UnauthorizedException("La cuenta está desactivada.");

        user.LastLoginAt = clock.UtcNow;

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = JwtTokenGenerator.HashToken(refreshToken);

        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new UnauthorizedException("Sesión inválida.");

        // Reutilización de un token ya rotado: alguien lo interceptó. Se cierran todas las
        // sesiones del usuario, no solo esta.
        if (stored.RevokedAt is not null)
        {
            await RevokeAllForUserAsync(stored.UserId, ct);
            throw new UnauthorizedException("Sesión comprometida. Vuelva a iniciar sesión.");
        }

        if (stored.ExpiresAt <= clock.UtcNow)
            throw new UnauthorizedException("La sesión expiró.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);
        if (user is null || !user.IsActive)
            throw new UnauthorizedException();

        var response = await IssueTokensAsync(user, ct, rotatedFrom: stored);
        return response;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = JwtTokenGenerator.HashToken(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        // Un logout con un token desconocido no es un error para el cliente: la sesión ya no sirve.
        if (stored is null || stored.RevokedAt is not null) return;

        stored.RevokedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("El usuario no existe.");

        return await BuildCurrentUserAsync(user, ct);
    }

    private async Task<AuthResponse> IssueTokensAsync(
        AppUser user, CancellationToken ct, RefreshToken? rotatedFrom = null)
    {
        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault()
            ?? throw new ForbiddenException("El usuario no tiene un perfil asignado.");

        var branchIds = await db.UserBranches
            .IgnoreQueryFilters()
            .Where(ub => ub.UserId == user.Id)
            .Select(ub => ub.BranchId)
            .ToListAsync(ct);

        var (accessToken, expiresAt) = tokenGenerator.CreateAccessToken(user, role, branchIds);
        var (refreshToken, refreshHash) = tokenGenerator.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddDays(_jwt.RefreshTokenDays),
            CreatedByIp = requestInfo.RemoteIp,
            DeviceInfo = requestInfo.UserAgent
        });

        if (rotatedFrom is not null)
        {
            rotatedFrom.RevokedAt = clock.UtcNow;
            rotatedFrom.ReplacedByTokenHash = refreshHash;
        }

        await db.SaveChangesAsync(ct);

        return new AuthResponse(accessToken, refreshToken, expiresAt, await BuildCurrentUserAsync(user, ct));
    }

    private async Task<CurrentUserDto> BuildCurrentUserAsync(AppUser user, CancellationToken ct)
    {
        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == user.TenantId, ct)
            ?? throw new NotFoundException("El taller no existe.");

        // El Dueño ve todas las sucursales del taller; el resto solo las asignadas.
        var branchesQuery = db.Branches.IgnoreQueryFilters().Where(b => b.TenantId == user.TenantId && b.IsActive);

        if (role != AppRoles.Owner)
        {
            var assigned = db.UserBranches.IgnoreQueryFilters().Where(ub => ub.UserId == user.Id).Select(ub => ub.BranchId);
            branchesQuery = branchesQuery.Where(b => assigned.Contains(b.Id));
        }

        var branches = await branchesQuery
            .OrderBy(b => b.Name)
            .Select(b => new BranchSummaryDto(b.Id, b.Name, b.Code))
            .ToListAsync(ct);

        return new CurrentUserDto(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            role,
            user.TenantId,
            tenant.Name,
            tenant.LogoStorageKey is null ? null : ITenantService.LogoPath(tenant.Id),
            branches,
            user.CustomerId);
    }

    private async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }
}

/// <summary>
/// Datos de la petición HTTP que el servicio necesita para etiquetar la sesión. Se abstrae
/// para que AuthService siga siendo testeable sin un HttpContext.
/// </summary>
public interface IHttpContextAccessorAdapter
{
    string? RemoteIp { get; }
    string? UserAgent { get; }
}

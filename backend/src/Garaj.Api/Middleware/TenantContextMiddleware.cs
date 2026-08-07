using System.Security.Claims;
using Garaj.Application.Abstractions;
using Garaj.Application.Common;

namespace Garaj.Api.Middleware;

/// <summary>
/// Traslada los claims del JWT al <see cref="ITenantContext"/>, que es lo que el DbContext
/// consulta para filtrar por taller. Debe correr después de UseAuthentication y antes de
/// cualquier endpoint: si no, las consultas saldrían vacías.
/// </summary>
public class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var user = context.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var tenantId = ParseGuid(user.FindFirstValue(AppClaims.TenantId));
            var userId = ParseGuid(user.FindFirstValue(ClaimTypes.NameIdentifier));
            var role = user.FindFirstValue(ClaimTypes.Role);
            var customerId = ParseGuid(user.FindFirstValue(AppClaims.CustomerId));

            var branchIds = (user.FindFirstValue(AppClaims.BranchIds) ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseGuid)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToArray();

            tenantContext.Initialize(tenantId, userId, role, branchIds, customerId);
        }

        await next(context);
    }

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;
}

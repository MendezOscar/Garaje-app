using Garaj.Application.Common;
using Garaj.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Garaj.Infrastructure.Persistence;

/// <summary>
/// Los tres roles de Identity. Son globales, no por taller, y hacen falta tanto en el seeder
/// de desarrollo como al dar de alta un taller real: de ahí que vivan aparte.
/// </summary>
public static class RoleSeeder
{
    public static async Task EnsureAsync(RoleManager<AppRole> roleManager)
    {
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new AppRole(role));
        }
    }
}

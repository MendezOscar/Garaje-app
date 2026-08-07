using Garaj.Application.Abstractions;
using Garaj.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Garaj.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Rellena tenant y auditoría al guardar. Que el TenantId se asigne aquí y no en cada caso de
/// uso evita el error de crear una entidad sin tenant, que el filtro luego volvería invisible.
/// </summary>
public class AuditableEntityInterceptor(ITenantContext tenantContext, IDateTimeProvider clock)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;

        var now = clock.UtcNow;
        var userId = tenantContext.UserId;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            if (entry.Entity is ITenantEntity tenantEntity
                && entry.State == EntityState.Added
                && tenantEntity.TenantId == Guid.Empty)
            {
                tenantEntity.TenantId = tenantContext.TenantId
                    ?? throw new InvalidOperationException(
                        $"No se puede guardar {entry.Entity.GetType().Name} sin tenant: " +
                        "la petición no está autenticada o el TenantContext no se resolvió.");
            }

            if (entry.Entity is not IAuditable auditable)
                continue;

            if (entry.State == EntityState.Added)
            {
                auditable.CreatedAt = now;
                auditable.CreatedByUserId = userId;
            }
            else
            {
                auditable.UpdatedAt = now;
                auditable.UpdatedByUserId = userId;
            }
        }
    }
}

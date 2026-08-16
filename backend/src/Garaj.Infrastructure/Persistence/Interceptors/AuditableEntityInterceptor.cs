using Garaj.Application.Abstractions;
using Garaj.Domain.Common;
using Garaj.Infrastructure.Identity;
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

            // AppUser queda fuera, igual que del global query filter y por un motivo parecido:
            // no todo usuario pertenece a un taller. El de plataforma —el nuestro— se guarda
            // con el taller vacío a propósito, y eso es justo lo que le impide ver datos
            // ajenos; rellenárselo aquí le entregaría las llaves del primer taller que pase.
            // Los cuatro sitios que crean usuarios ponen el taller a mano.
            if (entry.Entity is ITenantEntity tenantEntity
                && entry.Entity is not AppUser
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
                // Si la entidad ya trae fecha y autor, se respetan: es el caso del sembrador
                // de demostración, que escribe seis semanas de historia hacia atrás. Sellarlo
                // todo con la hora actual dejaría los reportes con un único día de actividad.
                if (auditable.CreatedAt == default) auditable.CreatedAt = now;
                auditable.CreatedByUserId ??= userId;
            }
            else
            {
                auditable.UpdatedAt = now;
                auditable.UpdatedByUserId = userId;
            }
        }
    }
}

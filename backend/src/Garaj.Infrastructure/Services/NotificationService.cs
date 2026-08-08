using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Notifications;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Las dos caras del mismo asunto: los servicios de negocio emiten avisos por
/// <see cref="INotificationPublisher"/> y las apps los leen por <see cref="INotificationService"/>.
/// </summary>
public class NotificationService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    IPushSender push,
    ILogger<NotificationService> logger) : INotificationService, INotificationPublisher
{
    // ---------- Lectura: la campana ----------

    public async Task<PagedResult<NotificationDto>> ListAsync(
        NotificationQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var q = db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == scope.UserId);

        if (query.OnlyUnread) q = q.Where(n => n.ReadAt == null);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(n => n.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(n => new NotificationDto(
                n.Id, n.Type, n.Title, n.Body,
                n.WorkOrderId, n.QuoteId, n.ServiceRequestId,
                n.ReadAt != null, n.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<NotificationDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        return await db.Notifications
            .CountAsync(n => n.RecipientUserId == scope.UserId && n.ReadAt == null, ct);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        // Filtra por destinatario además de por id: marcar el aviso de otro no rompe nada,
        // pero confirmaría que ese id existe.
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == scope.UserId, ct)
            ?? throw new NotFoundException("El aviso no existe.");

        if (notification.ReadAt is not null) return;

        notification.ReadAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var now = clock.UtcNow;

        return await db.Notifications
            .Where(n => n.RecipientUserId == scope.UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }

    // ---------- Dispositivos ----------

    public async Task RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var token = request.Token.Trim();

        if (string.IsNullOrEmpty(token))
            throw new AppException("El token del dispositivo viene vacío.");

        // Sin filtro de tenant: el token es único en todo el sistema y hay que poder
        // encontrarlo aunque lo tuviera un usuario de otro taller —un teléfono reinstalado,
        // un aparato prestado—. Se reasigna, y así el dueño anterior deja de recibirlo.
        var existing = await db.DeviceTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Token == token, ct);

        if (existing is null)
        {
            db.DeviceTokens.Add(new DeviceToken
            {
                TenantId = tenantContext.TenantId!.Value,
                UserId = scope.UserId,
                Token = token,
                Platform = request.Platform,
                LastSeenAt = clock.UtcNow
            });
        }
        else
        {
            existing.TenantId = tenantContext.TenantId!.Value;
            existing.UserId = scope.UserId;
            existing.Platform = request.Platform;
            existing.LastSeenAt = clock.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UnregisterDeviceAsync(string token, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        await db.DeviceTokens
            .Where(d => d.Token == token && d.UserId == scope.UserId)
            .ExecuteDeleteAsync(ct);
    }

    // ---------- Escritura: los avisos que emite el negocio ----------

    public Task NotifyUserAsync(
        Guid tenantId, Guid userId, NotificationDraft draft, CancellationToken ct = default) =>
        PublishAsync(tenantId, [userId], draft, ct);

    public async Task NotifyOwnersAsync(
        Guid tenantId, NotificationDraft draft, CancellationToken ct = default)
    {
        var owners = await db.Users
            .Where(u => u.TenantId == tenantId && u.IsActive)
            .Join(db.UserRoles, u => u.Id, ur => ur.UserId, (u, ur) => new { u.Id, ur.RoleId })
            .Join(db.Roles.Where(r => r.Name == AppRoles.Owner), x => x.RoleId, r => r.Id, (x, _) => x.Id)
            .Distinct()
            .ToListAsync(ct);

        await PublishAsync(tenantId, owners, draft, ct);
    }

    public async Task NotifyCustomerAsync(
        Guid tenantId, Guid customerId, NotificationDraft draft, CancellationToken ct = default)
    {
        // IgnoreQueryFilters con el tenant a mano: esto también corre desde la respuesta
        // pública a una cotización, donde no hay sesión y el filtro global no coincidiría.
        var userId = await db.Customers.IgnoreQueryFilters()
            .Where(c => c.Id == customerId && c.TenantId == tenantId)
            .Select(c => c.AppUserId)
            .FirstOrDefaultAsync(ct);

        // Un cliente sin usuario de la app existe y es normal: se le avisa por WhatsApp.
        if (userId is null) return;

        await PublishAsync(tenantId, [userId.Value], draft, ct);
    }

    private async Task PublishAsync(
        Guid tenantId, IReadOnlyCollection<Guid> userIds, NotificationDraft draft, CancellationToken ct)
    {
        if (userIds.Count == 0) return;

        var notifications = userIds.Select(userId => new Notification
        {
            TenantId = tenantId,
            RecipientUserId = userId,
            Type = draft.Type,
            Title = draft.Title,
            Body = draft.Body,
            WorkOrderId = draft.WorkOrderId,
            QuoteId = draft.QuoteId,
            ServiceRequestId = draft.ServiceRequestId
        }).ToList();

        try
        {
            db.Notifications.AddRange(notifications);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Se descarta lo que no se pudo guardar: si quedara en el rastreador, el
            // siguiente SaveChanges de la petición volvería a intentarlo y fallaría con él.
            foreach (var notification in notifications)
                db.Entry(notification).State = EntityState.Detached;

            logger.LogWarning(ex, "No se pudo registrar el aviso «{Title}».", draft.Title);
            return;
        }

        await PushAsync(userIds, draft, ct);
    }

    private async Task PushAsync(
        IReadOnlyCollection<Guid> userIds, NotificationDraft draft, CancellationToken ct)
    {
        if (!push.IsConfigured) return;

        try
        {
            var tokens = await db.DeviceTokens.IgnoreQueryFilters()
                .Where(d => userIds.Contains(d.UserId))
                .Select(d => d.Token)
                .ToListAsync(ct);

            if (tokens.Count == 0) return;

            var dead = await push.SendAsync(tokens, draft, ct);

            // Los tokens que el proveedor rechaza por muertos no se recuperan: se borran
            // aquí para no volver a intentarlo en cada aviso durante meses.
            if (dead.Count > 0)
                await db.DeviceTokens.IgnoreQueryFilters()
                    .Where(d => dead.Contains(d.Token))
                    .ExecuteDeleteAsync(ct);
        }
        catch (Exception ex)
        {
            // El aviso ya está guardado y el usuario lo verá al abrir la app. Que falle el
            // empujón no debe tumbar el cambio de estado que lo originó.
            logger.LogWarning(ex, "Falló el envío push de «{Title}».", draft.Title);
        }
    }
}

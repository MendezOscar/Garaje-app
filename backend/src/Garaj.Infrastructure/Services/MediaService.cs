using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Media;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Garaj.Infrastructure.Services;

public class MediaService(
    GarajDbContext db,
    IStorageService storage,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    IOptions<StorageOptions> options,
    ILogger<MediaService> logger) : IMediaService
{
    private static readonly string[] AllowedContentTypes =
        ["image/jpeg", "image/png", "image/webp", "image/heic", "image/heif"];

    private readonly StorageOptions _options = options.Value;

    public async Task<PresignedUploadDto> CreateUploadUrlAsync(
        CreateUploadRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        var contentType = request.ContentType.Trim().ToLowerInvariant();
        if (!AllowedContentTypes.Contains(contentType))
            throw new AppException($"Tipo de archivo no admitido: {request.ContentType}.");

        if (request.SizeBytes <= 0 || request.SizeBytes > _options.MaxUploadBytes)
            throw new AppException(
                $"El archivo debe pesar entre 1 byte y {_options.MaxUploadBytes / (1024 * 1024)} MB.");

        await EnsureCanAttachAsync(scope, request.OwnerType, request.OwnerId, ct);

        var attachment = new MediaAttachment
        {
            OwnerType = request.OwnerType,
            OwnerId = request.OwnerId,
            StorageKey = BuildKey(request.OwnerType, request.OwnerId, contentType),
            ContentType = contentType,
            SizeBytes = request.SizeBytes,
            OriginalFileName = Truncate(request.FileName, 300),
            Caption = Truncate(request.Caption, 500),
            UploadedByUserId = scope.UserId,
            // Una foto de la cola offline trae su fecha real; si no viene, es de ahora.
            TakenAt = request.TakenAt ?? clock.UtcNow,
            IsConfirmed = false,
            // El cliente nunca sube fotos ocultas para sí mismo: sería absurdo.
            IsVisibleToCustomer = scope.IsCustomer || request.IsVisibleToCustomer
        };

        db.MediaAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        var upload = await storage.CreateUploadUrlAsync(
            attachment.StorageKey,
            contentType,
            TimeSpan.FromMinutes(_options.PresignedUrlMinutes),
            ct);

        return new PresignedUploadDto(
            attachment.Id, upload.Url, upload.Key, upload.Headers, upload.ExpiresAt);
    }

    public async Task<MediaAttachmentDto> ConfirmAsync(Guid attachmentId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        var attachment = await db.MediaAttachments.FirstOrDefaultAsync(m => m.Id == attachmentId, ct)
            ?? throw new NotFoundException("El adjunto no existe.");

        // Solo quien pidió la URL puede confirmarla: otro usuario del taller no tiene por qué
        // publicar una subida ajena.
        if (attachment.UploadedByUserId != scope.UserId)
            throw new NotFoundException("El adjunto no existe.");

        if (attachment.IsConfirmed)
            return await MapAsync(attachment, ct);

        // El móvil sube en segundo plano y reintenta: si confirma sin que el PUT haya
        // terminado, la galería mostraría un hueco roto. Mejor fallar y que reintente.
        if (!await storage.ExistsAsync(attachment.StorageKey, ct))
            throw new ConflictException("El archivo todavía no llegó al almacenamiento. Reintente.");

        attachment.IsConfirmed = true;
        attachment.ThumbnailKey = await TryCreateThumbnailAsync(attachment, ct);

        await db.SaveChangesAsync(ct);
        return await MapAsync(attachment, ct);
    }

    public async Task<IReadOnlyList<MediaAttachmentDto>> ListAsync(
        MediaQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        await EnsureCanViewAsync(scope, query.OwnerType, query.OwnerId, ct);

        var items = await Visible(scope)
            .Where(m => m.OwnerType == query.OwnerType && m.OwnerId == query.OwnerId)
            .OrderBy(m => m.TakenAt)
            .ToListAsync(ct);

        return await MapAsync(items, ct);
    }

    public async Task<IReadOnlyList<MediaAttachmentDto>> ListForWorkOrderAsync(
        Guid workOrderId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        await EnsureCanViewAsync(scope, MediaOwnerType.WorkOrder, workOrderId, ct);

        var taskIds = await db.WorkOrderTasks
            .Where(t => t.WorkOrderId == workOrderId)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var items = await Visible(scope)
            .Where(m =>
                (m.OwnerType == MediaOwnerType.WorkOrder && m.OwnerId == workOrderId)
                || (m.OwnerType == MediaOwnerType.WorkOrderTask && taskIds.Contains(m.OwnerId)))
            .OrderBy(m => m.TakenAt)
            .ToListAsync(ct);

        return await MapAsync(items, ct);
    }

    public async Task<IReadOnlyList<MediaAttachmentDto>> ListForOrderPublicAsync(
        Guid tenantId, Guid workOrderId, CancellationToken ct = default)
    {
        var taskIds = await db.WorkOrderTasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.WorkOrderId == workOrderId)
            .Select(t => t.Id)
            .ToListAsync(ct);

        // Sin filtro de tenant y acotado a mano: la petición viene del enlace público, así que
        // no hay sesión de la que sacar el taller.
        var items = await db.MediaAttachments.AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId
                        && m.IsConfirmed
                        && m.IsVisibleToCustomer
                        && ((m.OwnerType == MediaOwnerType.WorkOrder && m.OwnerId == workOrderId)
                            || (m.OwnerType == MediaOwnerType.WorkOrderTask && taskIds.Contains(m.OwnerId))))
            .OrderBy(m => m.TakenAt)
            .ToListAsync(ct);

        return await MapAsync(items, ct);
    }

    public async Task DeleteAsync(Guid attachmentId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        var attachment = await db.MediaAttachments.FirstOrDefaultAsync(m => m.Id == attachmentId, ct)
            ?? throw new NotFoundException("El adjunto no existe.");

        // El técnico borra lo que subió él —una foto movida, un encuadre malo—; el Dueño,
        // cualquiera del taller. El cliente no borra evidencia.
        if (!scope.IsOwner && attachment.UploadedByUserId != scope.UserId)
            throw new NotFoundException("El adjunto no existe.");

        if (scope.IsCustomer && attachment.OwnerType != MediaOwnerType.ServiceRequest)
            throw new ForbiddenException("Un cliente no puede eliminar la evidencia del taller.");

        await EnsureCanViewAsync(scope, attachment.OwnerType, attachment.OwnerId, ct);

        db.MediaAttachments.Remove(attachment);
        await db.SaveChangesAsync(ct);

        // El bucket se limpia después de confirmar el borrado en la base: si falla, queda un
        // objeto huérfano —barato— en vez de una fila apuntando a un archivo inexistente.
        await TryDeleteObjectAsync(attachment.StorageKey, ct);
        if (attachment.ThumbnailKey is { } thumbKey) await TryDeleteObjectAsync(thumbKey, ct);
    }

    /// <summary>
    /// Las subidas sin confirmar no existen para nadie, y el cliente solo ve lo que el taller
    /// marcó visible: una foto del motor abierto a medio desarmar preocupa sin explicar nada.
    /// </summary>
    private IQueryable<MediaAttachment> Visible(AccessScope scope)
    {
        var q = db.MediaAttachments.AsNoTracking().Where(m => m.IsConfirmed);

        return scope.IsCustomer ? q.Where(m => m.IsVisibleToCustomer) : q;
    }

    /// <summary>Quién puede <b>añadir</b> evidencia a este recurso.</summary>
    private async Task EnsureCanAttachAsync(
        AccessScope scope, MediaOwnerType ownerType, Guid ownerId, CancellationToken ct)
    {
        await EnsureCanViewAsync(scope, ownerType, ownerId, ct);

        if (!scope.IsCustomer) return;

        // El cliente documenta su propio problema al pedir el servicio; el proceso de
        // reparación lo documenta el taller.
        if (ownerType != MediaOwnerType.ServiceRequest)
            throw new ForbiddenException("Un cliente solo puede adjuntar fotos a sus requerimientos.");
    }

    /// <summary>
    /// Reutiliza el alcance de la orden y del requerimiento en vez de reimplementarlo: si
    /// el usuario no puede ver el recurso, tampoco puede ver ni tocar sus fotos.
    /// </summary>
    private async Task EnsureCanViewAsync(
        AccessScope scope, MediaOwnerType ownerType, Guid ownerId, CancellationToken ct)
    {
        var workOrderId = ownerType switch
        {
            MediaOwnerType.WorkOrder => ownerId,
            MediaOwnerType.WorkOrderTask => await db.WorkOrderTasks
                .Where(t => t.Id == ownerId)
                .Select(t => (Guid?)t.WorkOrderId)
                .FirstOrDefaultAsync(ct),
            _ => null
        };

        if (ownerType == MediaOwnerType.ServiceRequest)
        {
            var exists = await ScopedRequests(scope).AnyAsync(r => r.Id == ownerId, ct);
            if (!exists) throw new NotFoundException("El requerimiento no existe.");
            return;
        }

        if (workOrderId is not { } id || !await ScopedOrders(scope).AnyAsync(w => w.Id == id, ct))
            throw new NotFoundException("La orden de trabajo no existe.");
    }

    private IQueryable<WorkOrder> ScopedOrders(AccessScope scope)
    {
        var q = db.WorkOrders.AsNoTracking();

        if (scope.IsTechnician) return q.Where(w => w.AssignedTechnicianId == scope.UserId);
        if (scope.IsCustomer) return q.Where(w => w.Vehicle.CustomerId == scope.CustomerId);

        return q;
    }

    private IQueryable<ServiceRequest> ScopedRequests(AccessScope scope)
    {
        var q = db.ServiceRequests.AsNoTracking();

        if (scope.IsTechnician) return q.Where(r => scope.BranchIds.Contains(r.BranchId));
        if (scope.IsCustomer) return q.Where(r => r.Vehicle.CustomerId == scope.CustomerId);

        return q;
    }

    /// <summary>
    /// La miniatura se genera al confirmar, no en segundo plano: son unos cientos de
    /// milisegundos sobre una foto ya comprimida por el móvil, y evita el estado intermedio
    /// de "subida pero todavía sin miniatura" que habría que resolver en los tres clientes.
    /// </summary>
    private async Task<string?> TryCreateThumbnailAsync(MediaAttachment attachment, CancellationToken ct)
    {
        try
        {
            await using var original = await storage.DownloadAsync(attachment.StorageKey, ct);
            var thumbnail = ThumbnailGenerator.Create(original, _options.ThumbnailSize);
            if (thumbnail is null) return null;

            var key = Path.ChangeExtension(attachment.StorageKey, null) + "_thumb.jpg";

            using var stream = new MemoryStream(thumbnail);
            await storage.UploadAsync(key, stream, ThumbnailGenerator.ContentType, ct);

            return key;
        }
        catch (Exception e)
        {
            // Sin miniatura la galería cae al original: se ve más lento, pero se ve. No vale
            // perder la foto del técnico por esto.
            logger.LogWarning(e, "No se pudo generar la miniatura de {Key}.", attachment.StorageKey);
            return null;
        }
    }

    private async Task TryDeleteObjectAsync(string key, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(key, ct);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "No se pudo borrar {Key} del almacenamiento.", key);
        }
    }

    /// <summary>
    /// Clave con el tenant al frente: si algún día hay que auditar, migrar o borrar un taller
    /// entero, es un prefijo. El guid del archivo evita que dos fotos del mismo segundo choquen.
    /// </summary>
    private string BuildKey(MediaOwnerType ownerType, Guid ownerId, string contentType)
    {
        var folder = ownerType switch
        {
            MediaOwnerType.ServiceRequest => "service-requests",
            MediaOwnerType.WorkOrder => "work-orders",
            MediaOwnerType.WorkOrderTask => "work-order-tasks",
            _ => "other"
        };

        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/heic" or "image/heif" => ".heic",
            _ => ".jpg"
        };

        return $"tenants/{tenantContext.TenantId}/{folder}/{ownerId}/{Guid.NewGuid():N}{extension}";
    }

    private async Task<MediaAttachmentDto> MapAsync(MediaAttachment attachment, CancellationToken ct) =>
        (await MapAsync([attachment], ct)).Single();

    private async Task<IReadOnlyList<MediaAttachmentDto>> MapAsync(
        IReadOnlyList<MediaAttachment> attachments, CancellationToken ct)
    {
        if (attachments.Count == 0) return [];

        var userIds = attachments.Select(m => m.UploadedByUserId).Distinct().ToList();
        var names = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var taskIds = attachments
            .Where(m => m.OwnerType == MediaOwnerType.WorkOrderTask)
            .Select(m => m.OwnerId)
            .Distinct()
            .ToList();

        var taskTitles = taskIds.Count == 0
            ? []
            : await db.WorkOrderTasks
                .Where(t => taskIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Title, ct);

        var expiry = TimeSpan.FromMinutes(_options.PresignedUrlMinutes);
        var result = new List<MediaAttachmentDto>(attachments.Count);

        foreach (var m in attachments)
        {
            var url = await storage.GetDownloadUrlAsync(m.StorageKey, expiry, ct);
            var thumbnailUrl = m.ThumbnailKey is { } key
                ? await storage.GetDownloadUrlAsync(key, expiry, ct)
                : url;

            result.Add(new MediaAttachmentDto(
                m.Id,
                m.OwnerType,
                m.OwnerId,
                url,
                thumbnailUrl,
                m.ContentType,
                m.SizeBytes,
                m.Caption,
                m.UploadedByUserId,
                names.GetValueOrDefault(m.UploadedByUserId, "—"),
                m.TakenAt,
                m.CreatedAt,
                m.IsVisibleToCustomer,
                m.OwnerType == MediaOwnerType.WorkOrderTask ? taskTitles.GetValueOrDefault(m.OwnerId) : null));
        }

        return result;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

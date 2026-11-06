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
    /// <summary>
    /// Fotos, y el PDF. El PDF está aquí por el informe del escáner: la tableta de
    /// diagnóstico lo comparte a la app y queda colgado de la orden como un adjunto más.
    /// Cualquier escáner que exporte un informe entra por la misma puerta.
    /// </summary>
    private static readonly string[] AllowedContentTypes =
        ["image/jpeg", "image/png", "image/webp", "image/heic", "image/heif", "application/pdf"];

    public const string Pdf = "application/pdf";

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
        var pesaDeVerdad = await storage.SizeAsync(attachment.StorageKey, ct)
            ?? throw new ConflictException("El archivo todavía no llegó al almacenamiento. Reintente.");

        // El tamaño que se declaró al pedir la URL era una promesa: la firma del PUT ata el
        // tipo de archivo, no el peso. Aquí se mira lo que subió de verdad, y lo que se pasa
        // se borra del bucket en vez de quedarse ocupando sitio sin que nadie lo mire.
        if (pesaDeVerdad > _options.MaxUploadBytes)
        {
            await storage.DeleteAsync(attachment.StorageKey, ct);
            db.MediaAttachments.Remove(attachment);
            await db.SaveChangesAsync(ct);

            throw new AppException(
                $"El archivo pesa más de {_options.MaxUploadBytes / (1024 * 1024)} MB.");
        }

        attachment.SizeBytes = pesaDeVerdad;

        // Lo mismo que el peso: el tipo que se declaró al pedir la URL es una promesa. La
        // firma del PUT ata la etiqueta, no el contenido, así que un PDF se mira por dentro
        // antes de darlo por bueno —y lo que no lo sea se borra en vez de quedar servido a
        // un cliente como si fuera el informe de su carro—.
        if (attachment.ContentType == Pdf && !await EsPdfDeVerdadAsync(attachment.StorageKey, ct))
        {
            await storage.DeleteAsync(attachment.StorageKey, ct);
            db.MediaAttachments.Remove(attachment);
            await db.SaveChangesAsync(ct);

            throw new AppException("El archivo no es un PDF.");
        }

        attachment.IsConfirmed = true;

        // Un PDF no tiene miniatura: la galería lo dibuja como documento, con su nombre.
        attachment.ThumbnailKey = attachment.ContentType.StartsWith("image/")
            ? await TryCreateThumbnailAsync(attachment, ct)
            : null;

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

    // ---------- Traer a la cotización lo que el técnico fotografió en la orden ----------

    public async Task<IReadOnlyList<QuotePhotoCandidateDto>> QuotePhotoCandidatesAsync(
        Guid quoteId, CancellationToken ct = default)
    {
        var (_, workOrderId) = await CotizacionConOrdenAsync(quoteId, ct);

        var deLaOrden = await ListForWorkOrderAsync(workOrderId, ct);

        var yaTraidas = await db.MediaAttachments.AsNoTracking()
            .Where(m => m.OwnerType == MediaOwnerType.Quote
                        && m.OwnerId == quoteId
                        && m.CopiedFromId != null)
            .Select(m => m.CopiedFromId!.Value)
            .ToListAsync(ct);

        return deLaOrden
            .Select(foto => new QuotePhotoCandidateDto(foto, yaTraidas.Contains(foto.Id)))
            .ToList();
    }

    public async Task<IReadOnlyList<MediaAttachmentDto>> CopyToQuoteAsync(
        Guid quoteId, CopyQuotePhotosRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var (quote, workOrderId) = await CotizacionConOrdenAsync(quoteId, ct);

        // Una cotización ya respondida es un documento cerrado: cambiarle las fotos sería
        // cambiar lo que el cliente miró cuando dijo que sí. Es el mismo criterio que decide
        // si se le pueden tocar las líneas.
        if (quote.Status is not (QuoteStatus.Draft or QuoteStatus.Sent or QuoteStatus.Expired))
            throw new AppException(
                $"La cotización {quote.Number} ya fue respondida: sus fotos no se cambian.");

        if (request.MediaIds.Count == 0) return [];

        var taskIds = await db.WorkOrderTasks
            .Where(t => t.WorkOrderId == workOrderId)
            .Select(t => t.Id)
            .ToListAsync(ct);

        // Acotado a las fotos de esta orden: un id de otra orden —o de otro taller— no trae
        // nada. El filtro global del tenant ya cierra lo segundo; esto cierra lo primero.
        var origenes = await db.MediaAttachments.AsNoTracking()
            .Where(m => request.MediaIds.Contains(m.Id)
                        && m.IsConfirmed
                        && ((m.OwnerType == MediaOwnerType.WorkOrder && m.OwnerId == workOrderId)
                            || (m.OwnerType == MediaOwnerType.WorkOrderTask && taskIds.Contains(m.OwnerId))))
            .ToListAsync(ct);

        var yaTraidas = await db.MediaAttachments.AsNoTracking()
            .Where(m => m.OwnerType == MediaOwnerType.Quote
                        && m.OwnerId == quoteId
                        && m.CopiedFromId != null)
            .Select(m => m.CopiedFromId!.Value)
            .ToListAsync(ct);

        var copiadas = new List<MediaAttachment>();

        foreach (var origen in origenes)
        {
            // Traer dos veces la misma no es un error, es el segundo clic de quien no vio que
            // el primero entró. Se ignora y ya.
            if (yaTraidas.Contains(origen.Id)) continue;

            var copia = new MediaAttachment
            {
                OwnerType = MediaOwnerType.Quote,
                OwnerId = quoteId,
                StorageKey = BuildKey(MediaOwnerType.Quote, quoteId, origen.ContentType),
                ContentType = origen.ContentType,
                SizeBytes = origen.SizeBytes,
                OriginalFileName = origen.OriginalFileName,
                Caption = origen.Caption,
                UploadedByUserId = scope.UserId,
                // La fecha de la toma es la de la foto, no la de hoy: la cotización se lee
                // como una línea de tiempo del trabajo y una copia no es una foto nueva.
                TakenAt = origen.TakenAt,
                IsConfirmed = true,
                // Lo que se trae a la cotización es justamente lo que va a ver el cliente.
                IsVisibleToCustomer = true,
                CopiedFromId = origen.Id
            };

            if (!await CopiarObjetoAsync(origen.StorageKey, copia.StorageKey, copia.ContentType, ct))
                continue;

            if (origen.ThumbnailKey is { } miniatura)
            {
                var destino = copia.StorageKey + ".thumb.jpg";
                if (await CopiarObjetoAsync(miniatura, destino, "image/jpeg", ct))
                    copia.ThumbnailKey = destino;
            }

            db.MediaAttachments.Add(copia);
            copiadas.Add(copia);
        }

        if (copiadas.Count == 0) return [];

        await db.SaveChangesAsync(ct);

        return await MapAsync(copiadas, ct);
    }

    /// <summary>
    /// Duplica el objeto en el bucket. Pasa por el servidor en vez de pedirle al almacenamiento
    /// que copie: son fotos de teléfono, el viaje es corto, y así no hace falta una operación
    /// propia de S3 en una interfaz que el resto del sistema usa sin saber qué hay detrás.
    /// </summary>
    private async Task<bool> CopiarObjetoAsync(
        string origen, string destino, string contentType, CancellationToken ct)
    {
        try
        {
            await using var contenido = await storage.DownloadAsync(origen, ct);
            await storage.UploadAsync(destino, contenido, contentType, ct);
            return true;
        }
        catch (Exception ex)
        {
            // Una foto que no se pudo copiar no puede tumbar las otras cinco: se deja fuera y
            // queda en el registro. El bucket puede haber perdido el objeto de una foto vieja.
            logger.LogWarning(ex, "No se pudo copiar la foto {Origen} a la cotización.", origen);
            return false;
        }
    }

    /// <summary>La cotización y la orden de la que salió, con los dos permisos comprobados.</summary>
    private async Task<(Quote Quote, Guid WorkOrderId)> CotizacionConOrdenAsync(
        Guid quoteId, CancellationToken ct)
    {
        var scope = AccessScope.From(tenantContext);
        await EnsureCanViewAsync(scope, MediaOwnerType.Quote, quoteId, ct);

        var quote = await db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new NotFoundException("La cotización no existe.");

        if (quote.WorkOrderId is not { } workOrderId)
            throw new AppException(
                $"La cotización {quote.Number} no salió de una orden de trabajo, así que no hay "
                + "fotos que traer.");

        await EnsureCanViewAsync(scope, MediaOwnerType.WorkOrder, workOrderId, ct);

        return (quote, workOrderId);
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

    public async Task<IReadOnlyList<MediaAttachmentDto>> ListForQuotePublicAsync(
        Guid tenantId, Guid quoteId, CancellationToken ct = default)
    {
        // Igual que en la orden: sin filtro de tenant y acotado a mano, porque quien llega es
        // el cliente por el link de WhatsApp y no hay sesión de la que sacar el taller.
        var items = await db.MediaAttachments.AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId
                        && m.IsConfirmed
                        && m.IsVisibleToCustomer
                        && m.OwnerType == MediaOwnerType.Quote
                        && m.OwnerId == quoteId)
            .OrderBy(m => m.TakenAt)
            .ToListAsync(ct);

        return await MapAsync(items, ct);
    }

    public Task<bool> HasDocumentsAsync(
        MediaOwnerType ownerType, Guid ownerId, Guid tenantId, CancellationToken ct = default) =>
        db.MediaAttachments.AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(m => m.TenantId == tenantId
                           && m.IsConfirmed
                           && m.IsVisibleToCustomer
                           && m.OwnerType == ownerType
                           && m.OwnerId == ownerId
                           && !m.ContentType.StartsWith("image/"), ct);

    public async Task<IReadOnlyList<byte[]>> DownloadThumbnailsAsync(
        MediaOwnerType ownerType, Guid ownerId, Guid tenantId, int max, CancellationToken ct = default)
    {
        if (max <= 0) return [];

        var keys = await db.MediaAttachments.AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId
                        && m.IsConfirmed
                        && m.IsVisibleToCustomer
                        && m.OwnerType == ownerType
                        && m.OwnerId == ownerId
                        // Solo imágenes: el que arma el PDF espera una imagen, y un informe
                        // adjunto metido por aquí tumbaría la cotización entera.
                        && m.ContentType.StartsWith("image/"))
            .OrderBy(m => m.TakenAt)
            .Take(max)
            .Select(m => m.ThumbnailKey ?? m.StorageKey)
            .ToListAsync(ct);

        var images = new List<byte[]>(keys.Count);

        foreach (var key in keys)
        {
            try
            {
                await using var stream = await storage.DownloadAsync(key, ct);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                images.Add(buffer.ToArray());
            }
            catch (Exception e)
            {
                // Una foto que no se pudo bajar no vale un PDF sin cotización: se omite.
                logger.LogWarning(e, "No se pudo leer {Key} para el documento.", key);
            }
        }

        return images;
    }

    public async Task PurgeAsync(
        MediaOwnerType ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken ct = default)
    {
        if (ownerIds.Count == 0) return;

        var attachments = await db.MediaAttachments
            .Where(m => m.OwnerType == ownerType && ownerIds.Contains(m.OwnerId))
            .ToListAsync(ct);

        if (attachments.Count == 0) return;

        db.MediaAttachments.RemoveRange(attachments);
        await db.SaveChangesAsync(ct);

        foreach (var attachment in attachments)
        {
            await TryDeleteObjectAsync(attachment.StorageKey, ct);
            if (attachment.ThumbnailKey is { } thumbKey) await TryDeleteObjectAsync(thumbKey, ct);
        }
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
            MediaOwnerType.WorkOrder or MediaOwnerType.Reception => ownerId,
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

        if (ownerType == MediaOwnerType.Quote)
        {
            var exists = await ScopedQuotes(scope).AnyAsync(q => q.Id == ownerId, ct);
            if (!exists) throw new NotFoundException("La cotización no existe.");
            return;
        }

        // El comprobante del gasto. Solo el Dueño: lo que sale de la caja es del negocio, y
        // el filtro global del tenant ya acota el gasto al taller de quien pregunta.
        if (ownerType == MediaOwnerType.Expense)
        {
            scope.EnsureManagement();

            if (!await db.Expenses.AsNoTracking().AnyAsync(e => e.Id == ownerId, ct))
                throw new NotFoundException("El gasto no existe.");

            return;
        }

        // La hoja de recepción se llena al recibir, cuando la orden todavía no existe: ahí
        // las fotos cuelgan del requerimiento, y al aprobarlo se pasan a la orden con la hoja.
        if (ownerType == MediaOwnerType.Reception
            && await ScopedRequests(scope).AnyAsync(r => r.Id == ownerId, ct))
        {
            return;
        }

        if (workOrderId is not { } id || !await ScopedOrders(scope).AnyAsync(w => w.Id == id, ct))
            throw new NotFoundException("La orden de trabajo no existe.");
    }

    private IQueryable<WorkOrder> ScopedOrders(AccessScope scope)
    {
        var q = db.WorkOrders.AsNoTracking();

        if (scope.IsTechnician) return q.Where(w => w.AssignedTechnicianId == scope.UserId);
        if (scope.IsCustomer)
            return q.Where(w =>
                (w.Vehicle != null && w.Vehicle.CustomerId == scope.CustomerId)
                || (w.Equipment != null && w.Equipment.CustomerId == scope.CustomerId));

        return q;
    }

    /// <summary>
    /// El mismo alcance que el servicio de cotizaciones: el Cliente ve las suyas y el Técnico
    /// no participa en la parte comercial.
    /// </summary>
    private IQueryable<Quote> ScopedQuotes(AccessScope scope)
    {
        var q = db.Quotes.AsNoTracking();

        if (scope.IsTechnician) return q.Where(_ => false);
        if (scope.IsCustomer) return q.Where(x => x.CustomerId == scope.CustomerId);

        return q;
    }

    private IQueryable<ServiceRequest> ScopedRequests(AccessScope scope)
    {
        var q = db.ServiceRequests.AsNoTracking();

        if (scope.IsTechnician) return q.Where(r => scope.BranchIds.Contains(r.BranchId));
        if (scope.IsCustomer)
            return q.Where(r =>
                (r.Vehicle != null && r.Vehicle.CustomerId == scope.CustomerId)
                || (r.Equipment != null && r.Equipment.CustomerId == scope.CustomerId));

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

    /// <summary>
    /// Los cinco primeros bytes de un PDF son siempre <c>%PDF-</c>. No valida el documento
    /// entero —para eso haría falta parsearlo— pero sí descarta lo que no lo es.
    /// </summary>
    private async Task<bool> EsPdfDeVerdadAsync(string key, CancellationToken ct)
    {
        try
        {
            await using var stream = await storage.DownloadAsync(key, ct);

            var cabecera = new byte[5];
            var leidos = await stream.ReadAtLeastAsync(cabecera, 5, throwOnEndOfStream: false, ct);

            return leidos == 5 && "%PDF-"u8.SequenceEqual(cabecera);
        }
        catch (Exception e)
        {
            // Si no se pudo leer no se afirma que esté mal: el adjunto se queda y el técnico
            // lo ve en la orden. Perder el informe por un tropiezo del bucket sería peor.
            logger.LogWarning(e, "No se pudo comprobar si {Key} es un PDF.", key);
            return true;
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
            MediaOwnerType.Quote => "quotes",
            MediaOwnerType.Expense => "expenses",
            MediaOwnerType.Reception => "receptions",
            _ => "other"
        };

        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/heic" or "image/heif" => ".heic",
            Pdf => ".pdf",
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
                m.OriginalFileName,
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

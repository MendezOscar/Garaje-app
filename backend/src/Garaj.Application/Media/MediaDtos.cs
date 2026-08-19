using Garaj.Domain.Enums;

namespace Garaj.Application.Media;

/// <param name="Url">URL prefirmada de lectura del original. Caduca; no se guarda en caché.</param>
/// <param name="ThumbnailUrl">URL de la miniatura, o la del original si no se pudo generar.</param>
public record MediaAttachmentDto(
    Guid Id,
    MediaOwnerType OwnerType,
    Guid OwnerId,
    string Url,
    string ThumbnailUrl,
    string ContentType,
    long SizeBytes,
    string? Caption,
    Guid UploadedByUserId,
    string UploadedByName,
    DateTimeOffset TakenAt,
    DateTimeOffset UploadedAt,
    bool IsVisibleToCustomer,
    // Título del paso al que pertenece, cuando la foto documenta un paso concreto.
    string? TaskTitle);

/// <param name="TakenAt">
/// Fecha real de la toma en el teléfono. Se pide explícita porque el móvil puede subir desde
/// la cola offline días después: usar la hora del servidor descuadraría la línea de tiempo.
/// </param>
public record CreateUploadRequest(
    MediaOwnerType OwnerType,
    Guid OwnerId,
    string ContentType,
    long SizeBytes,
    string? FileName,
    string? Caption,
    DateTimeOffset? TakenAt,
    bool IsVisibleToCustomer = true);

/// <param name="AttachmentId">Id que hay que confirmar cuando el PUT al bucket termine.</param>
public record PresignedUploadDto(
    Guid AttachmentId,
    string UploadUrl,
    string Key,
    IDictionary<string, string> Headers,
    DateTimeOffset ExpiresAt);

public record MediaQuery(MediaOwnerType OwnerType, Guid OwnerId);

public interface IMediaService
{
    /// <summary>
    /// Reserva el adjunto y devuelve la URL prefirmada para subir el binario directo al
    /// bucket. La fila queda sin confirmar hasta que el cliente llame a <c>ConfirmAsync</c>.
    /// </summary>
    Task<PresignedUploadDto> CreateUploadUrlAsync(CreateUploadRequest request, CancellationToken ct = default);

    /// <summary>Verifica que el objeto llegó al bucket, genera la miniatura y publica la foto.</summary>
    Task<MediaAttachmentDto> ConfirmAsync(Guid attachmentId, CancellationToken ct = default);

    Task<IReadOnlyList<MediaAttachmentDto>> ListAsync(MediaQuery query, CancellationToken ct = default);

    /// <summary>Galería completa de la orden: sus fotos y las de todos sus pasos, en una llamada.</summary>
    Task<IReadOnlyList<MediaAttachmentDto>> ListForWorkOrderAsync(Guid workOrderId, CancellationToken ct = default);

    /// <summary>
    /// Las fotos de la orden que puede ver su dueño desde el enlace de seguimiento, sin sesión.
    /// </summary>
    /// <remarks>
    /// El taller llega por parámetro y no del contexto porque aquí no hay usuario: quien llama
    /// ya lo resolvió a partir del token del enlace. Devuelve solo las confirmadas y marcadas
    /// como visibles al cliente, que es el mismo criterio que aplica el perfil Cliente.
    /// </remarks>
    Task<IReadOnlyList<MediaAttachmentDto>> ListForOrderPublicAsync(
        Guid tenantId, Guid workOrderId, CancellationToken ct = default);

    /// <summary>
    /// Las fotos de la cotización que ve el cliente en el link de WhatsApp, sin sesión. Mismo
    /// criterio que arriba: el taller llega por parámetro porque aquí no hay usuario.
    /// </summary>
    Task<IReadOnlyList<MediaAttachmentDto>> ListForQuotePublicAsync(
        Guid tenantId, Guid quoteId, CancellationToken ct = default);

    /// <summary>
    /// Las miniaturas del recurso ya descargadas, para incrustarlas en un PDF que viaja por
    /// WhatsApp y tiene que verse sin conexión. Devuelve como mucho <paramref name="max"/>.
    /// </summary>
    Task<IReadOnlyList<byte[]>> DownloadThumbnailsAsync(
        MediaOwnerType ownerType, Guid ownerId, Guid tenantId, int max, CancellationToken ct = default);

    Task DeleteAsync(Guid attachmentId, CancellationToken ct = default);

    /// <summary>
    /// Borra las fotos de estos recursos, del registro y del bucket, sin comprobar permisos.
    /// </summary>
    /// <remarks>
    /// La llama el servicio que acaba de borrar el recurso dueño de las fotos: para entonces
    /// ya comprobó quién podía hacerlo, y el recurso ya no existe para volver a comprobarlo.
    /// </remarks>
    Task PurgeAsync(
        MediaOwnerType ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken ct = default);
}

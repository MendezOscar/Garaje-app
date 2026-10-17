namespace Garaj.Application.Abstractions;

/// <summary>
/// Almacenamiento de archivos S3-compatible. La implementación es la misma para MinIO en
/// desarrollo y para R2/S3 en producción: solo cambia el endpoint en appsettings.
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// URL prefirmada para que el cliente suba el archivo directo al bucket, sin pasar el
    /// binario por la API. Es lo que permite que el móvil suba fotos en segundo plano.
    /// </summary>
    Task<PresignedUpload> CreateUploadUrlAsync(
        string key, string contentType, TimeSpan expiresIn, CancellationToken ct = default);

    /// <summary>URL temporal de lectura. Nunca se exponen objetos públicos.</summary>
    Task<string> GetDownloadUrlAsync(string key, TimeSpan expiresIn, CancellationToken ct = default);

    Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    Task<Stream> DownloadAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Lo que pesa el objeto, o <c>null</c> si todavía no está. Hace falta porque la subida va
    /// directa al bucket con una URL prefirmada: el tamaño que declara el cliente al pedirla es
    /// una promesa, y esta es la única forma de comprobar qué subió de verdad.
    /// </summary>
    Task<long?> SizeAsync(string key, CancellationToken ct = default);
}

/// <param name="Url">Destino del PUT.</param>
/// <param name="Key">Clave con la que quedará el objeto; se guarda en MediaAttachment.</param>
/// <param name="Headers">Cabeceras que el cliente debe repetir tal cual o la firma no valida.</param>
public record PresignedUpload(string Url, string Key, IDictionary<string, string> Headers, DateTimeOffset ExpiresAt);

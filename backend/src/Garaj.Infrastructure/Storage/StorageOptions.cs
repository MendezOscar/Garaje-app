namespace Garaj.Infrastructure.Storage;

/// <summary>
/// Configuración del bucket S3-compatible. Es la misma clase para MinIO en desarrollo y para
/// Cloudflare R2 en producción: solo cambian los valores.
/// </summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Endpoint del servicio. MinIO: http://localhost:9010. R2: https://{account}.r2.cloudflarestorage.com.</summary>
    public string ServiceUrl { get; set; } = "";

    /// <summary>R2 ignora la región pero la firma SigV4 la exige; "auto" es el valor que documenta Cloudflare.</summary>
    public string Region { get; set; } = "auto";

    public string Bucket { get; set; } = "garaj-media";

    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";

    /// <summary>MinIO y R2 usan rutas (bucket en el path), no subdominios por bucket.</summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>
    /// Vigencia de las URL prefirmadas. Corta a propósito: la URL de lectura viaja en JSON y
    /// puede quedar en caché o en un log, así que caduca sola. El móvil pide otra si hace falta.
    /// </summary>
    public int PresignedUrlMinutes { get; set; } = 15;

    /// <summary>Tope por archivo. Una foto de taller comprimida no llega ni a 1 MB.</summary>
    public long MaxUploadBytes { get; set; } = 15 * 1024 * 1024;

    /// <summary>Lado mayor de la miniatura, en píxeles.</summary>
    public int ThumbnailSize { get; set; } = 400;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServiceUrl)
        && !string.IsNullOrWhiteSpace(AccessKey)
        && !string.IsNullOrWhiteSpace(SecretKey);
}

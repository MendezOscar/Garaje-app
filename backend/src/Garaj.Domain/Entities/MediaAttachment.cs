using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Foto o archivo de evidencia. El binario vive en el almacenamiento S3-compatible; aquí solo
/// queda la clave. Nunca se expone una URL pública: se sirve por URL prefirmada temporal.
/// </summary>
public class MediaAttachment : TenantEntity
{
    /// <summary>A qué se adjunta. Relación polimórfica: no hay FK, se valida en la aplicación.</summary>
    public MediaOwnerType OwnerType { get; set; }

    public Guid OwnerId { get; set; }

    /// <summary>Ruta del objeto en el bucket, ej. "tenants/{tenantId}/work-orders/{id}/{guid}.jpg".</summary>
    public string StorageKey { get; set; } = null!;

    /// <summary>Clave de la miniatura. Null hasta que el generador la procesa.</summary>
    public string? ThumbnailKey { get; set; }

    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string? OriginalFileName { get; set; }
    public string? Caption { get; set; }

    public Guid UploadedByUserId { get; set; }

    /// <summary>Fecha real de la toma en el dispositivo; puede ser anterior a la subida si venía en cola offline.</summary>
    public DateTimeOffset TakenAt { get; set; }

    /// <summary>False mientras la subida a S3 no se confirma; las no confirmadas se purgan.</summary>
    public bool IsConfirmed { get; set; }

    /// <summary>Si es false, la foto solo la ve el personal del taller.</summary>
    public bool IsVisibleToCustomer { get; set; } = true;
}

using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Aviso dirigido a una persona concreta del taller —o a un cliente— sobre algo que pasó.
/// </summary>
/// <remarks>
/// Se guarda aunque el envío push falle o el usuario no tenga la app instalada: la campana
/// dentro de la aplicación es el canal principal, y el push es solo un empujón para que la
/// abra. Al revés —fiarlo todo al push— un aviso perdido no se recupera nunca.
/// </remarks>
public class Notification : TenantEntity
{
    /// <summary>Destinatario. Un mismo hecho genera una fila por cada persona avisada.</summary>
    public Guid RecipientUserId { get; set; }

    public NotificationType Type { get; set; }

    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;

    // Referencias opcionales: el cliente decide a qué pantalla navegar con la primera que
    // venga informada. No son FK con borrado en cascada a propósito —un aviso viejo sigue
    // teniendo sentido como registro aunque la orden ya no exista.
    public Guid? WorkOrderId { get; set; }
    public Guid? QuoteId { get; set; }
    public Guid? ServiceRequestId { get; set; }

    /// <summary>Null mientras no se haya leído. Guarda el cuándo, no solo el sí/no.</summary>
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>
/// Token de un dispositivo para recibir push. Un usuario puede tener varios —el teléfono y
/// la tableta del taller— y el mismo teléfono puede cambiar de token cuando el sistema lo
/// renueva, así que el token es la clave y la fila se reasigna al usuario que inicie sesión.
/// </summary>
public class DeviceToken : TenantEntity
{
    public Guid UserId { get; set; }

    /// <summary>Token del proveedor de push (FCM). Único en todo el sistema.</summary>
    public string Token { get; set; } = null!;

    public DevicePlatform Platform { get; set; }

    /// <summary>
    /// Se refresca en cada arranque de la app. Sirve para purgar dispositivos que llevan
    /// meses sin aparecer: seguir empujando avisos a un teléfono muerto cuesta y no llega.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; set; }
}

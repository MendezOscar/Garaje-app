using Garaj.Application.Common;
using Garaj.Domain.Enums;

namespace Garaj.Application.Notifications;

public record NotificationDto(
    Guid Id,
    NotificationType Type,
    string Title,
    string Body,
    Guid? WorkOrderId,
    Guid? QuoteId,
    Guid? ServiceRequestId,
    bool IsRead,
    DateTimeOffset CreatedAt);

public record NotificationQuery : PageQuery
{
    public bool OnlyUnread { get; init; }
}

public record RegisterDeviceRequest(string Token, DevicePlatform Platform);

/// <summary>La campana de la aplicación: lo que ve y marca el usuario de la petición.</summary>
public interface INotificationService
{
    Task<PagedResult<NotificationDto>> ListAsync(NotificationQuery query, CancellationToken ct = default);
    Task<int> UnreadCountAsync(CancellationToken ct = default);
    Task MarkReadAsync(Guid id, CancellationToken ct = default);
    Task<int> MarkAllReadAsync(CancellationToken ct = default);
    Task RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct = default);
    Task UnregisterDeviceAsync(string token, CancellationToken ct = default);
}

/// <summary>Contenido de un aviso, sin destinatario todavía.</summary>
public record NotificationDraft(
    NotificationType Type,
    string Title,
    string Body,
    Guid? WorkOrderId = null,
    Guid? QuoteId = null,
    Guid? ServiceRequestId = null);

/// <summary>
/// Emite avisos desde los servicios de negocio.
/// </summary>
/// <remarks>
/// El tenant va explícito y no se toma del contexto de la petición: la respuesta pública a
/// una cotización llega sin sesión, y ahí el único que sabe de qué taller se trata es el
/// propio registro. Ninguna de estas llamadas propaga excepciones —quedarse sin avisar es
/// molesto, pero tumbar la operación que lo provocó es peor.
/// </remarks>
public interface INotificationPublisher
{
    Task NotifyUserAsync(Guid tenantId, Guid userId, NotificationDraft draft, CancellationToken ct = default);

    /// <summary>Avisa a todos los Dueños activos del taller.</summary>
    Task NotifyOwnersAsync(Guid tenantId, NotificationDraft draft, CancellationToken ct = default);

    /// <summary>Avisa al cliente, si tiene usuario de la app. Si no lo tiene, no hace nada.</summary>
    Task NotifyCustomerAsync(Guid tenantId, Guid customerId, NotificationDraft draft, CancellationToken ct = default);
}

/// <summary>Envío push al dispositivo. Detrás hay FCM, pero el dominio no lo sabe.</summary>
public interface IPushSender
{
    /// <summary>False cuando no hay proyecto de Firebase configurado: el aviso solo queda en la campana.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Devuelve los tokens que el proveedor rechazó por muertos, para poder borrarlos.
    /// </summary>
    Task<IReadOnlyCollection<string>> SendAsync(
        IReadOnlyCollection<string> deviceTokens, NotificationDraft draft, CancellationToken ct = default);
}

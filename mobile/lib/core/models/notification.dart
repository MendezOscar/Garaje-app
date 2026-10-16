// Espejo de los DTOs de la Fase 6.

/// Motivo del aviso. Los enteros son los de `Garaj.Domain.Enums.NotificationType`.
enum AppNotificationType {
  serviceRequestCreated(1, '📥'),
  workOrderAssigned(2, '🔧'),
  workOrderStatusChanged(3, '🚗'),
  quoteSent(4, '📄'),
  quoteAnswered(5, '✅'),
  appointmentRequested(6, '📅'),
  appointmentAnswered(7, '📅'),
  unknown(0, '🔔');

  const AppNotificationType(this.value, this.icon);

  final int value;
  final String icon;

  /// Un valor que esta versión de la app no conoce cae en `unknown` en vez de reventar:
  /// el servidor se actualiza antes que los teléfonos, y un aviso nuevo no debe romper
  /// la lista entera de avisos viejos.
  static AppNotificationType from(int value) =>
      AppNotificationType.values.firstWhere(
        (t) => t.value == value,
        orElse: () => AppNotificationType.unknown,
      );
}

class AppNotification {
  const AppNotification({
    required this.id,
    required this.type,
    required this.title,
    required this.body,
    required this.isRead,
    required this.createdAt,
    this.workOrderId,
    this.quoteId,
    this.serviceRequestId,
    this.appointmentId,
  });

  factory AppNotification.fromJson(Map<String, dynamic> json) => AppNotification(
        id: json['id'] as String,
        type: AppNotificationType.from(json['type'] as int),
        title: json['title'] as String,
        body: json['body'] as String,
        workOrderId: json['workOrderId'] as String?,
        quoteId: json['quoteId'] as String?,
        serviceRequestId: json['serviceRequestId'] as String?,
        appointmentId: json['appointmentId'] as String?,
        isRead: json['isRead'] as bool,
        createdAt: DateTime.parse(json['createdAt'] as String).toLocal(),
      );

  final String id;
  final AppNotificationType type;
  final String title;
  final String body;
  final String? workOrderId;
  final String? quoteId;
  final String? serviceRequestId;
  final String? appointmentId;
  final bool isRead;
  final DateTime createdAt;

  AppNotification copyWith({bool? isRead}) => AppNotification(
        id: id,
        type: type,
        title: title,
        body: body,
        workOrderId: workOrderId,
        quoteId: quoteId,
        serviceRequestId: serviceRequestId,
        appointmentId: appointmentId,
        isRead: isRead ?? this.isRead,
        createdAt: createdAt,
      );
}

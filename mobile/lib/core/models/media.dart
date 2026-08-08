// Espejo de los DTOs de la Fase 2. Los valores numéricos coinciden con Garaj.Domain.Enums.

enum MediaOwnerType {
  serviceRequest(1),
  workOrder(2),
  workOrderTask(3);

  const MediaOwnerType(this.value);

  final int value;

  static MediaOwnerType fromValue(int value) =>
      MediaOwnerType.values.firstWhere((t) => t.value == value,
          orElse: () => MediaOwnerType.workOrder);
}

class MediaAttachment {
  const MediaAttachment({
    required this.id,
    required this.ownerType,
    required this.ownerId,
    required this.url,
    required this.thumbnailUrl,
    required this.uploadedByName,
    required this.takenAt,
    required this.isVisibleToCustomer,
    this.caption,
    this.taskTitle,
  });

  factory MediaAttachment.fromJson(Map<String, dynamic> json) => MediaAttachment(
        id: json['id'] as String,
        ownerType: MediaOwnerType.fromValue(json['ownerType'] as int),
        ownerId: json['ownerId'] as String,
        url: json['url'] as String,
        thumbnailUrl: json['thumbnailUrl'] as String,
        caption: json['caption'] as String?,
        uploadedByName: json['uploadedByName'] as String,
        takenAt: DateTime.parse(json['takenAt'] as String),
        isVisibleToCustomer: json['isVisibleToCustomer'] as bool,
        taskTitle: json['taskTitle'] as String?,
      );

  final String id;
  final MediaOwnerType ownerType;
  final String ownerId;

  /// URL prefirmada temporal. Caduca, así que no se cachea ni se comparte fuera de la app.
  final String url;
  final String thumbnailUrl;
  final String? caption;
  final String uploadedByName;
  final DateTime takenAt;
  final bool isVisibleToCustomer;
  final String? taskTitle;
}

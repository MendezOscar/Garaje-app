// Espejo de los DTOs de la Fase 2. Los valores numéricos coinciden con Garaj.Domain.Enums.

enum MediaOwnerType {
  serviceRequest(1),
  workOrder(2),
  workOrderTask(3),

  /// Fotos del daño que justifican el presupuesto. Las ve el cliente en su cotización.
  quote(4),

  /// El comprobante del gasto. Es lo que lo respalda cuando alguien lo discute.
  expense(5),

  /// Cómo entró el vehículo o el equipo. Son las que salen en la ficha de recepción.
  reception(6);

  const MediaOwnerType(this.value);

  final int value;

  static MediaOwnerType fromValue(int value) =>
      MediaOwnerType.values.firstWhere((t) => t.value == value,
          orElse: () => MediaOwnerType.workOrder);
}

/// Una foto de la orden que se puede traer a la cotización, y si ya se trajo.
///
/// El técnico fotografía en la orden —a Cotizaciones no entra, y no tiene por qué: ahí están
/// los precios—. Esto es lo que deja que su evidencia llegue al cliente sin volver a subirla.
class QuotePhotoCandidate {
  const QuotePhotoCandidate({required this.photo, required this.alreadyCopied});

  factory QuotePhotoCandidate.fromJson(Map<String, dynamic> json) => QuotePhotoCandidate(
        photo: MediaAttachment.fromJson(json['photo'] as Map<String, dynamic>),
        alreadyCopied: json['alreadyCopied'] as bool,
      );

  final MediaAttachment photo;
  final bool alreadyCopied;
}

class MediaAttachment {
  const MediaAttachment({
    required this.id,
    required this.ownerType,
    required this.ownerId,
    required this.url,
    required this.thumbnailUrl,
    required this.contentType,
    required this.sizeBytes,
    required this.uploadedByName,
    required this.takenAt,
    required this.isVisibleToCustomer,
    this.fileName,
    this.caption,
    this.taskTitle,
  });

  factory MediaAttachment.fromJson(Map<String, dynamic> json) => MediaAttachment(
        id: json['id'] as String,
        ownerType: MediaOwnerType.fromValue(json['ownerType'] as int),
        ownerId: json['ownerId'] as String,
        url: json['url'] as String,
        thumbnailUrl: json['thumbnailUrl'] as String,
        contentType: json['contentType'] as String? ?? 'image/jpeg',
        sizeBytes: (json['sizeBytes'] as num?)?.toInt() ?? 0,
        fileName: json['fileName'] as String?,
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

  /// Qué es el archivo. Lo que no empieza por `image/` es un documento —el informe del
  /// escáner— y se dibuja como tal: no tiene miniatura que enseñar.
  final String contentType;
  final int sizeBytes;

  /// Cómo se llamaba el archivo al llegar. Es lo que identifica un informe en la lista.
  final String? fileName;
  final String? caption;

  bool get esImagen => contentType.startsWith('image/');
  final String uploadedByName;
  final DateTime takenAt;
  final bool isVisibleToCustomer;
  final String? taskTitle;
}

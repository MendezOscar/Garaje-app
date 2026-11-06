// Espejo de los DTOs de VehicleScans. Los valores numéricos coinciden con
// Garaj.Domain.Enums.DtcStatus.

/// En qué memoria del vehículo estaba el código. No significan lo mismo para quien repara:
/// una confirmada se atiende hoy, una pendiente puede ser un falso positivo de un arranque.
enum DtcStatus {
  stored(1, 'Confirmada'),
  pending(2, 'Pendiente'),
  permanent(3, 'Permanente');

  const DtcStatus(this.value, this.etiqueta);

  final int value;
  final String etiqueta;

  static DtcStatus fromValue(int value) =>
      DtcStatus.values.firstWhere((s) => s.value == value, orElse: () => DtcStatus.stored);
}

class VehicleScanCode {
  const VehicleScanCode({
    required this.code,
    required this.status,
    required this.isGeneric,
    this.description,
  });

  factory VehicleScanCode.fromJson(Map<String, dynamic> json) => VehicleScanCode(
        code: json['code'] as String,
        status: DtcStatus.fromValue(json['status'] as int),
        description: json['description'] as String?,
        isGeneric: json['isGeneric'] as bool? ?? true,
      );

  final String code;
  final DtcStatus status;

  /// Qué significa, en español. Null en los códigos propios del fabricante: esa información
  /// la da el escáner de la marca y no se inventa.
  final String? description;

  final bool isGeneric;
}

/// Una lectura del vehículo con el adaptador OBD2.
class VehicleScan {
  const VehicleScan({
    required this.id,
    required this.workOrderId,
    required this.scannedAt,
    required this.scannedByName,
    required this.codes,
    this.vin,
    this.mileage,
    this.deviceName,
    this.protocol,
    this.notes,
    this.attachmentId,
  });

  factory VehicleScan.fromJson(Map<String, dynamic> json) => VehicleScan(
        id: json['id'] as String,
        workOrderId: json['workOrderId'] as String,
        scannedAt: DateTime.parse(json['scannedAt'] as String),
        scannedByName: json['scannedByName'] as String,
        codes: (json['codes'] as List<dynamic>? ?? [])
            .map((e) => VehicleScanCode.fromJson(e as Map<String, dynamic>))
            .toList(),
        vin: json['vin'] as String?,
        mileage: (json['mileage'] as num?)?.toInt(),
        deviceName: json['deviceName'] as String?,
        protocol: json['protocol'] as String?,
        notes: json['notes'] as String?,
        attachmentId: json['attachmentId'] as String?,
      );

  final String id;
  final String workOrderId;
  final DateTime scannedAt;
  final String scannedByName;
  final List<VehicleScanCode> codes;
  final String? vin;
  final int? mileage;
  final String? deviceName;
  final String? protocol;
  final String? notes;

  /// El informe en PDF que quedó adjunto a la orden. Null si no se pudo armar.
  final String? attachmentId;
}

/// Dónde ha salido un código en el taller. Es lo que el informe adjunto no podía dar.
class VehicleScanHit {
  const VehicleScanHit({
    required this.scanId,
    required this.workOrderId,
    required this.workOrderNumber,
    required this.scannedAt,
    required this.code,
    required this.status,
    required this.vehicleLabel,
    required this.customerName,
    this.description,
    this.vehicleId,
    this.plate,
  });

  factory VehicleScanHit.fromJson(Map<String, dynamic> json) => VehicleScanHit(
        scanId: json['scanId'] as String,
        workOrderId: json['workOrderId'] as String,
        workOrderNumber: json['workOrderNumber'] as String,
        scannedAt: DateTime.parse(json['scannedAt'] as String),
        code: json['code'] as String,
        status: DtcStatus.fromValue(json['status'] as int),
        description: json['description'] as String?,
        vehicleId: json['vehicleId'] as String?,
        plate: json['plate'] as String?,
        vehicleLabel: json['vehicleLabel'] as String? ?? '—',
        customerName: json['customerName'] as String? ?? '—',
      );

  final String scanId;
  final String workOrderId;
  final String workOrderNumber;
  final DateTime scannedAt;
  final String code;
  final DtcStatus status;
  final String? description;
  final String? vehicleId;
  final String? plate;
  final String vehicleLabel;
  final String customerName;
}

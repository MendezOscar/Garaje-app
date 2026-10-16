import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import 'sucursal_activa.dart';

/// La franja del día. Son dos a propósito: un taller no cumple una hora exacta.
enum AppointmentSlot {
  morning(1, 'Por la mañana'),
  afternoon(2, 'Por la tarde');

  const AppointmentSlot(this.value, this.label);

  final int value;
  final String label;

  static AppointmentSlot fromValue(int value) =>
      AppointmentSlot.values.firstWhere((s) => s.value == value, orElse: () => morning);
}

enum AppointmentStatus {
  requested(1, 'Por confirmar'),
  confirmed(2, 'Confirmada'),
  attended(3, 'Atendida'),
  rejected(4, 'Rechazada'),
  cancelled(5, 'Cancelada');

  const AppointmentStatus(this.value, this.label);

  final int value;
  final String label;

  static AppointmentStatus fromValue(int value) =>
      AppointmentStatus.values.firstWhere((s) => s.value == value, orElse: () => requested);
}

enum AppointmentSource {
  publicLink(1, 'Por el enlace'),
  customerApp(2, 'Desde la app'),
  workshop(3, 'La anotó el taller');

  const AppointmentSource(this.value, this.label);

  final int value;
  final String label;

  static AppointmentSource fromValue(int value) =>
      AppointmentSource.values.firstWhere((s) => s.value == value, orElse: () => publicLink);
}

/// Una cita: un día apartado, antes de que exista orden alguna.
class AppointmentItem {
  const AppointmentItem({
    required this.id,
    required this.branchId,
    required this.branchName,
    required this.contactName,
    required this.contactPhone,
    required this.vehicleLabel,
    this.vehicleId,
    required this.reason,
    required this.day,
    required this.slot,
    required this.status,
    required this.source,
    this.responseNote,
    this.workOrderId,
  });

  factory AppointmentItem.fromJson(Map<String, dynamic> json) => AppointmentItem(
        id: json['id'] as String,
        branchId: json['branchId'] as String,
        branchName: json['branchName'] as String,
        contactName: json['contactName'] as String,
        contactPhone: json['contactPhone'] as String,
        vehicleLabel: json['vehicleLabel'] as String,
        vehicleId: json['vehicleId'] as String?,
        reason: json['reason'] as String,
        // Llega como «2026-10-20»: se parte a mano, que `DateTime.parse` la leería como
        // medianoche UTC y en Honduras pintaría el día anterior.
        day: _dia(json['day'] as String),
        slot: AppointmentSlot.fromValue(json['slot'] as int),
        status: AppointmentStatus.fromValue(json['status'] as int),
        source: AppointmentSource.fromValue(json['source'] as int),
        responseNote: json['responseNote'] as String?,
        workOrderId: json['workOrderId'] as String?,
      );

  final String id;
  final String branchId;
  final String branchName;
  final String contactName;
  final String contactPhone;
  final String vehicleLabel;

  /// El vehículo del padrón. Null en la cita pedida por el enlace: ahí solo hay texto.
  final String? vehicleId;
  final String reason;
  final DateTime day;
  final AppointmentSlot slot;
  final AppointmentStatus status;
  final AppointmentSource source;
  final String? responseNote;
  final String? workOrderId;

  bool get esperaRespuesta => status == AppointmentStatus.requested;

  bool get esDeHoy {
    final hoy = DateTime.now();
    return day.year == hoy.year && day.month == hoy.month && day.day == hoy.day;
  }
}

DateTime _dia(String valor) {
  final partes = valor.substring(0, 10).split('-').map(int.parse).toList();
  return DateTime(partes[0], partes[1], partes[2]);
}

/// El día como lo espera la API: «2026-10-20», sin hora y sin huso.
String diaApi(DateTime dia) =>
    '${dia.year}-${dia.month.toString().padLeft(2, '0')}-${dia.day.toString().padLeft(2, '0')}';

final appointmentRepositoryProvider = Provider<AppointmentRepository>(
  (ref) => AppointmentRepository(ref.watch(apiClientProvider).dio),
);

class AppointmentRepository {
  AppointmentRepository(this._dio);

  final Dio _dio;

  /// La agenda. La API ya la recorta por perfil: el Cliente ve las suyas y el Encargado las
  /// de sus sucursales.
  Future<List<AppointmentItem>> list({String? branchId, bool includeClosed = false}) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/appointments',
      queryParameters: {
        'pageSize': 100,
        if (branchId != null) 'branchId': branchId,
        if (includeClosed) 'includeClosed': true,
      },
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => AppointmentItem.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// La pide el Cliente sobre uno de sus vehículos.
  Future<AppointmentItem> request({
    required String branchId,
    required String vehicleId,
    required String reason,
    required DateTime day,
    required AppointmentSlot slot,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/appointments/request',
      data: {
        'branchId': branchId,
        'vehicleId': vehicleId,
        'reason': reason,
        'day': diaApi(day),
        'slot': slot.value,
      },
    );

    return AppointmentItem.fromJson(response.data!);
  }

  /// La anota el taller, por teléfono o en el mostrador. Nace confirmada.
  Future<AppointmentItem> create({
    required String branchId,
    required String contactName,
    required String contactPhone,
    required String reason,
    required DateTime day,
    required AppointmentSlot slot,
    String? vehicleNote,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/appointments',
      data: {
        'branchId': branchId,
        'contactName': contactName,
        'contactPhone': contactPhone,
        'vehicleNote': vehicleNote,
        'reason': reason,
        'day': diaApi(day),
        'slot': slot.value,
      },
    );

    return AppointmentItem.fromJson(response.data!);
  }

  Future<void> confirm(String id, {DateTime? day, AppointmentSlot? slot, String? note}) async {
    await _dio.post<Map<String, dynamic>>(
      '/api/appointments/$id/confirm',
      data: {
        'day': day == null ? null : diaApi(day),
        'slot': slot?.value,
        'note': note,
      },
    );
  }

  Future<void> reject(String id, String reason) async {
    await _dio.post<Map<String, dynamic>>(
      '/api/appointments/$id/reject',
      data: {'reason': reason},
    );
  }

  /// Llegó el vehículo: la cita se cierra y deja de pesar en la agenda.
  Future<void> attended(String id, {String? workOrderId}) async {
    await _dio.post<Map<String, dynamic>>(
      '/api/appointments/$id/attended',
      data: {'workOrderId': workOrderId},
    );
  }
}

/// La agenda del taller, por la sucursal que se esté mirando.
final appointmentsProvider = FutureProvider.autoDispose<List<AppointmentItem>>(
  (ref) => ref
      .watch(appointmentRepositoryProvider)
      .list(branchId: ref.watch(sucursalActivaProvider)),
);

/// Las citas del Cliente. Van aparte porque su pantalla no filtra por sucursal.
final misCitasProvider = FutureProvider.autoDispose<List<AppointmentItem>>(
  (ref) => ref.watch(appointmentRepositoryProvider).list(),
);

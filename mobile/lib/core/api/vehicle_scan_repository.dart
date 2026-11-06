import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../models/vehicle_scan.dart';
import '../scanner/elm327.dart';

final vehicleScanRepositoryProvider = Provider<VehicleScanRepository>(
  (ref) => VehicleScanRepository(ref.watch(apiClientProvider).dio),
);

/// Las lecturas del adaptador OBD2.
class VehicleScanRepository {
  VehicleScanRepository(this._dio);

  final Dio _dio;

  /// Sube la lectura. El servidor arma el informe en PDF y lo deja adjunto a la orden.
  Future<VehicleScan> create({
    required String workOrderId,
    required DateTime scannedAt,
    required List<CodigoLeido> codigos,
    String? vin,
    int? mileage,
    String? deviceName,
    String? protocol,
    String? notes,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/vehicle-scans',
      data: {
        'workOrderId': workOrderId,
        'scannedAt': scannedAt.toUtc().toIso8601String(),
        'codes': codigos.map((c) => c.toJson()).toList(),
        'vin': vin,
        'mileage': mileage,
        'deviceName': deviceName,
        'protocol': protocol,
        'notes': notes,
      },
    );

    return VehicleScan.fromJson(response.data!);
  }

  Future<List<VehicleScan>> listForWorkOrder(String workOrderId) async {
    final response =
        await _dio.get<List<dynamic>>('/api/vehicle-scans/work-order/$workOrderId');

    return response.data!
        .map((e) => VehicleScan.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// El historial del vehículo: qué ha reportado cada vez que entró al taller.
  Future<List<VehicleScan>> listForVehicle(String vehicleId) async {
    final response = await _dio.get<List<dynamic>>('/api/vehicle-scans/vehicle/$vehicleId');

    return response.data!
        .map((e) => VehicleScan.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<List<VehicleScanHit>> search(String code) async {
    final response = await _dio.get<List<dynamic>>(
      '/api/vehicle-scans/search',
      queryParameters: {'code': code},
    );

    return response.data!
        .map((e) => VehicleScanHit.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<void> delete(String id) => _dio.delete<void>('/api/vehicle-scans/$id');
}

/// Las lecturas de una orden, para la ficha. Se recarga sola al guardar una nueva.
final vehicleScansProvider =
    FutureProvider.autoDispose.family<List<VehicleScan>, String>((ref, workOrderId) =>
        ref.watch(vehicleScanRepositoryProvider).listForWorkOrder(workOrderId));

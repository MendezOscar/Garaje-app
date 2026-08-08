import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';

/// Vehículo del cliente, en lo mínimo que hace falta para elegirlo en una lista.
class VehicleOption {
  const VehicleOption({required this.id, required this.label, this.mileage});

  factory VehicleOption.fromJson(Map<String, dynamic> json) => VehicleOption(
        id: json['id'] as String,
        label: [
          json['brand'] as String,
          json['model'] as String,
          if (json['plate'] != null) '· ${json['plate']}',
        ].join(' '),
        mileage: json['mileage'] as int?,
      );

  final String id;
  final String label;
  final int? mileage;
}

class BranchOption {
  const BranchOption({required this.id, required this.name});

  factory BranchOption.fromJson(Map<String, dynamic> json) =>
      BranchOption(id: json['id'] as String, name: json['name'] as String);

  final String id;
  final String name;
}

final serviceRequestRepositoryProvider = Provider<ServiceRequestRepository>(
  (ref) => ServiceRequestRepository(ref.watch(apiClientProvider).dio),
);

class ServiceRequestRepository {
  ServiceRequestRepository(this._dio);

  final Dio _dio;

  /// Los vehículos que el usuario puede elegir. Al Cliente la API le devuelve solo los
  /// suyos, así que aquí no hay que filtrar nada.
  Future<List<VehicleOption>> vehicles() async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/vehicles',
      queryParameters: {'pageSize': 50},
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => VehicleOption.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<List<BranchOption>> branches() async {
    final response = await _dio.get<List<dynamic>>('/api/branches');

    return response.data!
        .map((e) => BranchOption.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Devuelve el id del requerimiento creado, que es lo que hace falta para colgarle fotos.
  Future<String> create({
    required String branchId,
    required String vehicleId,
    required String description,
    String? reportedSymptoms,
    DateTime? preferredDate,
    int? mileage,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/service-requests',
      data: {
        'branchId': branchId,
        'vehicleId': vehicleId,
        'description': description,
        'reportedSymptoms': reportedSymptoms,
        'preferredDate': preferredDate?.toUtc().toIso8601String(),
        'mileage': mileage,
      },
    );

    return response.data!['id'] as String;
  }
}

final vehicleOptionsProvider =
    FutureProvider.autoDispose<List<VehicleOption>>((ref) => ref.watch(serviceRequestRepositoryProvider).vehicles());

final branchOptionsProvider =
    FutureProvider.autoDispose<List<BranchOption>>((ref) => ref.watch(serviceRequestRepositoryProvider).branches());

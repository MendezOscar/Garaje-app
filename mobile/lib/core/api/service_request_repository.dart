import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';

/// Vehículo del cliente, en lo mínimo que hace falta para elegirlo en una lista.
class VehicleOption {
  const VehicleOption({
    required this.id,
    required this.label,
    required this.customerName,
    required this.searchTerm,
    this.mileage,
  });

  factory VehicleOption.fromJson(Map<String, dynamic> json) => VehicleOption(
        id: json['id'] as String,
        label: [
          json['brand'] as String,
          json['model'] as String,
          if (json['plate'] != null) '· ${json['plate']}',
        ].join(' '),
        customerName: json['customerName'] as String? ?? '',
        searchTerm: (json['plate'] ?? json['brand']) as String,
        mileage: json['mileage'] as int?,
      );

  final String id;
  final String label;

  /// Con qué buscarlo para volver a encontrarlo: la placa, o la marca si no tiene.
  final String searchTerm;

  /// Vacío para el Cliente, que solo ve los suyos; el taller necesita saber de quién es.
  final String customerName;
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
  /// suyos, así que aquí no hay que filtrar nada; al taller, todos los del negocio, y por
  /// eso admite búsqueda por placa, marca o nombre del dueño.
  Future<List<VehicleOption>> vehicles({String? search}) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/vehicles',
      queryParameters: {
        'pageSize': 50,
        if (search != null && search.trim().isNotEmpty) 'search': search.trim(),
      },
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => VehicleOption.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Da de alta cliente y vehículo de una vez: es lo que pasa en el mostrador cuando llega
  /// alguien que nunca ha venido. Devuelve el vehículo, que es lo que hace falta después.
  Future<VehicleOption> registerCustomerAndVehicle({
    required String fullName,
    required String phone,
    required int vehicleType,
    required String brand,
    required String model,
    String? plate,
  }) async {
    final customer = await _dio.post<Map<String, dynamic>>(
      '/api/customers',
      data: {'fullName': fullName, 'phone': phone},
    );

    final vehicle = await _dio.post<Map<String, dynamic>>(
      '/api/vehicles',
      data: {
        'customerId': customer.data!['id'],
        'type': vehicleType,
        'brand': brand,
        'model': model,
        'plate': plate,
      },
    );

    return VehicleOption.fromJson(vehicle.data!);
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

/// Parametrizado por el texto de búsqueda: cadena vacía es "los primeros que haya", que es
/// todo lo que necesita el Cliente, y con texto es la búsqueda del mostrador.
final vehicleOptionsProvider =
    FutureProvider.autoDispose.family<List<VehicleOption>, String>(
  (ref, search) => ref.watch(serviceRequestRepositoryProvider).vehicles(search: search),
);

final branchOptionsProvider =
    FutureProvider.autoDispose<List<BranchOption>>((ref) => ref.watch(serviceRequestRepositoryProvider).branches());

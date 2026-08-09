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

enum ServiceRequestStatus {
  pending(1, 'Pendiente'),
  quoted(2, 'Cotizado'),
  approved(3, 'Aprobado'),
  rejected(4, 'Rechazado'),
  converted(5, 'Convertido en orden');

  const ServiceRequestStatus(this.value, this.label);

  final int value;
  final String label;

  static ServiceRequestStatus fromValue(int value) =>
      ServiceRequestStatus.values.firstWhere((s) => s.value == value, orElse: () => pending);
}

/// Un requerimiento en la bandeja del taller.
class ServiceRequestItem {
  const ServiceRequestItem({
    required this.id,
    required this.branchId,
    required this.branchName,
    required this.vehicleLabel,
    required this.customerName,
    required this.customerPhone,
    required this.description,
    required this.status,
    required this.createdAt,
    this.reportedSymptoms,
    this.preferredDate,
    this.mileage,
    this.rejectionReason,
    this.workOrderId,
    this.workOrderNumber,
  });

  factory ServiceRequestItem.fromJson(Map<String, dynamic> json) => ServiceRequestItem(
        id: json['id'] as String,
        branchId: json['branchId'] as String,
        branchName: json['branchName'] as String,
        vehicleLabel: json['vehicleLabel'] as String,
        customerName: json['customerName'] as String,
        customerPhone: json['customerPhone'] as String,
        description: json['description'] as String,
        status: ServiceRequestStatus.fromValue(json['status'] as int),
        createdAt: DateTime.parse(json['createdAt'] as String),
        reportedSymptoms: json['reportedSymptoms'] as String?,
        preferredDate: json['preferredDate'] == null
            ? null
            : DateTime.parse(json['preferredDate'] as String),
        mileage: json['mileage'] as int?,
        rejectionReason: json['rejectionReason'] as String?,
        workOrderId: json['workOrderId'] as String?,
        workOrderNumber: json['workOrderNumber'] as String?,
      );

  final String id;
  final String branchId;
  final String branchName;
  final String vehicleLabel;
  final String customerName;
  final String customerPhone;
  final String description;
  final ServiceRequestStatus status;
  final DateTime createdAt;
  final String? reportedSymptoms;
  final DateTime? preferredDate;
  final int? mileage;
  final String? rejectionReason;
  final String? workOrderId;
  final String? workOrderNumber;

  bool get isPending => status == ServiceRequestStatus.pending;
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

  /// La bandeja. La API ya la recorta por perfil: el Técnico ve la de sus sucursales y el
  /// Cliente solo lo de sus vehículos, así que aquí no hay que filtrar nada.
  Future<List<ServiceRequestItem>> list() async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/service-requests',
      queryParameters: {'pageSize': 100},
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => ServiceRequestItem.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Lo convierte en orden de trabajo y devuelve el id de la orden creada.
  Future<String> approve(String id, {String? technicianId}) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/service-requests/$id/approve',
      data: {'assignedTechnicianId': technicianId},
    );

    return response.data!['workOrderId'] as String;
  }

  Future<void> reject(String id, String reason) async {
    await _dio.post<Map<String, dynamic>>(
      '/api/service-requests/$id/reject',
      data: {'reason': reason},
    );
  }
}

/// `autoDispose` para que al volver de aprobar uno se recargue: si no, el requerimiento
/// recién convertido seguiría apareciendo como pendiente.
final serviceRequestsProvider = FutureProvider.autoDispose<List<ServiceRequestItem>>(
  (ref) => ref.watch(serviceRequestRepositoryProvider).list(),
);

/// Parametrizado por el texto de búsqueda: cadena vacía es "los primeros que haya", que es
/// todo lo que necesita el Cliente, y con texto es la búsqueda del mostrador.
final vehicleOptionsProvider =
    FutureProvider.autoDispose.family<List<VehicleOption>, String>(
  (ref, search) => ref.watch(serviceRequestRepositoryProvider).vehicles(search: search),
);

final branchOptionsProvider =
    FutureProvider.autoDispose<List<BranchOption>>((ref) => ref.watch(serviceRequestRepositoryProvider).branches());

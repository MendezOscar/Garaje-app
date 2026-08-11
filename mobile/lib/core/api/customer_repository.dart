import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../models/work_order.dart';

/// Alguien del padrón del taller. El acceso a la app es opcional y nace de aquí: así todo
/// usuario con perfil Cliente corresponde a una ficha, y no al revés.
class Customer {
  const Customer({
    required this.id,
    required this.fullName,
    required this.phone,
    required this.isActive,
    required this.vehicleCount,
    required this.hasAppAccess,
    this.email,
    this.address,
    this.notes,
    this.appUserEmail,
    this.taxId,
  });

  factory Customer.fromJson(Map<String, dynamic> json) => Customer(
        id: json['id'] as String,
        fullName: json['fullName'] as String,
        phone: json['phone'] as String,
        email: json['email'] as String?,
        taxId: json['taxId'] as String?,
        address: json['address'] as String?,
        notes: json['notes'] as String?,
        isActive: json['isActive'] as bool? ?? true,
        vehicleCount: json['vehicleCount'] as int? ?? 0,
        hasAppAccess: json['hasAppAccess'] as bool? ?? false,
        appUserEmail: json['appUserEmail'] as String?,
      );

  final String id;
  final String fullName;
  final String phone;
  final String? email;
  final String? address;
  final String? notes;
  final bool isActive;
  final int vehicleCount;
  final bool hasAppAccess;
  final String? appUserEmail;

  /// RTN, para la factura con CAI. Se escribe desde el panel; aquí solo se muestra.
  final String? taxId;
}

/// Un vehículo del cliente, tal como sale del padrón.
class CustomerVehicle {
  const CustomerVehicle({
    required this.id,
    required this.type,
    required this.brand,
    required this.model,
    this.year,
    this.plate,
    this.mileage,
  });

  factory CustomerVehicle.fromJson(Map<String, dynamic> json) => CustomerVehicle(
        id: json['id'] as String,
        type: VehicleType.fromValue(json['type'] as int),
        brand: json['brand'] as String,
        model: json['model'] as String,
        year: json['year'] as int?,
        plate: json['plate'] as String?,
        mileage: json['mileage'] as int?,
      );

  final String id;
  final VehicleType type;
  final String brand;
  final String model;
  final int? year;
  final String? plate;
  final int? mileage;

  String get label => '$brand $model${year == null ? '' : ' $year'}';
}

final customerRepositoryProvider = Provider<CustomerRepository>(
  (ref) => CustomerRepository(ref.watch(apiClientProvider).dio),
);

class CustomerRepository {
  CustomerRepository(this._dio);

  final Dio _dio;

  /// Busca por nombre, teléfono o placa de alguno de sus vehículos: en el mostrador el
  /// cliente se identifica con cualquiera de las tres cosas.
  Future<List<Customer>> search(String? search) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/customers',
      queryParameters: {
        if (search != null && search.trim().isNotEmpty) 'search': search.trim(),
        'pageSize': 50,
      },
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => Customer.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<List<CustomerVehicle>> vehicles(String customerId) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/vehicles',
      queryParameters: {'customerId': customerId, 'pageSize': 50},
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => CustomerVehicle.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<Customer> save({
    String? id,
    required String fullName,
    required String phone,
    String? email,
    String? address,
    String? notes,
    String? taxId,
  }) async {
    final data = {
      'fullName': fullName,
      'phone': phone,
      'email': email,
      'address': address,
      'notes': notes,
      // RTN: solo lo tienen los clientes que piden factura con CAI.
      'taxId': taxId,
    };

    final response = id == null
        ? await _dio.post<Map<String, dynamic>>('/api/customers', data: data)
        : await _dio.put<Map<String, dynamic>>('/api/customers/$id', data: data);

    return Customer.fromJson(response.data!);
  }

  /// Le abre acceso a la app. Uno por cliente: un segundo usuario sobre los mismos
  /// vehículos no habría cómo quitarlo desde ninguna pantalla.
  Future<void> grantAppAccess(String id, String email, String password) =>
      _dio.post<Map<String, dynamic>>(
        '/api/customers/$id/app-access',
        data: {'email': email, 'password': password},
      );
}

final customerSearchProvider =
    FutureProvider.autoDispose.family<List<Customer>, String>(
  (ref, search) => ref.watch(customerRepositoryProvider).search(search),
);

final customerVehiclesProvider =
    FutureProvider.autoDispose.family<List<CustomerVehicle>, String>(
  (ref, customerId) => ref.watch(customerRepositoryProvider).vehicles(customerId),
);

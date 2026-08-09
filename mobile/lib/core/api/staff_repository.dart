import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';

/// Técnico del taller, en lo mínimo que hace falta para elegirlo en una lista.
class TechnicianOption {
  const TechnicianOption({
    required this.id,
    required this.name,
    required this.isActive,
    required this.branchIds,
  });

  factory TechnicianOption.fromJson(Map<String, dynamic> json) => TechnicianOption(
        id: json['id'] as String,
        name: json['fullName'] as String,
        isActive: json['isActive'] as bool? ?? true,
        branchIds: ((json['branchIds'] as List<dynamic>?) ?? [])
            .map((e) => e as String)
            .toList(),
      );

  final String id;
  final String name;
  final bool isActive;

  /// Sucursales donde trabaja. Asignarle una orden de otra la API la rechaza, así que la
  /// lista se filtra antes de enseñarla: es mejor no ofrecer lo que va a fallar.
  final List<String> branchIds;

  bool worksAt(String branchId) => branchIds.contains(branchId);
}

/// Un usuario del taller con todo lo que el Dueño necesita para administrarlo.
class StaffUser {
  const StaffUser({
    required this.id,
    required this.email,
    required this.fullName,
    required this.role,
    required this.isActive,
    required this.branchIds,
    this.lastLoginAt,
  });

  factory StaffUser.fromJson(Map<String, dynamic> json) => StaffUser(
        id: json['id'] as String,
        email: json['email'] as String,
        fullName: json['fullName'] as String,
        role: json['role'] as String,
        isActive: json['isActive'] as bool? ?? true,
        branchIds:
            ((json['branchIds'] as List<dynamic>?) ?? []).map((e) => e as String).toList(),
        lastLoginAt: json['lastLoginAt'] == null
            ? null
            : DateTime.parse(json['lastLoginAt'] as String),
      );

  final String id;
  final String email;
  final String fullName;
  final String role;
  final bool isActive;
  final List<String> branchIds;
  final DateTime? lastLoginAt;

  bool get isTechnician => role == 'Technician';
}

final staffRepositoryProvider = Provider<StaffRepository>(
  (ref) => StaffRepository(ref.watch(apiClientProvider).dio),
);

class StaffRepository {
  StaffRepository(this._dio);

  final Dio _dio;

  Future<List<TechnicianOption>> technicians() async {
    final response = await _dio.get<List<dynamic>>(
      '/api/users',
      queryParameters: {'role': 'Technician'},
    );

    return response.data!
        .map((e) => TechnicianOption.fromJson(e as Map<String, dynamic>))
        .where((t) => t.isActive)
        .toList();
  }

  /// Todos los usuarios del taller, incluidos los dados de baja: el Dueño necesita verlos
  /// para reactivarlos.
  Future<List<StaffUser>> users({String? role}) async {
    final response = await _dio.get<List<dynamic>>(
      '/api/users',
      queryParameters: {if (role != null) 'role': role},
    );

    return response.data!
        .map((e) => StaffUser.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Da de alta un técnico. Sin sucursal no vería ninguna orden, así que la API la exige.
  Future<StaffUser> createTechnician({
    required String email,
    required String fullName,
    required String password,
    required List<String> branchIds,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/users',
      data: {
        'email': email,
        'fullName': fullName,
        'role': 'Technician',
        'password': password,
        'branchIds': branchIds,
      },
    );

    return StaffUser.fromJson(response.data!);
  }

  /// Nombre, alta o baja y sucursales. Un usuario no se borra: el histórico lleva su nombre.
  Future<StaffUser> updateUser(
    StaffUser user, {
    String? fullName,
    bool? isActive,
    List<String>? branchIds,
  }) async {
    final response = await _dio.put<Map<String, dynamic>>(
      '/api/users/${user.id}',
      data: {
        'fullName': fullName ?? user.fullName,
        'isActive': isActive ?? user.isActive,
        'branchIds': branchIds ?? user.branchIds,
      },
    );

    return StaffUser.fromJson(response.data!);
  }

  /// El Dueño no conoce la contraseña actual: la reemplaza y cierra las sesiones abiertas.
  Future<void> resetPassword(String userId, String newPassword) async {
    await _dio.post<void>(
      '/api/users/$userId/password',
      data: {'newPassword': newPassword},
    );
  }
}

/// Solo el Dueño puede listar usuarios; a los demás la API responde 403. Las pantallas que
/// lo leen ya son suyas, pero se devuelve lista vacía si falla para no romper la vista.
/// La lista completa para la pantalla de usuarios. Solo el Dueño: la API responde 403 a los
/// demás, y ahí sí conviene que el error se vea.
final staffUsersProvider = FutureProvider.autoDispose<List<StaffUser>>(
  (ref) => ref.watch(staffRepositoryProvider).users(),
);

final technicianOptionsProvider = FutureProvider.autoDispose<List<TechnicianOption>>(
  (ref) async {
    try {
      return await ref.watch(staffRepositoryProvider).technicians();
    } catch (_) {
      return const [];
    }
  },
);

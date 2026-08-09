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
}

/// Solo el Dueño puede listar usuarios; a los demás la API responde 403. Las pantallas que
/// lo leen ya son suyas, pero se devuelve lista vacía si falla para no romper la vista.
final technicianOptionsProvider = FutureProvider.autoDispose<List<TechnicianOption>>(
  (ref) async {
    try {
      return await ref.watch(staffRepositoryProvider).technicians();
    } catch (_) {
      return const [];
    }
  },
);

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../models/inventory.dart';

final inventoryRepositoryProvider = Provider<InventoryRepository>(
  (ref) => InventoryRepository(ref.watch(apiClientProvider).dio),
);

class InventoryRepository {
  InventoryRepository(this._dio);

  final Dio _dio;

  /// Catálogo con la existencia de las sucursales del usuario. El técnico necesita ver
  /// cuánto queda antes de comprometer una reparación.
  Future<List<Part>> searchParts(String? search) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/parts',
      queryParameters: {
        if (search != null && search.trim().isNotEmpty) 'search': search.trim(),
        'pageSize': 50,
      },
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => Part.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Carga el repuesto a la orden y lo descuenta de la bodega de su sucursal.
  /// Devuelve 409 si no alcanza, con un mensaje que dice cuánto queda.
  Future<WorkOrderPart> addPart(
    String workOrderId, {
    required String partId,
    required double quantity,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/work-orders/$workOrderId/parts',
      data: {'partId': partId, 'quantity': quantity},
    );

    return WorkOrderPart.fromJson(response.data!);
  }

  Future<void> removePart(String workOrderId, String partLineId) =>
      _dio.delete<void>('/api/work-orders/$workOrderId/parts/$partLineId');
}

/// Búsqueda del catálogo. `autoDispose` porque solo vive mientras el selector está abierto.
final partSearchProvider = FutureProvider.autoDispose.family<List<Part>, String>(
  (ref, search) => ref.watch(inventoryRepositoryProvider).searchParts(search),
);

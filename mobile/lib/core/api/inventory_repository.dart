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

  /// Existencias por sucursal. Sin sucursal, las de todas las que el usuario ve.
  Future<List<StockItem>> stock({
    String? branchId,
    String? search,
    bool onlyBelowMinimum = false,
  }) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/stock',
      queryParameters: {
        if (branchId != null) 'branchId': branchId,
        if (search != null && search.trim().isNotEmpty) 'search': search.trim(),
        if (onlyBelowMinimum) 'onlyBelowMinimum': true,
        'pageSize': 100,
      },
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => StockItem.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Kardex de un repuesto: de dónde salió y a dónde fue cada unidad.
  Future<List<StockMovement>> movements({required String partId, String? branchId}) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/stock/movements',
      queryParameters: {
        'partId': partId,
        if (branchId != null) 'branchId': branchId,
        'pageSize': 50,
      },
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => StockMovement.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Entrada por compra. El costo actualiza el de referencia del catálogo.
  Future<void> receive({
    required String branchId,
    required String partId,
    required double quantity,
    double? unitCost,
    String? reference,
  }) =>
      _dio.post<Map<String, dynamic>>(
        '/api/stock/receive',
        data: {
          'branchId': branchId,
          'partId': partId,
          'quantity': quantity,
          'unitCost': unitCost,
          'reference': reference,
        },
      );

  /// Ajuste por conteo físico: se manda **lo contado**, no la diferencia, que es lo que la
  /// persona tiene delante al hacer inventario.
  Future<void> adjust({
    required String branchId,
    required String partId,
    required double countedQuantity,
    required String reason,
  }) =>
      _dio.post<Map<String, dynamic>>(
        '/api/stock/adjust',
        data: {
          'branchId': branchId,
          'partId': partId,
          'countedQuantity': countedQuantity,
          'reason': reason,
        },
      );

  Future<void> transfer({
    required String fromBranchId,
    required String toBranchId,
    required String partId,
    required double quantity,
    String? notes,
  }) =>
      _dio.post<Map<String, dynamic>>(
        '/api/stock/transfer',
        data: {
          'fromBranchId': fromBranchId,
          'toBranchId': toBranchId,
          'partId': partId,
          'quantity': quantity,
          'notes': notes,
        },
      );

  /// Mínimo de reposición y ubicación. No mueven existencias.
  Future<void> saveSettings({
    required String branchId,
    required String partId,
    required double minQuantity,
    String? location,
  }) =>
      _dio.put<Map<String, dynamic>>(
        '/api/stock/settings',
        data: {
          'branchId': branchId,
          'partId': partId,
          'minQuantity': minQuantity,
          'location': location,
        },
      );

  /// Alta o edición del catálogo. Con [id] edita; sin él, crea.
  Future<Part> savePart({
    String? id,
    required String sku,
    required String name,
    String? brand,
    String? category,
    required String unit,
    required double costPrice,
    required double salePrice,
    bool isActive = true,
  }) async {
    final data = {
      'sku': sku,
      'name': name,
      'brand': brand,
      'category': category,
      'unit': unit,
      'costPrice': costPrice,
      'salePrice': salePrice,
      'isActive': isActive,
    };

    final response = id == null
        ? await _dio.post<Map<String, dynamic>>('/api/parts', data: data)
        : await _dio.put<Map<String, dynamic>>('/api/parts/$id', data: data);

    return Part.fromJson(response.data!);
  }
}

/// Filtro de la pantalla de existencias. Va como una sola clave del provider porque los tres
/// campos cambian juntos: al tocar cualquiera hay que volver a preguntar.
class StockFilter {
  const StockFilter({this.branchId, this.search = '', this.onlyBelowMinimum = false});

  final String? branchId;
  final String search;
  final bool onlyBelowMinimum;

  StockFilter copyWith({
    String? branchId,
    String? search,
    bool? onlyBelowMinimum,
    bool clearBranch = false,
  }) =>
      StockFilter(
        branchId: clearBranch ? null : branchId ?? this.branchId,
        search: search ?? this.search,
        onlyBelowMinimum: onlyBelowMinimum ?? this.onlyBelowMinimum,
      );

  @override
  bool operator ==(Object other) =>
      other is StockFilter &&
      other.branchId == branchId &&
      other.search == search &&
      other.onlyBelowMinimum == onlyBelowMinimum;

  @override
  int get hashCode => Object.hash(branchId, search, onlyBelowMinimum);
}

final stockProvider = FutureProvider.autoDispose.family<List<StockItem>, StockFilter>(
  (ref, filter) => ref.watch(inventoryRepositoryProvider).stock(
        branchId: filter.branchId,
        search: filter.search,
        onlyBelowMinimum: filter.onlyBelowMinimum,
      ),
);

/// Kardex de un repuesto en una sucursal. La clave lleva las dos cosas.
final movementsProvider =
    FutureProvider.autoDispose.family<List<StockMovement>, (String partId, String? branchId)>(
  (ref, key) => ref.watch(inventoryRepositoryProvider).movements(
        partId: key.$1,
        branchId: key.$2,
      ),
);

/// Búsqueda del catálogo. `autoDispose` porque solo vive mientras el selector está abierto.
final partSearchProvider = FutureProvider.autoDispose.family<List<Part>, String>(
  (ref, search) => ref.watch(inventoryRepositoryProvider).searchParts(search),
);

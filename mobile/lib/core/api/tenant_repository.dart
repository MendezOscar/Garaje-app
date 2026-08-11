import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';

/// Rango de facturación autorizado por el SAR, visto desde el teléfono.
///
/// La app no registra rangos —eso se hace una vez, desde el panel— pero sí necesita saber si
/// la sucursal puede emitir con CAI antes de ofrecer la casilla al facturar.
class FiscalRange {
  const FiscalRange({
    required this.id,
    required this.branchId,
    required this.nextFiscalNumber,
    required this.remaining,
    required this.isActive,
    required this.isExpired,
    required this.isExhausted,
  });

  factory FiscalRange.fromJson(Map<String, dynamic> json) => FiscalRange(
        id: json['id'] as String,
        branchId: json['branchId'] as String,
        nextFiscalNumber: json['nextFiscalNumber'] as String,
        remaining: json['remaining'] as int,
        isActive: json['isActive'] as bool,
        isExpired: json['isExpired'] as bool,
        isExhausted: json['isExhausted'] as bool,
      );

  final String id;
  final String branchId;
  final String nextFiscalNumber;
  final int remaining;
  final bool isActive;
  final bool isExpired;
  final bool isExhausted;

  /// Puede emitir: está activo, no venció y le quedan números.
  bool get canIssue => isActive && !isExpired && !isExhausted;
}

class TenantRepository {
  TenantRepository(this._dio);

  final Dio _dio;

  Future<List<FiscalRange>> fiscalRanges() async {
    final response = await _dio.get<List<dynamic>>('/api/tenant/fiscal-ranges');

    return (response.data ?? [])
        .map((e) => FiscalRange.fromJson(e as Map<String, dynamic>))
        .toList();
  }
}

final tenantRepositoryProvider = Provider<TenantRepository>(
  (ref) => TenantRepository(ref.watch(apiClientProvider).dio),
);

/// El rango vigente de una sucursal, o null si no tiene. Solo el Dueño puede consultarlo, así
/// que el error se traga: para el Técnico simplemente no hay casilla de CAI.
final branchFiscalRangeProvider =
    FutureProvider.autoDispose.family<FiscalRange?, String>((ref, branchId) async {
  try {
    final ranges = await ref.watch(tenantRepositoryProvider).fiscalRanges();
    return ranges.where((r) => r.isActive && r.branchId == branchId).firstOrNull;
  } catch (_) {
    return null;
  }
});

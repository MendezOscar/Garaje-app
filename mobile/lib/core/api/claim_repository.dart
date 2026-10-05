import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';

/// En qué va un reclamo. Nace abierto y termina de una de tres formas, porque son las tres
/// cosas que de verdad pasan: se repara sin cobrar, se repara cobrando, o no procede.
enum ClaimStatus {
  open(1, 'Abierto'),
  repairedUnderWarranty(2, 'Reparado en garantía'),
  repairedAndCharged(3, 'Reparado y cobrado'),
  rejected(4, 'No procede');

  const ClaimStatus(this.value, this.label);

  final int value;
  final String label;

  static ClaimStatus fromValue(int value) =>
      ClaimStatus.values.firstWhere((s) => s.value == value, orElse: () => ClaimStatus.open);
}

/// Un reclamo: el cliente volvió diciendo que el trabajo quedó mal.
class Claim {
  const Claim({
    required this.id,
    required this.number,
    required this.status,
    required this.saleId,
    required this.saleNumber,
    required this.reason,
    required this.wasUnderWarranty,
    required this.receivedAt,
    this.workOrderId,
    this.workOrderNumber,
    this.customerName,
    this.customerPhone,
    this.vehicleLabel,
    this.resolution,
    this.resolvedAt,
    this.repairWorkOrderId,
    this.repairWorkOrderNumber,
    this.repairCost = 0,
  });

  factory Claim.fromJson(Map<String, dynamic> json) => Claim(
        id: json['id'] as String,
        number: json['number'] as String,
        status: ClaimStatus.fromValue(json['status'] as int),
        saleId: json['saleId'] as String,
        saleNumber: json['saleNumber'] as String,
        workOrderId: json['workOrderId'] as String?,
        workOrderNumber: json['workOrderNumber'] as String?,
        customerName: json['customerName'] as String?,
        customerPhone: json['customerPhone'] as String?,
        vehicleLabel: json['vehicleLabel'] as String?,
        reason: json['reason'] as String,
        wasUnderWarranty: json['wasUnderWarranty'] as bool? ?? false,
        receivedAt: DateTime.parse(json['receivedAt'] as String),
        resolution: json['resolution'] as String?,
        resolvedAt: json['resolvedAt'] == null
            ? null
            : DateTime.parse(json['resolvedAt'] as String),
        repairWorkOrderId: json['repairWorkOrderId'] as String?,
        repairWorkOrderNumber: json['repairWorkOrderNumber'] as String?,
        repairCost: (json['repairCost'] as num?)?.toDouble() ?? 0,
      );

  final String id;
  final String number;
  final ClaimStatus status;
  final String saleId;
  final String saleNumber;
  final String? workOrderId;
  final String? workOrderNumber;
  final String? customerName;
  final String? customerPhone;
  final String? vehicleLabel;
  final String reason;

  /// Si estaba en garantía el día que se recibió. Se congela a propósito.
  final bool wasUnderWarranty;
  final DateTime receivedAt;
  final String? resolution;
  final DateTime? resolvedAt;
  final String? repairWorkOrderId;
  final String? repairWorkOrderNumber;

  /// Lo que costó repararlo, cuando se abrió orden de garantía.
  final double repairCost;
}

class ClaimRepository {
  ClaimRepository(this._dio);

  final Dio _dio;

  Future<List<Claim>> list({bool onlyOpen = true}) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/claims',
      queryParameters: {'onlyOpen': onlyOpen, 'pageSize': 50},
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => Claim.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<Claim> create({required String saleId, required String reason}) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/claims',
      data: {'saleId': saleId, 'reason': reason},
    );

    return Claim.fromJson(response.data!);
  }

  /// Lo cierra. Con [openRepairOrder] abre la orden de la reparación, ligada al reclamo.
  Future<Claim> resolve(
    String id, {
    required ClaimStatus status,
    required String resolution,
    bool openRepairOrder = false,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/claims/$id/resolve',
      data: {
        'status': status.value,
        'resolution': resolution,
        'openRepairOrder': openRepairOrder,
      },
    );

    return Claim.fromJson(response.data!);
  }

  Future<Claim> reopen(String id) async {
    final response = await _dio.post<Map<String, dynamic>>('/api/claims/$id/reopen');
    return Claim.fromJson(response.data!);
  }
}

final claimRepositoryProvider = Provider<ClaimRepository>(
  (ref) => ClaimRepository(ref.watch(apiClientProvider).dio),
);

/// Los reclamos, abiertos primero. `autoDispose` porque se miran y se vuelve.
final claimsProvider = FutureProvider.autoDispose.family<List<Claim>, bool>(
  (ref, onlyOpen) => ref.watch(claimRepositoryProvider).list(onlyOpen: onlyOpen),
);

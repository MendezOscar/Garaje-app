import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';

/// Formas de pago. Mismos valores que `Garaj.Domain.Enums.PaymentMethod`.
enum PaymentMethod {
  cash(1, 'Efectivo'),
  card(2, 'Tarjeta'),
  transfer(3, 'Transferencia'),
  other(4, 'Otro');

  const PaymentMethod(this.value, this.label);

  final int value;
  final String label;
}

/// Una factura con saldo pendiente.
class Receivable {
  const Receivable({
    required this.id,
    required this.number,
    required this.branchName,
    required this.total,
    required this.amountPaid,
    required this.balance,
    required this.isOverdue,
    required this.saleDate,
    this.customerName,
    this.dueDate,
  });

  factory Receivable.fromJson(Map<String, dynamic> json) => Receivable(
        id: json['id'] as String,
        number: json['number'] as String,
        branchName: json['branchName'] as String,
        total: (json['total'] as num).toDouble(),
        amountPaid: (json['amountPaid'] as num).toDouble(),
        balance: (json['balance'] as num).toDouble(),
        isOverdue: json['isOverdue'] as bool? ?? false,
        saleDate: DateTime.parse(json['saleDate'] as String),
        customerName: json['customerName'] as String?,
        dueDate: json['dueDate'] == null ? null : DateTime.parse(json['dueDate'] as String),
      );

  final String id;
  final String number;
  final String branchName;
  final double total;
  final double amountPaid;
  final double balance;
  final bool isOverdue;
  final DateTime saleDate;
  final String? customerName;
  final DateTime? dueDate;
}

final saleRepositoryProvider = Provider<SaleRepository>(
  (ref) => SaleRepository(ref.watch(apiClientProvider).dio),
);

class SaleRepository {
  SaleRepository(this._dio);

  final Dio _dio;

  /// Lo facturado que todavía no entró en caja, con lo que vence antes al principio.
  Future<List<Receivable>> receivables({String? branchId}) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/sales',
      queryParameters: {
        'onlyUnpaid': true,
        'pageSize': 100,
        if (branchId != null) 'branchId': branchId,
      },
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => Receivable.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<void> registerPayment(
    String saleId, {
    required double amount,
    required PaymentMethod method,
    String? reference,
  }) async {
    await _dio.post<Map<String, dynamic>>(
      '/api/sales/$saleId/payments',
      data: {
        'amount': amount,
        'method': method.value,
        'reference': reference,
      },
    );
  }
}

/// Solo el Dueño: a los demás la API responde 403 en el listado de ventas.
final receivablesProvider =
    FutureProvider.autoDispose.family<List<Receivable>, String?>(
  (ref, branchId) => ref.watch(saleRepositoryProvider).receivables(branchId: branchId),
);

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

/// Un abono a una venta. Lo cobrado sale de sumarlos, no de un campo aparte.
class SalePayment {
  const SalePayment({
    required this.id,
    required this.amount,
    required this.method,
    required this.paidAt,
    this.reference,
  });

  factory SalePayment.fromJson(Map<String, dynamic> json) => SalePayment(
        id: json['id'] as String,
        amount: (json['amount'] as num).toDouble(),
        method: PaymentMethod.values.firstWhere(
          (m) => m.value == json['method'] as int,
          orElse: () => PaymentMethod.other,
        ),
        paidAt: DateTime.parse(json['paidAt'] as String),
        reference: json['reference'] as String?,
      );

  final String id;
  final double amount;
  final PaymentMethod method;
  final DateTime paidAt;
  final String? reference;
}

/// La factura de una orden, con sus abonos.
class Sale {
  const Sale({
    required this.id,
    required this.number,
    required this.total,
    required this.amountPaid,
    required this.balance,
    required this.paymentMethod,
    required this.saleDate,
    required this.isOverdue,
    required this.isVoided,
    required this.currency,
    required this.payments,
    this.dueDate,
    this.fiscalNumber,
    this.fiscalCai,
  });

  factory Sale.fromJson(Map<String, dynamic> json) => Sale(
        id: json['id'] as String,
        number: json['number'] as String,
        total: (json['total'] as num).toDouble(),
        amountPaid: (json['amountPaid'] as num?)?.toDouble() ?? 0,
        balance: (json['balance'] as num?)?.toDouble() ?? 0,
        paymentMethod: PaymentMethod.values.firstWhere(
          (m) => m.value == json['paymentMethod'] as int,
          orElse: () => PaymentMethod.other,
        ),
        saleDate: DateTime.parse(json['saleDate'] as String),
        dueDate: json['dueDate'] == null ? null : DateTime.parse(json['dueDate'] as String),
        isOverdue: json['isOverdue'] as bool? ?? false,
        isVoided: json['isVoided'] as bool? ?? false,
        currency: json['currency'] as String? ?? 'HNL',
        fiscalNumber: json['fiscalNumber'] as String?,
        fiscalCai: json['fiscalCai'] as String?,
        payments: ((json['payments'] as List<dynamic>?) ?? [])
            .map((p) => SalePayment.fromJson(p as Map<String, dynamic>))
            .toList(),
      );

  final String id;
  final String number;
  final double total;
  final double amountPaid;
  final double balance;
  final PaymentMethod paymentMethod;
  final DateTime saleDate;
  final DateTime? dueDate;
  final bool isOverdue;
  final bool isVoided;

  /// Correlativo del SAR, o null si la factura salió sin CAI.
  final String? fiscalNumber;
  final String? fiscalCai;
  final String currency;
  final List<SalePayment> payments;
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

  Future<void> removePayment(String saleId, String paymentId) =>
      _dio.delete<void>('/api/sales/$saleId/payments/$paymentId');

  /// Las ventas de una orden, con sus abonos. El listado no los trae y son lo que dice
  /// cuánto falta por cobrar, así que se pide el detalle de cada una: es una, o dos si
  /// alguna se anuló.
  Future<List<Sale>> ofWorkOrder(String workOrderId) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/sales',
      queryParameters: {'workOrderId': workOrderId, 'pageSize': 20},
    );

    final ids = (response.data!['items'] as List<dynamic>)
        .map((e) => (e as Map<String, dynamic>)['id'] as String)
        .toList();

    return Future.wait(ids.map(_get));
  }

  Future<Sale> _get(String id) async {
    final response = await _dio.get<Map<String, dynamic>>('/api/sales/$id');
    return Sale.fromJson(response.data!);
  }

  /// Cierra la orden: factura los repuestos consumidos más la mano de obra que se elija, y
  /// entrega el vehículo. Es el paso que alimenta los reportes de ingresos.
  ///
  /// [laborFromQuoteId] cobra la mano de obra de esa cotización en lugar de la de los pasos:
  /// es el precio que el cliente vio y aprobó por WhatsApp. [initialPayment] solo va en las
  /// entregas a crédito; omitido significa que paga todo.
  Future<Sale> closeWorkOrder({
    required String workOrderId,
    required PaymentMethod paymentMethod,
    bool includeLabor = true,
    String? laborFromQuoteId,
    double? initialPayment,
    DateTime? dueDate,
    bool fiscal = false,
    String? customerTaxId,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/sales/close-work-order',
      data: {
        'workOrderId': workOrderId,
        'paymentMethod': paymentMethod.value,
        'includeLabor': includeLabor,
        'laborFromQuoteId': laborFromQuoteId,
        'initialPayment': initialPayment,
        'dueDate': dueDate?.toUtc().toIso8601String(),
        // Consume un número del rango autorizado por el SAR. Falso salvo que el cliente
        // pida la factura: cada una quema un correlativo.
        'fiscal': fiscal,
        'customerTaxId': customerTaxId,
      },
    );

    return Sale.fromJson(response.data!);
  }

  /// El PDF de la factura. Se baja con la sesión puesta —el endpoint pide `Authorization`,
  /// que el navegador del sistema no manda— y se devuelven los bytes para compartirlos.
  Future<List<int>> invoicePdf(String saleId) async {
    final response = await _dio.get<List<int>>(
      '/api/sales/$saleId/pdf',
      options: Options(responseType: ResponseType.bytes),
    );

    return response.data!;
  }
}

/// Ventas de una orden. Solo el Dueño: a los demás la API responde 403 en el listado.
final workOrderSalesProvider =
    FutureProvider.autoDispose.family<List<Sale>, String>(
  (ref, workOrderId) => ref.watch(saleRepositoryProvider).ofWorkOrder(workOrderId),
);

/// Solo el Dueño: a los demás la API responde 403 en el listado de ventas.
final receivablesProvider =
    FutureProvider.autoDispose.family<List<Receivable>, String?>(
  (ref, branchId) => ref.watch(saleRepositoryProvider).receivables(branchId: branchId),
);

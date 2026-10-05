import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import 'sale_repository.dart' show PaymentMethod;

/// En qué se le va la plata al taller. Lista fija: con categorías libres el mismo gasto
/// termina escrito de tres formas y el mes deja de poder compararse contra el anterior.
///
/// La compra de repuestos no está: eso es inventario, y se vuelve costo cuando se vende.
enum ExpenseCategory {
  salaries(1, 'Salarios'),
  rent(2, 'Alquiler'),
  utilities(3, 'Servicios (agua, luz, internet)'),
  tools(4, 'Herramienta y equipo'),
  transport(5, 'Transporte'),
  taxesAndPermits(6, 'Impuestos y permisos'),
  advertising(7, 'Publicidad'),
  maintenance(8, 'Mantenimiento del local'),
  other(9, 'Otros');

  const ExpenseCategory(this.value, this.label);

  final int value;
  final String label;

  static ExpenseCategory fromValue(int value) =>
      ExpenseCategory.values.firstWhere((c) => c.value == value, orElse: () => ExpenseCategory.other);
}

/// Un gasto del taller: lo que sale por el otro lado de la caja.
class Expense {
  const Expense({
    required this.id,
    required this.branchId,
    required this.branchName,
    required this.category,
    required this.description,
    required this.amount,
    required this.paymentMethod,
    required this.expenseDate,
    this.supplierName,
    this.notes,
    this.createdByName,
    this.photoCount = 0,
  });

  factory Expense.fromJson(Map<String, dynamic> json) => Expense(
        id: json['id'] as String,
        branchId: json['branchId'] as String,
        branchName: json['branchName'] as String,
        category: ExpenseCategory.fromValue(json['category'] as int),
        description: json['description'] as String,
        amount: (json['amount'] as num).toDouble(),
        paymentMethod: PaymentMethod.values.firstWhere(
          (m) => m.value == json['paymentMethod'] as int,
          orElse: () => PaymentMethod.other,
        ),
        supplierName: json['supplierName'] as String?,
        expenseDate: DateTime.parse(json['expenseDate'] as String),
        notes: json['notes'] as String?,
        createdByName: json['createdByName'] as String?,
        photoCount: (json['photoCount'] as num?)?.toInt() ?? 0,
      );

  final String id;
  final String branchId;
  final String branchName;
  final ExpenseCategory category;
  final String description;
  final double amount;
  final PaymentMethod paymentMethod;
  final String? supplierName;
  final DateTime expenseDate;
  final String? notes;
  final String? createdByName;
  final int photoCount;
}

/// Qué dejó el taller en un periodo.
class IncomeStatement {
  const IncomeStatement({
    required this.currency,
    required this.revenue,
    required this.costOfSales,
    required this.grossProfit,
    required this.expenseTotal,
    required this.netProfit,
    required this.netMarginPercent,
    required this.expenses,
  });

  factory IncomeStatement.fromJson(Map<String, dynamic> json) => IncomeStatement(
        currency: json['currency'] as String? ?? 'HNL',
        revenue: (json['revenue'] as num).toDouble(),
        costOfSales: (json['costOfSales'] as num).toDouble(),
        grossProfit: (json['grossProfit'] as num).toDouble(),
        expenseTotal: (json['expenseTotal'] as num).toDouble(),
        netProfit: (json['netProfit'] as num).toDouble(),
        netMarginPercent: (json['netMarginPercent'] as num).toDouble(),
        expenses: ((json['expenses'] as List<dynamic>?) ?? [])
            .map((e) => ExpenseGroup.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  final String currency;
  final double revenue;
  final double costOfSales;
  final double grossProfit;
  final double expenseTotal;
  final double netProfit;
  final double netMarginPercent;
  final List<ExpenseGroup> expenses;
}

class ExpenseGroup {
  const ExpenseGroup({required this.category, required this.amount});

  factory ExpenseGroup.fromJson(Map<String, dynamic> json) => ExpenseGroup(
        category: ExpenseCategory.fromValue(json['category'] as int),
        amount: (json['amount'] as num).toDouble(),
      );

  final ExpenseCategory category;
  final double amount;
}

class ExpenseRepository {
  ExpenseRepository(this._dio);

  final Dio _dio;

  Future<List<Expense>> list({DateTime? from}) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/expenses',
      queryParameters: {
        if (from != null) 'from': from.toIso8601String(),
        'pageSize': 100,
      },
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => Expense.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<Expense> create({
    required String branchId,
    required ExpenseCategory category,
    required String description,
    required double amount,
    required PaymentMethod paymentMethod,
    String? supplierName,
    DateTime? expenseDate,
    String? notes,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/expenses',
      data: {
        'branchId': branchId,
        'category': category.value,
        'description': description,
        'amount': amount,
        'paymentMethod': paymentMethod.value,
        'supplierName': supplierName,
        'expenseDate': expenseDate?.toUtc().toIso8601String(),
        'notes': notes,
      },
    );

    return Expense.fromJson(response.data!);
  }

  Future<void> remove(String id) => _dio.delete<void>('/api/expenses/$id');

  /// Sin fecha, el mes corriente, que es el periodo que el dueño mira.
  Future<IncomeStatement> incomeStatement({DateTime? from}) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/expenses/income-statement',
      queryParameters: {if (from != null) 'from': from.toIso8601String()},
    );

    return IncomeStatement.fromJson(response.data!);
  }
}

final expenseRepositoryProvider = Provider<ExpenseRepository>(
  (ref) => ExpenseRepository(ref.watch(apiClientProvider).dio),
);

final expensesProvider = FutureProvider.autoDispose<List<Expense>>(
  (ref) => ref.watch(expenseRepositoryProvider).list(),
);

final incomeStatementProvider = FutureProvider.autoDispose<IncomeStatement>(
  (ref) => ref.watch(expenseRepositoryProvider).incomeStatement(),
);

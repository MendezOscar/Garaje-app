import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';

/// Resumen de ingresos del taller. Es lo que el Dueño quiere ver de un vistazo desde el
/// teléfono, sin abrir el panel web.
class DashboardSummary {
  const DashboardSummary({
    required this.currency,
    required this.today,
    required this.week,
    required this.month,
    required this.openWorkOrders,
    required this.lateWorkOrders,
    required this.pendingRequests,
    required this.partsBelowMinimum,
  });

  factory DashboardSummary.fromJson(Map<String, dynamic> json) => DashboardSummary(
        currency: json['currency'] as String? ?? 'HNL',
        today: (json['revenueToday'] as num).toDouble(),
        week: (json['revenueWeek'] as num).toDouble(),
        month: (json['revenueMonth'] as num).toDouble(),
        openWorkOrders: json['openWorkOrders'] as int,
        lateWorkOrders: json['lateWorkOrders'] as int,
        pendingRequests: json['pendingRequests'] as int,
        partsBelowMinimum: json['partsBelowMinimum'] as int,
      );

  final String currency;
  final double today;
  final double week;
  final double month;
  final int openWorkOrders;
  final int lateWorkOrders;
  final int pendingRequests;
  final int partsBelowMinimum;
}

final dashboardRepositoryProvider = Provider<DashboardRepository>(
  (ref) => DashboardRepository(ref.watch(apiClientProvider).dio),
);

class DashboardRepository {
  DashboardRepository(this._dio);

  final Dio _dio;

  Future<DashboardSummary> get() async {
    final response = await _dio.get<Map<String, dynamic>>('/api/reports/dashboard');
    return DashboardSummary.fromJson(response.data!);
  }
}

/// Solo lo puede pedir el Dueño; a los demás la API responde 403, así que la pantalla
/// comprueba el perfil antes de leer este provider.
final dashboardProvider = FutureProvider.autoDispose<DashboardSummary>(
  (ref) => ref.watch(dashboardRepositoryProvider).get(),
);

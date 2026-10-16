import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../models/work_order.dart';
import 'sucursal_activa.dart';

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
    required this.appointmentsToConfirm,
    required this.appointmentsToday,
    required this.partsBelowMinimum,
    required this.quotesAwaitingResponse,
    required this.receivables,
    required this.overdueReceivables,
    required this.byStatus,
  });

  factory DashboardSummary.fromJson(Map<String, dynamic> json) => DashboardSummary(
        currency: json['currency'] as String? ?? 'HNL',
        today: (json['revenueToday'] as num).toDouble(),
        week: (json['revenueWeek'] as num).toDouble(),
        month: (json['revenueMonth'] as num).toDouble(),
        openWorkOrders: json['openWorkOrders'] as int,
        lateWorkOrders: json['lateWorkOrders'] as int,
        pendingRequests: json['pendingRequests'] as int,
        appointmentsToConfirm: json['appointmentsToConfirm'] as int? ?? 0,
        appointmentsToday: json['appointmentsToday'] as int? ?? 0,
        partsBelowMinimum: json['partsBelowMinimum'] as int,
        quotesAwaitingResponse: json['quotesAwaitingResponse'] as int? ?? 0,
        receivables: (json['receivables'] as num?)?.toDouble() ?? 0,
        overdueReceivables: (json['overdueReceivables'] as num?)?.toDouble() ?? 0,
        byStatus: {
          for (final e in (json['workOrdersByStatus'] as List<dynamic>? ?? const []))
            WorkOrderStatus.fromValue((e as Map<String, dynamic>)['status'] as int):
                e['count'] as int,
        },
      );

  final String currency;
  final double today;
  final double week;
  final double month;
  final int openWorkOrders;
  final int lateWorkOrders;
  final int pendingRequests;

  /// Citas que esperan respuesta del taller, y las apartadas para hoy.
  final int appointmentsToConfirm;
  final int appointmentsToday;
  final int partsBelowMinimum;

  /// Cotizaciones mandadas que el cliente no ha contestado.
  final int quotesAwaitingResponse;

  /// Facturado y no cobrado, y cuánto de eso ya venció.
  final double receivables;
  final double overdueReceivables;

  /// Cuántas órdenes vivas hay en cada estado: es el patio del taller de un vistazo. Viene en
  /// la misma respuesta, así que la pantalla de inicio no pide nada más para pintarlo.
  final Map<WorkOrderStatus, int> byStatus;

  /// Suma de los estados que se piden, saltándose los que el taller no tiene ahora mismo.
  int count(List<WorkOrderStatus> statuses) =>
      statuses.fold(0, (sum, s) => sum + (byStatus[s] ?? 0));
}

final dashboardRepositoryProvider = Provider<DashboardRepository>(
  (ref) => DashboardRepository(ref.watch(apiClientProvider).dio),
);

class DashboardRepository {
  DashboardRepository(this._dio);

  final Dio _dio;

  Future<DashboardSummary> get({String? branchId}) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/reports/dashboard',
      queryParameters: {if (branchId != null) 'branchId': branchId},
    );
    return DashboardSummary.fromJson(response.data!);
  }
}

/// Solo lo puede pedir el Dueño; a los demás la API responde 403, así que la pantalla
/// comprueba el perfil antes de leer este provider.
/// Sigue la sucursal elegida: con «todas» el resumen es el del taller completo.
final dashboardProvider = FutureProvider.autoDispose<DashboardSummary>(
  (ref) => ref
      .watch(dashboardRepositoryProvider)
      .get(branchId: ref.watch(sucursalActivaProvider)),
);

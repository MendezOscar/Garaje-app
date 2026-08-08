import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';

/// Reporte de ingresos del taller, el mismo que se ve en el panel web.
///
/// Vive también en el teléfono porque el dueño de un taller pequeño no está delante de una
/// computadora: revisa cómo va el día mientras camina por el patio o antes de cerrar.

enum RevenueGrouping {
  day(1, 'Día'),
  week(2, 'Semana'),
  month(3, 'Mes');

  const RevenueGrouping(this.value, this.label);

  final int value;
  final String label;
}

class RevenuePoint {
  const RevenuePoint({
    required this.label,
    required this.partsRevenue,
    required this.laborRevenue,
    required this.total,
  });

  factory RevenuePoint.fromJson(Map<String, dynamic> json) => RevenuePoint(
        label: json['periodLabel'] as String,
        partsRevenue: (json['partsRevenue'] as num).toDouble(),
        laborRevenue: (json['laborRevenue'] as num).toDouble(),
        total: (json['total'] as num).toDouble(),
      );

  final String label;
  final double partsRevenue;
  final double laborRevenue;
  final double total;
}

/// Una fila del reparto: por sucursal o por técnico, que se pintan igual.
class RevenueSlice {
  const RevenueSlice({
    required this.name,
    required this.partsRevenue,
    required this.laborRevenue,
    required this.total,
    required this.saleCount,
    this.margin,
  });

  factory RevenueSlice.branch(Map<String, dynamic> json) => RevenueSlice(
        name: json['branchName'] as String,
        partsRevenue: (json['partsRevenue'] as num).toDouble(),
        laborRevenue: (json['laborRevenue'] as num).toDouble(),
        total: (json['total'] as num).toDouble(),
        saleCount: json['saleCount'] as int,
      );

  factory RevenueSlice.technician(Map<String, dynamic> json) => RevenueSlice(
        name: json['technicianName'] as String,
        partsRevenue: (json['partsRevenue'] as num).toDouble(),
        laborRevenue: (json['laborRevenue'] as num).toDouble(),
        total: (json['total'] as num).toDouble(),
        saleCount: json['saleCount'] as int,
        margin: (json['margin'] as num?)?.toDouble(),
      );

  final String name;
  final double partsRevenue;
  final double laborRevenue;
  final double total;
  final int saleCount;
  final double? margin;
}

class TopPart {
  const TopPart({
    required this.sku,
    required this.name,
    required this.quantity,
    required this.revenue,
  });

  factory TopPart.fromJson(Map<String, dynamic> json) => TopPart(
        sku: json['sku'] as String,
        name: json['name'] as String,
        quantity: (json['quantity'] as num).toDouble(),
        revenue: (json['revenue'] as num).toDouble(),
      );

  final String sku;
  final String name;
  final double quantity;
  final double revenue;
}

class RevenueReport {
  const RevenueReport({
    required this.currency,
    required this.partsRevenue,
    required this.laborRevenue,
    required this.total,
    required this.margin,
    required this.marginPercent,
    required this.saleCount,
    required this.points,
    required this.branches,
    required this.technicians,
    required this.topParts,
  });

  factory RevenueReport.fromJson(Map<String, dynamic> json) => RevenueReport(
        currency: json['currency'] as String? ?? 'HNL',
        partsRevenue: (json['partsRevenue'] as num).toDouble(),
        laborRevenue: (json['laborRevenue'] as num).toDouble(),
        total: (json['total'] as num).toDouble(),
        margin: (json['margin'] as num).toDouble(),
        marginPercent: (json['marginPercent'] as num).toDouble(),
        saleCount: json['saleCount'] as int,
        points: ((json['points'] as List<dynamic>?) ?? [])
            .map((e) => RevenuePoint.fromJson(e as Map<String, dynamic>))
            .toList(),
        branches: ((json['branches'] as List<dynamic>?) ?? [])
            .map((e) => RevenueSlice.branch(e as Map<String, dynamic>))
            .toList(),
        technicians: ((json['technicians'] as List<dynamic>?) ?? [])
            .map((e) => RevenueSlice.technician(e as Map<String, dynamic>))
            .toList(),
        topParts: ((json['topParts'] as List<dynamic>?) ?? [])
            .map((e) => TopPart.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  final String currency;
  final double partsRevenue;
  final double laborRevenue;
  final double total;
  final double margin;
  final double marginPercent;
  final int saleCount;
  final List<RevenuePoint> points;
  final List<RevenueSlice> branches;
  final List<RevenueSlice> technicians;
  final List<TopPart> topParts;
}

/// Qué se está mirando. Va en el provider como clave, así que necesita igualdad por valor:
/// sin ella, cada reconstrucción de la pantalla dispararía otra petición.
class ReportFilter {
  const ReportFilter({
    this.groupBy = RevenueGrouping.day,
    this.days = 30,
    this.branchId,
    this.technicianId,
  });

  final RevenueGrouping groupBy;

  /// Cuántos días hacia atrás. En el teléfono se elige de una lista corta: escribir dos
  /// fechas en una pantalla pequeña es más trabajo del que vale.
  final int days;
  final String? branchId;
  final String? technicianId;

  ReportFilter copyWith({
    RevenueGrouping? groupBy,
    int? days,
    String? branchId,
    String? technicianId,
    bool clearBranch = false,
    bool clearTechnician = false,
  }) =>
      ReportFilter(
        groupBy: groupBy ?? this.groupBy,
        days: days ?? this.days,
        branchId: clearBranch ? null : branchId ?? this.branchId,
        technicianId: clearTechnician ? null : technicianId ?? this.technicianId,
      );

  @override
  bool operator ==(Object other) =>
      other is ReportFilter &&
      other.groupBy == groupBy &&
      other.days == days &&
      other.branchId == branchId &&
      other.technicianId == technicianId;

  @override
  int get hashCode => Object.hash(groupBy, days, branchId, technicianId);
}

/// Técnico del taller, en lo mínimo para llenar el filtro.
class TechnicianOption {
  const TechnicianOption({required this.id, required this.name});

  factory TechnicianOption.fromJson(Map<String, dynamic> json) => TechnicianOption(
        id: json['id'] as String,
        name: json['fullName'] as String,
      );

  final String id;
  final String name;
}

final reportRepositoryProvider = Provider<ReportRepository>(
  (ref) => ReportRepository(ref.watch(apiClientProvider).dio),
);

class ReportRepository {
  ReportRepository(this._dio);

  final Dio _dio;

  Future<RevenueReport> revenue(ReportFilter filter) async {
    // El rango se manda en UTC, que es lo único que acepta la API; el corte por día del
    // taller lo hace ella, que conoce la hora de Honduras.
    final from = DateTime.now().toUtc().subtract(Duration(days: filter.days));

    final response = await _dio.get<Map<String, dynamic>>(
      '/api/reports/revenue',
      queryParameters: {
        'from': from.toIso8601String(),
        'groupBy': filter.groupBy.value,
        if (filter.branchId != null) 'branchId': filter.branchId,
        if (filter.technicianId != null) 'technicianId': filter.technicianId,
      },
    );

    return RevenueReport.fromJson(response.data!);
  }

  Future<List<TechnicianOption>> technicians() async {
    final response = await _dio.get<List<dynamic>>(
      '/api/users',
      queryParameters: {'role': 'Technician'},
    );

    return response.data!
        .map((e) => TechnicianOption.fromJson(e as Map<String, dynamic>))
        .where((t) => t.id.isNotEmpty)
        .toList();
  }
}

/// Solo lo puede pedir el Dueño: a los demás la API responde 403.
final revenueReportProvider =
    FutureProvider.autoDispose.family<RevenueReport, ReportFilter>(
  (ref, filter) => ref.watch(reportRepositoryProvider).revenue(filter),
);

final technicianOptionsProvider = FutureProvider.autoDispose<List<TechnicianOption>>(
  (ref) => ref.watch(reportRepositoryProvider).technicians(),
);

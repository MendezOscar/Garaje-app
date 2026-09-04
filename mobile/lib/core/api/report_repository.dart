import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import 'sale_repository.dart' show PaymentMethod;

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
    required this.counterPartsRevenue,
    required this.counterSaleCount,
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
        counterPartsRevenue: (json['counterPartsRevenue'] as num?)?.toDouble() ?? 0,
        counterSaleCount: json['counterSaleCount'] as int? ?? 0,
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

  /// Repuestos vendidos en mostrador, sin orden de trabajo de por medio, y en cuántas ventas.
  /// En [partsRevenue] no se distinguen de los que se le montaron a un vehículo, y es otra
  /// pregunta: si el mostrador se sostiene solo.
  final double counterPartsRevenue;
  final int counterSaleCount;
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

/// Lo **cobrado** en un día, que no es lo facturado: una venta a crédito suma en los ingresos
/// el día que se emite y aquí el día que el cliente paga.
class CashClose {
  const CashClose({
    required this.day,
    required this.dayLabel,
    required this.branchName,
    required this.currency,
    required this.total,
    required this.paymentCount,
    required this.byMethod,
    required this.byReceiver,
    required this.payments,
    required this.voidedCount,
    required this.voidedAmount,
  });

  factory CashClose.fromJson(Map<String, dynamic> json) => CashClose(
        day: DateTime.parse(json['day'] as String),
        dayLabel: json['dayLabel'] as String,
        branchName: json['branchName'] as String?,
        currency: json['currency'] as String? ?? 'HNL',
        total: (json['total'] as num).toDouble(),
        paymentCount: json['paymentCount'] as int,
        byMethod: ((json['byMethod'] as List<dynamic>?) ?? [])
            .map((e) => CashCloseSlice.method(e as Map<String, dynamic>))
            .toList(),
        byReceiver: ((json['byReceiver'] as List<dynamic>?) ?? [])
            .map((e) => CashCloseSlice.receiver(e as Map<String, dynamic>))
            .toList(),
        payments: ((json['payments'] as List<dynamic>?) ?? [])
            .map((e) => CashClosePayment.fromJson(e as Map<String, dynamic>))
            .toList(),
        voidedCount: json['voidedCount'] as int? ?? 0,
        voidedAmount: (json['voidedAmount'] as num?)?.toDouble() ?? 0,
      );

  final DateTime day;
  final String dayLabel;
  final String? branchName;
  final String currency;
  final double total;
  final int paymentCount;
  final List<CashCloseSlice> byMethod;
  final List<CashCloseSlice> byReceiver;
  final List<CashClosePayment> payments;

  /// Abonos que quedaron fuera porque su venta está anulada. Se informan, no se esconden.
  final int voidedCount;
  final double voidedAmount;
}

/// Una fila del resumen: por forma de pago o por quién lo recibió, que se pintan igual.
class CashCloseSlice {
  const CashCloseSlice({required this.label, required this.total, required this.count});

  factory CashCloseSlice.method(Map<String, dynamic> json) => CashCloseSlice(
        label: PaymentMethod.values
            .firstWhere(
              (m) => m.value == json['method'] as int,
              orElse: () => PaymentMethod.other,
            )
            .label,
        total: (json['total'] as num).toDouble(),
        count: json['count'] as int,
      );

  factory CashCloseSlice.receiver(Map<String, dynamic> json) => CashCloseSlice(
        label: json['receiverName'] as String,
        total: (json['total'] as num).toDouble(),
        count: json['count'] as int,
      );

  final String label;
  final double total;
  final int count;
}

class CashClosePayment {
  const CashClosePayment({
    required this.paidAt,
    required this.saleNumber,
    required this.customerName,
    required this.branchName,
    required this.method,
    required this.reference,
    required this.receiverName,
    required this.amount,
  });

  factory CashClosePayment.fromJson(Map<String, dynamic> json) => CashClosePayment(
        paidAt: DateTime.parse(json['paidAt'] as String),
        saleNumber: json['saleNumber'] as String,
        customerName: json['customerName'] as String?,
        branchName: json['branchName'] as String,
        method: PaymentMethod.values.firstWhere(
          (m) => m.value == json['method'] as int,
          orElse: () => PaymentMethod.other,
        ),
        reference: json['reference'] as String?,
        receiverName: json['receiverName'] as String,
        amount: (json['amount'] as num).toDouble(),
      );

  final DateTime paidAt;
  final String saleNumber;
  final String? customerName;
  final String branchName;
  final PaymentMethod method;
  final String? reference;
  final String receiverName;
  final double amount;
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
    //
    // Arranca al **comienzo del día** y no en el instante en que se abrió la pantalla: el
    // gráfico agrupa por día, así que con la hora suelta la barra más vieja salía siempre a
    // medias y el total se movía a lo largo de la jornada. «7 días» son hoy y los seis
    // anteriores.
    final hoy = DateTime.now();
    final from = DateTime(hoy.year, hoy.month, hoy.day)
        .subtract(Duration(days: filter.days - 1))
        .toUtc();

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

  /// Lo cobrado en un día. `day` es la fecha del taller; sin ella, hoy.
  Future<CashClose> cashClose({DateTime? day, String? branchId}) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/reports/cash-close',
      queryParameters: {
        // A mediodía y no a medianoche: la API resuelve el día del taller a partir de este
        // instante, y las 00:00 de otro desplazamiento podrían caer en el día anterior.
        if (day != null)
          'date': DateTime(day.year, day.month, day.day, 12).toUtc().toIso8601String(),
        if (branchId != null) 'branchId': branchId,
      },
    );

    return CashClose.fromJson(response.data!);
  }

  /// El libro de ventas de un mes, en CSV, tal como lo pide el contador.
  Future<List<int>> salesBookCsv({required int year, required int month}) async {
    final response = await _dio.get<List<int>>(
      '/api/reports/sales-book.csv',
      queryParameters: {'year': year, 'month': month},
      options: Options(responseType: ResponseType.bytes),
    );

    return response.data!;
  }
}

/// Solo lo puede pedir el Dueño: a los demás la API responde 403.
final revenueReportProvider =
    FutureProvider.autoDispose.family<RevenueReport, ReportFilter>(
  (ref, filter) => ref.watch(reportRepositoryProvider).revenue(filter),
);

/// Lo cobrado en el día que se esté mirando. `null` es hoy.
final cashCloseProvider = FutureProvider.autoDispose.family<CashClose, DateTime?>(
  (ref, day) => ref.watch(reportRepositoryProvider).cashClose(day: day),
);

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';

/// Trabajos frecuentes: el cambio de aceite, las pastillas de adelante, lo que el taller repite.
///
/// En el teléfono es donde más se nota, porque teclear de pie y con las manos sucias es lo caro.
/// Aquí solo se listan y se aplican; crearlos y corregirlos se hace en el panel, que es una
/// acción de catálogo y se hace una vez.

class JobTemplate {
  const JobTemplate({
    required this.id,
    required this.name,
    required this.description,
    required this.taskCount,
    required this.partCount,
    required this.total,
    required this.usageCount,
  });

  factory JobTemplate.fromJson(Map<String, dynamic> json) => JobTemplate(
        id: json['id'] as String,
        name: json['name'] as String,
        description: json['description'] as String?,
        taskCount: ((json['tasks'] as List<dynamic>?) ?? []).length,
        partCount: ((json['parts'] as List<dynamic>?) ?? []).length,
        total: (json['total'] as num?)?.toDouble() ?? 0,
        usageCount: json['usageCount'] as int? ?? 0,
      );

  final String id;
  final String name;
  final String? description;
  final int taskCount;
  final int partCount;

  /// Lo que costaría hoy. Sale del catálogo en cada consulta, no de lo que se guardó.
  final double total;
  final int usageCount;
}

/// Un repuesto que el trabajo lleva y que **todavía no se cargó**.
///
/// Cargar un repuesto descuenta la bodega, y al aplicar la plantilla el trabajo apenas empieza:
/// se proponen aquí y se cargan uno a uno cuando de verdad se instalan.
class SuggestedPart {
  const SuggestedPart({
    required this.partId,
    required this.sku,
    required this.partName,
    required this.unit,
    required this.quantity,
    required this.unitPrice,
    required this.available,
    required this.description,
  });

  factory SuggestedPart.fromJson(Map<String, dynamic> json) => SuggestedPart(
        partId: json['partId'] as String?,
        sku: json['sku'] as String? ?? '',
        partName: json['partName'] as String,
        unit: json['unit'] as String? ?? 'unidad',
        quantity: (json['quantity'] as num).toDouble(),
        unitPrice: (json['unitPrice'] as num).toDouble(),
        available: (json['available'] as num?)?.toDouble() ?? 0,
        description: json['description'] as String?,
      );

  final String? partId;
  final String sku;
  final String partName;
  final String unit;
  final double quantity;
  final double unitPrice;

  /// Existencia en la bodega de la sucursal de la orden, para ver que no hay antes de intentar.
  final double available;
  final String? description;

  bool get isShort => partId != null && available < quantity;
}

class ApplyTemplateResult {
  const ApplyTemplateResult({required this.templateName, required this.suggestedParts});

  factory ApplyTemplateResult.fromJson(Map<String, dynamic> json) => ApplyTemplateResult(
        templateName: json['templateName'] as String,
        suggestedParts: ((json['suggestedParts'] as List<dynamic>?) ?? [])
            .map((e) => SuggestedPart.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  final String templateName;
  final List<SuggestedPart> suggestedParts;
}

final jobTemplateRepositoryProvider = Provider<JobTemplateRepository>(
  (ref) => JobTemplateRepository(ref.watch(apiClientProvider).dio),
);

class JobTemplateRepository {
  JobTemplateRepository(this._dio);

  final Dio _dio;

  Future<List<JobTemplate>> list() async {
    final response = await _dio.get<List<dynamic>>('/api/job-templates');

    return response.data!
        .map((e) => JobTemplate.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Anexa los pasos del trabajo a la orden. Los repuestos vuelven como sugerencia.
  Future<ApplyTemplateResult> apply(String workOrderId, String templateId) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/work-orders/$workOrderId/apply-template',
      data: {'templateId': templateId},
    );

    return ApplyTemplateResult.fromJson(response.data!);
  }
}

/// Los trabajos frecuentes activos, el más usado primero. Al Cliente la API le responde 403.
final jobTemplatesProvider = FutureProvider.autoDispose<List<JobTemplate>>(
  (ref) => ref.watch(jobTemplateRepositoryProvider).list(),
);

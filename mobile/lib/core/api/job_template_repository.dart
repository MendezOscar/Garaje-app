import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';

/// Trabajos frecuentes: el cambio de aceite, las pastillas de adelante, lo que el taller repite.
///
/// En el teléfono es donde más se nota, porque teclear de pie y con las manos sucias es lo
/// caro. Se listan, se aplican, y se guardan desde una orden ya hecha: ese es el camino
/// bueno, porque los pasos y los repuestos ya están ahí y ya están bien.

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

  /// Guarda una orden ya hecha como trabajo frecuente.
  ///
  /// Es el camino principal y el único que vale la pena en el teléfono: los pasos, sus
  /// servicios y sus repuestos ya están en la orden, y salieron de un trabajo real.
  Future<JobTemplate> fromWorkOrder({
    required String workOrderId,
    required String name,
    String? description,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/job-templates/from-work-order',
      data: {'workOrderId': workOrderId, 'name': name, 'description': description},
    );

    return JobTemplate.fromJson(response.data!);
  }

  /// Crea un trabajo frecuente desde cero: nombre, descripción y sus pasos.
  ///
  /// Guardar una orden ya hecha sigue siendo el mejor camino —ahí los repuestos ya están—,
  /// pero el taller también quiere armar el trabajo de memoria antes de haberlo hecho nunca,
  /// y para eso no hay orden de donde copiar.
  Future<JobTemplate> create({
    required String name,
    String? description,
    required List<({String title, String? laborServiceId, double? estimatedHours})> tasks,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/job-templates',
      data: {
        'name': name,
        'description': description,
        'isActive': true,
        'tasks': [
          for (final paso in tasks)
            {
              'title': paso.title,
              'description': null,
              'laborServiceId': paso.laborServiceId,
              'estimatedHours': paso.estimatedHours,
            },
        ],
        // Los repuestos se agregan después, desde una orden: aquí todavía no se sabe de qué
        // marca ni cuántos lleva este carro en concreto.
        'parts': <Map<String, dynamic>>[],
      },
    );

    return JobTemplate.fromJson(response.data!);
  }

  /// El detalle: sus pasos y sus repuestos, a precios de hoy.
  Future<JobTemplateDetail> get(String id) async {
    final response = await _dio.get<Map<String, dynamic>>('/api/job-templates/$id');
    return JobTemplateDetail.fromJson(response.data!);
  }

  /// Le cambia el nombre, la descripción o si está activo. Los pasos y los repuestos se
  /// mandan tal como están: el servidor reemplaza la plantilla completa con lo que reciba.
  Future<JobTemplateDetail> rename(
    JobTemplateDetail plantilla, {
    required String name,
    String? description,
    required bool isActive,
  }) async {
    final response = await _dio.put<Map<String, dynamic>>(
      '/api/job-templates/${plantilla.id}',
      data: {
        'name': name,
        'description': description,
        'isActive': isActive,
        'tasks': [
          for (final paso in plantilla.tasks)
            {
              'title': paso.title,
              'description': paso.description,
              'laborServiceId': paso.laborServiceId,
              'estimatedHours': paso.estimatedHours,
            },
        ],
        'parts': [
          for (final repuesto in plantilla.parts)
            {
              'partId': repuesto.partId,
              'description': repuesto.partId == null ? repuesto.partName : null,
              'quantity': repuesto.quantity,
            },
        ],
      },
    );

    return JobTemplateDetail.fromJson(response.data!);
  }

  Future<void> remove(String id) => _dio.delete<void>('/api/job-templates/$id');

  /// Anexa los pasos del trabajo a la orden. Los repuestos vuelven como sugerencia.
  Future<ApplyTemplateResult> apply(String workOrderId, String templateId) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/work-orders/$workOrderId/apply-template',
      data: {'templateId': templateId},
    );

    return ApplyTemplateResult.fromJson(response.data!);
  }
}

/// Un trabajo frecuente con lo que lleva: sus pasos y sus repuestos, a precios de hoy.
class JobTemplateDetail {
  const JobTemplateDetail({
    required this.id,
    required this.name,
    required this.isActive,
    required this.tasks,
    required this.parts,
    required this.total,
    this.description,
  });

  factory JobTemplateDetail.fromJson(Map<String, dynamic> json) => JobTemplateDetail(
        id: json['id'] as String,
        name: json['name'] as String,
        description: json['description'] as String?,
        isActive: json['isActive'] as bool? ?? true,
        tasks: ((json['tasks'] as List<dynamic>?) ?? [])
            .map((e) => JobTemplateTask.fromJson(e as Map<String, dynamic>))
            .toList(),
        parts: ((json['parts'] as List<dynamic>?) ?? [])
            .map((e) => JobTemplatePart.fromJson(e as Map<String, dynamic>))
            .toList(),
        total: (json['total'] as num?)?.toDouble() ?? 0,
      );

  final String id;
  final String name;
  final String? description;
  final bool isActive;
  final List<JobTemplateTask> tasks;
  final List<JobTemplatePart> parts;
  final double total;
}

class JobTemplateTask {
  const JobTemplateTask({
    required this.title,
    this.description,
    this.laborServiceId,
    this.laborServiceName,
    this.estimatedHours,
    this.price,
  });

  factory JobTemplateTask.fromJson(Map<String, dynamic> json) => JobTemplateTask(
        title: json['title'] as String,
        description: json['description'] as String?,
        laborServiceId: json['laborServiceId'] as String?,
        laborServiceName: json['laborServiceName'] as String?,
        estimatedHours: (json['estimatedHours'] as num?)?.toDouble(),
        price: (json['price'] as num?)?.toDouble(),
      );

  final String title;
  final String? description;
  final String? laborServiceId;
  final String? laborServiceName;
  final double? estimatedHours;

  /// Lo que se cobraría por el paso hoy. Null si no lleva servicio del catálogo.
  final double? price;
}

class JobTemplatePart {
  const JobTemplatePart({
    required this.partName,
    required this.quantity,
    required this.unitPrice,
    required this.total,
    this.partId,
    this.sku,
    this.unit,
  });

  factory JobTemplatePart.fromJson(Map<String, dynamic> json) => JobTemplatePart(
        partId: json['partId'] as String?,
        sku: json['sku'] as String?,
        partName: json['partName'] as String,
        unit: json['unit'] as String?,
        quantity: (json['quantity'] as num).toDouble(),
        unitPrice: (json['unitPrice'] as num?)?.toDouble() ?? 0,
        total: (json['total'] as num?)?.toDouble() ?? 0,
      );

  final String? partId;
  final String? sku;
  final String partName;
  final String? unit;
  final double quantity;
  final double unitPrice;
  final double total;
}

/// El detalle de un trabajo frecuente. `autoDispose` porque se abre, se mira y se vuelve.
final jobTemplateDetailProvider =
    FutureProvider.autoDispose.family<JobTemplateDetail, String>(
  (ref, id) => ref.watch(jobTemplateRepositoryProvider).get(id),
);

/// Los trabajos frecuentes activos, el más usado primero. Al Cliente la API le responde 403.
final jobTemplatesProvider = FutureProvider.autoDispose<List<JobTemplate>>(
  (ref) => ref.watch(jobTemplateRepositoryProvider).list(),
);

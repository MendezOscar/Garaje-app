import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../models/quote.dart';

final quoteRepositoryProvider = Provider<QuoteRepository>(
  (ref) => QuoteRepository(ref.watch(apiClientProvider).dio),
);

class QuoteRepository {
  QuoteRepository(this._dio);

  final Dio _dio;

  /// El backend ya decide qué corresponde: el Dueño las ve todas, el Cliente solo las suyas
  /// y el Técnico ninguna.
  Future<List<Quote>> list({String? workOrderId}) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/quotes',
      queryParameters: {
        if (workOrderId != null) 'workOrderId': workOrderId,
        'pageSize': 50,
      },
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => Quote.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<Quote> get(String id) async {
    final response = await _dio.get<Map<String, dynamic>>('/api/quotes/$id');
    return Quote.fromJson(response.data!);
  }

  /// Respuesta desde dentro de la app. El Cliente aprueba lo suyo; el Dueño registra lo que
  /// le contestaron por teléfono.
  Future<Quote> respond(String id, {required bool approve, String? note}) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/quotes/$id/respond',
      data: {'approve': approve, 'note': note},
    );

    return Quote.fromJson(response.data!);
  }

  /// Marca la cotización como enviada y devuelve el link `wa.me` con el mensaje armado.
  Future<String> sendLink(String id) async {
    final response = await _dio.post<Map<String, dynamic>>('/api/quotes/$id/send');
    return response.data!['url'] as String;
  }

  /// Arma el borrador con lo que la orden ya tiene: los repuestos cargados y los pasos con
  /// servicio del catálogo. Es el punto de partida, no la cotización final.
  Future<Quote> createFromWorkOrder(String workOrderId, {DateTime? validUntil}) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/quotes/from-work-order',
      data: {
        'workOrderId': workOrderId,
        'validUntil': validUntil?.toUtc().toIso8601String(),
      },
    );

    return Quote.fromJson(response.data!);
  }

  /// Agrega una línea. El precio va null cuando sale del catálogo: lo pone el servidor, que
  /// es el que manda en los totales.
  Future<Quote> addLine(
    String id, {
    required LineType lineType,
    String? partId,
    String? laborServiceId,
    String? description,
    required double quantity,
    double? unitPrice,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/quotes/$id/lines',
      data: {
        'lineType': lineType.value,
        'partId': partId,
        'laborServiceId': laborServiceId,
        'description': description,
        'quantity': quantity,
        'unitPrice': unitPrice,
      },
    );

    return Quote.fromJson(response.data!);
  }

  Future<Quote> removeLine(String id, String lineId) async {
    final response = await _dio.delete<Map<String, dynamic>>('/api/quotes/$id/lines/$lineId');
    return Quote.fromJson(response.data!);
  }

  /// Vigencia y notas. El impuesto se deja como está: lo fija el taller, no la cotización.
  Future<Quote> update(String id, {DateTime? validUntil, String? notes}) async {
    final response = await _dio.put<Map<String, dynamic>>(
      '/api/quotes/$id',
      data: {
        'validUntil': validUntil?.toUtc().toIso8601String(),
        'notes': notes,
      },
    );

    return Quote.fromJson(response.data!);
  }
}

/// Cotizaciones de una orden, **con sus líneas**: el listado no las trae y son las líneas lo
/// que se lee y lo que se factura, así que se pide el detalle de cada una. Son una o dos por
/// orden. `autoDispose` para que al volver se relean: el cliente pudo haber respondido desde
/// el link de WhatsApp mientras tanto.
final workOrderQuotesProvider =
    FutureProvider.autoDispose.family<List<Quote>, String>((ref, workOrderId) async {
  final repository = ref.watch(quoteRepositoryProvider);
  final list = await repository.list(workOrderId: workOrderId);

  return Future.wait(list.map((q) => repository.get(q.id)));
});

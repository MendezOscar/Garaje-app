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
}

/// Cotizaciones de una orden. `autoDispose` para que al volver se relean: el cliente pudo
/// haber respondido desde el link de WhatsApp mientras tanto.
final workOrderQuotesProvider =
    FutureProvider.autoDispose.family<List<Quote>, String>(
  (ref, workOrderId) => ref.watch(quoteRepositoryProvider).list(workOrderId: workOrderId),
);

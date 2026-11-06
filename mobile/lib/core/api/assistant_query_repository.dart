import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../models/assistant_query.dart';

final assistantQueryRepositoryProvider = Provider<AssistantQueryRepository>(
  (ref) => AssistantQueryRepository(ref.watch(apiClientProvider).dio),
);

/// Preguntar con el vehículo delante.
class AssistantQueryRepository {
  AssistantQueryRepository(this._dio);

  final Dio _dio;

  Future<List<ConversationListItem>> list(String workOrderId) async {
    final response = await _dio.get<List<dynamic>>(
      '/api/work-orders/$workOrderId/assistant/queries',
    );

    return (response.data ?? [])
        .map((e) => ConversationListItem.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<Conversation> get(String workOrderId, String conversationId) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/work-orders/$workOrderId/assistant/queries/$conversationId',
    );

    return Conversation.fromJson(response.data!);
  }

  /// Sin [conversationId] abre una consulta nueva. Vale 5 créditos y se cierra a las seis.
  Future<Conversation> ask(
    String workOrderId,
    String question, {
    String? conversationId,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/work-orders/$workOrderId/assistant/queries',
      data: {'question': question, 'conversationId': conversationId},
    );

    return Conversation.fromJson(response.data!);
  }
}

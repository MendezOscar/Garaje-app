import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../models/notification.dart';

final notificationRepositoryProvider = Provider<NotificationRepository>(
  (ref) => NotificationRepository(ref.watch(apiClientProvider).dio),
);

class NotificationRepository {
  NotificationRepository(this._dio);

  final Dio _dio;

  Future<List<AppNotification>> list() async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/notifications',
      queryParameters: {'pageSize': 30},
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => AppNotification.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<int> unreadCount() async {
    final response = await _dio.get<Map<String, dynamic>>('/api/notifications/unread-count');
    return response.data!['unread'] as int;
  }

  Future<void> markRead(String id) => _dio.post<void>('/api/notifications/$id/read');

  Future<void> markAllRead() => _dio.post<void>('/api/notifications/read-all');

  /// Registra el token de push del aparato. Se llama en cada arranque porque FCM rota el
  /// token por su cuenta y un token viejo deja de entregar sin avisar.
  Future<void> registerDevice(String token, String platform) => _dio.post<void>(
        '/api/notifications/devices',
        data: {'token': token, 'platform': platform == 'ios' ? 2 : 1},
      );

  /// Da de baja el aparato al salir. En el taller el teléfono se comparte: sin esto, el
  /// siguiente en entrar recibiría los avisos del anterior.
  Future<void> unregisterDevice(String token) =>
      _dio.delete<void>('/api/notifications/devices/$token');
}

final notificationsProvider =
    FutureProvider.autoDispose<List<AppNotification>>((ref) => ref.watch(notificationRepositoryProvider).list());

/// Contador del globo rojo.
///
/// Se refresca solo cada minuto. Sin esto habría que abrir la pantalla de avisos para
/// enterarse de que hay algo nuevo, que es justo lo que el globo evita.
final unreadCountProvider = StreamProvider.autoDispose<int>((ref) async* {
  final repository = ref.watch(notificationRepositoryProvider);

  while (true) {
    try {
      yield await repository.unreadCount();
    } catch (_) {
      // Sin red no hay contador, pero tampoco un error a pantalla completa por una
      // campana: se reintenta en el siguiente ciclo.
    }
    await Future<void>.delayed(const Duration(seconds: 60));
  }
});

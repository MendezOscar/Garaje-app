import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/api_client.dart';
import '../models/current_user.dart';
import '../push/push_messaging.dart';
import 'token_store.dart';

final tokenStoreProvider = Provider<TokenStore>((ref) => TokenStore());

final apiClientProvider = Provider<ApiClient>((ref) {
  return ApiClient(
    tokenStore: ref.watch(tokenStoreProvider),
    onSessionExpired: () async => ref.read(authControllerProvider.notifier).forceLogout(),
  );
});

/// Estado de sesión. `loading` cubre el arranque, mientras se decide si el token guardado
/// sigue siendo válido: sin ese estado la app parpadearía en el login antes de entrar.
sealed class AuthState {
  const AuthState();
}

class AuthLoading extends AuthState {
  const AuthLoading();
}

class AuthSignedOut extends AuthState {
  const AuthSignedOut();
}

class AuthSignedIn extends AuthState {
  const AuthSignedIn(this.user);

  final CurrentUser user;
}

final authControllerProvider = NotifierProvider<AuthController, AuthState>(AuthController.new);

class AuthController extends Notifier<AuthState> {
  @override
  AuthState build() {
    Future.microtask(restoreSession);
    return const AuthLoading();
  }

  Dio get _dio => ref.read(apiClientProvider).dio;
  TokenStore get _tokens => ref.read(tokenStoreProvider);

  /// Valida contra el backend el token guardado. Si no sirve, se cierra sesión en silencio.
  Future<void> restoreSession() async {
    if (await _tokens.readAccessToken() == null) {
      state = const AuthSignedOut();
      return;
    }

    try {
      final response = await _dio.get<Map<String, dynamic>>('/api/auth/me');
      state = AuthSignedIn(CurrentUser.fromJson(response.data!));
    } on DioException {
      await _tokens.clear();
      state = const AuthSignedOut();
    }
  }

  Future<void> login(String email, String password) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/auth/login',
      data: {'email': email, 'password': password},
    );

    final auth = AuthResponse.fromJson(response.data!);
    await _tokens.save(accessToken: auth.accessToken, refreshToken: auth.refreshToken);
    state = AuthSignedIn(auth.user);
  }

  Future<void> logout() async {
    // Primero se da de baja el aparato, mientras la sesión todavía sirve: después el
    // endpoint respondería 401 y el teléfono seguiría recibiendo los avisos de quien salió.
    await ref.read(pushMessagingProvider).stop();

    final refreshToken = await _tokens.readRefreshToken();

    if (refreshToken != null) {
      // Si la petición falla igual cerramos la sesión local: el usuario pidió salir.
      try {
        await _dio.post<void>('/api/auth/logout', data: {'refreshToken': refreshToken});
      } on DioException {
        // Ignorado a propósito.
      }
    }

    await forceLogout();
  }

  /// Cierre de sesión sin avisar al backend: lo usa el interceptor cuando el refresh falla.
  Future<void> forceLogout() async {
    await _tokens.clear();
    state = const AuthSignedOut();
  }
}

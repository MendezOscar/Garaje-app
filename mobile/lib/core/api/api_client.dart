import 'package:dio/dio.dart';

import '../auth/token_store.dart';

/// URL de la API.
///
/// Por defecto apunta a producción para que `flutter run` funcione tal cual en cualquier
/// dispositivo. No hay un valor local que sirva en todos: el emulador de Android llega al
/// host por 10.0.2.2, el simulador de iOS por localhost, y un teléfono físico necesita la
/// IP de la máquina en la red. Un defecto que solo funciona en uno de los tres confunde
/// más de lo que ayuda.
///
/// Para desarrollar contra la API local, se pasa al compilar:
///   `flutter run --dart-define=API_URL=http://localhost:5080`      (simulador iOS)
///   `flutter run --dart-define=API_URL=http://10.0.2.2:5080`       (emulador Android)
///   `flutter run --dart-define=API_URL=http://192.168.1.10:5080`   (dispositivo físico)
const apiBaseUrl = String.fromEnvironment(
  'API_URL',
  defaultValue: 'https://garaje-app.onrender.com',
);

class ApiClient {
  ApiClient({required TokenStore tokenStore, required this.onSessionExpired})
      : _tokenStore = tokenStore {
    dio = Dio(BaseOptions(
      baseUrl: apiBaseUrl,
      connectTimeout: const Duration(seconds: 15),
      // Generoso a propósito: la cobertura dentro de un taller es mala y una subida de
      // fotos por 3G lenta no debería abortarse como si fuera un error.
      receiveTimeout: const Duration(seconds: 60),
      contentType: Headers.jsonContentType,
    ));

    dio.interceptors.add(InterceptorsWrapper(
      onRequest: _attachToken,
      onError: _refreshOnUnauthorized,
    ));
  }

  late final Dio dio;
  final TokenStore _tokenStore;

  /// Se llama cuando el refresh falla y hay que devolver al usuario al login.
  final Future<void> Function() onSessionExpired;

  /// Un solo refresh en vuelo: si varias peticiones reciben 401 a la vez y cada una
  /// intentara refrescar, la rotación del backend invalidaría las siguientes y cerraría
  /// la sesión del técnico en medio del trabajo.
  Future<String?>? _refreshInFlight;

  Future<void> _attachToken(RequestOptions options, RequestInterceptorHandler handler) async {
    if (!options.path.contains('/api/auth/')) {
      final token = await _tokenStore.readAccessToken();
      if (token != null) options.headers['Authorization'] = 'Bearer $token';
    }
    handler.next(options);
  }

  Future<void> _refreshOnUnauthorized(DioException error, ErrorInterceptorHandler handler) async {
    final request = error.requestOptions;
    final alreadyRetried = request.extra['retried'] == true;

    if (error.response?.statusCode != 401 ||
        alreadyRetried ||
        request.path.contains('/api/auth/')) {
      return handler.next(error);
    }

    final token = await (_refreshInFlight ??= _refresh().whenComplete(() {
      _refreshInFlight = null;
    }));

    if (token == null) {
      await onSessionExpired();
      return handler.next(error);
    }

    request.extra['retried'] = true;
    request.headers['Authorization'] = 'Bearer $token';

    try {
      handler.resolve(await dio.fetch(request));
    } on DioException catch (e) {
      handler.next(e);
    }
  }

  Future<String?> _refresh() async {
    final refreshToken = await _tokenStore.readRefreshToken();
    if (refreshToken == null) return null;

    try {
      // Dio aparte: el interceptor de esta instancia reentraría en el mismo flujo.
      final response = await Dio(BaseOptions(baseUrl: apiBaseUrl)).post<Map<String, dynamic>>(
        '/api/auth/refresh',
        data: {'refreshToken': refreshToken},
      );

      final data = response.data!;
      await _tokenStore.save(
        accessToken: data['accessToken'] as String,
        refreshToken: data['refreshToken'] as String,
      );
      return data['accessToken'] as String;
    } on DioException {
      await _tokenStore.clear();
      return null;
    }
  }
}

/// Mensaje legible del ProblemDetails que devuelve la API.
String apiErrorMessage(Object error, [String fallback = 'Ocurrió un error inesperado.']) {
  if (error is DioException) {
    final data = error.response?.data;
    if (data is Map<String, dynamic> && data['detail'] is String) {
      return data['detail'] as String;
    }
    if (error.type == DioExceptionType.connectionError ||
        error.type == DioExceptionType.connectionTimeout) {
      return 'No se pudo conectar con el servidor. Revise la red.';
    }
  }
  return fallback;
}

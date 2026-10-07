import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:package_info_plus/package_info_plus.dart';

import '../auth/auth_controller.dart';

/// Qué le toca a esta versión de la app.
enum ExigenciaDeVersion {
  /// Al día: no se dice nada.
  pasa,

  /// Hay una más nueva. Se avisa arriba, pero se puede seguir trabajando.
  avisar,

  /// Demasiado vieja: no se deja pasar hasta actualizar.
  bloquear,
}

/// Lo que el servidor exige, ya comparado con la versión instalada.
class VersionDeLaApp {
  const VersionDeLaApp({
    required this.exigencia,
    required this.storeUrl,
    required this.compilacionInstalada,
    required this.compilacionMinima,
    this.mensaje,
  });

  /// Lo que se asume mientras no se sabe, y lo que queda si el servidor no contesta: nadie
  /// se queda sin trabajar porque la consulta de versión falló.
  static const desconocida = VersionDeLaApp(
    exigencia: ExigenciaDeVersion.pasa,
    storeUrl: '',
    compilacionInstalada: 0,
    compilacionMinima: 0,
  );

  final ExigenciaDeVersion exigencia;
  final String storeUrl;
  final int compilacionInstalada;
  final int compilacionMinima;
  final String? mensaje;

  bool get bloquea => exigencia == ExigenciaDeVersion.bloquear;
  bool get avisa => exigencia == ExigenciaDeVersion.avisar;
}

class AppVersionRepository {
  AppVersionRepository(this._dio);

  final Dio _dio;

  /// Le pregunta al servidor qué versión hace falta.
  ///
  /// El servidor decide: manda `pasa`, `avisar` o `bloquear` mirando la versión que la app
  /// puso en su cabecera. Comparar números aquí sería repetir la regla en dos sitios, y el
  /// día que cambie quedaría mal en uno.
  Future<VersionDeLaApp> consultar() async {
    final info = await PackageInfo.fromPlatform();
    final instalada = int.tryParse(info.buildNumber) ?? 0;

    final response = await _dio.get<Map<String, dynamic>>('/api/app/version');
    final data = response.data!;

    return VersionDeLaApp(
      exigencia: switch (data['required'] as String?) {
        'bloquear' => ExigenciaDeVersion.bloquear,
        'avisar' => ExigenciaDeVersion.avisar,
        _ => ExigenciaDeVersion.pasa,
      },
      storeUrl: data['storeUrl'] as String? ?? _tiendaPorDefecto,
      compilacionInstalada: instalada,
      compilacionMinima: (data['minimumBuild'] as num?)?.toInt() ?? 0,
      mensaje: data['message'] as String?,
    );
  }

  static String get _tiendaPorDefecto => Platform.isAndroid
      ? 'https://play.google.com/store/apps/details?id=com.garaj.garaj_app'
      : 'https://apps.apple.com/app/id6805656010';
}

final appVersionRepositoryProvider = Provider<AppVersionRepository>(
  (ref) => AppVersionRepository(ref.watch(apiClientProvider).dio),
);

/// Lo que exige el servidor, consultado al arrancar.
///
/// Si la consulta falla —sin señal, servidor dormido— queda en «pasa»: dejar al taller sin
/// app porque no se pudo preguntar sería peor que cualquier incompatibilidad.
class VersionDeLaAppNotifier extends AsyncNotifier<VersionDeLaApp> {
  @override
  Future<VersionDeLaApp> build() async {
    try {
      return await ref.read(appVersionRepositoryProvider).consultar();
    } catch (_) {
      return VersionDeLaApp.desconocida;
    }
  }

  /// Lo que llama el cliente HTTP cuando el servidor responde 426 en cualquier petición.
  ///
  /// Es el caso de quien ya tenía la app abierta cuando se subió el mínimo: no va a volver a
  /// arrancar, así que la primera petición que le rebote es la que lo entera.
  Future<void> bloquearAhora() async {
    final anterior = state.value;
    if (anterior?.bloquea == true) return;

    // Se vuelve a preguntar para traer el enlace de la tienda y el mensaje del servidor; si
    // eso también falla, se bloquea igual con lo que haya.
    try {
      state = AsyncData(await ref.read(appVersionRepositoryProvider).consultar());
    } catch (_) {
      state = AsyncData(VersionDeLaApp(
        exigencia: ExigenciaDeVersion.bloquear,
        storeUrl: AppVersionRepository._tiendaPorDefecto,
        compilacionInstalada: anterior?.compilacionInstalada ?? 0,
        compilacionMinima: anterior?.compilacionMinima ?? 0,
      ));
    }
  }
}

final versionDeLaAppProvider =
    AsyncNotifierProvider<VersionDeLaAppNotifier, VersionDeLaApp>(
  VersionDeLaAppNotifier.new,
);

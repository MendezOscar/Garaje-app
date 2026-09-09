import 'dart:developer' as developer;
import 'dart:ui';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/auth/auth_controller.dart';
import 'core/push/push_messaging.dart';
import 'core/router/app_router.dart';
import 'core/sync/upload_queue.dart';
import 'core/theme/garaj_brand.dart';

void main() {
  // Un error que no tumba la app —una excepción dentro de un build, un Future sin capturar—
  // no dejaba rastro de ninguna clase. Los fallos que sí tumban el proceso los recoge Play
  // Console por su cuenta, con versión, modelo y sistema, sin SDK ni permisos; lo que no
  // puede saber es en qué iba el usuario, y eso es lo que se anota aquí.
  //
  // No sale nada del teléfono: la app no lleva analítica ni informes de fallos, y así está
  // declarado en Play y en la política de privacidad.
  final presentarComoSiempre = FlutterError.onError;
  FlutterError.onError = (details) {
    presentarComoSiempre?.call(details);
    _anotarFallo(details.exception, details.stack);
  };
  PlatformDispatcher.instance.onError = (error, stack) {
    _anotarFallo(error, stack);
    return true;
  };

  runApp(const ProviderScope(child: GarajApp()));
}

/// Cómo preguntar por la pantalla actual, para no tener que importar el router aquí. Se llena
/// cuando el router ya existe; un fallo durante el arranque lo encuentra en nulo, y el
/// registro lo dice en vez de mentir.
String Function()? _pantallaActual;

void _anotarFallo(Object error, StackTrace? stack) {
  String donde;
  try {
    donde = _pantallaActual?.call() ?? 'antes de abrir la primera pantalla';
  } catch (_) {
    donde = 'pantalla desconocida';
  }

  // `developer.log` y no `debugPrint` porque este lleva el error y la traza como tales: al
  // leerlo con `adb logcat` o con la consola de Xcode sale la pila completa, que es lo que
  // sirve cuando un taller reporta que «se cerró sola».
  developer.log(
    'Fallo en $donde',
    name: 'GarajApp',
    error: error,
    stackTrace: stack,
  );
}

class GarajApp extends ConsumerStatefulWidget {
  const GarajApp({super.key});

  @override
  ConsumerState<GarajApp> createState() => _GarajAppState();
}

class _GarajAppState extends ConsumerState<GarajApp> {
  late final AppLifecycleListener _lifecycle;

  @override
  void initState() {
    super.initState();

    // Las fotos que quedaron esperando red se reintentan al volver a la app. Antes solo se
    // reintentaban al abrir la galería de esa orden, así que una foto tomada en un taller sin
    // señal podía quedarse ahí días aunque el teléfono ya tuviera internet — y la pantalla de
    // bienvenida promete que se suben solas.
    _lifecycle = AppLifecycleListener(
      onResume: () => ref.read(uploadQueueProvider.notifier).flush(),
    );
  }

  @override
  void dispose() {
    _lifecycle.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final router = ref.watch(appRouterProvider);
    _pantallaActual ??= () => router.routerDelegate.currentConfiguration.uri.path;

    // El push se enciende con la sesión y se apaga al salir: el aparato se registra a nombre
    // de quien entró. Sin proyecto de Firebase esto no hace nada y la app funciona igual.
    ref.listen<AuthState>(authControllerProvider, (previous, next) {
      final push = ref.read(pushMessagingProvider);
      if (next is AuthSignedIn && previous is! AuthSignedIn) {
        push.start();
        // Y al entrar: si la app se cerró con fotos pendientes, se suben ahora.
        ref.read(uploadQueueProvider.notifier).flush();
      } else if (next is AuthSignedOut && previous is AuthSignedIn) {
        push.stop();
      }
    });

    return MaterialApp.router(
      title: 'GarajApp',
      debugShowCheckedModeBanner: false,
      routerConfig: router,
      theme: garajTheme,
      darkTheme: garajDarkTheme,
    );
  }
}

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/auth/auth_controller.dart';
import 'core/push/push_messaging.dart';
import 'core/router/app_router.dart';
import 'core/sync/upload_queue.dart';
import 'core/theme/garaj_brand.dart';

void main() {
  runApp(const ProviderScope(child: GarajApp()));
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

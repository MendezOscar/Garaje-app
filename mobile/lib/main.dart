import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/auth/auth_controller.dart';
import 'core/push/push_messaging.dart';
import 'core/router/app_router.dart';
import 'core/theme/garaj_brand.dart';

void main() {
  runApp(const ProviderScope(child: GarajApp()));
}

class GarajApp extends ConsumerWidget {
  const GarajApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(appRouterProvider);

    // El push se enciende con la sesión y se apaga al salir: el aparato se registra a nombre
    // de quien entró. Sin proyecto de Firebase esto no hace nada y la app funciona igual.
    ref.listen<AuthState>(authControllerProvider, (previous, next) {
      final push = ref.read(pushMessagingProvider);
      if (next is AuthSignedIn && previous is! AuthSignedIn) {
        push.start();
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

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/garaj_brand.dart';
import '../shared/brand_logo.dart';

/// Se muestra mientras se valida el token guardado. Sin ella la app parpadearía en el login
/// antes de entrar, en cada arranque.
///
/// Repite el azul y la tuerca de la pantalla de arranque nativa: así el paso del arranque
/// del sistema a Flutter no se nota, en lugar de un parpadeo blanco entre las dos.
///
/// Si el servidor no contesta también se queda aquí, con un botón para reintentar: la sesión
/// sigue guardada y lo que falló fue la red, así que mandar al login sería mentirle al
/// usuario y hacerle escribir su contraseña de gusto.
class SplashScreen extends ConsumerWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authControllerProvider);

    return Scaffold(
      backgroundColor: GarajColors.brand,
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              const BrandLockup(inverted: true),
              // Pegado al logotipo: a 40 px se veía flotando solo, como un elemento suelto.
              const SizedBox(height: 24),
              if (auth is AuthUnreachable)
                _SinConexion(auth.message)
              else
                const SizedBox(
                  width: 22,
                  height: 22,
                  child: CircularProgressIndicator(
                    strokeWidth: 2,
                    valueColor: AlwaysStoppedAnimation(Colors.white70),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _SinConexion extends ConsumerWidget {
  const _SinConexion(this.message);

  final String message;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final controller = ref.read(authControllerProvider.notifier);

    return Column(
      children: [
        Text(
          message,
          textAlign: TextAlign.center,
          style: const TextStyle(color: Colors.white),
        ),
        const SizedBox(height: 8),
        const Text(
          'Su sesión sigue abierta. Cuando haya señal, vuelva a intentarlo.',
          textAlign: TextAlign.center,
          style: TextStyle(color: Colors.white70, fontSize: 13),
        ),
        const SizedBox(height: 24),
        FilledButton(
          onPressed: controller.restoreSession,
          style: FilledButton.styleFrom(
            backgroundColor: Colors.white,
            foregroundColor: GarajColors.brand,
          ),
          child: const Text('Reintentar'),
        ),
        const SizedBox(height: 4),
        // Salida por si quien tiene el teléfono en la mano no es quien dejó la sesión abierta.
        TextButton(
          onPressed: controller.forceLogout,
          style: TextButton.styleFrom(foregroundColor: Colors.white70),
          child: const Text('Entrar con otra cuenta'),
        ),
      ],
    );
  }
}

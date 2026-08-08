import 'package:flutter/material.dart';

import '../../core/theme/garaj_brand.dart';
import '../shared/brand_logo.dart';

/// Se muestra mientras se valida el token guardado. Sin ella la app parpadearía en el login
/// antes de entrar, en cada arranque.
///
/// Repite el azul y la tuerca de la pantalla de arranque nativa: así el paso del arranque
/// del sistema a Flutter no se nota, en lugar de un parpadeo blanco entre las dos.
class SplashScreen extends StatelessWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const Scaffold(
      backgroundColor: GarajColors.brand,
      body: Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            BrandLockup(inverted: true),
            SizedBox(height: 40),
            SizedBox(
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
    );
  }
}

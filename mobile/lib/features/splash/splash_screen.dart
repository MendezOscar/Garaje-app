import 'package:flutter/material.dart';

/// Se muestra mientras se valida el token guardado. Sin ella la app parpadearía en el
/// login antes de entrar, en cada arranque.
class SplashScreen extends StatelessWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const Scaffold(
      body: Center(child: CircularProgressIndicator()),
    );
  }
}

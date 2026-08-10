import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';

/// El logo del taller, para la barra de la aplicación.
///
/// La ruta que devuelve la sesión es relativa y la sirve un endpoint público: `Image.network`
/// no manda cabecera de autorización, y una URL prefirmada caducaría a los 15 minutos
/// dejando el logo roto en una aplicación abierta toda la mañana.
///
/// Si el taller no subió logo —o si el teléfono está sin señal— no deja hueco ni error a la
/// vista: simplemente no dibuja nada.
class TenantLogo extends ConsumerWidget {
  const TenantLogo({this.height = 20, super.key});

  final double height;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authControllerProvider);
    if (auth is! AuthSignedIn) return const SizedBox.shrink();

    final path = auth.user.tenantLogoUrl;
    if (path == null) return const SizedBox.shrink();

    return Image.network(
      '$apiBaseUrl$path',
      height: height,
      fit: BoxFit.contain,
      // Sin marcador de carga: aparece a los milisegundos y un cuadro gris parpadeando en la
      // barra se ve peor que el logo llegando un instante tarde.
      errorBuilder: (_, __, ___) => const SizedBox.shrink(),
    );
  }
}

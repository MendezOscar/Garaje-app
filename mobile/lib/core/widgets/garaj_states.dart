import 'package:flutter/material.dart';

import '../theme/garaj_brand.dart';

/// Lo que se ve cuando no hay nada que ver.
///
/// Una lista vacía casi nunca es un error: es que el taller todavía no ha hecho eso. El
/// texto dice qué va a aparecer aquí y, cuando hay algo que hacer, el botón lo hace. Lo que
/// no se deja es el hueco en blanco: quien abre la pantalla por primera vez no sabe si está
/// vacía o si se rompió.
class GarajEmpty extends StatelessWidget {
  const GarajEmpty({super.key, required this.title, this.hint, this.action});

  /// Qué falta, en una línea: «Todavía no hay órdenes».
  final String title;

  /// Por qué está vacío o qué hacer para llenarlo.
  final String? hint;

  /// La acción que llena la lista, si la hay.
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context);

    return ListView(
      // Lista y no Column para que el gesto de halar a recargar siga funcionando: en el
      // taller, lo primero que se hace con una pantalla vacía es halarla.
      padding: const EdgeInsets.symmetric(
        horizontal: GarajSpace.lg,
        vertical: GarajSpace.xl * 2,
      ),
      children: [
        Text(
          title,
          textAlign: TextAlign.center,
          style: t.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600),
        ),
        if (hint != null) ...[
          const SizedBox(height: GarajSpace.sm),
          Text(
            hint!,
            textAlign: TextAlign.center,
            style: t.textTheme.bodyMedium?.copyWith(color: t.hintColor),
          ),
        ],
        if (action != null) ...[
          const SizedBox(height: GarajSpace.lg),
          Center(child: action),
        ],
      ],
    );
  }
}

/// El aviso de que algo falló, con la salida a mano.
///
/// Siempre con botón: un error sin salida deja al usuario cerrando y abriendo la aplicación,
/// que es justo cuando empieza a desconfiar de que los datos estén bien.
class GarajError extends StatelessWidget {
  const GarajError({super.key, required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.symmetric(
        horizontal: GarajSpace.lg,
        vertical: GarajSpace.xl * 2,
      ),
      children: [
        Text(message, textAlign: TextAlign.center),
        const SizedBox(height: GarajSpace.md),
        Center(
          child: FilledButton.tonal(onPressed: onRetry, child: const Text('Reintentar')),
        ),
      ],
    );
  }
}

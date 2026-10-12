import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/sucursal_activa.dart';
import '../../core/auth/auth_controller.dart';

/// El botón que cambia la sucursal que se está mirando.
///
/// No se pinta cuando el taller tiene una sola: sería un botón con una única opción, y lo que
/// enseñaría —el nombre de la sucursal— ya lo dice la orden.
class SelectorDeSucursal extends ConsumerWidget {
  const SelectorDeSucursal({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authControllerProvider);
    if (auth is! AuthSignedIn || auth.user.branches.length < 2) {
      return const SizedBox.shrink();
    }

    final theme = Theme.of(context);
    final elegida = ref.watch(sucursalActivaProvider);
    final encontrada = auth.user.branches.where((b) => b.id == elegida);
    final nombre = encontrada.isEmpty ? 'Todas las sucursales' : encontrada.first.name;

    return PopupMenuButton<String>(
      // Cadena vacía y no null: null es «se salió del menú sin elegir».
      onSelected: (value) =>
          ref.read(sucursalActivaProvider.notifier).set(value.isEmpty ? null : value),
      itemBuilder: (_) => [
        CheckedPopupMenuItem(
          value: '',
          checked: elegida == null,
          child: const Text('Todas las sucursales'),
        ),
        for (final sucursal in auth.user.branches)
          CheckedPopupMenuItem(
            value: sucursal.id,
            checked: sucursal.id == elegida,
            child: Text(sucursal.name),
          ),
      ],
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.store_outlined, size: 16, color: theme.colorScheme.primary),
          const SizedBox(width: 4),
          ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 180),
            child: Text(
              nombre,
              overflow: TextOverflow.ellipsis,
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.primary,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
          Icon(Icons.arrow_drop_down, size: 18, color: theme.colorScheme.primary),
        ],
      ),
    );
  }
}

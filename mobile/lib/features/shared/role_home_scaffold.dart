import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_controller.dart';

/// Andamio común de las tres pantallas de inicio: título, datos de sesión y salir.
/// Se comparte porque en la Fase 0 las tres solo confirman que el login funciona; en la
/// Fase 1 cada perfil se lleva su propio contenido en [child].
class RoleHomeScaffold extends ConsumerWidget {
  const RoleHomeScaffold({
    required this.title,
    required this.roleLabel,
    required this.pendingHint,
    super.key,
  });

  final String title;
  final String roleLabel;
  final String pendingHint;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authControllerProvider);
    final user = auth is AuthSignedIn ? auth.user : null;
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(
        title: Text(title),
        actions: [
          IconButton(
            tooltip: 'Salir',
            icon: const Icon(Icons.logout),
            onPressed: () => ref.read(authControllerProvider.notifier).logout(),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(user?.fullName ?? '—', style: theme.textTheme.titleMedium),
                  Text(user?.email ?? '', style: theme.textTheme.bodySmall),
                  const SizedBox(height: 12),
                  Text('Perfil: $roleLabel'),
                  Text('Taller: ${user?.tenantName ?? '—'}'),
                  Text(
                    'Sucursales: ${user == null || user.branches.isEmpty ? '—' : user.branches.map((b) => b.name).join(', ')}',
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 16),
          Text(
            pendingHint,
            style: theme.textTheme.bodyMedium
                ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
          ),
        ],
      ),
    );
  }
}

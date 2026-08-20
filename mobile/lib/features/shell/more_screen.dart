import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/dashboard_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/current_user.dart';
import '../reports/reports_screen.dart' show money;
import '../shared/delete_account.dart';

/// El resto de la app, en una pantalla.
///
/// Antes todo esto era un menú «⋯» en la barra de la bandeja: entradas iguales, sin saber si
/// había algo dentro hasta abrirlas. Aquí van agrupadas por lo que se va a hacer y con el dato
/// al lado, para no entrar a ver si hay algo. Lo que aparece depende del perfil: el Dueño
/// administra un taller, el Técnico solo necesita salir de aquí, y el Cliente menos.
class MoreScreen extends ConsumerWidget {
  const MoreScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authControllerProvider);
    if (auth is! AuthSignedIn) return const SizedBox.shrink();

    final theme = Theme.of(context);
    final role = auth.user.role;

    // El resumen es del Dueño: a los demás la API responde 403, así que ni se pide.
    final d = role == AppRole.owner ? ref.watch(dashboardProvider).value : null;
    final recordatorios =
        role == AppRole.owner ? ref.watch(remindersDueProvider).value : null;

    return Scaffold(
      appBar: AppBar(title: const Text('Más')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 12, 16, 32),
        children: [
          Row(
            children: [
              CircleAvatar(
                backgroundColor: theme.colorScheme.surfaceContainerHighest,
                child: Icon(Icons.person_outline, color: theme.colorScheme.onSurfaceVariant),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(auth.user.fullName, style: theme.textTheme.titleSmall),
                    Text(
                      '${_perfil(role)} · ${auth.user.tenantName}',
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 20),

          if (role == AppRole.owner) ...[
            _Group(
              title: 'Trabajo',
              rows: [
                _Row(
                  icon: Icons.inbox_outlined,
                  label: 'Requerimientos',
                  foot: d == null
                      ? null
                      : d.pendingRequests == 0
                          ? 'nada sin atender'
                          : '${d.pendingRequests} sin atender',
                  route: '/requerimientos',
                ),
                _Row(
                  icon: Icons.notifications_active_outlined,
                  label: 'Recordatorios',
                  foot: recordatorios == null
                      ? null
                      : recordatorios.isEmpty
                          ? 'ninguno este mes'
                          : '${recordatorios.length} este mes',
                  route: '/recordatorios',
                ),
              ],
            ),
            _Group(
              title: 'Dinero',
              rows: [
                _Row(
                  icon: Icons.payments_outlined,
                  label: 'Por cobrar',
                  foot: d == null
                      ? null
                      : d.overdueReceivables > 0
                          ? '${money(d.overdueReceivables, d.currency)} vencido'
                          : money(d.receivables, d.currency),
                  route: '/por-cobrar',
                ),
                _Row(icon: Icons.insights_outlined, label: 'Reportes', route: '/reportes'),
              ],
            ),
            _Group(
              title: 'Catálogos y taller',
              rows: [
                _Row(icon: Icons.contacts_outlined, label: 'Clientes', route: '/clientes'),
                _Row(
                  icon: Icons.inventory_2_outlined,
                  label: 'Inventario',
                  foot: d == null || d.partsBelowMinimum == 0
                      ? null
                      : '${d.partsBelowMinimum} bajo mínimo',
                  route: '/inventario',
                ),
                _Row(icon: Icons.group_outlined, label: 'Usuarios', route: '/usuarios'),
              ],
            ),
          ],

          if (role == AppRole.technician)
            _Group(
              title: 'Trabajo',
              rows: [
                // Las citas que el mostrador todavía no ha convertido en orden: el técnico las
                // consulta para saber qué le va a caer.
                _Row(
                  icon: Icons.inbox_outlined,
                  label: 'Requerimientos',
                  route: '/requerimientos',
                ),
                _Row(icon: Icons.notifications_outlined, label: 'Avisos', route: '/avisos'),
                _Row(
                  icon: Icons.search,
                  label: 'Buscar una orden',
                  foot: 'por placa, folio o cliente',
                  route: '/ordenes',
                ),
                // A veces el mostrador es él: recibir un vehículo no es solo del Dueño.
                _Row(
                  icon: Icons.add_circle_outline,
                  label: 'Recibir un vehículo',
                  route: '/nueva-cita',
                ),
              ],
            ),

          if (role == AppRole.customer)
            _Group(
              title: 'Su taller',
              rows: [
                _Row(icon: Icons.notifications_outlined, label: 'Avisos', route: '/avisos'),
                _Row(
                  icon: Icons.add_circle_outline,
                  label: 'Pedir una cita',
                  route: '/nueva-cita',
                ),
              ],
            ),

          _Group(
            title: 'Cuenta',
            rows: [
              _Row(
                icon: Icons.logout,
                label: 'Salir',
                onTap: () => ref.read(authControllerProvider.notifier).logout(),
              ),
              // Quien entra tiene que poder salirse del todo, no solo cerrar la sesión: es lo
              // que Apple exige, y aquí es donde el revisor la va a buscar.
              _Row(
                icon: Icons.person_remove_outlined,
                label: 'Eliminar mi cuenta',
                danger: true,
                onTap: () => confirmarEliminarCuenta(context, ref, role),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

String _perfil(AppRole role) => switch (role) {
      AppRole.owner => 'Dueño',
      AppRole.technician => 'Mecánico',
      AppRole.customer => 'Cliente',
    };

class _Group extends StatelessWidget {
  const _Group({required this.title, required this.rows});

  final String title;
  final List<_Row> rows;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title.toUpperCase(),
            style: theme.textTheme.labelSmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
              letterSpacing: 0.6,
            ),
          ),
          const SizedBox(height: 6),
          Card(
            clipBehavior: Clip.antiAlias,
            child: Column(
              children: [
                for (var i = 0; i < rows.length; i++) ...[
                  if (i > 0) Divider(height: 1, color: theme.dividerColor),
                  rows[i],
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _Row extends StatelessWidget {
  const _Row({
    required this.icon,
    required this.label,
    this.foot,
    this.route,
    this.onTap,
    this.danger = false,
  });

  final IconData icon;
  final String label;

  /// El dato de la entrada. Null mientras el resumen va en camino: aparece al llegar en vez
  /// de reservar un hueco vacío.
  final String? foot;

  final String? route;
  final VoidCallback? onTap;
  final bool danger;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final color = danger ? theme.colorScheme.error : null;

    return ListTile(
      leading: Icon(icon, color: color ?? theme.colorScheme.onSurfaceVariant),
      title: Text(label, style: TextStyle(color: color)),
      subtitle: foot == null ? null : Text(foot!),
      trailing: Icon(Icons.chevron_right, size: 18, color: theme.colorScheme.onSurfaceVariant),
      onTap: onTap ?? (route == null ? null : () => context.push(route!)),
    );
  }
}

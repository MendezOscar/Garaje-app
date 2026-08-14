import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/api/dashboard_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/current_user.dart';
import '../../core/models/work_order.dart';
import '../reports/reports_screen.dart' show money;
import '../notifications/notifications_screen.dart';
import '../shared/status_chip.dart';
import '../shared/tenant_logo.dart';

/// Bandeja de órdenes. La comparten el Técnico ("mis asignaciones"), el Dueño (las del
/// taller) y el Cliente (las de sus vehículos): el backend ya filtra por perfil, así que
/// solo cambia el título.
class WorkOrderListScreen extends ConsumerWidget {
  const WorkOrderListScreen({required this.title, required this.emptyMessage, super.key});

  final String title;
  final String emptyMessage;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final orders = ref.watch(myWorkOrdersProvider);
    final auth = ref.watch(authControllerProvider);

    return Scaffold(
      appBar: AppBar(
        // El logo del taller a la izquierda del título: la aplicación es de su taller, y en
        // un teléfono compartido eso también dice en qué taller está uno metido.
        title: Row(
          children: [
            const TenantLogo(),
            const SizedBox(width: 8),
            Expanded(child: Text(title, overflow: TextOverflow.ellipsis)),
          ],
        ),
        actions: [
          if (auth is AuthSignedIn && auth.user.role == AppRole.owner)
            IconButton(
              tooltip: 'Por cobrar',
              icon: const Icon(Icons.payments_outlined),
              onPressed: () => context.push('/por-cobrar'),
            ),
          if (auth is AuthSignedIn && auth.user.role != AppRole.customer)
            IconButton(
              tooltip: 'Requerimientos',
              icon: const Icon(Icons.inbox_outlined),
              onPressed: () => context.push('/requerimientos'),
            ),
          const NotificationBell(),
          // El resto del taller va en un menú y no en más iconos: la barra de un teléfono no
          // aguanta seis, y estas pantallas se abren de vez en cuando, no a cada rato.
          if (auth is AuthSignedIn)
            PopupMenuButton<String>(
              tooltip: 'Más',
              onSelected: (value) {
                if (value == 'salir') {
                  ref.read(authControllerProvider.notifier).logout();
                } else {
                  context.push(value);
                }
              },
              itemBuilder: (context) => [
                if (auth.user.role == AppRole.owner) ...[
                  const PopupMenuItem(
                    value: '/por-cobrar',
                    child: ListTile(
                      leading: Icon(Icons.payments_outlined),
                      title: Text('Por cobrar'),
                    ),
                  ),
                  const PopupMenuItem(
                    value: '/reportes',
                    child: ListTile(
                      leading: Icon(Icons.insights_outlined),
                      title: Text('Reportes'),
                    ),
                  ),
                  const PopupMenuItem(
                    value: '/clientes',
                    child: ListTile(
                      leading: Icon(Icons.contacts_outlined),
                      title: Text('Clientes'),
                    ),
                  ),
                  const PopupMenuItem(
                    value: '/usuarios',
                    child: ListTile(
                      leading: Icon(Icons.group_outlined),
                      title: Text('Usuarios'),
                    ),
                  ),
                ],
                if (auth.user.role != AppRole.customer)
                  const PopupMenuItem(
                    value: '/inventario',
                    child: ListTile(
                      leading: Icon(Icons.inventory_2_outlined),
                      title: Text('Inventario'),
                    ),
                  ),
                const PopupMenuItem(
                  value: 'salir',
                  child: ListTile(leading: Icon(Icons.logout), title: Text('Salir')),
                ),
              ],
            ),
        ],
        // El filtro va en la barra y no dentro de la lista: así no se va con el scroll
        // justo cuando se está buscando una orden vieja.
        bottom: auth is AuthSignedIn ? const _OrdersFilter() : null,
      ),
      // Los tres perfiles: el Cliente pide cita desde su casa y el taller recibe el vehículo
      // en el mostrador, que es de donde entra la mayoría de los ingresos.
      floatingActionButton: auth is AuthSignedIn
          ? FloatingActionButton.extended(
              onPressed: () => context.push('/nueva-cita'),
              icon: const Icon(Icons.add),
              label: Text(
                auth.user.role == AppRole.customer ? 'Pedir cita' : 'Recibir vehículo',
              ),
            )
          : null,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(myWorkOrdersProvider),
        child: orders.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => _ErrorState(
            message: apiErrorMessage(e, 'No se pudieron cargar las órdenes.'),
            onRetry: () => ref.invalidate(myWorkOrdersProvider),
          ),
          data: (items) => items.isEmpty
              // ListView aunque esté vacío: si no, no se puede tirar para refrescar.
              ? ListView(
                  children: [
                    const SizedBox(height: 120),
                    Center(
                      child: Padding(
                        padding: const EdgeInsets.all(24),
                        child: Text(
                          // Con una búsqueda escrita, el mensaje de "no hay nada" del perfil
                          // haría dudar de la búsqueda misma.
                          ref.watch(ordersSearchProvider).trim().isEmpty
                              ? emptyMessage
                              : 'Ninguna orden coincide con esa búsqueda.',
                          textAlign: TextAlign.center,
                        ),
                      ),
                    ),
                  ],
                )
              : ListView.separated(
                  padding: const EdgeInsets.all(12),
                  // Una fila más al principio para el resumen de ingresos del Dueño.
                  itemCount: items.length + 1,
                  separatorBuilder: (_, __) => const SizedBox(height: 8),
                  itemBuilder: (context, i) => i == 0
                      ? const _RevenueSummary()
                      : _OrderCard(order: items[i - 1]),
                ),
        ),
      ),
      bottomNavigationBar: auth is AuthSignedIn
          ? SafeArea(
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                child: Text(
                  '${auth.user.fullName} · ${auth.user.tenantName}',
                  style: Theme.of(context).textTheme.bodySmall,
                  textAlign: TextAlign.center,
                ),
              ),
            )
          : null,
    );
  }
}

/// Buscador y filtro de la bandeja.
///
/// Una orden entregada salía de la bandeja y no había forma de volver a ella —ni para ver qué
/// se le hizo al vehículo, ni para compartir otra vez la factura—. Buscando, el filtro se
/// ignora: quien teclea una placa la quiere encontrar aunque el carro ya haya salido.
class _OrdersFilter extends ConsumerStatefulWidget implements PreferredSizeWidget {
  const _OrdersFilter();

  @override
  Size get preferredSize => const Size.fromHeight(108);

  @override
  ConsumerState<_OrdersFilter> createState() => _OrdersFilterState();
}

class _OrdersFilterState extends ConsumerState<_OrdersFilter> {
  late final TextEditingController _controller =
      TextEditingController(text: ref.read(ordersSearchProvider));

  Timer? _debounce;

  @override
  void dispose() {
    _debounce?.cancel();
    _controller.dispose();
    super.dispose();
  }

  /// Media pausa antes de preguntar: en el taller la señal es mala y una petición por tecla
  /// haría que la lista bailara mientras se escribe la placa.
  void _onChanged(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 400), () {
      if (mounted) ref.read(ordersSearchProvider.notifier).set(value);
    });
  }

  void _clear() {
    _debounce?.cancel();
    _controller.clear();
    ref.read(ordersSearchProvider.notifier).set('');
  }

  @override
  Widget build(BuildContext context) {
    final onlyOpen = ref.watch(onlyOpenOrdersProvider);
    final buscando = ref.watch(ordersSearchProvider).trim().isNotEmpty;

    return Padding(
      padding: const EdgeInsets.only(left: 12, right: 12, bottom: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          TextField(
            controller: _controller,
            textInputAction: TextInputAction.search,
            onChanged: _onChanged,
            onSubmitted: (value) => ref.read(ordersSearchProvider.notifier).set(value),
            decoration: InputDecoration(
              isDense: true,
              filled: true,
              hintText: 'Placa, número de orden o cliente',
              prefixIcon: const Icon(Icons.search, size: 20),
              suffixIcon: buscando || _controller.text.isNotEmpty
                  ? IconButton(
                      tooltip: 'Limpiar',
                      icon: const Icon(Icons.close, size: 20),
                      onPressed: _clear,
                    )
                  : null,
              border: const OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 8),
          // Buscando, el selector no manda: se apaga para no prometer un filtro que no aplica.
          SegmentedButton<bool>(
            segments: const [
              ButtonSegment(value: true, label: Text('En el taller')),
              ButtonSegment(value: false, label: Text('Todas')),
            ],
            selected: {buscando ? false : onlyOpen},
            showSelectedIcon: false,
            style: const ButtonStyle(visualDensity: VisualDensity.compact),
            onSelectionChanged: buscando
                ? null
                : (value) => ref.read(onlyOpenOrdersProvider.notifier).set(value.first),
          ),
        ],
      ),
    );
  }
}

class _OrderCard extends StatelessWidget {
  const _OrderCard({required this.order});

  final WorkOrderListItem order;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => context.push('/ordenes/${order.id}'),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Text(order.number, style: theme.textTheme.titleSmall),
                  const Spacer(),
                  if (order.isLate)
                    Padding(
                      padding: const EdgeInsets.only(right: 6),
                      child: Icon(Icons.schedule, size: 16, color: theme.colorScheme.error),
                    ),
                  StatusChip(status: order.status),
                ],
              ),
              const SizedBox(height: 6),
              Row(
                children: [
                  Icon(
                    order.vehicleType == VehicleType.motorcycle
                        ? Icons.two_wheeler
                        : Icons.directions_car,
                    size: 18,
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                  const SizedBox(width: 6),
                  Expanded(child: Text(order.vehicleLabel)),
                  if (order.plate != null)
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
                      decoration: BoxDecoration(
                        border: Border.all(color: theme.dividerColor),
                        borderRadius: BorderRadius.circular(3),
                      ),
                      child: Text(order.plate!, style: theme.textTheme.labelSmall),
                    ),
                ],
              ),
              const SizedBox(height: 4),
              Text(
                '${order.customerName} · ${order.branchName}',
                style: theme.textTheme.bodySmall,
              ),
              const SizedBox(height: 6),
              Text(
                order.description,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: theme.textTheme.bodyMedium,
              ),
              if (order.taskCount > 0) ...[
                const SizedBox(height: 10),
                LinearProgressIndicator(
                  value: order.tasksDone / order.taskCount,
                  minHeight: 4,
                ),
                const SizedBox(height: 4),
                Text(
                  '${order.tasksDone} de ${order.taskCount} pasos',
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _ErrorState extends StatelessWidget {
  const _ErrorState({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    return ListView(
      children: [
        const SizedBox(height: 120),
        Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              children: [
                Text(message, textAlign: TextAlign.center),
                const SizedBox(height: 16),
                FilledButton.tonal(onPressed: onRetry, child: const Text('Reintentar')),
              ],
            ),
          ),
        ),
      ],
    );
  }
}


/// Resumen de ingresos del Dueño. Los otros perfiles no lo ven —la API les respondería 403—
/// y para ellos el widget desaparece sin dejar hueco.
class _RevenueSummary extends ConsumerWidget {
  const _RevenueSummary();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authControllerProvider);
    if (auth is! AuthSignedIn || auth.user.role != AppRole.owner) {
      return const SizedBox.shrink();
    }

    final theme = Theme.of(context);
    final summary = ref.watch(dashboardProvider);

    return summary.maybeWhen(
      data: (d) => Card(
        margin: const EdgeInsets.only(bottom: 4),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'INGRESOS',
                style: theme.textTheme.labelSmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                  letterSpacing: 0.6,
                ),
              ),
              const SizedBox(height: 8),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  _Figure(label: 'Hoy', value: _money(d.today, d.currency), emphasis: true),
                  _Figure(label: 'Semana', value: _money(d.week, d.currency)),
                  _Figure(label: 'Mes', value: _money(d.month, d.currency)),
                ],
              ),
              if (d.lateWorkOrders > 0 || d.pendingRequests > 0 || d.partsBelowMinimum > 0) ...[
                const SizedBox(height: 10),
                Wrap(
                  spacing: 12,
                  runSpacing: 4,
                  children: [
                    if (d.pendingRequests > 0)
                      Text('${d.pendingRequests} por atender', style: theme.textTheme.bodySmall),
                    if (d.lateWorkOrders > 0)
                      Text(
                        '${d.lateWorkOrders} atrasadas',
                        style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error),
                      ),
                    if (d.partsBelowMinimum > 0)
                      Text('${d.partsBelowMinimum} repuestos bajo mínimo',
                          style: theme.textTheme.bodySmall),
                  ],
                ),
              ],
            ],
          ),
        ),
      ),
      orElse: () => const SizedBox.shrink(),
    );
  }

  // El símbolo y con separador de miles, igual que los reportes: «L 4,890» y no «HNL 4890».
  static String _money(double value, String currency) => money(value, currency);
}

class _Figure extends StatelessWidget {
  const _Figure({required this.label, required this.value, this.emphasis = false});

  final String label;
  final String value;
  final bool emphasis;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: theme.textTheme.bodySmall),
        Text(
          value,
          style: emphasis ? theme.textTheme.titleLarge : theme.textTheme.titleMedium,
        ),
      ],
    );
  }
}

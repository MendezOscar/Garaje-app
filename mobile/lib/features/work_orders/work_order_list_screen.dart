import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/current_user.dart';
import '../../core/models/work_order.dart';
import '../notifications/notifications_screen.dart';
import '../shared/status_chip.dart';
import '../shared/subscription_banner.dart';
import '../shared/tenant_logo.dart';

/// Bandeja de órdenes. La comparten el Técnico ("mis asignaciones"), el Dueño (las del
/// taller) y el Cliente (las de sus vehículos): el backend ya filtra por perfil, así que
/// solo cambia el título.
class WorkOrderListScreen extends ConsumerWidget {
  const WorkOrderListScreen({
    required this.title,
    required this.emptyMessage,
    super.key,
  });

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
        actions: const [NotificationBell()],
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
      // La franja del cobro va pegada arriba de la bandeja: el Dueño tiene que toparse con
      // ella, pero sin tapar nada —con la mensualidad por vencer se sigue trabajando igual—.
      // Solo llega con datos para el Dueño: al Técnico el backend le manda null.
      body: Column(children: [
        if (auth is AuthSignedIn && (auth.user.subscription?.shouldWarn ?? false))
          SubscriptionBanner(info: auth.user.subscription!),
        Expanded(child: _OrdersList(orders: orders, emptyMessage: emptyMessage)),
      ]),
    );
  }
}

/// La lista en sí. Salió del `build` de arriba al aparecer la franja del cobro: dejarla
/// anidada dentro de una Column empujaba todo dos niveles más adentro sin ganar nada.
class _OrdersList extends ConsumerWidget {
  const _OrdersList({required this.orders, required this.emptyMessage});

  final AsyncValue<List<WorkOrderListItem>> orders;
  final String emptyMessage;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return RefreshIndicator(
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
                itemCount: items.length,
                separatorBuilder: (_, __) => const SizedBox(height: 8),
                itemBuilder: (context, i) => _OrderCard(order: items[i]),
              ),
      ),
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

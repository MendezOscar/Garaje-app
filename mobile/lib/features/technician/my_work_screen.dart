import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/work_order.dart';
import '../notifications/notifications_screen.dart';
import '../shared/status_chip.dart';
import '../work_orders/photo_capture.dart';

/// El día del Técnico, ordenado por lo que toca.
///
/// Antes abría la misma bandeja que el Dueño: tarjetas por folio, buscador y un selector de
/// «En el taller / Todas». Pero él no administra un taller, hace un trabajo a la vez. Aquí
/// arriba está la orden que tiene en el banco —con el paso que sigue y los dos botones que
/// usa, marcar y fotografiar—, después lo que va a empezar y al final lo que está detenido
/// esperando a alguien de fuera.
class MyWorkScreen extends ConsumerWidget {
  const MyWorkScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authControllerProvider);
    if (auth is! AuthSignedIn) return const SizedBox.shrink();

    final ordenes = ref.watch(openOrdersProvider);

    return Scaffold(
      appBar: AppBar(
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('Mi trabajo'),
            Text(
              '${auth.user.fullName} · ${auth.user.tenantName}',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
        ),
        actions: [
          IconButton(
            tooltip: 'Buscar una orden',
            icon: const Icon(Icons.search),
            // Lo entregado sale de esta pantalla; para una orden vieja está la bandeja, que
            // busca por placa, folio o cliente.
            onPressed: () => context.push('/ordenes'),
          ),
          const NotificationBell(),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(openOrdersProvider),
        child: ordenes.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(
            children: [
              const SizedBox(height: 100),
              Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Column(
                    children: [
                      Text(
                        apiErrorMessage(e, 'No se pudo cargar tu trabajo.'),
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 16),
                      FilledButton.tonal(
                        onPressed: () => ref.invalidate(openOrdersProvider),
                        child: const Text('Reintentar'),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
          data: (items) => _Cuerpo(items: items),
        ),
      ),
    );
  }
}

class _Cuerpo extends StatelessWidget {
  const _Cuerpo({required this.items});

  final List<WorkOrderListItem> items;

  @override
  Widget build(BuildContext context) {
    // La del banco es la que ya está en proceso o en pruebas. Si hay varias —pasa en un taller
    // grande— la primera es la de fecha prometida más cercana.
    final enMano = items
        .where((o) =>
            o.status == WorkOrderStatus.inProgress || o.status == WorkOrderStatus.testing)
        .toList()
      ..sort((a, b) => (a.promisedAt ?? DateTime(2100)).compareTo(b.promisedAt ?? DateTime(2100)));

    final porEmpezar = items
        .where((o) =>
            o.status == WorkOrderStatus.received || o.status == WorkOrderStatus.diagnosing)
        .toList();
    final detenidas = items.where((o) => o.status.isBlocked).toList();
    final listas = items.where((o) => o.status == WorkOrderStatus.ready).toList();

    if (items.isEmpty) {
      return ListView(
        children: const [
          SizedBox(height: 120),
          Center(
            child: Padding(
              padding: EdgeInsets.all(24),
              child: Text(
                'No tienes órdenes asignadas ahora mismo.',
                textAlign: TextAlign.center,
              ),
            ),
          ),
        ],
      );
    }

    return ListView(
      padding: const EdgeInsets.fromLTRB(12, 12, 12, 32),
      children: [
        if (enMano.isNotEmpty) _AhoraCard(order: enMano.first),
        // Las demás en proceso van con las de empezar: no hay una sola «ahora».
        _Grupo(title: 'Por empezar', orders: [...enMano.skip(1), ...porEmpezar]),
        _Grupo(title: 'Detenidas', orders: detenidas),
        _Grupo(title: 'Listas para entrega', orders: listas),
      ],
    );
  }
}

/// La orden que tiene en el banco: el paso que sigue y los dos botones que usa.
class _AhoraCard extends ConsumerStatefulWidget {
  const _AhoraCard({required this.order});

  final WorkOrderListItem order;

  @override
  ConsumerState<_AhoraCard> createState() => _AhoraCardState();
}

class _AhoraCardState extends ConsumerState<_AhoraCard> {
  bool _busy = false;

  /// El detalle hace falta para saber cuál es el paso que sigue: el listado solo trae el
  /// contador. Es una petición más, y es la pantalla que el técnico tiene abierta todo el día.
  WorkOrderTask? _siguiente(WorkOrderDetail? detail) {
    final pendientes = (detail?.tasks ?? const <WorkOrderTask>[])
        .where((t) => !t.isDone)
        .toList()
      ..sort((a, b) => a.sequence.compareTo(b.sequence));

    return pendientes.isEmpty ? null : pendientes.first;
  }

  Future<void> _marcar(WorkOrderTask task) async {
    setState(() => _busy = true);
    try {
      await ref
          .read(workOrderRepositoryProvider)
          .completeTask(widget.order.id, task.id, isDone: true);

      ref
        ..invalidate(workOrderDetailProvider(widget.order.id))
        ..invalidate(openOrdersProvider);
    } catch (e) {
      _aviso(apiErrorMessage(e, 'No se pudo marcar el paso.'));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _foto() async {
    setState(() => _busy = true);
    try {
      final tomada = await capturarFoto(ref, workOrderId: widget.order.id);
      if (tomada) _aviso('Foto guardada en la orden.');
    } catch (e) {
      _aviso(apiErrorMessage(e, 'No se pudo guardar la foto.'));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _aviso(String mensaje) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(mensaje)));
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final order = widget.order;
    final detail = ref.watch(workOrderDetailProvider(order.id)).value;
    final siguiente = _siguiente(detail);

    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Card(
        clipBehavior: Clip.antiAlias,
        shape: RoundedRectangleBorder(
          side: BorderSide(color: theme.colorScheme.primary.withValues(alpha: 0.45)),
          borderRadius: const BorderRadius.all(Radius.circular(10)),
        ),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Text(
                    'AHORA',
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: theme.colorScheme.primary,
                      letterSpacing: 0.6,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const Spacer(),
                  Text(order.number, style: theme.textTheme.titleSmall),
                ],
              ),
              const SizedBox(height: 6),
              InkWell(
                onTap: () => context.push('/ordenes/${order.id}'),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Icon(
                          order.vehicleType == VehicleType.motorcycle
                              ? Icons.two_wheeler
                              : Icons.directions_car,
                          color: theme.colorScheme.onSurfaceVariant,
                        ),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Text(order.vehicleLabel, style: theme.textTheme.titleMedium),
                        ),
                        if (order.plate != null) _Placa(plate: order.plate!),
                      ],
                    ),
                    const SizedBox(height: 2),
                    Text(
                      '${order.customerName}${_prometida(order.promisedAt)}',
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              if (order.taskCount > 0) ...[
                const SizedBox(height: 10),
                Row(
                  children: [
                    Expanded(
                      child: LinearProgressIndicator(
                        value: order.tasksDone / order.taskCount,
                        minHeight: 4,
                      ),
                    ),
                    const SizedBox(width: 8),
                    Text(
                      '${order.tasksDone}/${order.taskCount}',
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ],
              const SizedBox(height: 10),
              Text.rich(
                TextSpan(
                  children: [
                    TextSpan(
                      text: siguiente == null ? '' : 'Sigue: ',
                      style: theme.textTheme.bodySmall,
                    ),
                    TextSpan(
                      text: siguiente?.title ??
                          (detail == null
                              ? 'Cargando los pasos…'
                              : 'Todos los pasos están hechos.'),
                      style: theme.textTheme.bodyLarge,
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 12),
              Row(
                children: [
                  Expanded(
                    child: FilledButton(
                      onPressed: _busy || siguiente == null ? null : () => _marcar(siguiente),
                      child: const Text('Marcar hecho'),
                    ),
                  ),
                  const SizedBox(width: 8),
                  SizedBox(
                    width: 56,
                    height: 48,
                    child: OutlinedButton(
                      onPressed: _busy ? null : _foto,
                      style: OutlinedButton.styleFrom(
                        padding: EdgeInsets.zero,
                        shape: const RoundedRectangleBorder(
                          borderRadius: BorderRadius.all(Radius.circular(6)),
                        ),
                        side: BorderSide(color: theme.dividerColor),
                      ),
                      child: const Icon(Icons.photo_camera_outlined, size: 20),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Grupo extends StatelessWidget {
  const _Grupo({required this.title, required this.orders});

  final String title;
  final List<WorkOrderListItem> orders;

  @override
  Widget build(BuildContext context) {
    if (orders.isEmpty) return const SizedBox.shrink();
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            '${title.toUpperCase()} · ${orders.length}',
            style: theme.textTheme.labelSmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
              letterSpacing: 0.6,
            ),
          ),
          const SizedBox(height: 6),
          for (final order in orders)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: _OrdenCompacta(order: order),
            ),
        ],
      ),
    );
  }
}

class _OrdenCompacta extends StatelessWidget {
  const _OrdenCompacta({required this.order});

  final WorkOrderListItem order;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => context.push('/ordenes/${order.id}'),
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Text(order.number, style: theme.textTheme.titleSmall),
                  const Spacer(),
                  StatusChip(status: order.status),
                ],
              ),
              const SizedBox(height: 4),
              Row(
                children: [
                  Expanded(child: Text(order.vehicleLabel)),
                  if (order.plate != null) _Placa(plate: order.plate!),
                ],
              ),
              Text(
                '${order.customerName}${_prometida(order.promisedAt)}',
                style: theme.textTheme.bodySmall,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Placa extends StatelessWidget {
  const _Placa({required this.plate});

  final String plate;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
      decoration: BoxDecoration(
        border: Border.all(color: theme.dividerColor),
        borderRadius: BorderRadius.circular(3),
      ),
      child: Text(plate, style: theme.textTheme.labelSmall),
    );
  }
}

/// «prometida hoy 4:00 p. m.», que es como se dice en el taller.
String _prometida(DateTime? promisedAt) {
  if (promisedAt == null) return '';

  final fecha = promisedAt.toLocal();
  final hoy = DateTime.now();
  final dias = DateTime(fecha.year, fecha.month, fecha.day)
      .difference(DateTime(hoy.year, hoy.month, hoy.day))
      .inDays;

  final cuando = switch (dias) {
    0 => 'hoy',
    1 => 'mañana',
    -1 => 'ayer',
    _ when dias < 0 => 'hace ${-dias} días',
    _ => 'en $dias días',
  };

  final hora = '${fecha.hour.toString().padLeft(2, '0')}:'
      '${fecha.minute.toString().padLeft(2, '0')}';

  return ' · prometida $cuando $hora';
}

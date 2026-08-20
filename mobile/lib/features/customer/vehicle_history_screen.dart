import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/api/service_request_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/models/work_order.dart';

/// Qué vehículo se está mirando en el historial. Fuera de la pantalla para que la tarjeta de
/// «Mi vehículo» pueda elegirlo antes de cambiar de pestaña.
final vehiculoElegidoProvider =
    NotifierProvider<VehiculoElegido, String?>(VehiculoElegido.new);

class VehiculoElegido extends Notifier<String?> {
  @override
  String? build() => null;

  void set(String? id) => state = id;
}

/// Lo que se le ha hecho al vehículo, visita por visita.
///
/// Es la pregunta del cliente cuando vuelve —«¿qué le hicieron la vez pasada?»— y hasta hoy
/// solo se podía contestar entrando a una orden y desplegando su historial. Sin importes: al
/// Cliente el backend no le da las facturas, que van por el enlace público del taller, así
/// que aquí se cuenta el trabajo y no el dinero.
class VehicleHistoryScreen extends ConsumerWidget {
  const VehicleHistoryScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final vehiculos = ref.watch(vehicleOptionsProvider('')).value ?? const [];
    final elegido = ref.watch(vehiculoElegidoProvider) ??
        (vehiculos.isEmpty ? null : vehiculos.first.id);

    return Scaffold(
      appBar: AppBar(title: const Text('Historial')),
      body: elegido == null
          ? const Center(
              child: Padding(
                padding: EdgeInsets.all(24),
                child: Text(
                  'Todavía no hay vehículos suyos registrados en el taller.',
                  textAlign: TextAlign.center,
                ),
              ),
            )
          : RefreshIndicator(
              onRefresh: () async => ref.invalidate(vehicleHistoryProvider(elegido)),
              child: ListView(
                padding: const EdgeInsets.fromLTRB(12, 12, 12, 32),
                children: [
                  if (vehiculos.length > 1)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 12),
                      child: Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          for (final v in vehiculos)
                            ChoiceChip(
                              label: Text(v.label),
                              selected: v.id == elegido,
                              onSelected: (_) =>
                                  ref.read(vehiculoElegidoProvider.notifier).set(v.id),
                            ),
                        ],
                      ),
                    ),
                  _Historial(vehicleId: elegido),
                ],
              ),
            ),
    );
  }
}

class _Historial extends ConsumerWidget {
  const _Historial({required this.vehicleId});

  final String vehicleId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final visitas = ref.watch(vehicleHistoryProvider(vehicleId));

    return visitas.when(
      loading: () => const Padding(
        padding: EdgeInsets.symmetric(vertical: 64),
        child: Center(child: CircularProgressIndicator()),
      ),
      error: (e, _) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 48),
        child: Center(
          child: Text(
            apiErrorMessage(e, 'No se pudo cargar el historial.'),
            textAlign: TextAlign.center,
          ),
        ),
      ),
      data: (items) {
        if (items.isEmpty) {
          return const Padding(
            padding: EdgeInsets.symmetric(vertical: 48, horizontal: 12),
            child: Text(
              'Este vehículo todavía no tiene visitas registradas.',
              textAlign: TextAlign.center,
            ),
          );
        }

        final ordenadas = [...items]..sort((a, b) => b.openedAt.compareTo(a.openedAt));
        final primera = ordenadas.last.openedAt.toLocal();

        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Card(
              child: Padding(
                padding: const EdgeInsets.all(14),
                child: Row(
                  children: [
                    _Cifra(rotulo: 'VISITAS', valor: '${ordenadas.length}'),
                    const SizedBox(width: 24),
                    _Cifra(
                      rotulo: 'DESDE',
                      valor: '${_mes(primera.month)} ${primera.year}',
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 12),
            Card(
              clipBehavior: Clip.antiAlias,
              child: Column(
                children: [
                  for (var i = 0; i < ordenadas.length; i++) ...[
                    if (i > 0) Divider(height: 1, color: theme.dividerColor),
                    _Visita(order: ordenadas[i]),
                  ],
                ],
              ),
            ),
            const SizedBox(height: 10),
            Text(
              'Cada visita guarda lo que se le hizo, las fotos y quién lo atendió.',
              style: theme.textTheme.bodySmall,
            ),
          ],
        );
      },
    );
  }
}

class _Cifra extends StatelessWidget {
  const _Cifra({required this.rotulo, required this.valor});

  final String rotulo;
  final String valor;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          rotulo,
          style: theme.textTheme.labelSmall?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
            letterSpacing: 0.6,
          ),
        ),
        Text(valor, style: theme.textTheme.titleLarge),
      ],
    );
  }
}

class _Visita extends StatelessWidget {
  const _Visita({required this.order});

  final WorkOrderListItem order;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final fecha = order.openedAt.toLocal();

    return InkWell(
      onTap: () => context.push('/ordenes/${order.id}'),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: 56,
              child: Text(
                '${fecha.day} ${_mes(fecha.month)}',
                style: theme.textTheme.bodySmall,
              ),
            ),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(order.description, maxLines: 2, overflow: TextOverflow.ellipsis),
                  Text(
                    [
                      order.number,
                      if (order.status.isOpen) 'en el taller ahora',
                      if (order.taskCount > 0) '${order.taskCount} pasos',
                    ].join(' · '),
                    style: theme.textTheme.bodySmall,
                  ),
                ],
              ),
            ),
            Icon(Icons.chevron_right, size: 18, color: theme.colorScheme.onSurfaceVariant),
          ],
        ),
      ),
    );
  }
}

String _mes(int mes) => const [
      'ene', 'feb', 'mar', 'abr', 'may', 'jun',
      'jul', 'ago', 'sep', 'oct', 'nov', 'dic',
    ][mes - 1];

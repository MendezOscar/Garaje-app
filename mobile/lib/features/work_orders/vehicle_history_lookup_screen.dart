import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/models/work_order.dart';
import '../../core/widgets/garaj_skeleton.dart';
import '../../core/widgets/garaj_states.dart';
import '../shared/status_chip.dart';

/// Lo que se le ha hecho a un vehículo, buscándolo por placa o por cliente.
///
/// Es la pregunta del mostrador cuando el cliente vuelve: «¿qué le hicieron la vez pasada?».
/// Hasta hoy había que encontrar primero una orden suya para llegar al historial, y si la
/// última fue hace un año no aparecía en ninguna lista.
class VehicleHistoryLookupScreen extends ConsumerStatefulWidget {
  const VehicleHistoryLookupScreen({super.key});

  @override
  ConsumerState<VehicleHistoryLookupScreen> createState() => _VehicleHistoryLookupScreenState();
}

class _VehicleHistoryLookupScreenState extends ConsumerState<VehicleHistoryLookupScreen> {
  final _buscador = TextEditingController();

  /// Lo que se está buscando de verdad: se fija al enviar, no en cada tecla. Buscar por
  /// letra traería el taller entero con la primera.
  String _busqueda = '';

  /// El vehículo elegido cuando la búsqueda devuelve varios.
  String? _vehiculoId;
  String? _vehiculoLabel;

  @override
  void dispose() {
    _buscador.dispose();
    super.dispose();
  }

  void _buscar(String texto) {
    setState(() {
      _busqueda = texto.trim();
      _vehiculoId = null;
      _vehiculoLabel = null;
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(title: const Text('Historial')),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
            child: TextField(
              controller: _buscador,
              textInputAction: TextInputAction.search,
              onSubmitted: _buscar,
              decoration: InputDecoration(
                prefixIcon: const Icon(Icons.search),
                hintText: 'Placa, cliente o número de orden',
                border: const OutlineInputBorder(),
                suffixIcon: _buscador.text.isEmpty
                    ? null
                    : IconButton(
                        tooltip: 'Limpiar la búsqueda',
                        icon: const Icon(Icons.close),
                        onPressed: () {
                          _buscador.clear();
                          _buscar('');
                        },
                      ),
              ),
            ),
          ),

          if (_vehiculoLabel case final label?)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16),
              child: Row(
                children: [
                  Expanded(child: Text(label, style: theme.textTheme.titleSmall)),
                  TextButton(
                    onPressed: () => setState(() {
                      _vehiculoId = null;
                      _vehiculoLabel = null;
                    }),
                    child: const Text('Ver todos'),
                  ),
                ],
              ),
            ),

          Expanded(child: _busqueda.isEmpty ? const _Vacio() : _resultados()),
        ],
      ),
    );
  }

  Widget _resultados() {
    // Con un vehículo elegido se piden todas sus visitas; sin él, lo que coincida con el
    // texto. Las dos consultas son la misma lista, sin filtrar por estado: el historial es
    // justamente lo ya entregado.
    final provider = _vehiculoId == null
        ? historyBySearchProvider(_busqueda)
        : vehicleHistoryProvider(_vehiculoId!);

    final visitas = ref.watch(provider);

    return visitas.when(
      loading: () => const GarajSkeletonList(rows: 4),
      error: (e, _) => GarajError(
        message: apiErrorMessage(e, 'No se pudo cargar el historial.'),
        onRetry: () => ref.invalidate(provider),
      ),
      data: (lista) {
        if (lista.isEmpty) {
          return const GarajEmpty(
            title: 'Ninguna visita con esa placa ni ese cliente',
            hint: 'Pruebe con el nombre del cliente, o con parte de la placa.',
          );
        }

        // Varios vehículos en el resultado: primero se elige cuál, porque mezclar la moto y
        // el carro del mismo cliente en una sola lista no es el historial de ninguno.
        final vehiculos = {for (final o in lista) o.vehicleId: o};
        if (_vehiculoId == null && vehiculos.length > 1) {
          return ListView(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
            children: [
              Text(
                '${vehiculos.length} vehículos con esa búsqueda. Elija cuál.',
                style: Theme.of(context).textTheme.bodySmall,
              ),
              const SizedBox(height: 8),
              for (final orden in vehiculos.values)
                Card(
                  child: ListTile(
                    title: Text(orden.vehicleLabel),
                    subtitle: Text(
                      '${orden.plate ?? 'sin placa'} · ${orden.customerName}',
                    ),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => setState(() {
                      _vehiculoId = orden.vehicleId;
                      _vehiculoLabel = '${orden.vehicleLabel} · ${orden.customerName}';
                    }),
                  ),
                ),
            ],
          );
        }

        final ordenadas = [...lista]..sort((a, b) => b.openedAt.compareTo(a.openedAt));

        return ListView.builder(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
          itemCount: ordenadas.length,
          itemBuilder: (context, i) => _Visita(orden: ordenadas[i]),
        );
      },
    );
  }
}

class _Vacio extends StatelessWidget {
  const _Vacio();

  @override
  Widget build(BuildContext context) => const GarajEmpty(
        title: 'Busque un vehículo',
        hint: 'Escriba la placa o el nombre del cliente y verá todo lo que se le ha hecho, '
            'visita por visita.',
      );
}

class _Visita extends StatelessWidget {
  const _Visita({required this.orden});

  final WorkOrderListItem orden;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      child: ListTile(
        title: Row(
          children: [
            Expanded(child: Text(orden.number, style: theme.textTheme.titleSmall)),
            StatusChip(status: orden.status),
          ],
        ),
        subtitle: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const SizedBox(height: 2),
            Text('${_fecha(orden.openedAt)} · ${orden.vehicleLabel}'),
            Text(orden.description, maxLines: 2, overflow: TextOverflow.ellipsis),
            if (orden.assignedTechnicianName case final tecnico?)
              Text('Lo atendió $tecnico', style: theme.textTheme.bodySmall),
          ],
        ),
        isThreeLine: true,
        onTap: () => context.push('/ordenes/${orden.id}'),
      ),
    );
  }
}

String _fecha(DateTime value) {
  final local = value.toLocal();
  return '${local.day.toString().padLeft(2, '0')}/'
      '${local.month.toString().padLeft(2, '0')}/${local.year}';
}

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/vehicle_scan_repository.dart';
import '../../core/models/vehicle_scan.dart';
import '../../core/models/work_order.dart';
import 'scan_vehicle_screen.dart';

/// Las lecturas del escáner que lleva la orden, y el botón para hacer una nueva.
class ScanSection extends ConsumerWidget {
  const ScanSection({required this.order, required this.canEdit, super.key});

  final WorkOrderDetail order;
  final bool canEdit;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final lecturas = ref.watch(vehicleScansProvider(order.id));
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (canEdit)
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: FilledButton.icon(
              onPressed: () async {
                final guardada = await Navigator.of(context).push<bool>(
                  MaterialPageRoute(
                    builder: (_) => ScanVehicleScreen(
                      workOrderId: order.id,
                      workOrderNumber: order.number,
                      vehicleLabel: order.plate ?? order.vehicleLabel,
                    ),
                  ),
                );

                if (guardada == true) ref.invalidate(vehicleScansProvider(order.id));
              },
              icon: const Icon(Icons.bluetooth_searching),
              label: const Text('Escanear vehículo'),
            ),
          ),
        lecturas.when(
          loading: () => const Center(
            child: Padding(padding: EdgeInsets.all(24), child: CircularProgressIndicator()),
          ),
          error: (e, _) => Text(
            apiErrorMessage(e, 'No se pudieron cargar las lecturas.'),
            style: TextStyle(color: theme.colorScheme.error),
          ),
          data: (data) {
            if (data.isEmpty) {
              return const Text(
                'Esta orden no tiene lecturas del escáner. Al hacer una, los códigos quedan '
                'guardados uno por uno y su informe se adjunta solo a la orden.',
              );
            }

            return Column(
              children: data.map((lectura) => _Lectura(lectura: lectura)).toList(),
            );
          },
        ),
      ],
    );
  }
}

class _Lectura extends StatelessWidget {
  const _Lectura({required this.lectura});

  final VehicleScan lectura;

  static String _fecha(DateTime value) {
    final local = value.toLocal();
    final d = local.day.toString().padLeft(2, '0');
    final m = local.month.toString().padLeft(2, '0');
    final h = local.hour.toString().padLeft(2, '0');
    final min = local.minute.toString().padLeft(2, '0');
    return '$d/$m/${local.year} $h:$min';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final fecha = _fecha(lectura.scannedAt);

    return Card(
      margin: const EdgeInsets.only(bottom: 10),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(fecha, style: theme.textTheme.titleSmall),
                Text(
                  lectura.codes.isEmpty
                      ? 'Sin códigos'
                      : '${lectura.codes.length} '
                          '${lectura.codes.length == 1 ? 'código' : 'códigos'}',
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ),
            Text(lectura.scannedByName, style: theme.textTheme.bodySmall),
            if (lectura.codes.isNotEmpty) ...[
              const SizedBox(height: 8),
              ...lectura.codes.map(
                (codigo) => Padding(
                  padding: const EdgeInsets.only(bottom: 6),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      SizedBox(
                        width: 56,
                        child: Text(
                          codigo.code,
                          style: const TextStyle(fontWeight: FontWeight.w700),
                        ),
                      ),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              codigo.description ?? 'Código propio del fabricante.',
                              style: theme.textTheme.bodySmall,
                            ),
                            Text(
                              codigo.status.etiqueta,
                              style: theme.textTheme.labelSmall
                                  ?.copyWith(color: theme.colorScheme.outline),
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ],
            if (lectura.notes != null) ...[
              const SizedBox(height: 6),
              Text(lectura.notes!, style: theme.textTheme.bodySmall),
            ],
            if (lectura.vin != null)
              Text('VIN: ${lectura.vin}', style: theme.textTheme.labelSmall),
          ],
        ),
      ),
    );
  }
}

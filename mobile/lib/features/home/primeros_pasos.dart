import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/customer_repository.dart';
import '../../core/api/sale_repository.dart';
import '../../core/api/work_order_repository.dart';

/// Qué ha hecho el taller de lo que hace falta para que la app sirva de algo.
///
/// Un taller recién dado de alta abre la app y ve todo en ceros: cero órdenes, cero ingresos,
/// cero repuestos bajo mínimo. La pantalla es correcta y no dice nada. Esto le dice por dónde
/// empezar, y desaparece solo cuando ya empezó —no hay nada que apagar ni que recordar—.
class PrimerosPasos {
  const PrimerosPasos({
    required this.tieneCliente,
    required this.recibioVehiculo,
    required this.cobro,
  });

  final bool tieneCliente;
  final bool recibioVehiculo;
  final bool cobro;

  bool get completo => tieneCliente && recibioVehiculo && cobro;
  int get hechos => (tieneCliente ? 1 : 0) + (recibioVehiculo ? 1 : 0) + (cobro ? 1 : 0);
}

/// Se pregunta en cadena y no de una: sin clientes no puede haber órdenes, y sin órdenes no
/// puede haber cobro. Un taller nuevo hace una sola consulta.
final primerosPasosProvider = FutureProvider.autoDispose<PrimerosPasos>((ref) async {
  final clientes = await ref.watch(customerRepositoryProvider).search(null);
  if (clientes.isEmpty) {
    return const PrimerosPasos(tieneCliente: false, recibioVehiculo: false, cobro: false);
  }

  // `onlyOpen: false` a propósito: una orden ya entregada también cuenta como vehículo
  // recibido, y con el filtro por defecto el paso se desmarcaría al cerrarla.
  final ordenes = await ref.watch(workOrderRepositoryProvider).list(onlyOpen: false);
  if (ordenes.isEmpty) {
    return const PrimerosPasos(tieneCliente: true, recibioVehiculo: false, cobro: false);
  }

  final ventas = await ref.watch(saleRepositoryProvider).list(pageSize: 1);
  return PrimerosPasos(
    tieneCliente: true,
    recibioVehiculo: true,
    cobro: ventas.total > 0,
  );
});

/// La tarjeta de primeros pasos, o nada si ya están dados.
///
/// Se prefirió esto a un recorrido con globitos sobre los botones: el recorrido se ve una vez,
/// se salta, y hay que mantenerlo cada vez que cambia una pantalla. La lista se queda hasta que
/// el trabajo está hecho, y cada línea lleva a donde se hace.
class PrimerosPasosCard extends ConsumerWidget {
  const PrimerosPasosCard({required this.onVerOrdenes, super.key});

  final VoidCallback onVerOrdenes;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final pasos = ref.watch(primerosPasosProvider).asData?.value;

    // Mientras se averigua no se muestra nada: una tarjeta que aparece y se va sola estorba
    // más de lo que ayuda.
    if (pasos == null || pasos.completo) return const SizedBox.shrink();

    final theme = Theme.of(context);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text('Primeros pasos', style: theme.textTheme.titleMedium),
                ),
                Text(
                  '${pasos.hechos} de 3',
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ),
            const SizedBox(height: 4),
            Text(
              'Con esto ya puede trabajar el día completo desde el teléfono.',
              style: theme.textTheme.bodySmall,
            ),
            const SizedBox(height: 8),
            _Paso(
              hecho: pasos.tieneCliente,
              texto: 'Registre su primer cliente',
              onTap: () => context.push('/clientes'),
            ),
            _Paso(
              hecho: pasos.recibioVehiculo,
              texto: 'Reciba un vehículo',
              onTap: () => context.push('/nueva-cita'),
            ),
            _Paso(
              hecho: pasos.cobro,
              texto: 'Cierre una orden y cóbrela',
              onTap: onVerOrdenes,
            ),
          ],
        ),
      ),
    );
  }
}

class _Paso extends StatelessWidget {
  const _Paso({required this.hecho, required this.texto, required this.onTap});

  final bool hecho;
  final String texto;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return InkWell(
      // Lo ya hecho no se toca: llevar a «registre su primer cliente» a quien tiene treinta
      // confunde. Se deja como constancia de que ese paso está dado.
      onTap: hecho ? null : onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 6),
        child: Row(
          children: [
            Icon(
              hecho ? Icons.check_circle : Icons.radio_button_unchecked,
              size: 20,
              color: hecho ? theme.colorScheme.primary : theme.disabledColor,
            ),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                texto,
                style: hecho
                    ? theme.textTheme.bodyMedium?.copyWith(
                        decoration: TextDecoration.lineThrough,
                        color: theme.textTheme.bodySmall?.color,
                      )
                    : theme.textTheme.bodyMedium,
              ),
            ),
            if (!hecho) const Icon(Icons.chevron_right, size: 20),
          ],
        ),
      ),
    );
  }
}

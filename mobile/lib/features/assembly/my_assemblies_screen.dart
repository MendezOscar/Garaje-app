import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/api/assembly_repository.dart';
import '../../core/models/assembly.dart';

/// Lo que el técnico lleva armado este mes, y lo que suma para él.
///
/// El número de arriba es el que de verdad le importa: lo que se le debe. Es su cifra, no la
/// del taller — lo que el taller cobra ni siquiera viaja en esta respuesta.
class MyAssembliesScreen extends ConsumerWidget {
  const MyAssembliesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final datos = ref.watch(myAssembliesProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Mis motos armadas')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => context.push('/armar-moto'),
        icon: const Icon(Icons.add),
        label: const Text('Armé una moto'),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(myAssembliesProvider),
        child: datos.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(
            children: [
              Padding(
                padding: const EdgeInsets.all(24),
                child: Text(apiErrorMessage(e), textAlign: TextAlign.center),
              ),
            ],
          ),
          data: (d) => ListView(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
            children: [
              _Resumen(datos: d),
              const SizedBox(height: 16),
              if (d.items.isEmpty)
                const Padding(
                  padding: EdgeInsets.symmetric(vertical: 32),
                  child: Text(
                    'Todavía no ha registrado ninguna este mes.',
                    textAlign: TextAlign.center,
                  ),
                ),
              for (final m in d.items) _Fila(moto: m),
            ],
          ),
        ),
      ),
    );
  }
}

class _Resumen extends StatelessWidget {
  const _Resumen({required this.datos});

  final MyAssemblies datos;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Del ${_dia(datos.from)} al ${_dia(datos.to)}',
              style: theme.textTheme.bodySmall,
            ),
            const SizedBox(height: 8),
            Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('${datos.count}', style: theme.textTheme.headlineMedium),
                      Text(
                        datos.count == 1 ? 'moto armada' : 'motos armadas',
                        style: theme.textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
                // Sin monto cuando el servidor no lo manda, que es el caso del propio
                // técnico: lo que gana por moto se liquida en su corte, no en esta pantalla.
                if (datos.pay case final total?)
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.end,
                    children: [
                      Text(
                        'L ${total.toStringAsFixed(2)}',
                        style: theme.textTheme.headlineMedium?.copyWith(
                          color: theme.colorScheme.primary,
                        ),
                      ),
                      Text('le corresponde', style: theme.textTheme.bodySmall),
                    ],
                  ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  static String _dia(DateTime d) =>
      '${d.day.toString().padLeft(2, '0')}/${d.month.toString().padLeft(2, '0')}';
}

class _Fila extends StatelessWidget {
  const _Fila({required this.moto});

  final MotorcycleAssembly moto;

  @override
  Widget build(BuildContext context) => ListTile(
        contentPadding: EdgeInsets.zero,
        leading: const Icon(Icons.two_wheeler),
        title: Text(moto.serial, style: const TextStyle(fontFamily: 'monospace')),
        subtitle: Text(
          '${moto.modelName} · ${moto.warehouseName} · '
          '${moto.assembledOn.day.toString().padLeft(2, '0')}/'
          '${moto.assembledOn.month.toString().padLeft(2, '0')}',
        ),
        trailing: moto.technicianPay == null
            ? null
            : Text('L ${moto.technicianPay!.toStringAsFixed(2)}'),
      );
}

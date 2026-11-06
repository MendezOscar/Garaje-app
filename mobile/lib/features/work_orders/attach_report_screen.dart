import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/api/media_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/models/media.dart';
import '../../core/models/work_order.dart';
import '../../core/sharing/informe_compartido.dart';
import 'photo_capture.dart';

/// «¿A qué orden va este informe?».
///
/// El escáner no sabe en qué orden está trabajando el taller: él solo sabe que acaba de leer
/// un carro. Así que la orden la elige el técnico, y ese es el único punto donde alguien se
/// puede equivocar —un informe pegado a la orden equivocada se le manda a otro cliente dentro
/// de una cotización—. Por eso cada orden sale con la placa y el cliente en grande, hay
/// buscador por placa, y antes de guardar se repite a qué vehículo va.
class AttachReportScreen extends ConsumerStatefulWidget {
  const AttachReportScreen({super.key});

  @override
  ConsumerState<AttachReportScreen> createState() => _AttachReportScreenState();
}

class _AttachReportScreenState extends ConsumerState<AttachReportScreen> {
  final _busqueda = TextEditingController();
  late Future<List<WorkOrderListItem>> _ordenes;
  bool _subiendo = false;

  @override
  void initState() {
    super.initState();
    _ordenes = _buscar();
  }

  @override
  void dispose() {
    _busqueda.dispose();
    super.dispose();
  }

  /// Solo las abiertas: un informe nuevo no va a una orden entregada hace tres meses. El
  /// backend ya acota además por perfil —el técnico ve las suyas—.
  Future<List<WorkOrderListItem>> _buscar() => ref
      .read(workOrderRepositoryProvider)
      .list(onlyOpen: true, search: _busqueda.text.trim().isEmpty ? null : _busqueda.text);

  void _refiltrar() => setState(() => _ordenes = _buscar());

  Future<void> _adjuntar(WorkOrderListItem orden) async {
    final informe = ref.read(informeCompartidoProvider);
    if (informe == null) return;

    final vehiculo = [orden.plate, orden.vehicleLabel].whereType<String>().join(' · ');

    final confirmado = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('¿Adjuntar el informe?'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Orden ${orden.number}'),
            const SizedBox(height: 4),
            Text(vehiculo, style: const TextStyle(fontWeight: FontWeight.w600)),
            Text(orden.customerName),
            const SizedBox(height: 12),
            Text(
              informe.nombre ?? 'Informe.pdf',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('No')),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Adjuntar'),
          ),
        ],
      ),
    );

    if (confirmado != true || !mounted) return;

    setState(() => _subiendo = true);

    try {
      await ref.read(mediaRepositoryProvider).upload(
            file: informe.archivo,
            ownerType: MediaOwnerType.workOrder,
            ownerId: orden.id,
            takenAt: DateTime.now(),
            contentType: 'application/pdf',
          );

      ref.invalidate(mediaProviderDe(MediaOwnerType.workOrder, orden.id));
      ref.read(informeCompartidoProvider.notifier).limpiar();

      if (!mounted) return;

      // Directo a la orden: el técnico comparte el informe para que quede ahí, y lo que
      // quiere ver después es la orden con el informe puesto.
      context.pushReplacement('/ordenes/${orden.id}');
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Informe adjuntado a la orden ${orden.number}.')),
      );
    } catch (e) {
      if (!mounted) return;
      setState(() => _subiendo = false);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(apiErrorMessage(e, 'No se pudo adjuntar el informe.'))),
      );
    }
  }

  void _cancelar() {
    ref.read(informeCompartidoProvider.notifier).limpiar();
    if (context.canPop()) {
      context.pop();
    } else {
      context.go('/');
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final informe = ref.watch(informeCompartidoProvider);

    return PopScope(
      // Salirse sin elegir orden también descarta el informe: si se quedara guardado, la
      // pantalla volvería a abrirse sola la próxima vez.
      onPopInvokedWithResult: (didPop, _) {
        if (didPop) ref.read(informeCompartidoProvider.notifier).limpiar();
      },
      child: Scaffold(
        appBar: AppBar(
          title: const Text('¿A qué orden va?'),
          leading: IconButton(onPressed: _cancelar, icon: const Icon(Icons.close)),
        ),
        body: Column(
          children: [
            Card(
              margin: const EdgeInsets.fromLTRB(16, 8, 16, 0),
              child: ListTile(
                leading: Icon(Icons.picture_as_pdf_outlined, color: theme.colorScheme.primary),
                title: Text(informe?.nombre ?? 'Informe.pdf'),
                subtitle: const Text('Elija la orden del vehículo que se escaneó.'),
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
              child: TextField(
                controller: _busqueda,
                textInputAction: TextInputAction.search,
                decoration: InputDecoration(
                  prefixIcon: const Icon(Icons.search),
                  hintText: 'Buscar por placa, cliente o número',
                  suffixIcon: _busqueda.text.isEmpty
                      ? null
                      : IconButton(
                          icon: const Icon(Icons.clear),
                          onPressed: () {
                            _busqueda.clear();
                            _refiltrar();
                          },
                        ),
                ),
                onChanged: (_) => setState(() {}),
                onSubmitted: (_) => _refiltrar(),
              ),
            ),
            if (_subiendo) const LinearProgressIndicator(),
            Expanded(
              child: FutureBuilder<List<WorkOrderListItem>>(
                future: _ordenes,
                builder: (context, snapshot) {
                  if (snapshot.connectionState == ConnectionState.waiting) {
                    return const Center(child: CircularProgressIndicator());
                  }

                  if (snapshot.hasError) {
                    return Center(
                      child: Padding(
                        padding: const EdgeInsets.all(24),
                        child: Text(
                          apiErrorMessage(snapshot.error!, 'No se pudieron cargar las órdenes.'),
                          textAlign: TextAlign.center,
                        ),
                      ),
                    );
                  }

                  final ordenes = snapshot.data ?? const <WorkOrderListItem>[];

                  if (ordenes.isEmpty) {
                    return const Center(
                      child: Padding(
                        padding: EdgeInsets.all(24),
                        child: Text(
                          'No hay órdenes abiertas que coincidan. El informe se adjunta a una '
                          'orden del taller, así que primero hay que recibir el vehículo.',
                          textAlign: TextAlign.center,
                        ),
                      ),
                    );
                  }

                  return ListView.separated(
                    itemCount: ordenes.length,
                    separatorBuilder: (_, __) => const Divider(height: 1),
                    itemBuilder: (context, i) {
                      final orden = ordenes[i];

                      return ListTile(
                        enabled: !_subiendo,
                        // La placa primero y grande: es lo que el técnico tiene delante
                        // cuando acaba de escanear.
                        title: Text(
                          orden.plate ?? orden.vehicleLabel,
                          style: theme.textTheme.titleMedium,
                        ),
                        subtitle: Text(
                          '${orden.customerName}\n${orden.number} · ${orden.vehicleLabel}',
                        ),
                        isThreeLine: true,
                        trailing: const Icon(Icons.attach_file),
                        onTap: () => _adjuntar(orden),
                      );
                    },
                  );
                },
              ),
            ),
          ],
        ),
      ),
    );
  }
}

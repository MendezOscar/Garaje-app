import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/claim_repository.dart';
import '../../core/api/sale_repository.dart';
import '../../core/widgets/garaj_skeleton.dart';
import '../../core/widgets/garaj_states.dart';
import '../reports/reports_screen.dart' show money;

/// Reclamos: el cliente volvió diciendo que el trabajo quedó mal.
///
/// En el teléfono porque es donde pasa: el cliente llega al taller con el carro y lo dice de
/// pie, no por correo. Los abiertos primero, que son los que hay que atender.
class ClaimsScreen extends ConsumerStatefulWidget {
  const ClaimsScreen({super.key});

  @override
  ConsumerState<ClaimsScreen> createState() => _ClaimsScreenState();
}

class _ClaimsScreenState extends ConsumerState<ClaimsScreen> {
  bool _soloAbiertos = true;
  bool _busy = false;

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
      ref.invalidate(claimsProvider(_soloAbiertos));
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e))),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// Anotar uno nuevo: primero se busca el trabajo, después se escribe lo que dice el cliente.
  Future<void> _anotar() async {
    final venta = await showModalBottomSheet<SaleListItem>(
      context: context,
      isScrollControlled: true,
      builder: (_) => const _BuscarTrabajo(),
    );

    if (venta == null || !mounted) return;

    final motivo = await _pedirTexto(
      titulo: 'Reclamo sobre ${venta.number}',
      etiqueta: 'Qué dice el cliente que pasó',
      ayuda: 'Con sus palabras: es lo que se lee después cuando hay que decidir.',
    );

    if (motivo == null) return;

    await _run(() async {
      await ref.read(claimRepositoryProvider).create(saleId: venta.id, reason: motivo);
    });
  }

  Future<void> _cerrar(Claim claim) async {
    var estado = claim.wasUnderWarranty
        ? ClaimStatus.repairedUnderWarranty
        : ClaimStatus.repairedAndCharged;
    var abrirOrden = false;
    final resolucion = TextEditingController();

    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, redibujar) => AlertDialog(
          title: Text('Cerrar ${claim.number}'),
          content: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                DropdownButtonFormField<ClaimStatus>(
                  initialValue: estado,
                  decoration: const InputDecoration(labelText: 'Cómo termina'),
                  items: const [
                    DropdownMenuItem(
                      value: ClaimStatus.repairedUnderWarranty,
                      child: Text('Reparado en garantía'),
                    ),
                    DropdownMenuItem(
                      value: ClaimStatus.repairedAndCharged,
                      child: Text('Reparado y cobrado'),
                    ),
                    DropdownMenuItem(
                      value: ClaimStatus.rejected,
                      child: Text('No procede'),
                    ),
                  ],
                  onChanged: (value) => redibujar(() => estado = value ?? estado),
                ),
                const SizedBox(height: 8),
                TextField(
                  controller: resolucion,
                  maxLines: 3,
                  textCapitalization: TextCapitalization.sentences,
                  decoration: const InputDecoration(labelText: 'Qué se hizo'),
                ),
                SwitchListTile(
                  value: abrirOrden,
                  onChanged: (value) => redibujar(() => abrirOrden = value),
                  contentPadding: EdgeInsets.zero,
                  title: const Text('Abrir una orden para la reparación'),
                  subtitle: const Text(
                    'Lleva sus propios pasos y repuestos, y es la que dice cuánto costó.',
                  ),
                ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Cancelar'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Cerrar el reclamo'),
            ),
          ],
        ),
      ),
    );

    if (ok != true || resolucion.text.trim().isEmpty) return;

    await _run(() async {
      await ref.read(claimRepositoryProvider).resolve(
            claim.id,
            status: estado,
            resolution: resolucion.text.trim(),
            openRepairOrder: abrirOrden,
          );
    });
  }

  Future<String?> _pedirTexto({
    required String titulo,
    required String etiqueta,
    String? ayuda,
  }) async {
    final controlador = TextEditingController();

    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(titulo),
        content: TextField(
          controller: controlador,
          autofocus: true,
          maxLines: 4,
          maxLength: 2000,
          textCapitalization: TextCapitalization.sentences,
          // Sin contador: con dos renglones de ayuda, el «45/2000» se le montaba encima. El
          // tope de 2000 sigue puesto, solo que nadie escribe un reclamo de 2000 letras.
          decoration: InputDecoration(
            labelText: etiqueta,
            helperText: ayuda,
            helperMaxLines: 2,
            counterText: '',
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancelar'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Anotar'),
          ),
        ],
      ),
    );

    final texto = controlador.text.trim();
    return ok == true && texto.isNotEmpty ? texto : null;
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final reclamos = ref.watch(claimsProvider(_soloAbiertos));

    return Scaffold(
      appBar: AppBar(
        title: const Text('Reclamos'),
        actions: [
          IconButton(
            tooltip: _soloAbiertos ? 'Ver todos' : 'Ver solo los abiertos',
            icon: Icon(_soloAbiertos ? Icons.filter_alt : Icons.filter_alt_off),
            onPressed: () => setState(() => _soloAbiertos = !_soloAbiertos),
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _busy ? null : _anotar,
        icon: const Icon(Icons.add),
        label: const Text('Anotar'),
      ),
      body: reclamos.when(
        loading: () => const GarajSkeletonList(),
        error: (e, _) => GarajError(
          message: apiErrorMessage(e, 'No se pudieron cargar los reclamos.'),
          onRetry: () => ref.invalidate(claimsProvider(_soloAbiertos)),
        ),
        data: (lista) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(claimsProvider(_soloAbiertos)),
          child: lista.isEmpty
              ? ListView(
                  padding: const EdgeInsets.all(24),
                  children: [
                    Text(
                      _soloAbiertos
                          ? 'No hay reclamos abiertos.'
                          : 'Todavía no hay reclamos.',
                      style: theme.textTheme.bodyMedium,
                    ),
                    const SizedBox(height: 8),
                    Text(
                      'Se anotan cuando el cliente vuelve diciendo que el trabajo quedó mal. '
                      'Queda escrito qué pasó, qué se hizo y si entró en garantía.',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                )
              : ListView.builder(
                  padding: const EdgeInsets.fromLTRB(16, 12, 16, 96),
                  itemCount: lista.length,
                  itemBuilder: (context, i) => _ClaimCard(
                    claim: lista[i],
                    busy: _busy,
                    onCerrar: () => _cerrar(lista[i]),
                    onReabrir: () => _run(() async {
                      await ref.read(claimRepositoryProvider).reopen(lista[i].id);
                    }),
                    onAbrirOrden: () => _run(() async {
                      await ref
                          .read(claimRepositoryProvider)
                          .openRepairOrder(lista[i].id);
                    }),
                  ),
                ),
        ),
      ),
    );
  }
}

class _ClaimCard extends StatelessWidget {
  const _ClaimCard({
    required this.claim,
    required this.busy,
    required this.onCerrar,
    required this.onReabrir,
    required this.onAbrirOrden,
  });

  final Claim claim;
  final bool busy;
  final VoidCallback onCerrar;
  final VoidCallback onReabrir;
  final VoidCallback onAbrirOrden;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final abierto = claim.status == ClaimStatus.open;

    return Card(
      margin: const EdgeInsets.only(bottom: 10),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(child: Text(claim.number, style: theme.textTheme.titleSmall)),
                Text(
                  claim.status.label,
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: abierto
                        ? theme.colorScheme.error
                        : theme.colorScheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 2),
            Text(
              [
                claim.customerName ?? 'Sin cliente',
                claim.saleNumber,
                if (claim.vehicleLabel != null) claim.vehicleLabel!,
              ].join(' · '),
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 8),
            Text(claim.reason, style: theme.textTheme.bodyMedium),

            if (claim.wasUnderWarranty)
              Padding(
                padding: const EdgeInsets.only(top: 6),
                child: Text(
                  'Estaba en garantía cuando se recibió.',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: theme.colorScheme.primary,
                  ),
                ),
              ),

            if (claim.resolution case final resolucion?)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text('Se hizo: $resolucion', style: theme.textTheme.bodySmall),
              ),

            if (claim.repairWorkOrderNumber case final orden?)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  'Orden $orden · costó ${money(claim.repairCost, 'HNL')}\n'
                  '${_garantiaEnPalabras(claim.repairWarrantyCovered)}',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ),
              ),

            // Abrir la orden sin cerrar el reclamo: primero entra el carro y se repara, y
            // hasta que se sabe qué pasó se cierra el reclamo.
            if (abierto && claim.repairWorkOrderId == null)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Align(
                  alignment: Alignment.centerLeft,
                  child: OutlinedButton.icon(
                    onPressed: busy ? null : onAbrirOrden,
                    icon: const Icon(Icons.build_outlined),
                    label: const Text('Abrir la orden de reparación'),
                  ),
                ),
              ),

            Align(
              alignment: Alignment.centerRight,
              child: abierto
                  ? FilledButton.tonal(
                      onPressed: busy ? null : onCerrar,
                      child: const Text('Cerrar'),
                    )
                  : TextButton(
                      onPressed: busy ? null : onReabrir,
                      child: const Text('Reabrir'),
                    ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Qué se decidió de la garantía de la orden de reparación, en palabras.
String _garantiaEnPalabras(bool? cubierta) => switch (cubierta) {
      null => 'Falta decir si la cubre la garantía: se decide en esa orden, con el '
          'diagnóstico hecho.',
      true => 'La paga el taller: no se le factura al cliente.',
      false => 'Es un servicio nuevo: se le cobra al cliente.',
    };

/// Busca el trabajo facturado sobre el que se reclama: por número de factura, de orden, o
/// por el nombre del cliente, que es lo que el taller tiene a mano cuando el cliente llega.
class _BuscarTrabajo extends ConsumerStatefulWidget {
  const _BuscarTrabajo();

  @override
  ConsumerState<_BuscarTrabajo> createState() => _BuscarTrabajoState();
}

class _BuscarTrabajoState extends ConsumerState<_BuscarTrabajo> {
  final _texto = TextEditingController();
  List<SaleListItem> _resultados = const [];
  bool _buscando = false;

  @override
  void dispose() {
    _texto.dispose();
    super.dispose();
  }

  Future<void> _buscar() async {
    final texto = _texto.text.trim();
    if (texto.length < 2) {
      setState(() => _resultados = const []);
      return;
    }

    setState(() => _buscando = true);
    try {
      final pagina = await ref.read(saleRepositoryProvider).list(search: texto, pageSize: 10);
      if (mounted) setState(() => _resultados = pagina.items);
    } catch (_) {
      // Es una ayuda para encontrarlo: si falla, se vuelve a intentar escribiendo.
    } finally {
      if (mounted) setState(() => _buscando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.only(
          left: 16,
          right: 16,
          top: 12,
          bottom: MediaQuery.of(context).viewInsets.bottom + 16,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Sobre qué trabajo', style: theme.textTheme.titleMedium),
            const SizedBox(height: 8),
            TextField(
              controller: _texto,
              autofocus: true,
              textInputAction: TextInputAction.search,
              decoration: const InputDecoration(
                labelText: 'Factura, orden o nombre del cliente',
                prefixIcon: Icon(Icons.search),
              ),
              onChanged: (_) => _buscar(),
              onSubmitted: (_) => _buscar(),
            ),
            const SizedBox(height: 8),
            if (_buscando)
              const Padding(
                padding: EdgeInsets.all(12),
                child: Center(child: CircularProgressIndicator()),
              )
            else
              for (final venta in _resultados)
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  dense: true,
                  title: Text(venta.number),
                  subtitle: Text(
                    '${venta.customerName ?? 'Sin cliente'} · ${money(venta.total, 'HNL')}',
                  ),
                  onTap: () => Navigator.pop(context, venta),
                ),
            if (!_buscando && _resultados.isEmpty && _texto.text.trim().length >= 2)
              Text('No se encontró ese trabajo.', style: theme.textTheme.bodySmall),
          ],
        ),
      ),
    );
  }
}

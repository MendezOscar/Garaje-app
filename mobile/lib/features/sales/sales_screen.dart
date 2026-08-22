import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:path_provider/path_provider.dart';
import 'package:share_plus/share_plus.dart';

import '../../core/api/api_client.dart';
import '../../core/api/sale_repository.dart';
import '../../core/api/service_request_repository.dart';
import '../../core/theme/garaj_brand.dart';
import '../reports/reports_screen.dart' show money;

/// El registro de ventas en el teléfono.
///
/// Una venta solo se veía desde adentro de su orden; Caja es lo cobrado de un día y Por cobrar
/// solo lo que tiene saldo. Una venta de mostrador, que no tiene orden, no se veía en ninguna
/// parte después de hacerla: ni para volver a mandar el comprobante, ni para anularla si salió
/// mal. Y eso pasa en el mostrador, con el cliente enfrente, que es donde está el teléfono y
/// no la computadora.
///
/// Cada renglón dice de dónde salió: de una orden, con enlace, o del mostrador.
class SalesScreen extends ConsumerStatefulWidget {
  const SalesScreen({super.key});

  @override
  ConsumerState<SalesScreen> createState() => _SalesScreenState();
}

class _SalesScreenState extends ConsumerState<SalesScreen> {
  /// El rango, en días hacia atrás. Arranca en el mes: es el corte con el que se piensa.
  int _dias = 30;
  static const _rangos = {1: 'Hoy', 7: '7 días', 30: '30 días'};

  bool _conAnuladas = false;
  String? _branchId;
  String _busqueda = '';

  final _search = TextEditingController();
  Timer? _debounce;
  bool _busy = false;

  @override
  void dispose() {
    _debounce?.cancel();
    _search.dispose();
    super.dispose();
  }

  /// El día del taller, no el UTC: una venta de las seis de la tarde es de hoy.
  SalesFilter get _filtro {
    final ahora = DateTime.now();
    final hoy = DateTime(ahora.year, ahora.month, ahora.day);
    return SalesFilter(
      from: hoy.subtract(Duration(days: _dias - 1)),
      to: hoy.add(const Duration(days: 1)).subtract(const Duration(seconds: 1)),
      search: _busqueda.trim().isEmpty ? null : _busqueda.trim(),
      branchId: _branchId,
      includeVoided: _conAnuladas,
    );
  }

  /// Se espera a que deje de escribir: cada pulsación es una consulta, y la lista parpadea.
  void _onSearch(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 400), () {
      if (mounted) setState(() => _busqueda = value);
    });
  }

  /// El comprobante a la hoja de compartir: de ahí sale al WhatsApp del cliente, que es lo
  /// que pide cuando llama diciendo que perdió el papel.
  Future<void> _compartir(SaleListItem venta) async {
    setState(() => _busy = true);
    try {
      final bytes = await ref.read(saleRepositoryProvider).invoicePdf(venta.id);
      final file = File('${(await getTemporaryDirectory()).path}/${venta.number}.pdf');
      await file.writeAsBytes(bytes);

      if (!mounted) return;
      await SharePlus.instance.share(
        ShareParams(
          files: [XFile(file.path, mimeType: 'application/pdf')],
          text: 'Venta ${venta.number}',
          sharePositionOrigin: _origen(context),
        ),
      );
    } catch (e) {
      _aviso(apiErrorMessage(e, 'No se pudo bajar el comprobante.'));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// Anular pide motivo y no borra: la venta conserva su número —el correlativo fiscal no
  /// vuelve al rango— y los repuestos regresan a la bodega.
  Future<void> _anular(SaleListItem venta) async {
    final motivo = await showDialog<String>(
      context: context,
      builder: (_) => _MotivoDialog(numero: venta.number),
    );

    if (motivo == null || motivo.trim().isEmpty) return;

    setState(() => _busy = true);
    try {
      await ref.read(saleRepositoryProvider).annul(venta.id, motivo.trim());
      ref.invalidate(salesRegistryProvider(_filtro));
      _aviso('Venta ${venta.number} anulada.');
    } catch (e) {
      _aviso(apiErrorMessage(e, 'No se pudo anular la venta.'));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _aviso(String texto) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(texto)));
  }

  static Rect _origen(BuildContext context) {
    final box = context.findRenderObject() as RenderBox?;
    if (box == null) return Rect.zero;
    return box.localToGlobal(Offset.zero) & box.size;
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final filtro = _filtro;
    final pagina = ref.watch(salesRegistryProvider(filtro));
    final branches = ref.watch(branchOptionsProvider).value ?? const [];

    return Scaffold(
      appBar: AppBar(
        title: const Text('Ventas'),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(112),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
            child: Column(
              children: [
                TextField(
                  controller: _search,
                  onChanged: _onSearch,
                  textInputAction: TextInputAction.search,
                  decoration: InputDecoration(
                    hintText: 'Cliente o número de venta',
                    prefixIcon: const Icon(Icons.search),
                    isDense: true,
                    border: const OutlineInputBorder(),
                    suffixIcon: _search.text.isEmpty
                        ? null
                        : IconButton(
                            icon: const Icon(Icons.close),
                            onPressed: () {
                              _search.clear();
                              _onSearch('');
                            },
                          ),
                  ),
                ),
                const SizedBox(height: 8),
                SizedBox(
                  height: 40,
                  child: ListView(
                    scrollDirection: Axis.horizontal,
                    children: [
                      for (final rango in _rangos.entries)
                        _Chip(
                          label: rango.value,
                          selected: _dias == rango.key,
                          onSelected: () => setState(() => _dias = rango.key),
                        ),
                      const VerticalDivider(width: 16),
                      _Chip(
                        label: 'Con anuladas',
                        selected: _conAnuladas,
                        onSelected: () => setState(() => _conAnuladas = !_conAnuladas),
                      ),
                      if (branches.length > 1) ...[
                        const VerticalDivider(width: 16),
                        _Chip(
                          label: 'Todas las sucursales',
                          selected: _branchId == null,
                          onSelected: () => setState(() => _branchId = null),
                        ),
                        for (final branch in branches)
                          _Chip(
                            label: branch.name,
                            selected: _branchId == branch.id,
                            onSelected: () => setState(() => _branchId = branch.id),
                          ),
                      ],
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _busy ? null : () => context.push('/mostrador'),
        icon: const Icon(Icons.add_shopping_cart_outlined),
        label: const Text('Vender repuesto'),
      ),
      body: pagina.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Text(
              apiErrorMessage(e, 'No se pudieron cargar las ventas.'),
              textAlign: TextAlign.center,
            ),
          ),
        ),
        data: (datos) {
          if (datos.items.isEmpty) {
            return Center(
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Text(
                  _busqueda.trim().isNotEmpty
                      ? 'Nada con esa búsqueda.'
                      : 'No hay ventas en ese rango.',
                  textAlign: TextAlign.center,
                  style: theme.textTheme.bodyMedium,
                ),
              ),
            );
          }

          final vivas = datos.items.where((v) => !v.isVoided).toList();
          final suma = vivas.fold<double>(0, (total, v) => total + v.total);
          final mostrador = vivas.where((v) => v.deMostrador).length;

          return RefreshIndicator(
            onRefresh: () async => ref.invalidate(salesRegistryProvider(filtro)),
            child: ListView.builder(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
              itemCount: datos.items.length + 1,
              itemBuilder: (context, i) {
                if (i == 0) {
                  return Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Expanded(
                              child: Text(
                                '${vivas.length} '
                                '${vivas.length == 1 ? 'venta' : 'ventas'}'
                                '${mostrador > 0 ? ', $mostrador de mostrador' : ''}',
                                style: theme.textTheme.bodySmall,
                              ),
                            ),
                            Text(
                              money(suma, 'HNL'),
                              style: theme.textTheme.titleMedium
                                  ?.copyWith(fontFamily: GarajFonts.mono),
                            ),
                          ],
                        ),
                        if (datos.total > datos.items.length)
                          Text(
                            'Se muestran las ${datos.items.length} más recientes '
                            'de ${datos.total}.',
                            style: theme.textTheme.bodySmall,
                          ),
                      ],
                    ),
                  );
                }

                return _SaleCard(
                  venta: datos.items[i - 1],
                  busy: _busy,
                  onCompartir: _compartir,
                  onAnular: _anular,
                );
              },
            ),
          );
        },
      ),
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip({required this.label, required this.selected, required this.onSelected});

  final String label;
  final bool selected;
  final VoidCallback onSelected;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(right: 8),
        child: ChoiceChip(
          label: Text(label),
          selected: selected,
          onSelected: (_) => onSelected(),
        ),
      );
}

class _SaleCard extends StatelessWidget {
  const _SaleCard({
    required this.venta,
    required this.busy,
    required this.onCompartir,
    required this.onAnular,
  });

  final SaleListItem venta;
  final bool busy;
  final Future<void> Function(SaleListItem venta) onCompartir;
  final Future<void> Function(SaleListItem venta) onAnular;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final origen = venta.deMostrador ? 'Mostrador' : venta.workOrderNumber ?? 'Orden';
    final detalle = [
      venta.number,
      origen,
      _fecha(venta.saleDate),
    ].join(' · ');

    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      child: ListTile(
        contentPadding: const EdgeInsets.fromLTRB(14, 6, 6, 6),
        // Tocar la tarjeta abre la orden que la originó; una de mostrador no tiene a dónde ir.
        onTap: venta.workOrderId == null
            ? null
            : () => context.push('/ordenes/${venta.workOrderId}'),
        title: Text(
          venta.customerName ?? 'Cliente de paso',
          style: venta.isVoided
              ? TextStyle(
                  decoration: TextDecoration.lineThrough,
                  color: theme.colorScheme.onSurfaceVariant,
                )
              : null,
        ),
        subtitle: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(detalle, style: theme.textTheme.bodySmall),
            if (venta.isVoided)
              Text(
                'Anulada',
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.error),
              )
            else if (venta.balance > 0)
              Text(
                'debe ${money(venta.balance, 'HNL')}'
                '${venta.isOverdue ? ' · vencido' : ''}',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: venta.isOverdue ? theme.colorScheme.error : null,
                ),
              ),
          ],
        ),
        trailing: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              money(venta.total, 'HNL'),
              style: theme.textTheme.titleSmall?.copyWith(fontFamily: GarajFonts.mono),
            ),
            PopupMenuButton<String>(
              enabled: !busy,
              onSelected: (opcion) {
                if (opcion == 'pdf') onCompartir(venta);
                if (opcion == 'anular') onAnular(venta);
              },
              itemBuilder: (_) => [
                const PopupMenuItem(value: 'pdf', child: Text('Mandar el comprobante')),
                if (!venta.isVoided)
                  const PopupMenuItem(value: 'anular', child: Text('Anular')),
              ],
            ),
          ],
        ),
      ),
    );
  }

  static String _fecha(DateTime value) =>
      '${value.day.toString().padLeft(2, '0')}/'
      '${value.month.toString().padLeft(2, '0')}/'
      '${value.year.toString().substring(2)}';
}

/// El motivo de la anulación. Se pide escrito porque queda en la venta para siempre: es lo
/// que va a leer quien pregunte el mes que viene por qué esa factura no cuadra.
class _MotivoDialog extends StatefulWidget {
  const _MotivoDialog({required this.numero});

  final String numero;

  @override
  State<_MotivoDialog> createState() => _MotivoDialogState();
}

class _MotivoDialogState extends State<_MotivoDialog> {
  final _motivo = TextEditingController();

  @override
  void dispose() {
    _motivo.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: Text('Anular ${widget.numero}'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'La venta conserva su número y los repuestos vuelven a la bodega. '
              'El motivo queda guardado.',
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _motivo,
              autofocus: true,
              maxLines: 2,
              decoration: const InputDecoration(
                labelText: 'Motivo',
                border: OutlineInputBorder(),
              ),
              onChanged: (_) => setState(() {}),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: const Text('Dejarla'),
          ),
          FilledButton(
            onPressed: _motivo.text.trim().isEmpty
                ? null
                : () => Navigator.of(context).pop(_motivo.text),
            child: const Text('Anular'),
          ),
        ],
      );
}

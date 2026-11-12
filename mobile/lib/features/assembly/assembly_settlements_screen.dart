import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:path_provider/path_provider.dart';
import 'package:share_plus/share_plus.dart';

import '../../core/api/api_client.dart';
import '../../core/api/assembly_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/assembly.dart';
import '../../core/theme/garaj_brand.dart';
import '../reports/reports_screen.dart' show money;

/// Los cortes del armado: las facturas a los contratistas y los pagos a los técnicos.
///
/// Cerrar un corte es facturar; cobrarlo es otra cosa y pasa semanas después. Por eso una
/// factura vive aquí dos veces: cerrada y por cobrar, y luego cobrada con su referencia.
class AssemblySettlementsScreen extends ConsumerStatefulWidget {
  const AssemblySettlementsScreen({super.key});

  @override
  ConsumerState<AssemblySettlementsScreen> createState() =>
      _AssemblySettlementsScreenState();
}

class _AssemblySettlementsScreenState
    extends ConsumerState<AssemblySettlementsScreen> {
  /// Qué se está mirando: todo, lo que falta por cobrar, o los pagos a los técnicos.
  String _vista = 'todo';

  Future<void> _cerrarFactura() async {
    final cerrada = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const _CerrarFacturaSheet(),
    );

    if (cerrada == true) _refrescar();
  }

  void _refrescar() {
    ref.invalidate(assemblySettlementsProvider);
    ref.invalidate(assemblyPickListsProvider);
  }

  Future<void> _cobrar(AssemblySettlement corte) async {
    final cobrada = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _CobrarSheet(corte: corte),
    );

    if (cobrada == true) _refrescar();
  }

  Future<void> _quitarCobro(AssemblySettlement corte) async {
    final confirmado = await _preguntar(
      titulo: '¿Quitarle el cobro?',
      texto: 'La factura de ${corte.quien} vuelve a quedar por cobrar.',
      accion: 'Quitar',
    );

    if (confirmado != true) return;

    await _correr(
      () => ref.read(assemblyRepositoryProvider).uncollect(corte.id),
      error: 'No se pudo quitar el cobro.',
      listo: 'La factura volvió a quedar por cobrar.',
    );
  }

  Future<void> _reabrir(AssemblySettlement corte) async {
    final confirmado = await _preguntar(
      titulo: '¿Reabrir el corte?',
      texto: corte.kind.esFactura
          ? 'Las ${corte.count} motos vuelven a quedar sin facturar y la factura desaparece. '
              'Es la única manera honesta de corregir un corte.'
          : 'Las ${corte.count} motos vuelven a quedar sin pagar y el gasto del periodo se '
              'borra.',
      accion: 'Reabrir',
    );

    if (confirmado != true) return;

    await _correr(
      () => ref.read(assemblyRepositoryProvider).reopen(corte.id),
      error: 'No se pudo reabrir el corte.',
      listo: 'Corte reabierto.',
    );
  }

  /// La hoja del corte, tal como la congeló el cierre, para mandársela al contratista por
  /// WhatsApp. Sale del corte y no de un filtro: un filtro de hoy puede dar otra cosa.
  Future<void> _mandarHoja(AssemblySettlement corte) async {
    try {
      final bytes = await ref.read(assemblyRepositoryProvider).settlementPdf(corte.id);
      final nombre = corte.kind.esFactura
          ? 'Motos armadas ${corte.quien} ${_dia(corte.periodTo)}.pdf'
          : 'Pago ${corte.quien} ${_dia(corte.periodTo)}.pdf';

      final file = File('${(await getTemporaryDirectory()).path}/$nombre');
      await file.writeAsBytes(bytes);

      if (!mounted) return;
      await SharePlus.instance.share(
        ShareParams(
          files: [XFile(file.path, mimeType: 'application/pdf')],
          text: nombre,
          // En iPad la hoja sale anclada a un punto; sin esto revienta.
          sharePositionOrigin: _origen(context),
        ),
      );
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(apiErrorMessage(e, 'No se pudo generar la hoja.'))),
      );
    }
  }

  static Rect _origen(BuildContext context) {
    final box = context.findRenderObject() as RenderBox?;
    if (box == null) return Rect.zero;
    return box.localToGlobal(Offset.zero) & box.size;
  }

  Future<bool?> _preguntar({
    required String titulo,
    required String texto,
    required String accion,
  }) =>
      showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: Text(titulo),
          content: Text(texto),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Cancelar'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: Text(accion),
            ),
          ],
        ),
      );

  Future<void> _correr(
    Future<void> Function() accion, {
    required String error,
    required String listo,
  }) async {
    try {
      await accion();
      _refrescar();
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(listo)));
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(apiErrorMessage(e, error))),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cortes = ref.watch(assemblySettlementsProvider);

    // Cerrar un corte y reabrirlo son del Dueño: es facturar y es deshacer una factura. El
    // Encargado mira, manda la hoja y anota el cobro. Lo impone el servidor.
    final auth = ref.watch(authControllerProvider);
    final esDueno = auth is AuthSignedIn && auth.user.esDueno;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Cortes y facturas'),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(48),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
            child: SizedBox(
              height: 40,
              child: ListView(
                scrollDirection: Axis.horizontal,
                children: [
                  for (final v in const {
                    'todo': 'Todo',
                    'cobrar': 'Por cobrar',
                    'pagos': 'Pagos a técnicos',
                  }.entries)
                    Padding(
                      padding: const EdgeInsets.only(right: 8),
                      child: ChoiceChip(
                        label: Text(v.value),
                        selected: _vista == v.key,
                        onSelected: (_) => setState(() => _vista = v.key),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
      ),
      floatingActionButton: esDueno
          ? FloatingActionButton.extended(
              onPressed: _cerrarFactura,
              icon: const Icon(Icons.receipt_long_outlined),
              label: const Text('Cerrar factura'),
            )
          : null,
      body: cortes.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Text(
              apiErrorMessage(e, 'No se pudieron cargar los cortes.'),
              textAlign: TextAlign.center,
            ),
          ),
        ),
        data: (todos) {
          final items = switch (_vista) {
            'cobrar' => todos.where((c) => c.porCobrar).toList(),
            'pagos' => todos.where((c) => !c.kind.esFactura).toList(),
            _ => todos,
          };

          final porCobrar = todos
              .where((c) => c.porCobrar)
              .fold<double>(0, (suma, c) => suma + c.total);

          return RefreshIndicator(
            onRefresh: () async => _refrescar(),
            child: ListView.builder(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
              itemCount: items.length + 1,
              itemBuilder: (context, i) {
                if (i == 0) {
                  if (porCobrar == 0) return const SizedBox(height: 4);

                  return Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: Row(
                      children: [
                        Expanded(
                          child: Text(
                            'Por cobrar a los contratistas',
                            style: theme.textTheme.bodySmall,
                          ),
                        ),
                        Text(
                          money(porCobrar, 'HNL'),
                          style: theme.textTheme.titleMedium
                              ?.copyWith(fontFamily: GarajFonts.mono),
                        ),
                      ],
                    ),
                  );
                }

                return _CorteCard(
                  corte: items[i - 1],
                  puedeReabrir: esDueno,
                  onCobrar: _cobrar,
                  onQuitarCobro: _quitarCobro,
                  onReabrir: _reabrir,
                  onMandarHoja: _mandarHoja,
                );
              },
            ),
          );
        },
      ),
    );
  }
}

class _CorteCard extends StatelessWidget {
  const _CorteCard({
    required this.corte,
    required this.puedeReabrir,
    required this.onCobrar,
    required this.onQuitarCobro,
    required this.onReabrir,
    required this.onMandarHoja,
  });

  final AssemblySettlement corte;
  final bool puedeReabrir;
  final Future<void> Function(AssemblySettlement corte) onCobrar;
  final Future<void> Function(AssemblySettlement corte) onQuitarCobro;
  final Future<void> Function(AssemblySettlement corte) onReabrir;
  final Future<void> Function(AssemblySettlement corte) onMandarHoja;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final esFactura = corte.kind.esFactura;

    final estado = !esFactura
        ? 'pagado'
        : corte.collectedOn == null
            ? 'por cobrar'
            : 'cobrada ${_dia(corte.collectedOn!)}';

    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(14, 10, 6, 10),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(corte.quien, style: theme.textTheme.titleSmall),
                      Text(
                        '${esFactura ? 'Factura' : 'Pago'} · '
                        '${_dia(corte.periodFrom)} a ${_dia(corte.periodTo)}',
                        style: theme.textTheme.bodySmall,
                      ),
                      Text(
                        '${corte.count} ${corte.count == 1 ? 'moto' : 'motos'} · $estado'
                        '${corte.reference == null ? '' : ' · ${corte.reference}'}',
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: corte.porCobrar ? theme.colorScheme.error : null,
                        ),
                      ),
                    ],
                  ),
                ),
                Column(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Text(
                      money(corte.total, 'HNL'),
                      style: theme.textTheme.titleSmall
                          ?.copyWith(fontFamily: GarajFonts.mono),
                    ),
                    PopupMenuButton<String>(
                      tooltip: 'Más',
                      onSelected: (v) => switch (v) {
                        'cobrar' => onCobrar(corte),
                        'quitar' => onQuitarCobro(corte),
                        'hoja' => onMandarHoja(corte),
                        _ => onReabrir(corte),
                      },
                      itemBuilder: (context) => [
                        if (esFactura && corte.collectedOn == null)
                          const PopupMenuItem(
                            value: 'cobrar',
                            child: Text('Anotar que pagó'),
                          ),
                        if (esFactura && corte.collectedOn != null)
                          const PopupMenuItem(
                            value: 'quitar',
                            child: Text('Quitar el cobro'),
                          ),
                        const PopupMenuItem(value: 'hoja', child: Text('Mandar la hoja')),
                        if (puedeReabrir)
                          const PopupMenuItem(value: 'reabrir', child: Text('Reabrir')),
                      ],
                    ),
                  ],
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// Cerrar la factura de un contratista: congela qué motos entraron y en qué periodo.
class _CerrarFacturaSheet extends ConsumerStatefulWidget {
  const _CerrarFacturaSheet();

  @override
  ConsumerState<_CerrarFacturaSheet> createState() => _CerrarFacturaSheetState();
}

class _CerrarFacturaSheetState extends ConsumerState<_CerrarFacturaSheet> {
  final _referencia = TextEditingController();

  String? _contractorId;
  late DateTime _desde = DateTime(DateTime.now().year, DateTime.now().month);
  DateTime _hasta = DateTime.now();

  bool _cerrando = false;
  String? _error;

  @override
  void dispose() {
    _referencia.dispose();
    super.dispose();
  }

  Future<void> _elegir({required bool desde}) async {
    final elegida = await showDatePicker(
      context: context,
      initialDate: desde ? _desde : _hasta,
      firstDate: DateTime(DateTime.now().year - 2),
      lastDate: DateTime.now(),
    );

    if (elegida == null) return;
    setState(() {
      if (desde) {
        _desde = elegida;
      } else {
        _hasta = elegida;
      }
    });
  }

  Future<void> _cerrar() async {
    if (_contractorId == null) {
      setState(() => _error = 'Elija el contratista.');
      return;
    }

    setState(() {
      _cerrando = true;
      _error = null;
    });

    try {
      await ref.read(assemblyRepositoryProvider).close(
            kind: AssemblySettlementKind.billing,
            from: _desde,
            to: _hasta,
            contractorId: _contractorId,
            reference: _referencia.text,
          );

      if (!mounted) return;
      Navigator.pop(context, true);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = apiErrorMessage(e, 'No se pudo cerrar la factura.');
        _cerrando = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final listas = ref.watch(assemblyPickListsProvider).value;

    return Padding(
      padding: EdgeInsets.fromLTRB(
        16,
        0,
        16,
        MediaQuery.of(context).viewInsets.bottom + 24,
      ),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Cerrar la factura', style: theme.textTheme.titleMedium),
            Padding(
              padding: const EdgeInsets.only(top: 4, bottom: 12),
              child: Text(
                'Entran todas las motos de ese contratista en el periodo que no estén ya '
                'facturadas. Al cerrarla quedan congeladas.',
                style: theme.textTheme.bodySmall,
              ),
            ),
            if (listas == null)
              const Center(child: CircularProgressIndicator())
            else
              DropdownButtonFormField<String>(
                initialValue: _contractorId,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Contratista', isDense: true),
                items: [
                  for (final c in listas.contractors)
                    DropdownMenuItem(value: c.id, child: Text(c.name)),
                ],
                onChanged: (v) => setState(() => _contractorId = v),
              ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: InkWell(
                    onTap: () => _elegir(desde: true),
                    child: InputDecorator(
                      decoration: const InputDecoration(labelText: 'Desde', isDense: true),
                      child: Text(_dia(_desde)),
                    ),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: InkWell(
                    onTap: () => _elegir(desde: false),
                    child: InputDecorator(
                      decoration: const InputDecoration(labelText: 'Hasta', isDense: true),
                      child: Text(_dia(_hasta)),
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _referencia,
              decoration: const InputDecoration(
                labelText: 'Número de factura',
                isDense: true,
              ),
            ),
            if (_error != null)
              Padding(
                padding: const EdgeInsets.only(top: 12),
                child: Text(
                  _error!,
                  style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error),
                ),
              ),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: _cerrando ? null : _cerrar,
              child: Text(_cerrando ? 'Cerrando…' : 'Cerrar la factura'),
            ),
          ],
        ),
      ),
    );
  }
}

/// Anotar que el contratista pagó: el día y el recibo.
class _CobrarSheet extends ConsumerStatefulWidget {
  const _CobrarSheet({required this.corte});

  final AssemblySettlement corte;

  @override
  ConsumerState<_CobrarSheet> createState() => _CobrarSheetState();
}

class _CobrarSheetState extends ConsumerState<_CobrarSheet> {
  final _referencia = TextEditingController();
  DateTime _cuando = DateTime.now();

  bool _guardando = false;
  String? _error;

  @override
  void dispose() {
    _referencia.dispose();
    super.dispose();
  }

  Future<void> _guardar() async {
    setState(() {
      _guardando = true;
      _error = null;
    });

    try {
      await ref.read(assemblyRepositoryProvider).collect(
            widget.corte.id,
            on: _cuando,
            reference: _referencia.text,
          );

      if (!mounted) return;
      Navigator.pop(context, true);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = apiErrorMessage(e, 'No se pudo anotar el cobro.');
        _guardando = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: EdgeInsets.fromLTRB(
        16,
        0,
        16,
        MediaQuery.of(context).viewInsets.bottom + 24,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Anotar que pagó', style: theme.textTheme.titleMedium),
          Padding(
            padding: const EdgeInsets.only(top: 4, bottom: 12),
            child: Text(
              '${widget.corte.quien} · ${money(widget.corte.total, 'HNL')}',
              style: theme.textTheme.bodySmall,
            ),
          ),
          InkWell(
            onTap: () async {
              final elegida = await showDatePicker(
                context: context,
                initialDate: _cuando,
                firstDate: DateTime(DateTime.now().year - 2),
                lastDate: DateTime.now(),
              );

              if (elegida != null) setState(() => _cuando = elegida);
            },
            child: InputDecorator(
              decoration: const InputDecoration(labelText: 'Día del pago', isDense: true),
              child: Text(_dia(_cuando)),
            ),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _referencia,
            decoration: const InputDecoration(
              labelText: 'Recibo, transferencia o cheque',
              isDense: true,
            ),
          ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.only(top: 12),
              child: Text(
                _error!,
                style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error),
              ),
            ),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: _guardando ? null : _guardar,
            child: Text(_guardando ? 'Guardando…' : 'Anotar el cobro'),
          ),
        ],
      ),
    );
  }
}

String _dia(DateTime value) =>
    '${value.day.toString().padLeft(2, '0')}/${value.month.toString().padLeft(2, '0')}/'
    '${value.year}';

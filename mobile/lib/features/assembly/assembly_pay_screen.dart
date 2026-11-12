import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/assembly_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/assembly.dart';
import '../../core/theme/garaj_brand.dart';
import '../reports/reports_screen.dart' show money;

/// Lo que se le debe a cada técnico por las motos que armó, y el cierre del pago.
///
/// Solo cuenta lo que todavía no entró en un corte de pago: lo ya pagado no vuelve a salir,
/// que es lo que impide pagar dos veces la misma quincena.
class AssemblyPayScreen extends ConsumerStatefulWidget {
  const AssemblyPayScreen({super.key});

  @override
  ConsumerState<AssemblyPayScreen> createState() => _AssemblyPayScreenState();
}

class _AssemblyPayScreenState extends ConsumerState<AssemblyPayScreen> {
  late DateTime _desde = _inicioDeMes(DateTime.now());
  DateTime _hasta = DateTime.now();

  static DateTime _inicioDeMes(DateTime d) => DateTime(d.year, d.month);

  AssemblyFilter get _filtro => AssemblyFilter(from: _desde, to: _hasta, onlyUnpaid: true);

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

  Future<void> _cerrarPago(AssemblyPayLine linea) async {
    final cerrado = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _CerrarPagoSheet(linea: linea, desde: _desde, hasta: _hasta),
    );

    if (cerrado != true) return;

    ref.invalidate(assemblyPayReportProvider(_filtro));
    ref.invalidate(assemblySettlementsProvider);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final reporte = ref.watch(assemblyPayReportProvider(_filtro));

    // Cerrar el pago es entregar la plata, y eso es del Dueño. Al Encargado le queda la
    // cuenta, que es lo que necesita para saber cómo va la quincena.
    final auth = ref.watch(authControllerProvider);
    final esDueno = auth is AuthSignedIn && auth.user.esDueno;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Pago a técnicos'),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(64),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 10),
            child: Row(
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
          ),
        ),
      ),
      body: reporte.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Text(
              apiErrorMessage(e, 'No se pudo cargar el pago.'),
              textAlign: TextAlign.center,
            ),
          ),
        ),
        data: (r) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(assemblyPayReportProvider(_filtro)),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      '${r.count} ${r.count == 1 ? 'moto' : 'motos'} sin pagar',
                      style: theme.textTheme.bodySmall,
                    ),
                  ),
                  Text(
                    money(r.total, 'HNL'),
                    style: theme.textTheme.titleMedium?.copyWith(fontFamily: GarajFonts.mono),
                  ),
                ],
              ),
              const SizedBox(height: 12),
              if (r.lines.isEmpty)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 32),
                  child: Text(
                    'No hay nada pendiente de pagar en ese periodo.',
                    textAlign: TextAlign.center,
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
              for (final l in r.lines)
                Card(
                  margin: const EdgeInsets.only(bottom: 8),
                  child: ListTile(
                    contentPadding: const EdgeInsets.fromLTRB(14, 6, 10, 6),
                    title: Text(l.technicianName),
                    subtitle: Text('${l.count} ${l.count == 1 ? 'moto' : 'motos'}'),
                    trailing: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      crossAxisAlignment: CrossAxisAlignment.end,
                      children: [
                        Text(
                          money(l.pay, 'HNL'),
                          style: theme.textTheme.titleSmall?.copyWith(fontFamily: GarajFonts.mono),
                        ),
                        if (esDueno)
                          TextButton(
                            onPressed: () => _cerrarPago(l),
                            style: TextButton.styleFrom(
                              padding: EdgeInsets.zero,
                              minimumSize: const Size(0, 28),
                              tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                            ),
                            child: const Text('Cerrar pago'),
                          ),
                      ],
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Cerrar el pago de un técnico: congela sus motos y deja el gasto en el periodo.
///
/// El monto se puede cambiar: hay acuerdos que no salen ni de la tarifa del modelo ni del
/// fijo por moto —un bono, un descuento acordado, la quincena redondeada—. Lo que se escriba
/// aquí es lo que entra en los gastos del taller.
class _CerrarPagoSheet extends ConsumerStatefulWidget {
  const _CerrarPagoSheet({required this.linea, required this.desde, required this.hasta});

  final AssemblyPayLine linea;
  final DateTime desde;
  final DateTime hasta;

  @override
  ConsumerState<_CerrarPagoSheet> createState() => _CerrarPagoSheetState();
}

class _CerrarPagoSheetState extends ConsumerState<_CerrarPagoSheet> {
  late final _monto = TextEditingController(text: widget.linea.pay.toStringAsFixed(2));
  final _referencia = TextEditingController();

  bool _cerrando = false;
  String? _error;

  @override
  void dispose() {
    _monto.dispose();
    _referencia.dispose();
    super.dispose();
  }

  Future<void> _cerrar() async {
    final monto = double.tryParse(_monto.text.trim());
    if (monto == null || monto < 0) {
      setState(() => _error = 'Escriba cuánto se le paga.');
      return;
    }

    setState(() {
      _cerrando = true;
      _error = null;
    });

    try {
      await ref
          .read(assemblyRepositoryProvider)
          .close(
            kind: AssemblySettlementKind.technicianPay,
            from: widget.desde,
            to: widget.hasta,
            technicianUserId: widget.linea.technicianUserId,
            reference: _referencia.text,
            // Siempre con monto: si no se tocó es el mismo que suman sus motos, y mandarlo
            // explícito evita que un redondeo de la pantalla cambie lo que se cierra.
            total: monto,
          );

      if (!mounted) return;
      Navigator.pop(context, true);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = apiErrorMessage(e, 'No se pudo cerrar el pago.');
        _cerrando = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: EdgeInsets.fromLTRB(16, 0, 16, MediaQuery.of(context).viewInsets.bottom + 24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Cerrar el pago', style: theme.textTheme.titleMedium),
          Padding(
            padding: const EdgeInsets.only(top: 4, bottom: 12),
            child: Text(
              '${widget.linea.technicianName} · ${widget.linea.count} '
              '${widget.linea.count == 1 ? 'moto' : 'motos'} · '
              '${_dia(widget.desde)} a ${_dia(widget.hasta)}',
              style: theme.textTheme.bodySmall,
            ),
          ),
          TextField(
            controller: _monto,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: const InputDecoration(labelText: 'Lo que se le paga', isDense: true),
          ),
          Padding(
            padding: const EdgeInsets.only(top: 6),
            child: Text(
              'Sale de sus motos. Cámbielo si lo acordado es otra cosa: lo que quede aquí es '
              'lo que entra en los gastos del periodo.',
              style: theme.textTheme.bodySmall,
            ),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _referencia,
            decoration: const InputDecoration(labelText: 'Nota de pago o recibo', isDense: true),
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
            child: Text(_cerrando ? 'Cerrando…' : 'Cerrar el pago'),
          ),
        ],
      ),
    );
  }
}

String _dia(DateTime value) =>
    '${value.day.toString().padLeft(2, '0')}/${value.month.toString().padLeft(2, '0')}/'
    '${value.year}';

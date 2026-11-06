import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/work_steps_repository.dart';
import '../../core/models/work_steps_proposal.dart';

/// «Proponer los pasos»: del diagnóstico sale una lista que se edita y se acepta.
///
/// Todo lo que se ve aquí es un borrador. La orden no cambia hasta que se toca «Aceptar», y
/// por eso el botón dice eso y no «Guardar»: lo que se guarda es lo que la persona corrigió,
/// no lo que el asistente propuso.
class ProposeStepsScreen extends ConsumerStatefulWidget {
  const ProposeStepsScreen({
    required this.workOrderId,
    required this.workOrderNumber,
    super.key,
  });

  final String workOrderId;
  final String workOrderNumber;

  @override
  ConsumerState<ProposeStepsScreen> createState() => _ProposeStepsScreenState();
}

class _ProposeStepsScreenState extends ConsumerState<ProposeStepsScreen> {
  WorkStepsProposal? _propuesta;
  bool _pensando = true;
  bool _guardando = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _proponer();
  }

  Future<void> _proponer() async {
    setState(() {
      _pensando = true;
      _error = null;
    });

    try {
      final r = await ref.read(workStepsRepositoryProvider).propose(widget.workOrderId);
      if (mounted) setState(() => _propuesta = r);
    } catch (e) {
      if (mounted) setState(() => _error = apiErrorMessage(e));
    } finally {
      if (mounted) setState(() => _pensando = false);
    }
  }

  List<ProposedTask> get _incluidos =>
      (_propuesta?.tasks ?? const <ProposedTask>[]).where((t) => t.incluido).toList();

  Future<void> _aceptar() async {
    final propuesta = _propuesta;
    if (propuesta?.id == null || _incluidos.isEmpty) return;

    setState(() {
      _guardando = true;
      _error = null;
    });

    try {
      await ref
          .read(workStepsRepositoryProvider)
          .accept(widget.workOrderId, propuesta!.id!, _incluidos);

      if (mounted) Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted) {
        setState(() {
          _error = apiErrorMessage(e);
          _guardando = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final propuesta = _propuesta;

    return Scaffold(
      appBar: AppBar(title: Text('Pasos · ${widget.workOrderNumber}')),
      body: _pensando
          ? const Center(child: CircularProgressIndicator())
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 100),
              children: [
                if (_error != null)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: Text(
                      _error!,
                      style: TextStyle(color: theme.colorScheme.error),
                    ),
                  ),

                // Que no se pueda no es un error: es que se acabaron las consultas del día o
                // que falta el diagnóstico. Se dice con esas palabras y ya.
                if (propuesta != null && !propuesta.available)
                  Text(propuesta.message ?? 'El asistente no pudo responder.'),

                if (propuesta != null && propuesta.available) ...[
                  Text(
                    'Es una propuesta. Tache lo que no, corrija las horas y acepte: hasta '
                    'entonces la orden no cambia.',
                    style: theme.textTheme.bodySmall,
                  ),
                  if (propuesta.note != null) ...[
                    const SizedBox(height: 12),
                    Container(
                      padding: const EdgeInsets.all(10),
                      decoration: BoxDecoration(
                        border: Border(
                          left: BorderSide(color: theme.colorScheme.primary, width: 3),
                        ),
                      ),
                      child: Text(propuesta.note!, style: theme.textTheme.bodySmall),
                    ),
                  ],
                  const SizedBox(height: 8),
                  if (propuesta.tasks.isEmpty)
                    Text(
                      'El asistente no propuso pasos con lo que hay escrito.',
                      style: theme.textTheme.bodyMedium,
                    ),
                  for (final paso in propuesta.tasks) _FilaPropuesta(paso: paso, onCambio: () => setState(() {})),
                  if (propuesta.parts.isNotEmpty) ...[
                    const SizedBox(height: 16),
                    Text(
                      'Repuestos que llevaría, para cargarlos cuando se usen:',
                      style: theme.textTheme.bodySmall,
                    ),
                    const SizedBox(height: 4),
                    // No se cargan al aceptar: cargarlos descuenta la bodega y el trabajo
                    // todavía no se ha hecho.
                    for (final r in propuesta.parts)
                      Padding(
                        padding: const EdgeInsets.symmetric(vertical: 2),
                        child: Text(
                          '${_cantidad(r.quantity)} ${r.unit} · ${r.name}'
                          '${r.available < r.quantity ? '  (quedan ${_cantidad(r.available)})' : ''}',
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: r.available < r.quantity ? theme.colorScheme.error : null,
                          ),
                        ),
                      ),
                  ],
                  const SizedBox(height: 16),
                  Text(
                    'Quedan ${propuesta.creditsLeftToday} consultas hoy',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                  ),
                ],
              ],
            ),
      bottomNavigationBar: propuesta != null && propuesta.available && propuesta.tasks.isNotEmpty
          ? SafeArea(
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: FilledButton(
                  onPressed: _guardando || _incluidos.isEmpty ? null : _aceptar,
                  child: Text(
                    'Aceptar ${_incluidos.length} '
                    '${_incluidos.length == 1 ? 'paso' : 'pasos'}',
                  ),
                ),
              ),
            )
          : null,
    );
  }

  static String _cantidad(double valor) =>
      valor == valor.roundToDouble() ? valor.toStringAsFixed(0) : valor.toStringAsFixed(2);
}

/// Un paso propuesto: se tacha con la casilla y se le corrigen las horas ahí mismo.
class _FilaPropuesta extends StatelessWidget {
  const _FilaPropuesta({required this.paso, required this.onCambio});

  final ProposedTask paso;
  final VoidCallback onCambio;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Opacity(
      opacity: paso.incluido ? 1 : 0.45,
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Checkbox(
            value: paso.incluido,
            onChanged: (v) {
              paso.incluido = v ?? false;
              onCambio();
            },
          ),
          Expanded(
            child: Padding(
              padding: const EdgeInsets.only(top: 12, bottom: 8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(paso.title, style: theme.textTheme.titleSmall),
                  if (paso.description != null)
                    Text(paso.description!, style: theme.textTheme.bodySmall),
                  Text(
                    paso.laborServiceName ?? 'Sin servicio del catálogo',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                  ),
                ],
              ),
            ),
          ),
          SizedBox(
            width: 70,
            child: TextFormField(
              initialValue: paso.hours == 0 ? '' : paso.hours.toString(),
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              textAlign: TextAlign.right,
              decoration: const InputDecoration(suffixText: 'h', isDense: true),
              onChanged: (v) => paso.hours = double.tryParse(v.replaceAll(',', '.')) ?? 0,
            ),
          ),
        ],
      ),
    );
  }
}

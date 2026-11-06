import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/assistant_query_repository.dart';
import '../../core/models/assistant_query.dart';

/// Preguntar con el vehículo en la rampa.
///
/// El técnico está de pie, con las manos sucias y a veces sin señal. Por eso lo escrito no se
/// borra hasta que la pregunta entra de verdad: quedarse sin red a medio turno no puede costar
/// volver a teclearlo todo.
class AssistantQueryScreen extends ConsumerStatefulWidget {
  const AssistantQueryScreen({
    required this.workOrderId,
    required this.workOrderNumber,
    super.key,
  });

  final String workOrderId;
  final String workOrderNumber;

  @override
  ConsumerState<AssistantQueryScreen> createState() => _AssistantQueryScreenState();
}

class _AssistantQueryScreenState extends ConsumerState<AssistantQueryScreen> {
  final _pregunta = TextEditingController();

  List<ConversationListItem> _anteriores = const [];
  Conversation? _abierta;
  bool _cargando = true;
  bool _pensando = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _cargar();
  }

  @override
  void dispose() {
    _pregunta.dispose();
    super.dispose();
  }

  Future<void> _cargar() async {
    try {
      final lista = await ref.read(assistantQueryRepositoryProvider).list(widget.workOrderId);
      if (mounted) setState(() => _anteriores = lista);
    } catch (e) {
      if (mounted) setState(() => _error = apiErrorMessage(e));
    } finally {
      if (mounted) setState(() => _cargando = false);
    }
  }

  Future<void> _abrir(String id) async {
    setState(() => _error = null);

    try {
      final c = await ref
          .read(assistantQueryRepositoryProvider)
          .get(widget.workOrderId, id);
      if (mounted) setState(() => _abierta = c);
    } catch (e) {
      if (mounted) setState(() => _error = apiErrorMessage(e));
    }
  }

  Future<void> _preguntar() async {
    final texto = _pregunta.text.trim();
    if (texto.isEmpty || _pensando) return;

    setState(() {
      _pensando = true;
      _error = null;
    });

    try {
      final c = await ref.read(assistantQueryRepositoryProvider).ask(
            widget.workOrderId,
            texto,
            conversationId: _abierta?.id,
          );

      if (!mounted) return;

      setState(() {
        _abierta = c;
        // Solo se limpia si la pregunta llegó a entrar: si no, lo escrito sigue ahí.
        if (c.turns.isNotEmpty) _pregunta.clear();
      });

      await _cargar();
    } catch (e) {
      if (mounted) setState(() => _error = apiErrorMessage(e));
    } finally {
      if (mounted) setState(() => _pensando = false);
    }
  }

  void _nueva() {
    setState(() {
      _abierta = null;
      _error = null;
    });
    _pregunta.clear();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final abierta = _abierta;

    return Scaffold(
      appBar: AppBar(
        title: Text('Preguntar · ${widget.workOrderNumber}'),
        actions: [
          if (abierta != null)
            TextButton(onPressed: _nueva, child: const Text('Otra consulta')),
        ],
      ),
      body: _cargando
          ? const Center(child: CircularProgressIndicator())
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 16),
              children: [
                if (_error != null)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: Text(_error!, style: TextStyle(color: theme.colorScheme.error)),
                  ),

                if (abierta == null) ...[
                  if (_anteriores.isNotEmpty) ...[
                    Text('Consultas de esta orden', style: theme.textTheme.labelMedium),
                    const SizedBox(height: 4),
                    for (final c in _anteriores)
                      ListTile(
                        contentPadding: EdgeInsets.zero,
                        title: Text(c.title, maxLines: 2, overflow: TextOverflow.ellipsis),
                        subtitle: Text(c.isClosed ? 'Cerrada' : '${c.turns} preguntas'),
                        onTap: () => _abrir(c.id),
                      ),
                    const Divider(height: 24),
                  ],
                ] else ...[
                  for (final t in abierta.turns) _Turno(turno: t),
                  if (abierta.message != null)
                    Padding(
                      padding: const EdgeInsets.only(top: 8),
                      child: Text(abierta.message!, style: theme.textTheme.bodySmall),
                    ),
                  const SizedBox(height: 12),
                ],

                if (abierta == null || !abierta.isClosed) ...[
                  TextField(
                    controller: _pregunta,
                    minLines: 2,
                    maxLines: 5,
                    enabled: !_pensando,
                    textCapitalization: TextCapitalization.sentences,
                    decoration: const InputDecoration(
                      labelText: '¿Qué quiere preguntar?',
                      hintText: 'Una cosa concreta se contesta mejor.',
                      border: OutlineInputBorder(),
                    ),
                  ),
                  const SizedBox(height: 10),
                  FilledButton(
                    onPressed: _pensando ? null : _preguntar,
                    child: Text(_pensando ? 'Pensando…' : 'Preguntar'),
                  ),
                ],

                if (abierta != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 10),
                    child: Text(
                      'Quedan ${abierta.creditsLeftToday} consultas hoy',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ),

                const SizedBox(height: 20),
                Text(
                  'No da torques ni capacidades del fabricante: eso se busca en el manual de '
                  'la marca. Lo que sí sabe es qué hizo este taller antes con el mismo modelo.',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
    );
  }
}

/// Un turno. Lo que escribió la persona va marcado: en una conversación corta, saber de un
/// vistazo quién dijo qué es la mitad de poder leerla.
class _Turno extends StatelessWidget {
  const _Turno({required this.turno});

  final ConversationTurn turno;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      margin: EdgeInsets.only(bottom: 8, left: turno.fromUser ? 28 : 0),
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(
        border: Border.all(
          color: turno.fromUser ? theme.colorScheme.primary : theme.dividerColor,
        ),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Text(turno.content, style: theme.textTheme.bodyMedium),
    );
  }
}

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/media_repository.dart';
import '../../core/api/quote_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/models/quote.dart';
import '../../core/theme/garaj_brand.dart';
import '../reports/reports_screen.dart' show money;

/// El presupuesto del Cliente, en su propia pantalla.
///
/// Aprobar y rechazar ya funcionaban en la app, pero vivían dentro del detalle de la orden,
/// cinco secciones abajo, entre cosas que al cliente no le tocan. Es la única decisión que él
/// tiene que tomar, así que aquí es lo único que hay: el total, qué incluye, las fotos del
/// daño y los dos botones fijos al alcance del pulgar.
class QuoteScreen extends ConsumerStatefulWidget {
  const QuoteScreen({required this.id, super.key});

  final String id;

  @override
  ConsumerState<QuoteScreen> createState() => _QuoteScreenState();
}

class _QuoteScreenState extends ConsumerState<QuoteScreen> {
  bool _busy = false;

  Future<void> _responder(bool aprobar) async {
    final controller = TextEditingController();

    final confirmado = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(aprobar ? '¿Aprobar el presupuesto?' : '¿Rechazar el presupuesto?'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              aprobar
                  ? 'El taller sigue con el trabajo y el presupuesto queda cerrado.'
                  : 'El taller detiene el trabajo y le van a llamar.',
            ),
            const SizedBox(height: 12),
            TextField(
              controller: controller,
              autofocus: !aprobar,
              maxLines: 2,
              decoration: const InputDecoration(
                labelText: 'Nota para el taller (opcional)',
              ),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancelar'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: Text(aprobar ? 'Aprobar' : 'Rechazar'),
          ),
        ],
      ),
    );

    if (confirmado != true) return;

    setState(() => _busy = true);
    try {
      final nota = controller.text.trim();
      await ref.read(quoteRepositoryProvider).respond(
            widget.id,
            approve: aprobar,
            note: nota.isEmpty ? null : nota,
          );

      // El estado de la orden cambia con la respuesta: el inicio del Cliente tiene que
      // enterarse, y la cotización deja de estar pendiente.
      ref
        ..invalidate(quoteDetailProvider(widget.id))
        ..invalidate(myQuotesProvider)
        ..invalidate(openOrdersProvider);

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(aprobar
                ? 'Presupuesto aprobado. El taller ya lo sabe.'
                : 'Presupuesto rechazado. El taller ya lo sabe.'),
          ),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e, 'No se pudo enviar su respuesta.'))),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cotizacion = ref.watch(quoteDetailProvider(widget.id));
    final quote = cotizacion.value;

    return Scaffold(
      appBar: AppBar(title: const Text('Presupuesto')),
      bottomNavigationBar: quote == null || !quote.canRespond
          ? null
          : Container(
              decoration: BoxDecoration(
                color: theme.colorScheme.surface,
                border: Border(top: BorderSide(color: theme.dividerColor)),
              ),
              child: SafeArea(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(16, 10, 16, 10),
                  child: Row(
                    children: [
                      Expanded(
                        child: FilledButton(
                          onPressed: _busy ? null : () => _responder(true),
                          child: const Text('Aprobar'),
                        ),
                      ),
                      const SizedBox(width: 8),
                      SizedBox(
                        width: 130,
                        height: 48,
                        child: OutlinedButton(
                          onPressed: _busy ? null : () => _responder(false),
                          style: OutlinedButton.styleFrom(
                            foregroundColor: theme.colorScheme.error,
                            shape: const RoundedRectangleBorder(
                              borderRadius: BorderRadius.all(Radius.circular(6)),
                            ),
                            side: BorderSide(
                              color: Color.lerp(
                                theme.dividerColor,
                                theme.colorScheme.error,
                                0.45,
                              )!,
                            ),
                          ),
                          child: const Text('Rechazar'),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
      body: cotizacion.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Text(
              apiErrorMessage(e, 'No se pudo cargar el presupuesto.'),
              textAlign: TextAlign.center,
            ),
          ),
        ),
        data: (q) => ListView(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 32),
          children: [
            Card(
              child: Padding(
                padding: const EdgeInsets.all(14),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('TOTAL', style: _rotulo(theme)),
                    Text(
                      money(q.total, q.currency),
                      style: theme.textTheme.headlineMedium?.copyWith(
                        fontFamily: GarajFonts.mono,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    Text(
                      [
                        if (q.vehicleLabel != null) q.vehicleLabel!,
                        q.number,
                        if (q.validUntil != null) 'vale hasta el ${_fecha(q.validUntil!)}',
                      ].join(' · '),
                      style: theme.textTheme.bodySmall,
                    ),
                    if (q.taxTotal > 0)
                      Text(
                        'Incluye ${money(q.taxTotal, q.currency)} de impuesto.',
                        style: theme.textTheme.bodySmall,
                      ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 12),
            if (q.lines.isNotEmpty)
              Card(
                clipBehavior: Clip.antiAlias,
                child: Column(
                  children: [
                    for (var i = 0; i < q.lines.length; i++) ...[
                      if (i > 0) Divider(height: 1, color: theme.dividerColor),
                      _Linea(line: q.lines[i], currency: q.currency),
                    ],
                  ],
                ),
              ),
            _Fotos(quoteId: q.id),
            if (q.notes != null && q.notes!.isNotEmpty) ...[
              const SizedBox(height: 12),
              Text('LO QUE DICE EL TALLER', style: _rotulo(theme)),
              const SizedBox(height: 4),
              Text(q.notes!),
            ],
            const SizedBox(height: 16),
            if (q.canRespond)
              Text(
                'Si aprueba, el taller sigue con el trabajo y el presupuesto queda cerrado. '
                'Si rechaza, le van a llamar.',
                style: theme.textTheme.bodySmall,
              )
            else
              Text(
                _yaRespondido(q),
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            if (q.customerResponseNote != null && q.customerResponseNote!.isNotEmpty)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  'Su nota: ${q.customerResponseNote}',
                  style: theme.textTheme.bodySmall,
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _Linea extends StatelessWidget {
  const _Linea({required this.line, required this.currency});

  final QuoteLine line;
  final String currency;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(line.description),
                Text(
                  line.lineType == LineType.labor
                      ? 'mano de obra'
                      : 'repuesto · ${_cantidad(line.quantity)}',
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ),
          ),
          const SizedBox(width: 8),
          Text(
            money(line.total, currency),
            style: theme.textTheme.titleSmall?.copyWith(fontFamily: GarajFonts.mono),
          ),
        ],
      ),
    );
  }
}

/// Las fotos del daño. Son lo que hace que un presupuesto se entienda sin ir al taller, así
/// que van antes de los botones y no dentro de una sección plegada.
class _Fotos extends ConsumerWidget {
  const _Fotos({required this.quoteId});

  final String quoteId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final fotos = ref.watch(quoteMediaProvider(quoteId)).value ?? const [];
    if (fotos.isEmpty) return const SizedBox.shrink();

    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('LAS FOTOS DEL DAÑO', style: _rotulo(theme)),
          const SizedBox(height: 6),
          SizedBox(
            height: 92,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              itemCount: fotos.length,
              separatorBuilder: (_, __) => const SizedBox(width: 8),
              itemBuilder: (context, i) => ClipRRect(
                borderRadius: BorderRadius.circular(6),
                child: Image.network(
                  fotos[i].thumbnailUrl,
                  width: 116,
                  height: 92,
                  fit: BoxFit.cover,
                  errorBuilder: (_, __, ___) => Container(
                    width: 116,
                    height: 92,
                    color: theme.colorScheme.surfaceContainerHighest,
                    child: Icon(Icons.broken_image_outlined,
                        color: theme.colorScheme.onSurfaceVariant),
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

TextStyle? _rotulo(ThemeData theme) => theme.textTheme.labelSmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
      letterSpacing: 0.6,
    );

String _yaRespondido(Quote q) => switch (q.status) {
      QuoteStatus.approved => 'Usted aprobó este presupuesto.',
      QuoteStatus.rejected => 'Usted rechazó este presupuesto.',
      _ when q.isExpired => 'Este presupuesto se venció. Pídale otro al taller.',
      _ => 'Este presupuesto todavía no se le mandó.',
    };

String _cantidad(double value) =>
    value == value.roundToDouble() ? value.toInt().toString() : value.toStringAsFixed(2);

String _fecha(DateTime value) {
  const meses = [
    'ene', 'feb', 'mar', 'abr', 'may', 'jun',
    'jul', 'ago', 'sep', 'oct', 'nov', 'dic',
  ];
  final local = value.toLocal();
  return '${local.day} de ${meses[local.month - 1]}';
}

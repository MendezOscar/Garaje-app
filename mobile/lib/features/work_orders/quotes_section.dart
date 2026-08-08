import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_client.dart';
import '../../core/api/quote_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/current_user.dart';
import '../../core/models/quote.dart';

/// Cotizaciones de la orden. Para el Cliente es donde aprueba el trabajo sin salir de la
/// app; para el Dueño, desde donde dispara el WhatsApp.
class QuotesSection extends ConsumerWidget {
  const QuotesSection({required this.workOrderId, super.key});

  final String workOrderId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authControllerProvider);
    final role = auth is AuthSignedIn ? auth.user.role : null;

    // El técnico no participa en la parte comercial: el backend le devolvería una lista
    // vacía, así que ni se pide.
    if (role == AppRole.technician) return const SizedBox.shrink();

    final theme = Theme.of(context);
    final quotes = ref.watch(workOrderQuotesProvider(workOrderId));

    return quotes.maybeWhen(
      data: (list) => list.isEmpty
          ? const SizedBox.shrink()
          : Padding(
              padding: const EdgeInsets.only(bottom: 20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'COTIZACIONES',
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                      letterSpacing: 0.6,
                    ),
                  ),
                  const SizedBox(height: 8),
                  for (final quote in list)
                    _QuoteCard(
                      quote: quote,
                      workOrderId: workOrderId,
                      isOwner: role == AppRole.owner,
                    ),
                ],
              ),
            ),
      orElse: () => const SizedBox.shrink(),
    );
  }
}

class _QuoteCard extends ConsumerStatefulWidget {
  const _QuoteCard({
    required this.quote,
    required this.workOrderId,
    required this.isOwner,
  });

  final Quote quote;
  final String workOrderId;
  final bool isOwner;

  @override
  ConsumerState<_QuoteCard> createState() => _QuoteCardState();
}

class _QuoteCardState extends ConsumerState<_QuoteCard> {
  bool _busy = false;

  Future<void> _respond(bool approve) async {
    final note = await _askNote(approve);
    if (note == null) return;

    setState(() => _busy = true);
    try {
      await ref.read(quoteRepositoryProvider).respond(
            widget.quote.id,
            approve: approve,
            note: note.isEmpty ? null : note,
          );
      ref.invalidate(workOrderQuotesProvider(widget.workOrderId));
    } catch (e) {
      _snack(apiErrorMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<String?> _askNote(bool approve) {
    final controller = TextEditingController();

    return showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(approve ? '¿Aprobar la cotización?' : '¿Rechazar la cotización?'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Text('Esta respuesta no se puede cambiar.'),
            const SizedBox(height: 12),
            TextField(
              controller: controller,
              decoration: InputDecoration(
                labelText: approve ? 'Comentario (opcional)' : '¿Por qué? (opcional)',
              ),
            ),
          ],
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancelar')),
          FilledButton(
            onPressed: () => Navigator.pop(context, controller.text.trim()),
            child: const Text('Confirmar'),
          ),
        ],
      ),
    );
  }

  Future<void> _share() async {
    setState(() => _busy = true);
    try {
      final url = await ref.read(quoteRepositoryProvider).sendLink(widget.quote.id);

      // externalApplication: abre WhatsApp de verdad, no una vista web dentro de la app.
      final launched = await launchUrl(
        Uri.parse(url),
        mode: LaunchMode.externalApplication,
      );

      if (!launched) _snack('No se pudo abrir WhatsApp.');
      ref.invalidate(workOrderQuotesProvider(widget.workOrderId));
    } catch (e) {
      _snack(apiErrorMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// Abre el PDF en el navegador del teléfono, desde donde se guarda o se reenvía.
  ///
  /// Va por la ruta pública y no por `/api/quotes/{id}/pdf`: el token de sesión viaja en una
  /// cabecera y el navegador del sistema no la manda, así que el endpoint autenticado
  /// respondería 401. El token aleatorio de la URL pública es la credencial, el mismo que ya
  /// tiene el cliente en su WhatsApp.
  Future<void> _openPdf() async {
    final publicUrl = widget.quote.publicUrl;
    if (publicUrl == null) return;

    final token = Uri.parse(publicUrl).pathSegments.last;
    final launched = await launchUrl(
      Uri.parse('$apiBaseUrl/public/quotes/$token/pdf'),
      mode: LaunchMode.externalApplication,
    );

    if (!launched) _snack('No se pudo abrir el PDF.');
  }

  void _snack(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final quote = widget.quote;

    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(child: Text(quote.number, style: theme.textTheme.titleSmall)),
                Chip(
                  label: Text(
                    quote.isExpired && quote.status == QuoteStatus.sent
                        ? 'Vencida'
                        : quote.status.label,
                    style: theme.textTheme.labelSmall,
                  ),
                  visualDensity: VisualDensity.compact,
                  padding: EdgeInsets.zero,
                ),
              ],
            ),

            for (final line in quote.lines)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(
                        '${line.description} (${line.lineType.label})',
                        style: theme.textTheme.bodySmall,
                      ),
                    ),
                    Text(_money(line.total, quote.currency), style: theme.textTheme.bodySmall),
                  ],
                ),
              ),

            const Divider(height: 20),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text('Total', style: theme.textTheme.bodyMedium),
                Text(_money(quote.total, quote.currency), style: theme.textTheme.titleMedium),
              ],
            ),

            if (quote.validUntil != null && quote.canRespond)
              Text(
                'Válida hasta ${_date(quote.validUntil!)}',
                style: theme.textTheme.bodySmall,
              ),

            if (quote.customerResponseNote != null)
              Padding(
                padding: const EdgeInsets.only(top: 6),
                child: Text('«${quote.customerResponseNote}»', style: theme.textTheme.bodySmall),
              ),

            Align(
              alignment: Alignment.centerRight,
              child: Wrap(
                alignment: WrapAlignment.end,
                children: [
                  // Solo cuando ya se envió: el borrador todavía no tiene enlace público,
                  // que es por donde se sirve el PDF sin sesión.
                  if (quote.publicUrl != null)
                    TextButton.icon(
                      onPressed: _busy ? null : _openPdf,
                      icon: const Icon(Icons.picture_as_pdf_outlined, size: 18),
                      label: const Text('PDF'),
                    ),
                  if (widget.isOwner &&
                      quote.status != QuoteStatus.approved &&
                      quote.status != QuoteStatus.rejected)
                    TextButton.icon(
                      onPressed: _busy ? null : _share,
                      icon: const Icon(Icons.send, size: 18),
                      label: Text(
                        quote.status == QuoteStatus.draft
                            ? 'Enviar por WhatsApp'
                            : 'Reenviar por WhatsApp',
                      ),
                    ),
                ],
              ),
            ),

            if (!widget.isOwner && quote.canRespond)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Row(
                  children: [
                    Expanded(
                      child: FilledButton(
                        onPressed: _busy ? null : () => _respond(true),
                        child: const Text('Aprobar'),
                      ),
                    ),
                    const SizedBox(width: 8),
                    TextButton(
                      onPressed: _busy ? null : () => _respond(false),
                      child: const Text('No por ahora'),
                    ),
                  ],
                ),
              ),
          ],
        ),
      ),
    );
  }

  static String _money(double value, String currency) =>
      '$currency ${value.toStringAsFixed(2)}';

  static String _date(DateTime value) {
    final local = value.toLocal();
    return '${local.day.toString().padLeft(2, '0')}/${local.month.toString().padLeft(2, '0')}/${local.year}';
  }
}

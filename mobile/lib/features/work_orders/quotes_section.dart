import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_client.dart';
import '../../core/api/inventory_repository.dart';
import '../../core/api/quote_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/current_user.dart';
import '../../core/models/inventory.dart';
import '../../core/models/media.dart';
import '../../core/models/quote.dart';
import 'photo_gallery.dart';

/// Cotizaciones de la orden. Para el Cliente es donde aprueba el trabajo sin salir de la
/// app; para el Dueño, desde donde la arma y la manda por WhatsApp.
class QuotesSection extends ConsumerStatefulWidget {
  const QuotesSection({required this.workOrderId, super.key});

  final String workOrderId;

  @override
  ConsumerState<QuotesSection> createState() => _QuotesSectionState();
}

class _QuotesSectionState extends ConsumerState<QuotesSection> {
  bool _busy = false;

  /// Arma el borrador con lo que la orden ya tiene. Después se le agregan o se le quitan
  /// líneas: casi nunca se cotiza exactamente lo que ya está cargado.
  Future<void> _create() async {
    setState(() => _busy = true);
    try {
      await ref.read(quoteRepositoryProvider).createFromWorkOrder(widget.workOrderId);
      ref.invalidate(workOrderQuotesProvider(widget.workOrderId));
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

  @override
  Widget build(BuildContext context) {
    final auth = ref.watch(authControllerProvider);
    final role = auth is AuthSignedIn ? auth.user.role : null;

    // El técnico no participa en la parte comercial: el backend le devolvería una lista
    // vacía, así que ni se pide.
    if (role == AppRole.technician) return const SizedBox.shrink();

    final theme = Theme.of(context);
    final isOwner = role == AppRole.owner;
    final quotes = ref.watch(workOrderQuotesProvider(widget.workOrderId));
    final list = quotes.value ?? const <Quote>[];

    // Al Cliente no se le enseña una sección vacía: si no hay nada que aprobar, no hay nada
    // que contarle.
    if (list.isEmpty && !isOwner) return const SizedBox.shrink();

    return Padding(
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
              workOrderId: widget.workOrderId,
              isOwner: isOwner,
            ),
          if (isOwner)
            TextButton.icon(
              onPressed: _busy ? null : _create,
              icon: const Icon(Icons.request_quote_outlined, size: 18),
              label: Text(list.isEmpty ? 'Cotizar' : 'Nueva cotización'),
            ),
          if (isOwner && list.isEmpty)
            Text(
              'Se arma con los repuestos cargados y los pasos que tengan mano de obra.',
              style: theme.textTheme.bodySmall,
            ),
        ],
      ),
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

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
      ref.invalidate(workOrderQuotesProvider(widget.workOrderId));
    } catch (e) {
      _snack(apiErrorMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// Qué se agrega. Un repuesto y una mano de obra salen del catálogo con su precio; la
  /// línea libre existe porque siempre hay algo que cobrar que no está en ningún catálogo.
  Future<void> _addLine() async {
    final kind = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const Icon(Icons.settings_outlined),
              title: const Text('Repuesto del catálogo'),
              onTap: () => Navigator.pop(context, 'part'),
            ),
            ListTile(
              leading: const Icon(Icons.build_outlined),
              title: const Text('Mano de obra del catálogo'),
              onTap: () => Navigator.pop(context, 'labor'),
            ),
            ListTile(
              leading: const Icon(Icons.edit_outlined),
              title: const Text('Línea libre'),
              subtitle: const Text('Lo que no está en ningún catálogo'),
              onTap: () => Navigator.pop(context, 'free'),
            ),
          ],
        ),
      ),
    );

    if (!mounted || kind == null) return;

    switch (kind) {
      case 'part':
        await _addPartLine();
      case 'labor':
        await _addLaborLine();
      case 'free':
        await _addFreeLine();
    }
  }

  Future<void> _addPartLine() async {
    final choice = await showModalBottomSheet<({Part part, double quantity})>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (context) => const _QuotePartPicker(),
    );

    if (choice == null) return;

    await _run(() async {
      await ref.read(quoteRepositoryProvider).addLine(
            widget.quote.id,
            lineType: LineType.part,
            partId: choice.part.id,
            quantity: choice.quantity,
          );
    });
  }

  Future<void> _addLaborLine() async {
    final services = ref.read(laborServicesProvider).value ?? const <LaborServiceOption>[];

    final serviceId = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: services.isEmpty
            ? const Padding(
                padding: EdgeInsets.all(24),
                child: Text('El catálogo de mano de obra está vacío.'),
              )
            : ListView(
                shrinkWrap: true,
                children: [
                  for (final service in services)
                    ListTile(
                      title: Text(service.name),
                      trailing: Text(_money(service.price, 'L')),
                      onTap: () => Navigator.pop(context, service.id),
                    ),
                ],
              ),
      ),
    );

    if (serviceId == null) return;

    await _run(() async {
      await ref.read(quoteRepositoryProvider).addLine(
            widget.quote.id,
            lineType: LineType.labor,
            laborServiceId: serviceId,
            quantity: 1,
          );
    });
  }

  Future<void> _addFreeLine() async {
    final description = TextEditingController();
    final quantity = TextEditingController(text: '1');
    final price = TextEditingController();
    var lineType = LineType.labor;

    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setInner) => AlertDialog(
          title: const Text('Línea libre'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              SegmentedButton<LineType>(
                segments: const [
                  ButtonSegment(value: LineType.labor, label: Text('Mano de obra')),
                  ButtonSegment(value: LineType.part, label: Text('Repuesto')),
                ],
                selected: {lineType},
                showSelectedIcon: false,
                onSelectionChanged: (s) => setInner(() => lineType = s.first),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: description,
                autofocus: true,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(labelText: 'Concepto'),
              ),
              const SizedBox(height: 8),
              Row(
                children: [
                  Expanded(
                    child: TextField(
                      controller: quantity,
                      keyboardType: const TextInputType.numberWithOptions(decimal: true),
                      decoration: const InputDecoration(labelText: 'Cantidad'),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: TextField(
                      controller: price,
                      keyboardType: const TextInputType.numberWithOptions(decimal: true),
                      decoration: const InputDecoration(labelText: 'Precio', prefixText: 'L '),
                    ),
                  ),
                ],
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
              child: const Text('Agregar'),
            ),
          ],
        ),
      ),
    );

    if (ok != true) return;

    final text = description.text.trim();
    final qty = double.tryParse(quantity.text.trim().replaceAll(',', '.')) ?? 1;
    final unitPrice = double.tryParse(price.text.trim().replaceAll(',', '.'));
    if (text.isEmpty || unitPrice == null) {
      _snack('Hace falta el concepto y el precio.');
      return;
    }

    await _run(() async {
      await ref.read(quoteRepositoryProvider).addLine(
            widget.quote.id,
            lineType: lineType,
            description: text,
            quantity: qty,
            unitPrice: unitPrice,
          );
    });
  }

  Future<void> _removeLine(QuoteLine line) async {
    await _run(() async {
      await ref.read(quoteRepositoryProvider).removeLine(widget.quote.id, line.id);
    });
  }

  /// Hasta cuándo aguanta el precio. Vencida no se puede aprobar: el precio de hace dos
  /// meses no obliga al taller.
  Future<void> _setValidUntil() async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: widget.quote.validUntil ?? now.add(const Duration(days: 15)),
      firstDate: now,
      lastDate: now.add(const Duration(days: 365)),
    );

    if (picked == null) return;

    await _run(() async {
      await ref.read(quoteRepositoryProvider).update(widget.quote.id, validUntil: picked);
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final quote = widget.quote;

    // Se le pueden tocar las líneas mientras nadie la haya respondido: una cotización
    // respondida es un documento cerrado y el backend devuelve 409 al editarla.
    final editable = widget.isOwner && quote.isEditable;

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
                    if (editable)
                      IconButton(
                        tooltip: 'Quitar la línea',
                        icon: const Icon(Icons.close, size: 16),
                        visualDensity: VisualDensity.compact,
                        onPressed: _busy ? null : () => _removeLine(line),
                      ),
                  ],
                ),
              ),

            if (quote.lines.isEmpty)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text('Sin líneas todavía.', style: theme.textTheme.bodySmall),
              ),

            if (editable)
              Align(
                alignment: Alignment.centerLeft,
                child: TextButton.icon(
                  onPressed: _busy ? null : _addLine,
                  icon: const Icon(Icons.add, size: 18),
                  label: const Text('Agregar línea'),
                ),
              ),

            // Las fotos del daño, que es lo que hace que un presupuesto se entienda sin ir al
            // taller. Se toman con el vehículo delante, así que tienen que poder subirse
            // desde el teléfono y no solo desde el panel.
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: PhotoGallery(
                ownerId: quote.id,
                ownerType: MediaOwnerType.quote,
                // Mientras se pueda editar: una cotización respondida es un documento
                // cerrado, y cambiarle las fotos es cambiarle lo que el cliente aprobó.
                canEdit: editable,
                titulo: 'FOTOS DEL DAÑO',
                vacioPropio: 'Sin fotos. Una del daño explica el presupuesto mejor que el texto.',
                vacioAjeno: 'El taller no adjuntó fotos a este presupuesto.',
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

            // Lo mismo que está leyendo el cliente en su copia.
            if (quote.taxRate == 0)
              Text(
                'No incluye ISV.',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),

            if (quote.validUntil != null && quote.canRespond && !editable)
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
                  if (editable)
                    TextButton.icon(
                      onPressed: _busy ? null : _setValidUntil,
                      icon: const Icon(Icons.event_outlined, size: 18),
                      label: Text(
                        quote.validUntil == null
                            ? 'Vigencia'
                            : 'Vence ${_date(quote.validUntil!)}',
                      ),
                    ),
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

/// Busca en el catálogo y devuelve el repuesto con su cantidad. Aquí no se descuenta nada de
/// la bodega: una cotización es un precio, no un consumo. El repuesto sale de la bodega
/// cuando el técnico lo carga a la orden.
class _QuotePartPicker extends ConsumerStatefulWidget {
  const _QuotePartPicker();

  @override
  ConsumerState<_QuotePartPicker> createState() => _QuotePartPickerState();
}

class _QuotePartPickerState extends ConsumerState<_QuotePartPicker> {
  String _search = '';

  @override
  Widget build(BuildContext context) {
    final results = ref.watch(partSearchProvider(_search));

    return Padding(
      // Deja sitio al teclado: sin esto el buscador queda tapado al escribir.
      padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
      child: SizedBox(
        height: MediaQuery.of(context).size.height * 0.7,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.all(16),
              child: TextField(
                autofocus: true,
                decoration: const InputDecoration(
                  labelText: 'Buscar repuesto',
                  hintText: 'SKU, nombre o marca',
                  prefixIcon: Icon(Icons.search),
                ),
                onChanged: (v) => setState(() => _search = v),
              ),
            ),
            Expanded(
              child: results.when(
                loading: () => const Center(child: CircularProgressIndicator()),
                error: (e, _) => Center(
                  child: Padding(
                    padding: const EdgeInsets.all(24),
                    child: Text(apiErrorMessage(e, 'No se pudo cargar el catálogo.')),
                  ),
                ),
                data: (parts) => parts.isEmpty
                    ? const Center(child: Text('Sin resultados.'))
                    : ListView.builder(
                        itemCount: parts.length,
                        itemBuilder: (context, i) {
                          final part = parts[i];

                          return ListTile(
                            title: Text(part.name),
                            subtitle: Text(
                              '${part.sku}'
                              '${part.brand != null ? ' · ${part.brand}' : ''}'
                              ' · L ${part.salePrice.toStringAsFixed(2)}',
                            ),
                            onTap: () => _askQuantity(part),
                          );
                        },
                      ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _askQuantity(Part part) async {
    final controller = TextEditingController(text: '1');

    final quantity = await showDialog<double>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(part.name),
        content: TextField(
          controller: controller,
          autofocus: true,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: InputDecoration(labelText: 'Cantidad (${part.unit})'),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancelar')),
          FilledButton(
            onPressed: () => Navigator.pop(
              context,
              double.tryParse(controller.text.replaceAll(',', '.')),
            ),
            child: const Text('Agregar'),
          ),
        ],
      ),
    );

    if (quantity == null || quantity <= 0 || !mounted) return;
    Navigator.pop(context, (part: part, quantity: quantity));
  }
}

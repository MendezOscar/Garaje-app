import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:path_provider/path_provider.dart';
import 'package:share_plus/share_plus.dart';

import '../../core/api/api_client.dart';
import '../../core/api/quote_repository.dart';
import '../../core/api/tenant_repository.dart';
import '../../core/api/sale_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/models/quote.dart';
import '../../core/models/work_order.dart';

/// De dónde sale la mano de obra que se factura.
sealed class _LaborSource {
  const _LaborSource();
}

/// La suma de los pasos con servicio del catálogo, o el total a mano.
class _FromTasks extends _LaborSource {
  const _FromTasks();
}

/// El precio que el cliente aprobó por WhatsApp.
class _FromQuote extends _LaborSource {
  const _FromQuote(this.quote, this.amount);

  final Quote quote;
  final double amount;
}

/// No se cobra mano de obra: solo los repuestos.
class _NoLabor extends _LaborSource {
  const _NoLabor();
}

/// Cerrar la orden y cobrarla, desde el taller. Es el último paso del trabajo —el vehículo
/// ya está listo y el cliente está enfrente— y hasta ahora obligaba a subir a la computadora.
///
/// Solo la ve el Dueño: el Técnico recibe 403 en todo lo que sea ventas.
class InvoiceSection extends ConsumerStatefulWidget {
  const InvoiceSection({required this.order, super.key});

  final WorkOrderDetail order;

  @override
  ConsumerState<InvoiceSection> createState() => _InvoiceSectionState();
}

class _InvoiceSectionState extends ConsumerState<InvoiceSection> {
  bool _busy = false;
  PaymentMethod _method = PaymentMethod.cash;
  _LaborSource? _laborSource;

  /// Factura con CAI. Sin marcar por defecto: cada factura fiscal quema un número del rango
  /// autorizado, y la mayoría de los clientes de taller no la pide.
  bool _fiscal = false;
  final _customerTaxId = TextEditingController();

  /// Entrega a crédito: lo que deja hoy y para cuándo queda el resto.
  bool _onCredit = false;
  final _initialPayment = TextEditingController();
  DateTime? _dueDate;

  @override
  void dispose() {
    _initialPayment.dispose();
    super.dispose();
  }

  /// Lo que suma la mano de obra de una cotización, sin sus repuestos.
  double _laborOf(Quote quote) => quote.lines
      .where((l) => l.lineType == LineType.labor)
      .fold(0.0, (sum, l) => sum + l.total);

  /// Si el cliente aprobó una cotización, esa es la mano de obra que espera pagar: cobrarle
  /// otra cosa al entregar es donde se pierden los clientes.
  _LaborSource _defaultSource(List<Quote> quotes) {
    for (final quote in quotes) {
      if (quote.status == QuoteStatus.approved && _laborOf(quote) > 0) {
        return _FromQuote(quote, _laborOf(quote));
      }
    }
    return const _FromTasks();
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
      ref.invalidate(workOrderSalesProvider(widget.order.id));
      ref.invalidate(workOrderDetailProvider(widget.order.id));
      ref.invalidate(myWorkOrdersProvider);
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

  Future<void> _close(_LaborSource source) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('¿Facturar y entregar?'),
        content: const Text(
          'Se cobra lo trabajado y la orden queda entregada. Para corregirla después hay '
          'que anular la factura.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancelar'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Facturar'),
          ),
        ],
      ),
    );

    if (confirmed != true) return;

    await _run(() async {
      await ref.read(saleRepositoryProvider).closeWorkOrder(
            workOrderId: widget.order.id,
            paymentMethod: _method,
            includeLabor: source is! _NoLabor,
            laborFromQuoteId: source is _FromQuote ? source.quote.id : null,
            // Sin crédito no se manda nada y el backend cobra el total, que es lo normal.
            initialPayment:
                _onCredit ? double.tryParse(_initialPayment.text.trim()) ?? 0 : null,
            dueDate: _onCredit ? _dueDate : null,
            fiscal: _fiscal,
            customerTaxId: _fiscal ? _customerTaxId.text.trim() : null,
          );
    });
  }

  Future<void> _addPayment(Sale sale) async {
    final amount = TextEditingController(text: sale.balance.toStringAsFixed(2));
    final reference = TextEditingController();
    var method = PaymentMethod.cash;

    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setInner) => AlertDialog(
          title: const Text('Registrar abono'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: amount,
                autofocus: true,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                decoration: InputDecoration(
                  labelText: 'Monto',
                  prefixText: 'L ',
                  helperText: 'Saldo ${_money(sale.balance, sale.currency)}',
                ),
              ),
              const SizedBox(height: 12),
              DropdownButtonFormField<PaymentMethod>(
                initialValue: method,
                decoration: const InputDecoration(labelText: 'Forma de pago'),
                items: [
                  for (final m in PaymentMethod.values)
                    DropdownMenuItem(value: m, child: Text(m.label)),
                ],
                onChanged: (value) => setInner(() => method = value ?? PaymentMethod.cash),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: reference,
                decoration: const InputDecoration(
                  labelText: 'Referencia',
                  hintText: 'Recibo, depósito…',
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
              child: const Text('Registrar'),
            ),
          ],
        ),
      ),
    );

    if (ok != true) return;

    final value = double.tryParse(amount.text.trim().replaceAll(',', '.'));
    if (value == null || value <= 0) return;

    await _run(() => ref.read(saleRepositoryProvider).registerPayment(
          sale.id,
          amount: value,
          method: method,
          reference: reference.text.trim().isEmpty ? null : reference.text.trim(),
        ));
  }

  Future<void> _removePayment(Sale sale, SalePayment payment) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('¿Quitar el abono?'),
        content: const Text(
          'Es para corregir una captura equivocada, no para devolver dinero: para eso se '
          'anula la factura entera.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancelar'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Quitar'),
          ),
        ],
      ),
    );

    if (confirmed != true) return;
    await _run(() => ref.read(saleRepositoryProvider).removePayment(sale.id, payment.id));
  }

  /// Baja el PDF con la sesión puesta y lo pasa a la hoja de compartir: de ahí sale a
  /// WhatsApp, al correo o a la impresora, que es lo que el taller hace con una factura.
  Future<void> _shareInvoice(Sale sale) async {
    setState(() => _busy = true);
    try {
      final bytes = await ref.read(saleRepositoryProvider).invoicePdf(sale.id);
      final file = File('${(await getTemporaryDirectory()).path}/${sale.number}.pdf');
      await file.writeAsBytes(bytes);

      if (!mounted) return;
      await SharePlus.instance.share(
        ShareParams(
          files: [XFile(file.path, mimeType: 'application/pdf')],
          text: 'Factura ${sale.number}',
          // En iPad la hoja sale anclada a un punto; sin esto revienta.
          sharePositionOrigin: _shareOrigin(context),
        ),
      );
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e, 'No se pudo generar la factura.'))),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  static Rect _shareOrigin(BuildContext context) {
    final box = context.findRenderObject() as RenderBox?;
    if (box == null) return Rect.zero;
    return box.localToGlobal(Offset.zero) & box.size;
  }

  @override
  Widget build(BuildContext context) {
    final sales = ref.watch(workOrderSalesProvider(widget.order.id));
    final quotes = ref.watch(workOrderQuotesProvider(widget.order.id));

    return sales.maybeWhen(
      data: (list) {
        final active = list.where((s) => !s.isVoided).toList();
        if (active.isNotEmpty) {
          return _SaleCard(
            sale: active.first,
            busy: _busy,
            onAddPayment: () => _addPayment(active.first),
            onRemovePayment: (p) => _removePayment(active.first, p),
            onShare: () => _shareInvoice(active.first),
          );
        }

        if (widget.order.status == WorkOrderStatus.cancelled) return const SizedBox.shrink();

        final withLabor = (quotes.value ?? const <Quote>[])
            .where((q) => _laborOf(q) > 0)
            .toList();
        // Sin elección explícita manda la cotización aprobada. No se guarda en el estado:
        // las cotizaciones llegan después que la orden y la preselección tiene que
        // corregirse sola cuando lleguen.
        final source = _laborSource ?? _defaultSource(withLabor);

        return _CloseCard(
          order: widget.order,
          quotes: withLabor,
          laborOf: _laborOf,
          source: source,
          method: _method,
          busy: _busy,
          fiscal: _fiscal,
          fiscalRange: ref
              .watch(branchFiscalRangeProvider(widget.order.branchId))
              .maybeWhen(data: (r) => r, orElse: () => null),
          customerTaxId: _customerTaxId,
          onFiscalChanged: (value) => setState(() => _fiscal = value),
          onCredit: _onCredit,
          initialPayment: _initialPayment,
          dueDate: _dueDate,
          onSourceChanged: (value) => setState(() => _laborSource = value),
          onMethodChanged: (value) => setState(() => _method = value),
          onCreditChanged: (value) => setState(() => _onCredit = value),
          onPickDueDate: () async {
            final now = DateTime.now();
            final picked = await showDatePicker(
              context: context,
              initialDate: _dueDate ?? now.add(const Duration(days: 15)),
              firstDate: now,
              lastDate: now.add(const Duration(days: 365)),
            );
            if (picked != null) setState(() => _dueDate = picked);
          },
          onClose: () => _close(source),
        );
      },
      orElse: () => const SizedBox.shrink(),
    );
  }
}

class _CloseCard extends StatelessWidget {
  const _CloseCard({
    required this.order,
    required this.quotes,
    required this.laborOf,
    required this.source,
    required this.method,
    required this.busy,
    required this.fiscal,
    required this.fiscalRange,
    required this.customerTaxId,
    required this.onFiscalChanged,
    required this.onCredit,
    required this.initialPayment,
    required this.dueDate,
    required this.onSourceChanged,
    required this.onMethodChanged,
    required this.onCreditChanged,
    required this.onPickDueDate,
    required this.onClose,
  });

  final WorkOrderDetail order;
  final List<Quote> quotes;
  final double Function(Quote) laborOf;
  final _LaborSource source;
  final PaymentMethod method;
  final bool busy;
  final bool onCredit;
  final TextEditingController initialPayment;
  final DateTime? dueDate;
  final void Function(_LaborSource) onSourceChanged;
  final void Function(PaymentMethod) onMethodChanged;
  final void Function(bool) onCreditChanged;
  final VoidCallback onPickDueDate;
  final VoidCallback onClose;

  final bool fiscal;
  final FiscalRange? fiscalRange;
  final TextEditingController customerTaxId;
  final ValueChanged<bool> onFiscalChanged;

  /// Por qué no se puede emitir con CAI, o null si sí se puede.
  String? get _impedimento {
    if (fiscalRange == null) {
      return 'Esta sucursal no tiene CAI registrado. Se registra en el panel, en Taller.';
    }
    if (fiscalRange!.isExpired) return 'El CAI de esta sucursal venció.';
    if (fiscalRange!.isExhausted) return 'Se agotó el rango autorizado de esta sucursal.';
    return null;
  }

  double get _labor => switch (source) {
        _FromQuote(:final amount) => amount,
        _FromTasks() => order.laborTotal,
        _NoLabor() => 0,
      };

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final total = order.partsTotal + _labor;

    return _Section(
      title: 'Cerrar y facturar',
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'Repuestos ${_money(order.partsTotal, 'L')} · mano de obra '
                '${_money(_labor, 'L')}',
                style: theme.textTheme.bodySmall,
              ),
              const SizedBox(height: 4),
              Text('Total ${_money(total, 'L')}', style: theme.textTheme.titleLarge),
              const SizedBox(height: 4),
              Text(
                'Sin impuesto; el ISV lo calcula la factura.',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),

              const SizedBox(height: 12),
              Text('MANO DE OBRA', style: _labelStyle(theme)),
              // La cotización aprobada primero: es el precio que el cliente ya aceptó.
              for (final quote in quotes)
                _Option(
                  selected: source is _FromQuote && (source as _FromQuote).quote.id == quote.id,
                  title: 'Cotización ${quote.number} · ${_money(laborOf(quote), 'L')}',
                  subtitle: quote.status.label,
                  onTap: busy
                      ? null
                      : () => onSourceChanged(_FromQuote(quote, laborOf(quote))),
                ),
              _Option(
                selected: source is _FromTasks,
                title: 'Los pasos de la orden · ${_money(order.laborTotal, 'L')}',
                onTap: busy ? null : () => onSourceChanged(const _FromTasks()),
              ),
              _Option(
                selected: source is _NoLabor,
                title: 'No cobrar mano de obra',
                onTap: busy ? null : () => onSourceChanged(const _NoLabor()),
              ),

              const SizedBox(height: 8),
              DropdownButtonFormField<PaymentMethod>(
                initialValue: method,
                decoration: const InputDecoration(labelText: 'Forma de pago', isDense: true),
                items: [
                  for (final m in PaymentMethod.values)
                    DropdownMenuItem(value: m, child: Text(m.label)),
                ],
                onChanged: busy ? null : (value) => onMethodChanged(value ?? method),
              ),

              SwitchListTile(
                value: fiscal,
                // Apagada y con el motivo a la vista, en lugar de escondida: que se sepa que
                // el sistema puede facturar con CAI aunque hoy falte registrarlo.
                onChanged: busy || _impedimento != null ? null : onFiscalChanged,
                contentPadding: EdgeInsets.zero,
                title: const Text('Factura con CAI'),
                subtitle: Text(
                  _impedimento ??
                      'Consume el número ${fiscalRange!.nextFiscalNumber} del rango autorizado.',
                ),
              ),

              if (fiscal)
                TextField(
                  controller: customerTaxId,
                  keyboardType: TextInputType.number,
                  decoration: const InputDecoration(
                    labelText: 'RTN del cliente',
                    helperText: 'Vacío: la factura sale a consumidor final.',
                  ),
                ),

              SwitchListTile(
                value: onCredit,
                onChanged: busy ? null : onCreditChanged,
                contentPadding: EdgeInsets.zero,
                title: const Text('Queda debiendo'),
                subtitle: const Text('Deja saldo pendiente en cuentas por cobrar.'),
              ),

              if (onCredit) ...[
                TextField(
                  controller: initialPayment,
                  keyboardType: const TextInputType.numberWithOptions(decimal: true),
                  decoration: const InputDecoration(
                    labelText: 'Abona hoy',
                    prefixText: 'L ',
                    helperText: 'Vacío es que no deja nada.',
                  ),
                ),
                const SizedBox(height: 8),
                OutlinedButton.icon(
                  onPressed: busy ? null : onPickDueDate,
                  icon: const Icon(Icons.event_outlined, size: 18),
                  label: Text(
                    dueDate == null ? 'Fecha de pago' : 'Paga el ${_date(dueDate!)}',
                  ),
                ),
              ],

              const SizedBox(height: 12),
              SizedBox(
                width: double.infinity,
                child: FilledButton.icon(
                  onPressed: busy ? null : onClose,
                  icon: const Icon(Icons.receipt_long_outlined),
                  label: const Text('Facturar y entregar'),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Una opción excluyente. Es un radio dibujado a mano: los `RadioListTile` de Material
/// quedaron atados a un `RadioGroup` ancestro y aquí las opciones no son una lista uniforme.
class _Option extends StatelessWidget {
  const _Option({
    required this.selected,
    required this.title,
    required this.onTap,
    this.subtitle,
  });

  final bool selected;
  final String title;
  final String? subtitle;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return ListTile(
      dense: true,
      contentPadding: EdgeInsets.zero,
      leading: Icon(
        selected ? Icons.radio_button_checked : Icons.radio_button_unchecked,
        color: selected ? theme.colorScheme.primary : theme.colorScheme.onSurfaceVariant,
      ),
      title: Text(title),
      subtitle: subtitle == null ? null : Text(subtitle!),
      onTap: onTap,
    );
  }
}

class _SaleCard extends StatelessWidget {
  const _SaleCard({
    required this.sale,
    required this.busy,
    required this.onAddPayment,
    required this.onRemovePayment,
    required this.onShare,
  });

  final Sale sale;
  final bool busy;
  final VoidCallback onAddPayment;
  final void Function(SalePayment) onRemovePayment;
  final VoidCallback onShare;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return _Section(
      title: 'Factura',
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(child: Text(sale.number, style: theme.textTheme.titleMedium)),
                  Text(_money(sale.total, sale.currency), style: theme.textTheme.titleMedium),
                ],
              ),
              Text(
                '${sale.paymentMethod.label} · ${_date(sale.saleDate)}',
                style: theme.textTheme.bodySmall,
              ),

              // El correlativo del SAR, cuando la factura salió con CAI: es el número con el
              // que existe para hacienda, y el de arriba es el del taller.
              if (sale.fiscalNumber case final fiscal?)
                Text(
                  'Factura fiscal $fiscal',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: theme.colorScheme.primary,
                  ),
                ),

              const SizedBox(height: 8),
              if (sale.balance > 0)
                Text(
                  'Debe ${_money(sale.balance, sale.currency)}'
                  '${sale.dueDate == null ? '' : sale.isOverdue ? ' · venció el ${_date(sale.dueDate!)}' : ' · paga el ${_date(sale.dueDate!)}'}',
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: sale.isOverdue ? theme.colorScheme.error : null,
                  ),
                )
              else
                Text('Pagada', style: theme.textTheme.bodyMedium),

              for (final payment in sale.payments)
                Padding(
                  padding: const EdgeInsets.only(top: 6),
                  child: Row(
                    children: [
                      Expanded(
                        child: Text(
                          '${_date(payment.paidAt)} · ${payment.method.label}'
                          '${payment.reference == null ? '' : ' · ${payment.reference}'}',
                          style: theme.textTheme.bodySmall,
                        ),
                      ),
                      Text(
                        _money(payment.amount, sale.currency),
                        style: theme.textTheme.bodySmall,
                      ),
                      IconButton(
                        tooltip: 'Quitar el abono',
                        icon: const Icon(Icons.close, size: 16),
                        visualDensity: VisualDensity.compact,
                        onPressed: busy ? null : () => onRemovePayment(payment),
                      ),
                    ],
                  ),
                ),

              const SizedBox(height: 8),
              Row(
                children: [
                  if (sale.balance > 0)
                    FilledButton.tonalIcon(
                      onPressed: busy ? null : onAddPayment,
                      icon: const Icon(Icons.payments_outlined, size: 18),
                      label: const Text('Abonar'),
                    ),
                  const Spacer(),
                  TextButton.icon(
                    onPressed: busy ? null : onShare,
                    icon: const Icon(Icons.ios_share, size: 18),
                    label: const Text('Compartir'),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Section extends StatelessWidget {
  const _Section({required this.title, required this.child});

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title.toUpperCase(), style: _labelStyle(theme)),
          const SizedBox(height: 8),
          child,
        ],
      ),
    );
  }
}

TextStyle? _labelStyle(ThemeData theme) => theme.textTheme.labelSmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
      letterSpacing: 0.6,
    );

String _money(double value, String currency) => '$currency ${value.toStringAsFixed(2)}';

String _date(DateTime value) {
  final local = value.toLocal();
  return '${local.day.toString().padLeft(2, '0')}/${local.month.toString().padLeft(2, '0')}';
}

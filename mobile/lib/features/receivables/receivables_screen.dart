import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_client.dart';
import '../../core/api/sale_repository.dart';
import '../../core/api/service_request_repository.dart';
import '../../core/theme/garaj_brand.dart';
import '../reports/reports_screen.dart' show money;

/// Cuentas por cobrar.
///
/// Tiene pantalla propia porque no es un reporte: es trabajo de todos los días. El cliente
/// llama o llega al mostrador, hay que encontrarlo por lo que dicte —su nombre, su teléfono,
/// el número de la factura o el de la orden—, ver cuánto debe y anotarle el abono. En
/// Reportes queda solo el total, que sí es un indicador.
class ReceivablesScreen extends ConsumerStatefulWidget {
  const ReceivablesScreen({super.key});

  @override
  ConsumerState<ReceivablesScreen> createState() => _ReceivablesScreenState();
}

class _ReceivablesScreenState extends ConsumerState<ReceivablesScreen> {
  ReceivableFilter _filter = const ReceivableFilter();

  final _search = TextEditingController();
  Timer? _debounce;

  @override
  void dispose() {
    _debounce?.cancel();
    _search.dispose();
    super.dispose();
  }

  /// Se espera a que deje de escribir: cada pulsación es una consulta a la API, y con la
  /// cobertura de un taller eso es una lista que parpadea.
  void _onSearch(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 400), () {
      if (mounted) setState(() => _filter = _filter.copyWith(search: value));
    });
  }

  Future<void> _collect(Receivable sale) async {
    final registered = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => PaymentSheet(sale: sale),
    );

    if (registered != true) return;
    ref.invalidate(filteredReceivablesProvider(_filter));
    ref.invalidate(saleDetailProvider(sale.id));
  }

  /// Los abonos que ya se le hicieron a esta factura, y desde ahí el estado de cuenta del
  /// cliente: es lo que se le manda cuando pregunta cuánto debe.
  Future<void> _history(Receivable sale) async {
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _HistorySheet(sale: sale),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final sales = ref.watch(filteredReceivablesProvider(_filter));
    final branches = ref.watch(branchOptionsProvider).value ?? const [];

    return Scaffold(
      appBar: AppBar(
        title: const Text('Por cobrar'),
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
                    // Corto a propósito: en un teléfono el resto se corta y un hint que no
                    // se puede leer no orienta a nadie.
                    hintText: 'Cliente, teléfono o número',
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
                      _Chip(
                        label: 'Todas',
                        selected: _filter.overdue == null,
                        onSelected: () => setState(
                          () => _filter = _filter.copyWith(limpiarVencimiento: true),
                        ),
                      ),
                      _Chip(
                        label: 'Vencidas',
                        selected: _filter.overdue == true,
                        onSelected: () =>
                            setState(() => _filter = _filter.copyWith(overdue: true)),
                      ),
                      _Chip(
                        label: 'Por vencer',
                        selected: _filter.overdue == false,
                        onSelected: () => setState(
                          () => _filter = _filter.copyWith(
                            overdue: false,
                            limpiarVencimiento: false,
                          ),
                        ),
                      ),
                      if (branches.length > 1) ...[
                        const VerticalDivider(width: 16),
                        _Chip(
                          label: 'Todas las sucursales',
                          selected: _filter.branchId == null,
                          onSelected: () => setState(
                            () => _filter = _filter.copyWith(limpiarSucursal: true),
                          ),
                        ),
                        for (final branch in branches)
                          _Chip(
                            label: branch.name,
                            selected: _filter.branchId == branch.id,
                            onSelected: () => setState(
                              () => _filter = _filter.copyWith(branchId: branch.id),
                            ),
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
      body: sales.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Text(
              apiErrorMessage(e, 'No se pudo cargar lo que está por cobrar.'),
              textAlign: TextAlign.center,
            ),
          ),
        ),
        data: (items) {
          if (items.isEmpty) {
            return Center(
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Text(
                  _filter.search?.trim().isNotEmpty == true ||
                          _filter.overdue != null ||
                          _filter.branchId != null
                      ? 'Nada con esos filtros.'
                      : 'No hay nada por cobrar. Todo lo facturado está pagado.',
                  textAlign: TextAlign.center,
                  style: theme.textTheme.bodyMedium,
                ),
              ),
            );
          }

          final total = items.fold<double>(0, (sum, s) => sum + s.balance);
          final vencido =
              items.where((s) => s.isOverdue).fold<double>(0, (sum, s) => sum + s.balance);

          return RefreshIndicator(
            onRefresh: () async => ref.invalidate(filteredReceivablesProvider(_filter)),
            child: ListView.builder(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
              // Una fila más: el encabezado con los totales de lo que está a la vista.
              itemCount: items.length + 1,
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
                                '${items.length} '
                                '${items.length == 1 ? 'factura' : 'facturas'} con saldo',
                                style: theme.textTheme.bodySmall,
                              ),
                            ),
                            Text(
                              money(total, 'HNL'),
                              style: theme.textTheme.titleMedium
                                  ?.copyWith(fontFamily: GarajFonts.mono),
                            ),
                          ],
                        ),
                        if (vencido > 0)
                          Text(
                            '${money(vencido, 'HNL')} ya vencido',
                            style: theme.textTheme.bodySmall
                                ?.copyWith(color: theme.colorScheme.error),
                          ),
                      ],
                    ),
                  );
                }

                return _ReceivableCard(
                  sale: items[i - 1],
                  onCollect: _collect,
                  onHistory: _history,
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

class _ReceivableCard extends StatelessWidget {
  const _ReceivableCard({
    required this.sale,
    required this.onCollect,
    required this.onHistory,
  });

  final Receivable sale;
  final Future<void> Function(Receivable sale) onCollect;
  final Future<void> Function(Receivable sale) onHistory;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final detalle = [
      sale.number,
      if (sale.workOrderNumber != null) sale.workOrderNumber!,
      sale.branchName,
    ].join(' · ');

    final vencimiento = sale.dueDate == null
        ? 'sin fecha acordada'
        : sale.isOverdue
            ? 'venció hace ${sale.diasDeAtraso} '
                '${sale.diasDeAtraso == 1 ? 'día' : 'días'}'
            : 'vence ${_date(sale.dueDate!)}';

    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      child: ListTile(
        contentPadding: const EdgeInsets.fromLTRB(14, 6, 8, 6),
        // Tocar la tarjeta abre los abonos: es lo que se busca cuando el cliente discute
        // cuánto ha pagado.
        onTap: () => onHistory(sale),
        title: Text(sale.customerName ?? 'Mostrador'),
        subtitle: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(detalle, style: theme.textTheme.bodySmall),
            Text(
              '$vencimiento · abonado ${money(sale.amountPaid, 'HNL')} '
              'de ${money(sale.total, 'HNL')}',
              style: theme.textTheme.bodySmall?.copyWith(
                color: sale.isOverdue ? theme.colorScheme.error : null,
              ),
            ),
          ],
        ),
        trailing: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Text(
              money(sale.balance, 'HNL'),
              style: theme.textTheme.titleSmall?.copyWith(fontFamily: GarajFonts.mono),
            ),
            TextButton(
              onPressed: () => onCollect(sale),
              style: TextButton.styleFrom(
                padding: EdgeInsets.zero,
                minimumSize: const Size(0, 28),
                tapTargetSize: MaterialTapTargetSize.shrinkWrap,
              ),
              child: const Text('Abonar'),
            ),
          ],
        ),
      ),
    );
  }

  static String _date(DateTime value) {
    final local = value.toLocal();
    return '${local.day.toString().padLeft(2, '0')}/${local.month.toString().padLeft(2, '0')}';
  }
}

/// Los abonos de una factura, y el estado de cuenta del cliente.
///
/// El listado de arriba no trae los abonos —serían todos los de todas las filas en cada
/// carga— así que se piden al abrir esta hoja.
class _HistorySheet extends ConsumerWidget {
  const _HistorySheet({required this.sale});

  final Receivable sale;

  Future<void> _sendStatement(BuildContext context, WidgetRef ref) async {
    try {
      final url = await ref.read(saleRepositoryProvider).statementLink(sale.customerId!);
      await launchUrl(Uri.parse(url), mode: LaunchMode.externalApplication);
    } catch (e) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e, 'No se pudo armar el enlace.'))),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final detail = ref.watch(saleDetailProvider(sale.id));

    return Padding(
      padding: EdgeInsets.only(
        left: 16,
        right: 16,
        top: 16,
        bottom: MediaQuery.of(context).viewInsets.bottom + 24,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Abonos', style: theme.textTheme.titleMedium),
          Text(
            '${sale.customerName ?? 'Mostrador'} · ${sale.number} · '
            'saldo ${money(sale.balance, 'HNL')}',
            style: theme.textTheme.bodySmall,
          ),
          const SizedBox(height: 12),

          detail.when(
            loading: () => const Padding(
              padding: EdgeInsets.symmetric(vertical: 24),
              child: Center(child: CircularProgressIndicator()),
            ),
            error: (e, _) => Text(apiErrorMessage(e, 'No se pudieron cargar los abonos.')),
            data: (venta) => venta.payments.isEmpty
                ? Text(
                    'Todavía no ha abonado nada a esta factura.',
                    style: theme.textTheme.bodyMedium,
                  )
                : Column(
                    children: [
                      for (final pago in venta.payments)
                        ListTile(
                          contentPadding: EdgeInsets.zero,
                          dense: true,
                          title: Text(_fecha(pago.paidAt)),
                          subtitle: Text(
                            [
                              pago.method.label,
                              if (pago.reference != null) pago.reference!,
                            ].join(' · '),
                          ),
                          trailing: Text(
                            money(pago.amount, 'HNL'),
                            style: theme.textTheme.bodyMedium
                                ?.copyWith(fontFamily: GarajFonts.mono),
                          ),
                        ),
                    ],
                  ),
          ),

          const SizedBox(height: 8),

          if (sale.customerId != null) ...[
            FilledButton.icon(
              onPressed: () => _sendStatement(context, ref),
              icon: const Icon(Icons.send_outlined, size: 18),
              label: const Text('Mandar estado de cuenta'),
            ),
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text(
                'Se abre WhatsApp con el enlace. Lleva todas las facturas con saldo de este '
                'cliente, no solo esta.',
                style: theme.textTheme.bodySmall,
              ),
            ),
          ] else
            Text(
              'Venta de mostrador sin cliente en el padrón: no hay a quién mandarle un estado '
              'de cuenta.',
              style: theme.textTheme.bodySmall,
            ),
        ],
      ),
    );
  }

  static String _fecha(DateTime value) {
    final local = value.toLocal();
    return '${local.day.toString().padLeft(2, '0')}/'
        '${local.month.toString().padLeft(2, '0')}/${local.year}';
  }
}

/// Captura de un abono. Propone el saldo completo porque es lo más frecuente —el cliente
/// llega a terminar de pagar— y deja bajarlo si trae menos.
class PaymentSheet extends ConsumerStatefulWidget {
  const PaymentSheet({required this.sale, super.key});

  final Receivable sale;

  @override
  ConsumerState<PaymentSheet> createState() => _PaymentSheetState();
}

class _PaymentSheetState extends ConsumerState<PaymentSheet> {
  late final TextEditingController _amount =
      TextEditingController(text: widget.sale.balance.toStringAsFixed(2));
  final _reference = TextEditingController();

  PaymentMethod _method = PaymentMethod.cash;
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _amount.dispose();
    _reference.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final amount = double.tryParse(_amount.text.trim().replaceAll(',', ''));

    if (amount == null || amount <= 0) {
      setState(() => _error = 'Escriba cuánto abonó.');
      return;
    }
    if (amount > widget.sale.balance) {
      setState(() => _error = 'El abono no puede pasar del saldo.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });

    try {
      await ref.read(saleRepositoryProvider).registerPayment(
            widget.sale.id,
            amount: amount,
            method: _method,
            reference: _reference.text.trim().isEmpty ? null : _reference.text.trim(),
          );

      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      setState(() => _error = apiErrorMessage(e, 'No se pudo registrar el abono.'));
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: EdgeInsets.only(
        left: 16,
        right: 16,
        top: 16,
        bottom: MediaQuery.of(context).viewInsets.bottom + 16,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Abono', style: theme.textTheme.titleMedium),
          Text(
            '${widget.sale.customerName ?? 'Mostrador'} · ${widget.sale.number} · '
            'saldo ${money(widget.sale.balance, 'HNL')}',
            style: theme.textTheme.bodySmall,
          ),
          const SizedBox(height: 16),

          TextField(
            controller: _amount,
            autofocus: true,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: const InputDecoration(labelText: 'Cuánto abona'),
          ),
          const SizedBox(height: 12),

          DropdownButtonFormField<PaymentMethod>(
            initialValue: _method,
            decoration: const InputDecoration(labelText: 'Forma de pago'),
            items: [
              for (final method in PaymentMethod.values)
                DropdownMenuItem(value: method, child: Text(method.label)),
            ],
            onChanged: (value) => setState(() => _method = value ?? PaymentMethod.cash),
          ),
          const SizedBox(height: 12),

          TextField(
            controller: _reference,
            decoration: const InputDecoration(
              labelText: 'Referencia (opcional)',
              hintText: 'Recibo, depósito, transferencia…',
            ),
          ),

          if (_error != null) ...[
            const SizedBox(height: 12),
            Text(_error!, style: TextStyle(color: theme.colorScheme.error)),
          ],

          const SizedBox(height: 20),
          FilledButton(
            onPressed: _saving ? null : _save,
            child: Text(_saving ? 'Guardando…' : 'Registrar abono'),
          ),
        ],
      ),
    );
  }
}

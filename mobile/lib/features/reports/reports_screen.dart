import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/report_repository.dart';
import '../../core/api/sale_repository.dart';
import '../../core/api/service_request_repository.dart';
import '../../core/api/staff_repository.dart';
import '../../core/theme/garaj_brand.dart';

/// Los reportes de ingresos en el teléfono.
///
/// El dueño de un taller pequeño no está sentado frente a una computadora: revisa cómo va el
/// día caminando por el patio o antes de bajar la cortina. Por eso está aquí lo que se mira
/// de verdad —cuánto entró, en qué se repartió y quién lo generó— y no el reporte completo:
/// exportar a CSV o comparar dos rangos se hace en el panel web, con teclado.
class ReportsScreen extends ConsumerStatefulWidget {
  const ReportsScreen({super.key});

  @override
  ConsumerState<ReportsScreen> createState() => _ReportsScreenState();
}

class _ReportsScreenState extends ConsumerState<ReportsScreen> {
  ReportFilter _filter = const ReportFilter();

  static const _ranges = {7: '7 días', 30: '30 días', 90: '90 días'};

  Future<void> _collect(Receivable sale) async {
    final registered = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _PaymentSheet(sale: sale),
    );

    if (registered != true) return;

    ref.invalidate(receivablesProvider(_filter.branchId));
    // El abono no cambia lo facturado, pero sí lo que queda por cobrar en el resumen.
    ref.invalidate(revenueReportProvider(_filter));
  }

  @override
  Widget build(BuildContext context) {
    final report = ref.watch(revenueReportProvider(_filter));
    final receivables = ref.watch(receivablesProvider(_filter.branchId));
    final branches = ref.watch(branchOptionsProvider);
    final technicians = ref.watch(technicianOptionsProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Reportes')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(revenueReportProvider(_filter)),
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 32),
          children: [
            _Filters(
              filter: _filter,
              ranges: _ranges,
              branches: branches.value ?? const [],
              technicians: technicians.value ?? const [],
              onChanged: (next) => setState(() => _filter = next),
            ),
            const SizedBox(height: 20),

            _Receivables(
              sales: receivables.value ?? const [],
              onCollect: _collect,
            ),

            report.when(
              loading: () => const Padding(
                padding: EdgeInsets.symmetric(vertical: 48),
                child: Center(child: CircularProgressIndicator()),
              ),
              error: (e, _) => Padding(
                padding: const EdgeInsets.symmetric(vertical: 48),
                child: Center(
                  child: Text(
                    apiErrorMessage(e, 'No se pudieron cargar los reportes.'),
                    textAlign: TextAlign.center,
                  ),
                ),
              ),
              data: (data) => _Report(report: data),
            ),
          ],
        ),
      ),
    );
  }
}

class _Filters extends StatelessWidget {
  const _Filters({
    required this.filter,
    required this.ranges,
    required this.branches,
    required this.technicians,
    required this.onChanged,
  });

  final ReportFilter filter;
  final Map<int, String> ranges;
  final List<BranchOption> branches;
  final List<TechnicianOption> technicians;
  final ValueChanged<ReportFilter> onChanged;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Wrap(
          spacing: 8,
          children: [
            for (final entry in ranges.entries)
              ChoiceChip(
                label: Text(entry.value),
                selected: filter.days == entry.key,
                onSelected: (_) => onChanged(filter.copyWith(
                  days: entry.key,
                  // La agrupación sigue al rango: 90 días por día son 90 barras de un píxel.
                  groupBy: entry.key >= 90 ? RevenueGrouping.week : RevenueGrouping.day,
                )),
              ),
          ],
        ),
        const SizedBox(height: 8),

        Wrap(
          spacing: 8,
          children: [
            for (final grouping in RevenueGrouping.values)
              ChoiceChip(
                label: Text(grouping.label),
                selected: filter.groupBy == grouping,
                onSelected: (_) => onChanged(filter.copyWith(groupBy: grouping)),
              ),
          ],
        ),
        const SizedBox(height: 12),

        // Solo si hay más de una: con una sola sucursal el selector no decide nada.
        if (branches.length > 1)
          DropdownButtonFormField<String?>(
            initialValue: filter.branchId,
            isExpanded: true,
            decoration: const InputDecoration(labelText: 'Sucursal', isDense: true),
            items: [
              const DropdownMenuItem(value: null, child: Text('Todas las sucursales')),
              for (final branch in branches)
                DropdownMenuItem(value: branch.id, child: Text(branch.name)),
            ],
            onChanged: (value) => onChanged(value == null
                ? filter.copyWith(clearBranch: true)
                : filter.copyWith(branchId: value)),
          ),

        if (technicians.isNotEmpty) ...[
          const SizedBox(height: 8),
          DropdownButtonFormField<String?>(
            initialValue: filter.technicianId,
            isExpanded: true,
            decoration: const InputDecoration(labelText: 'Técnico', isDense: true),
            items: [
              const DropdownMenuItem(value: null, child: Text('Todos los técnicos')),
              for (final technician in technicians)
                DropdownMenuItem(value: technician.id, child: Text(technician.name)),
            ],
            onChanged: (value) => onChanged(value == null
                ? filter.copyWith(clearTechnician: true)
                : filter.copyWith(technicianId: value)),
          ),
        ],
      ],
    );
  }
}

class _Report extends StatelessWidget {
  const _Report({required this.report});

  final RevenueReport report;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('TOTAL FACTURADO', style: _labelStyle(theme)),
                const SizedBox(height: 4),
                Text(
                  money(report.total, report.currency),
                  style: theme.textTheme.headlineSmall?.copyWith(
                    fontFamily: GarajFonts.mono,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 12),
                Row(
                  children: [
                    _Figure(
                      label: 'Repuestos',
                      value: money(report.partsRevenue, report.currency),
                    ),
                    _Figure(
                      label: 'Mano de obra',
                      value: money(report.laborRevenue, report.currency),
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                Row(
                  children: [
                    _Figure(
                      label: 'Margen',
                      value: '${money(report.margin, report.currency)}'
                          ' · ${report.marginPercent.toStringAsFixed(1)}%',
                    ),
                    _Figure(label: 'Ventas', value: '${report.saleCount}'),
                  ],
                ),
              ],
            ),
          ),
        ),

        if (report.points.isEmpty)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 32),
            child: Center(
              child: Text('No hubo ventas en el rango.', style: theme.textTheme.bodyMedium),
            ),
          )
        else ...[
          const SizedBox(height: 20),
          _Chart(points: report.points, currency: report.currency),
        ],

        // El reparto por técnico va primero: es lo nuevo y lo que más se pregunta.
        _Slices(
          title: 'Por técnico',
          slices: report.technicians,
          currency: report.currency,
          note: 'Se atribuye al técnico responsable de la orden. Lo vendido en mostrador '
              'aparece como «Sin técnico».',
        ),

        if (report.branches.length > 1)
          _Slices(
            title: 'Por sucursal',
            slices: report.branches,
            currency: report.currency,
          ),

        if (report.topParts.isNotEmpty) ...[
          const SizedBox(height: 24),
          Text('REPUESTOS MÁS VENDIDOS', style: _labelStyle(theme)),
          const SizedBox(height: 8),
          for (final part in report.topParts.take(5))
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(part.name, style: theme.textTheme.bodyMedium),
                        Text(
                          '${part.sku} · ${quantity(part.quantity)} u',
                          style: theme.textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ),
                  Text(
                    money(part.revenue, report.currency),
                    style: theme.textTheme.bodyMedium?.copyWith(fontFamily: GarajFonts.mono),
                  ),
                ],
              ),
            ),
        ],
      ],
    );
  }
}

/// Lo facturado que todavía no entró en caja.
///
/// Está en el teléfono porque el cobro pasa en el mostrador: el cliente llega a dejar un
/// abono y quien lo atiende tiene el teléfono en la mano, no la computadora de la oficina.
/// Se ordena por vencimiento —es el orden en que hay que cobrar— y lo vencido va en rojo.
class _Receivables extends StatelessWidget {
  const _Receivables({required this.sales, required this.onCollect});

  final List<Receivable> sales;
  final Future<void> Function(Receivable sale) onCollect;

  @override
  Widget build(BuildContext context) {
    if (sales.isEmpty) return const SizedBox.shrink();

    final theme = Theme.of(context);
    final total = sales.fold<double>(0, (sum, s) => sum + s.balance);
    final overdue = sales.where((s) => s.isOverdue).fold<double>(0, (sum, s) => sum + s.balance);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(child: Text('POR COBRAR', style: _labelStyle(theme))),
            Text(
              money(total, 'HNL'),
              style: theme.textTheme.titleMedium?.copyWith(fontFamily: GarajFonts.mono),
            ),
          ],
        ),
        if (overdue > 0)
          Text(
            '${money(overdue, 'HNL')} ya vencido',
            style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error),
          ),
        const SizedBox(height: 8),

        for (final sale in sales.take(10))
          Card(
            margin: const EdgeInsets.only(bottom: 8),
            child: ListTile(
              contentPadding: const EdgeInsets.fromLTRB(14, 6, 8, 6),
              title: Text(sale.customerName ?? 'Mostrador'),
              subtitle: Text(
                [
                  sale.number,
                  if (sale.dueDate != null)
                    '${sale.isOverdue ? 'venció' : 'vence'} ${_date(sale.dueDate!)}'
                  else
                    'sin fecha acordada',
                  'abonado ${money(sale.amountPaid, 'HNL')} de ${money(sale.total, 'HNL')}',
                ].join(' · '),
                style: theme.textTheme.bodySmall?.copyWith(
                  color: sale.isOverdue ? theme.colorScheme.error : null,
                ),
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
          ),

        if (sales.length > 10)
          Text(
            'y ${sales.length - 10} más. El resto se ve en el panel.',
            style: theme.textTheme.bodySmall,
          ),

        const SizedBox(height: 20),
      ],
    );
  }

  static String _date(DateTime value) {
    final local = value.toLocal();
    return '${local.day.toString().padLeft(2, '0')}/${local.month.toString().padLeft(2, '0')}';
  }
}

/// Captura de un abono. Propone el saldo completo porque es lo más frecuente —el cliente
/// llega a terminar de pagar— y deja bajarlo si trae menos.
class _PaymentSheet extends ConsumerStatefulWidget {
  const _PaymentSheet({required this.sale});

  final Receivable sale;

  @override
  ConsumerState<_PaymentSheet> createState() => _PaymentSheetState();
}

class _PaymentSheetState extends ConsumerState<_PaymentSheet> {
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

/// Barras apiladas: repuestos abajo, mano de obra encima. Sin librería de gráficas —son dos
/// rectángulos por periodo y no compensa arrastrar una dependencia entera por eso.
class _Chart extends StatelessWidget {
  const _Chart({required this.points, required this.currency});

  final List<RevenuePoint> points;
  final String currency;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final max = points.fold<double>(1, (m, p) => p.total > m ? p.total : m);

    // Con muchos periodos, las últimas barras son las que interesan: el resto se recorta.
    final visible = points.length > 20 ? points.sublist(points.length - 20) : points;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          height: 140,
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              for (final point in visible)
                // Con pocos periodos las barras se dejan estrechas: repartir el ancho entre
                // dos o tres pinta franjas de pared, no una gráfica.
                if (visible.length < 8)
                  SizedBox(width: 26, child: _Bar(point: point, max: max, currency: currency))
                else
                  Expanded(child: _Bar(point: point, max: max, currency: currency)),
              if (visible.length < 8) const Spacer(),
            ],
          ),
        ),
        const SizedBox(height: 6),
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(visible.first.label, style: theme.textTheme.bodySmall),
            Text(visible.last.label, style: theme.textTheme.bodySmall),
          ],
        ),
        const SizedBox(height: 8),
        Row(
          children: [
            const _Swatch(color: GarajColors.brand),
            const SizedBox(width: 4),
            Text('Repuestos', style: theme.textTheme.bodySmall),
            const SizedBox(width: 14),
            const _Swatch(color: GarajColors.warning),
            const SizedBox(width: 4),
            Text('Mano de obra', style: theme.textTheme.bodySmall),
          ],
        ),
      ],
    );
  }
}

class _Bar extends StatelessWidget {
  const _Bar({required this.point, required this.max, required this.currency});

  final RevenuePoint point;
  final double max;
  final String currency;

  @override
  Widget build(BuildContext context) {
    return Tooltip(
      message: '${point.label}: ${money(point.total, currency)}',
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 1.5),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.end,
          children: [
            Container(
              height: 120 * (point.laborRevenue / max),
              decoration: const BoxDecoration(
                color: GarajColors.warning,
                borderRadius: BorderRadius.vertical(top: Radius.circular(2)),
              ),
            ),
            Container(
              height: 120 * (point.partsRevenue / max),
              color: GarajColors.brand,
            ),
          ],
        ),
      ),
    );
  }
}

class _Swatch extends StatelessWidget {
  const _Swatch({required this.color});

  final Color color;

  @override
  Widget build(BuildContext context) => Container(
        width: 10,
        height: 10,
        decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(2)),
      );
}

/// Reparto en barras horizontales. Sirve igual para sucursales y para técnicos: la pregunta
/// es la misma —cuánto de este total salió de aquí— y la proporción se ve de un vistazo.
class _Slices extends StatelessWidget {
  const _Slices({
    required this.title,
    required this.slices,
    required this.currency,
    this.note,
  });

  final String title;
  final List<RevenueSlice> slices;
  final String currency;
  final String? note;

  @override
  Widget build(BuildContext context) {
    if (slices.isEmpty) return const SizedBox.shrink();

    final theme = Theme.of(context);
    final max = slices.fold<double>(1, (m, s) => s.total > m ? s.total : m);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SizedBox(height: 24),
        Text(title.toUpperCase(), style: _labelStyle(theme)),
        const SizedBox(height: 10),
        for (final slice in slices)
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(child: Text(slice.name, style: theme.textTheme.bodyMedium)),
                    Text(
                      money(slice.total, currency),
                      style: theme.textTheme.bodyMedium?.copyWith(
                        fontFamily: GarajFonts.mono,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 4),
                ClipRRect(
                  borderRadius: BorderRadius.circular(3),
                  child: LinearProgressIndicator(
                    value: slice.total / max,
                    minHeight: 6,
                    backgroundColor: theme.colorScheme.surfaceContainerHighest,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  '${slice.saleCount} venta(s) · repuestos ${money(slice.partsRevenue, currency)}'
                  ' · mano de obra ${money(slice.laborRevenue, currency)}',
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ),
          ),
        if (note != null)
          Text(note!, style: theme.textTheme.bodySmall),
      ],
    );
  }
}

class _Figure extends StatelessWidget {
  const _Figure({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Expanded(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: theme.textTheme.bodySmall),
          Text(
            value,
            style: theme.textTheme.bodyMedium?.copyWith(fontFamily: GarajFonts.mono),
          ),
        ],
      ),
    );
  }
}

TextStyle? _labelStyle(ThemeData theme) => theme.textTheme.labelSmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
      letterSpacing: 0.6,
    );

/// Lempiras con separador de miles y sin centavos: en un reporte los centavos son ruido y
/// en una pantalla de teléfono, además, ocupan sitio que hace falta.
String money(double value, String currency) {
  final rounded = value.round().abs().toString();
  final buffer = StringBuffer();

  for (var i = 0; i < rounded.length; i++) {
    if (i > 0 && (rounded.length - i) % 3 == 0) buffer.write(',');
    buffer.write(rounded[i]);
  }

  return '${_simbolo(currency)} ${value < 0 ? '−' : ''}$buffer';
}

/// El símbolo, no el código ISO: «L 1,000» es como se escribe un precio en el taller, y es lo
/// que ya imprimen la factura y la cotización.
String _simbolo(String currency) => switch (currency) {
      'HNL' => 'L',
      'USD' => '\$',
      _ => currency,
    };

String quantity(double value) =>
    value == value.roundToDouble() ? value.round().toString() : value.toStringAsFixed(2);

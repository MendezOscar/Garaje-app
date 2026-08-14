import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/report_repository.dart';
import '../../core/theme/garaj_brand.dart';

/// Cierre de caja: lo **cobrado** en el día.
///
/// Está en el teléfono porque el cierre se hace en el taller, con el cajón abierto y el dinero
/// en la mano, no delante de una computadora. Lo primero que se ve es el total por forma de
/// pago, que es lo que se compara con el efectivo; el detalle va abajo, para buscar la
/// diferencia cuando no cuadra.
///
/// No es lo mismo que los ingresos de Reportes, que son lo **facturado**: una venta a crédito
/// suma allí el día que se emite y aquí el día que el cliente paga.
class CashCloseScreen extends ConsumerStatefulWidget {
  const CashCloseScreen({super.key});

  @override
  ConsumerState<CashCloseScreen> createState() => _CashCloseScreenState();
}

class _CashCloseScreenState extends ConsumerState<CashCloseScreen> {
  /// El día que se está mirando. Null es hoy, que es el caso de siempre.
  DateTime? _day;

  Future<void> _pickDay() async {
    final hoy = DateTime.now();

    final elegido = await showDatePicker(
      context: context,
      initialDate: _day ?? hoy,
      // Un cierre de caja se consulta hacia atrás: hacia adelante no hay nada que cuadrar.
      firstDate: DateTime(hoy.year - 2),
      lastDate: hoy,
      helpText: 'Cierre de qué día',
    );

    if (elegido != null) setState(() => _day = elegido);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cierre = ref.watch(cashCloseProvider(_day));

    return Scaffold(
      appBar: AppBar(
        title: const Text('Cierre de caja'),
        actions: [
          IconButton(
            tooltip: 'Elegir el día',
            icon: const Icon(Icons.calendar_today_outlined),
            onPressed: _pickDay,
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(cashCloseProvider(_day)),
        child: cierre.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(
            padding: const EdgeInsets.all(24),
            children: [Text(apiErrorMessage(e, 'No se pudo cargar el cierre de caja.'))],
          ),
          data: (data) => ListView(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 32),
            children: [
              Text(data.dayLabel, style: theme.textTheme.bodySmall),
              const SizedBox(height: 4),
              Text(
                _money(data.total, data.currency),
                style: theme.textTheme.displaySmall?.copyWith(fontFamily: GarajFonts.mono),
              ),
              Text(
                '${data.paymentCount} abono(s) cobrados'
                '${data.branchName == null ? '' : ' · ${data.branchName}'}',
                style: theme.textTheme.bodySmall,
              ),
              const SizedBox(height: 24),

              if (data.paymentCount == 0)
                Text(
                  'No se recibió ningún pago este día.',
                  style: theme.textTheme.bodyMedium,
                )
              else ...[
                _Slices(
                  title: 'POR FORMA DE PAGO',
                  slices: data.byMethod,
                  currency: data.currency,
                ),
                _Slices(
                  title: 'QUIÉN LO RECIBIÓ',
                  slices: data.byReceiver,
                  currency: data.currency,
                ),
                Text('DETALLE', style: _label(theme)),
                const SizedBox(height: 4),
                for (final payment in data.payments)
                  _PaymentRow(payment: payment, currency: data.currency),
              ],

              if (data.voidedCount > 0) ...[
                const SizedBox(height: 20),
                Text(
                  'Se dejaron fuera ${data.voidedCount} abono(s) por '
                  '${_money(data.voidedAmount, data.currency)} de ventas anuladas.',
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _Slices extends StatelessWidget {
  const _Slices({required this.title, required this.slices, required this.currency});

  final String title;
  final List<CashCloseSlice> slices;
  final String currency;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(title, style: _label(theme)),
        const SizedBox(height: 4),
        for (final slice in slices)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 3),
            child: Row(
              children: [
                Expanded(child: Text(slice.label)),
                Text('${slice.count}', style: theme.textTheme.bodySmall),
                const SizedBox(width: 12),
                Text(
                  _money(slice.total, currency),
                  style: theme.textTheme.titleSmall?.copyWith(fontFamily: GarajFonts.mono),
                ),
              ],
            ),
          ),
        const SizedBox(height: 20),
      ],
    );
  }
}

class _PaymentRow extends StatelessWidget {
  const _PaymentRow({required this.payment, required this.currency});

  final CashClosePayment payment;
  final String currency;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final local = payment.paidAt.toLocal();
    final hora = '${local.hour.toString().padLeft(2, '0')}:'
        '${local.minute.toString().padLeft(2, '0')}';

    final detalle = [
      payment.saleNumber,
      payment.method.label,
      payment.receiverName,
      if (payment.reference != null && payment.reference!.isNotEmpty) payment.reference!,
    ].join(' · ');

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 5),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 44,
            child: Text(hora, style: theme.textTheme.bodySmall),
          ),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(payment.customerName ?? 'Mostrador'),
                Text(detalle, style: theme.textTheme.bodySmall),
              ],
            ),
          ),
          const SizedBox(width: 8),
          Text(
            _money(payment.amount, currency),
            style: theme.textTheme.titleSmall?.copyWith(fontFamily: GarajFonts.mono),
          ),
        ],
      ),
    );
  }
}

TextStyle? _label(ThemeData theme) => theme.textTheme.labelSmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
      letterSpacing: 0.6,
    );

/// Con centavos, al contrario que en los reportes: aquí se está contando dinero de verdad y
/// dos lempiras de diferencia son justo lo que se anda buscando.
String _money(double value, String currency) {
  final simbolo = switch (currency) {
    'HNL' => 'L',
    'USD' => '\$',
    _ => currency,
  };

  final partes = value.abs().toStringAsFixed(2).split('.');
  final enteros = partes[0];
  final buffer = StringBuffer();

  for (var i = 0; i < enteros.length; i++) {
    if (i > 0 && (enteros.length - i) % 3 == 0) buffer.write(',');
    buffer.write(enteros[i]);
  }

  return '$simbolo ${value < 0 ? '−' : ''}$buffer.${partes[1]}';
}

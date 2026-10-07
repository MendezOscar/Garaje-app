import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/expense_repository.dart';
import '../../core/api/sale_repository.dart' show PaymentMethod;
import '../../core/api/service_request_repository.dart'
    show BranchOption, branchOptionsProvider;
import '../../core/theme/garaj_brand.dart';
import '../../core/models/media.dart';
import '../reports/reports_screen.dart' show money;
import '../work_orders/photo_gallery.dart';

/// Gastos del taller y lo que dejó el mes.
///
/// En el teléfono porque el gasto se hace en la calle —el recibo de la ferretería, el pago
/// del alquiler— y lo que no se anota en el momento no se anota nunca.
class ExpensesScreen extends ConsumerStatefulWidget {
  const ExpensesScreen({super.key});

  @override
  ConsumerState<ExpensesScreen> createState() => _ExpensesScreenState();
}

class _ExpensesScreenState extends ConsumerState<ExpensesScreen> {
  bool _busy = false;

  /// El mes que se está mirando, siempre el día 1. Arranca en el corriente.
  late DateTime _mes = _primeroDeEsteMes();

  static DateTime _primeroDeEsteMes() {
    final hoy = DateTime.now();
    return DateTime(hoy.year, hoy.month);
  }

  bool get _esEsteMes => _mes == _primeroDeEsteMes();

  void _mover(int meses) {
    final destino = DateTime(_mes.year, _mes.month + meses);
    // Hacia adelante no se pasa del mes corriente: no hay resultados del mes que viene.
    if (destino.isAfter(_primeroDeEsteMes())) return;
    setState(() => _mes = destino);
  }

  Future<void> _registrar() async {
    // Con `ref.read` sobre un proveedor autoDispose que nadie estaba mirando, la lista venía
    // vacía y el botón se iba por el `return` sin decir nada: tocarlo no hacía absolutamente
    // nada. Pidiéndole el futuro se carga aquí mismo la primera vez.
    final List<BranchOption> branches;
    try {
      branches = await ref.read(branchOptionsProvider.future);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e, 'No se pudieron cargar las sucursales.'))),
        );
      }
      return;
    }

    if (branches.isEmpty) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('El taller no tiene sucursales donde anotar el gasto.')),
        );
      }
      return;
    }

    if (!mounted) return;

    final gasto = await showModalBottomSheet<_NuevoGasto>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _FormularioGasto(sucursales: branches),
    );

    if (gasto == null) return;

    setState(() => _busy = true);
    try {
      final creado = await ref.read(expenseRepositoryProvider).create(
            branchId: gasto.branchId,
            category: gasto.category,
            description: gasto.description,
            amount: gasto.amount,
            paymentMethod: gasto.paymentMethod,
            supplierName: gasto.supplierName,
          );

      ref.invalidate(expensesProvider);
      ref.invalidate(incomeStatementProvider);
      ref.invalidate(expensesOfMonthProvider(_mes));
      ref.invalidate(incomeStatementOfMonthProvider(_mes));

      // El comprobante se adjunta ahora o no se adjunta: es el único momento en que el papel
      // está en la mano.
      if (mounted) await _comprobante(creado);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e, 'No se pudo registrar el gasto.'))),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// El comprobante del gasto, en una hoja: se toma la foto del recibo y se cierra.
  Future<void> _comprobante(Expense gasto) async {
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      builder: (_) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 16),
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  gasto.description,
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                Text(
                  '${money(gasto.amount, 'HNL')} · ${gasto.category.label}',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                const SizedBox(height: 12),
                PhotoGallery(
                  ownerId: gasto.id,
                  ownerType: MediaOwnerType.expense,
                  canEdit: true,
                  titulo: 'COMPROBANTE',
                  vacioPropio: 'Tome una foto del recibo: un gasto sin comprobante se puede '
                      'discutir.',
                  vacioAjeno: 'Sin comprobante.',
                ),
              ],
            ),
          ),
        ),
      ),
    );

    ref.invalidate(expensesProvider);
    ref.invalidate(expensesOfMonthProvider(_mes));
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final resultado = ref.watch(incomeStatementOfMonthProvider(_mes));
    final gastos = ref.watch(expensesOfMonthProvider(_mes));

    return Scaffold(
      appBar: AppBar(title: const Text('Resultados y gastos')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _busy ? null : _registrar,
        icon: const Icon(Icons.add),
        label: const Text('Gasto'),
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          ref.invalidate(incomeStatementOfMonthProvider(_mes));
          ref.invalidate(expensesOfMonthProvider(_mes));
        },
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 96),
          children: [
            // El mes que se mira, con las flechas para ir atrás. Sin esto, el dueño solo
            // podía ver el mes corriente: el día 2 del mes, el taller entero parecía vacío.
            _BarraDeMes(
              mes: _mes,
              esEsteMes: _esEsteMes,
              onAnterior: () => _mover(-1),
              onSiguiente: _esEsteMes ? null : () => _mover(1),
              onEsteMes: _esEsteMes ? null : () => setState(() => _mes = _primeroDeEsteMes()),
            ),
            const SizedBox(height: 12),

            if (resultado.value case final r?) ...[
              Text(_rotuloDelMes(_mes, _esEsteMes), style: _rotulo(theme)),
              const SizedBox(height: 6),
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(14),
                  child: Column(
                    children: [
                      _Renglon(etiqueta: 'Ingresos', valor: money(r.revenue, r.currency)),
                      _Renglon(
                        etiqueta: 'Costo de lo vendido',
                        valor: '−${money(r.costOfSales, r.currency)}',
                      ),
                      const Divider(height: 16),
                      _Renglon(
                        etiqueta: 'Utilidad bruta',
                        valor: money(r.grossProfit, r.currency),
                        fuerte: true,
                      ),
                      _Renglon(
                        etiqueta: 'Gastos',
                        valor: '−${money(r.expenseTotal, r.currency)}',
                      ),
                      const Divider(height: 16),
                      _Renglon(
                        etiqueta: 'Utilidad neta',
                        valor: '${money(r.netProfit, r.currency)} · '
                            '${r.netMarginPercent.toStringAsFixed(1)}%',
                        fuerte: true,
                        rojo: r.netProfit < 0,
                      ),

                      // Un número suelto no dice si el mes fue bueno: dice cuánto quedó. La
                      // comparación es la que contesta la pregunta de verdad.
                      if (r.previousNetProfit case final anterior?) ...[
                        const SizedBox(height: 6),
                        _Comparacion(
                          actual: r.netProfit,
                          anterior: anterior,
                          currency: r.currency,
                        ),
                      ],
                    ],
                  ),
                ),
              ),
              if (r.expenses.isEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Text(
                    'Sin gastos registrados en este mes, así que la utilidad neta es la '
                    'bruta. Anote alquiler, salarios y servicios para que el número sea real.',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                  ),
                ),
            ],

            const SizedBox(height: 20),
            Text('GASTOS DE ${_mesEnPalabras(_mes).toUpperCase()}', style: _rotulo(theme)),
            const SizedBox(height: 6),

            if (gastos.value case final lista?)
              if (lista.isEmpty)
                Text('Ninguno registrado en este mes.', style: theme.textTheme.bodySmall)
              else
                for (final gasto in lista)
                  ListTile(
                    contentPadding: EdgeInsets.zero,
                    dense: true,
                    title: Text(gasto.description),
                    subtitle: Text(
                      '${gasto.category.label} · ${_fecha(gasto.expenseDate)}'
                      '${gasto.supplierName != null ? ' · ${gasto.supplierName}' : ''}',
                    ),
                    trailing: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(
                          money(gasto.amount, 'HNL'),
                          style: theme.textTheme.bodyMedium?.copyWith(
                            fontFamily: GarajFonts.mono,
                          ),
                        ),
                        const SizedBox(width: 6),
                        Icon(
                          gasto.photoCount > 0
                              ? Icons.receipt_long
                              : Icons.receipt_long_outlined,
                          size: 18,
                          color: gasto.photoCount > 0
                              ? theme.colorScheme.primary
                              : theme.colorScheme.onSurfaceVariant,
                        ),
                      ],
                    ),
                    onTap: () => _comprobante(gasto),
                  )
            else if (gastos.isLoading)
              const Center(child: Padding(padding: EdgeInsets.all(24), child: CircularProgressIndicator())),
          ],
        ),
      ),
    );
  }
}

class _Renglon extends StatelessWidget {
  const _Renglon({
    required this.etiqueta,
    required this.valor,
    this.fuerte = false,
    this.rojo = false,
  });

  final String etiqueta;
  final String valor;
  final bool fuerte;
  final bool rojo;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final estilo = fuerte ? theme.textTheme.titleSmall : theme.textTheme.bodyMedium;

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 2),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(etiqueta, style: estilo),
          Text(
            valor,
            style: estilo?.copyWith(
              fontFamily: GarajFonts.mono,
              color: rojo ? theme.colorScheme.error : null,
            ),
          ),
        ],
      ),
    );
  }
}

/// Lo que se devuelve del formulario, ya validado.
class _NuevoGasto {
  const _NuevoGasto({
    required this.branchId,
    required this.category,
    required this.description,
    required this.amount,
    required this.paymentMethod,
    this.supplierName,
  });

  final String branchId;
  final ExpenseCategory category;
  final String description;
  final double amount;
  final PaymentMethod paymentMethod;
  final String? supplierName;
}

class _FormularioGasto extends StatefulWidget {
  const _FormularioGasto({required this.sucursales});

  final List<BranchOption> sucursales;

  @override
  State<_FormularioGasto> createState() => _FormularioGastoState();
}

class _FormularioGastoState extends State<_FormularioGasto> {
  final _concepto = TextEditingController();
  final _monto = TextEditingController();
  final _proveedor = TextEditingController();

  late String _branchId = widget.sucursales.first.id;
  ExpenseCategory _categoria = ExpenseCategory.other;
  PaymentMethod _metodo = PaymentMethod.cash;

  @override
  void dispose() {
    _concepto.dispose();
    _monto.dispose();
    _proveedor.dispose();
    super.dispose();
  }

  void _guardar() {
    final concepto = _concepto.text.trim();
    final monto = double.tryParse(_monto.text.trim().replaceAll(',', '.'));
    if (concepto.isEmpty || monto == null || monto <= 0) return;

    Navigator.pop(
      context,
      _NuevoGasto(
        branchId: _branchId,
        category: _categoria,
        description: concepto,
        amount: monto,
        paymentMethod: _metodo,
        supplierName: _proveedor.text.trim().isEmpty ? null : _proveedor.text.trim(),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.only(
          left: 16,
          right: 16,
          top: 12,
          bottom: MediaQuery.of(context).viewInsets.bottom + 16,
        ),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Registrar un gasto', style: theme.textTheme.titleMedium),
              const SizedBox(height: 12),

              DropdownButtonFormField<ExpenseCategory>(
                initialValue: _categoria,
                decoration: const InputDecoration(labelText: 'Categoría', isDense: true),
                items: [
                  for (final c in ExpenseCategory.values)
                    DropdownMenuItem(value: c, child: Text(c.label)),
                ],
                onChanged: (value) => setState(() => _categoria = value ?? _categoria),
              ),
              const SizedBox(height: 8),

              TextField(
                controller: _concepto,
                autofocus: true,
                textCapitalization: TextCapitalization.sentences,
                maxLength: 300,
                decoration: const InputDecoration(labelText: 'En qué se gastó'),
              ),

              TextField(
                controller: _monto,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                decoration: const InputDecoration(labelText: 'Monto', prefixText: 'L '),
              ),
              const SizedBox(height: 8),

              TextField(
                controller: _proveedor,
                textCapitalization: TextCapitalization.words,
                maxLength: 200,
                decoration: const InputDecoration(labelText: 'A quién se le pagó (opcional)'),
              ),

              DropdownButtonFormField<PaymentMethod>(
                initialValue: _metodo,
                decoration: const InputDecoration(labelText: 'Forma de pago', isDense: true),
                items: [
                  for (final m in PaymentMethod.values)
                    DropdownMenuItem(value: m, child: Text(m.label)),
                ],
                onChanged: (value) => setState(() => _metodo = value ?? _metodo),
              ),

              if (widget.sucursales.length > 1) ...[
                const SizedBox(height: 8),
                DropdownButtonFormField<String>(
                  initialValue: _branchId,
                  decoration: const InputDecoration(labelText: 'Sucursal', isDense: true),
                  items: [
                    for (final b in widget.sucursales)
                      DropdownMenuItem(value: b.id, child: Text(b.name)),
                  ],
                  onChanged: (value) => setState(() => _branchId = value ?? _branchId),
                ),
              ],

              const SizedBox(height: 8),
              Text(
                'La compra de repuestos para bodega no va aquí: eso es inventario, y se vuelve '
                'costo cuando se vende.',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),

              const SizedBox(height: 12),
              Align(
                alignment: Alignment.centerRight,
                child: FilledButton(onPressed: _guardar, child: const Text('Registrar')),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

TextStyle? _rotulo(ThemeData theme) => theme.textTheme.labelSmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
      letterSpacing: 0.6,
    );

String _fecha(DateTime value) {
  final local = value.toLocal();
  return '${local.day.toString().padLeft(2, '0')}/${local.month.toString().padLeft(2, '0')}';
}

/// Las flechas para moverse de mes. Hacia adelante se apaga en el mes corriente: no hay
/// resultados del mes que viene.
class _BarraDeMes extends StatelessWidget {
  const _BarraDeMes({
    required this.mes,
    required this.esEsteMes,
    required this.onAnterior,
    required this.onSiguiente,
    required this.onEsteMes,
  });

  final DateTime mes;
  final bool esEsteMes;
  final VoidCallback onAnterior;
  final VoidCallback? onSiguiente;
  final VoidCallback? onEsteMes;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Row(
      children: [
        IconButton(
          onPressed: onAnterior,
          icon: const Icon(Icons.chevron_left),
          tooltip: 'Mes anterior',
        ),
        Expanded(
          child: Text(
            _mesEnPalabras(mes),
            textAlign: TextAlign.center,
            style: theme.textTheme.titleMedium,
          ),
        ),
        IconButton(
          onPressed: onSiguiente,
          icon: const Icon(Icons.chevron_right),
          tooltip: 'Mes siguiente',
        ),
        if (!esEsteMes)
          TextButton(onPressed: onEsteMes, child: const Text('Hoy')),
      ],
    );
  }
}

/// Cuánto mejor o peor que el mes pasado.
class _Comparacion extends StatelessWidget {
  const _Comparacion({
    required this.actual,
    required this.anterior,
    required this.currency,
  });

  final double actual;
  final double anterior;
  final String currency;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final diferencia = actual - anterior;

    // Sin mes anterior con el que comparar, el porcentaje sería una división por cero y
    // además no significaría nada.
    final porcentaje = anterior == 0
        ? null
        : (diferencia / anterior.abs() * 100).toStringAsFixed(0);

    final mejor = diferencia >= 0;
    final color = mejor ? GarajColors.successText : theme.colorScheme.error;

    final texto = diferencia == 0
        ? 'Igual que el mes pasado.'
        : '${mejor ? '+' : '−'}${money(diferencia.abs(), currency)}'
            '${porcentaje == null ? '' : ' · $porcentaje%'} que el mes pasado';

    return Row(
      children: [
        Icon(
          diferencia == 0
              ? Icons.remove
              : mejor
                  ? Icons.trending_up
                  : Icons.trending_down,
          size: 16,
          color: color,
        ),
        const SizedBox(width: 6),
        Expanded(
          child: Text(texto, style: theme.textTheme.bodySmall?.copyWith(color: color)),
        ),
      ],
    );
  }
}

const _meses = [
  'enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio',
  'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre',
];

String _mesEnPalabras(DateTime mes) {
  final nombre = _meses[mes.month - 1];
  final delMismoAno = mes.year == DateTime.now().year;
  return delMismoAno ? nombre : '$nombre ${mes.year}';
}

String _rotuloDelMes(DateTime mes, bool esEsteMes) =>
    esEsteMes ? 'ESTE MES' : _mesEnPalabras(mes).toUpperCase();

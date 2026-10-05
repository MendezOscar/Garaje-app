import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/expense_repository.dart';
import '../../core/api/sale_repository.dart' show PaymentMethod;
import '../../core/api/service_request_repository.dart'
    show BranchOption, branchOptionsProvider;
import '../../core/theme/garaj_brand.dart';
import '../reports/reports_screen.dart' show money;

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

  Future<void> _registrar() async {
    final branches = ref.read(branchOptionsProvider).value ?? const [];
    if (branches.isEmpty) return;

    final gasto = await showModalBottomSheet<_NuevoGasto>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _FormularioGasto(sucursales: branches),
    );

    if (gasto == null) return;

    setState(() => _busy = true);
    try {
      await ref.read(expenseRepositoryProvider).create(
            branchId: gasto.branchId,
            category: gasto.category,
            description: gasto.description,
            amount: gasto.amount,
            paymentMethod: gasto.paymentMethod,
            supplierName: gasto.supplierName,
          );

      ref.invalidate(expensesProvider);
      ref.invalidate(incomeStatementProvider);
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

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final resultado = ref.watch(incomeStatementProvider);
    final gastos = ref.watch(expensesProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Resultados y gastos')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _busy ? null : _registrar,
        icon: const Icon(Icons.add),
        label: const Text('Gasto'),
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          ref.invalidate(incomeStatementProvider);
          ref.invalidate(expensesProvider);
        },
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 96),
          children: [
            if (resultado.value case final r?) ...[
              Text('ESTE MES', style: _rotulo(theme)),
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
                    ],
                  ),
                ),
              ),
              if (r.expenses.isEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Text(
                    'Sin gastos registrados este mes, así que la utilidad neta es la bruta. '
                    'Anote alquiler, salarios y servicios para que el número sea real.',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                  ),
                ),
            ],

            const SizedBox(height: 20),
            Text('GASTOS DEL MES', style: _rotulo(theme)),
            const SizedBox(height: 6),

            if (gastos.value case final lista?)
              if (lista.isEmpty)
                Text('Ninguno registrado todavía.', style: theme.textTheme.bodySmall)
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
                    trailing: Text(
                      money(gasto.amount, 'HNL'),
                      style: theme.textTheme.bodyMedium?.copyWith(
                        fontFamily: GarajFonts.mono,
                      ),
                    ),
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

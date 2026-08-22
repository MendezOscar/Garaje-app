import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/customer_repository.dart';
import '../../core/api/inventory_repository.dart';
import '../../core/api/sale_repository.dart';
import '../../core/api/service_request_repository.dart';
import '../../core/api/tenant_repository.dart';
import '../../core/models/inventory.dart';
import '../../core/theme/garaj_brand.dart';
import '../reports/reports_screen.dart' show money;

/// Vender un repuesto sin recibir el vehículo.
///
/// Alguien entra por un filtro y se va: no hay orden que abrir, y hasta ahora la única forma
/// de registrarlo era inventarle una orden a una moto que nunca entró, o no registrarlo —y
/// entonces el repuesto sale de la bodega sin que la caja lo sepa—.
///
/// Se arma desde las existencias de la sucursal y no desde el catálogo: en el mostrador lo que
/// importa es lo que hay para entregar hoy, con su precio y cuánto queda.
class CounterSaleScreen extends ConsumerStatefulWidget {
  const CounterSaleScreen({super.key});

  @override
  ConsumerState<CounterSaleScreen> createState() => _CounterSaleScreenState();
}

class _CounterSaleScreenState extends ConsumerState<CounterSaleScreen> {
  String? _branchId;
  final _lineas = <_Linea>[];

  /// A quién se le vende. Opcional a propósito: obligar a crear una ficha para venderle un
  /// empaque de L 40 es lo que hace que la venta no se registre. Hace falta solo para
  /// facturarle con su RTN o para dejarle la compra en su historial.
  Customer? _cliente;

  PaymentMethod _metodo = PaymentMethod.cash;
  bool _fiscal = false;

  final _rtn = TextEditingController();
  final _aNombreDe = TextEditingController();
  final _nota = TextEditingController();

  bool _busy = false;

  /// La venta ya hecha. Se queda a la vista para mandar el comprobante y empezar otra.
  Sale? _hecha;

  @override
  void dispose() {
    _rtn.dispose();
    _aNombreDe.dispose();
    _nota.dispose();
    super.dispose();
  }

  double get _base =>
      _lineas.fold<double>(0, (total, l) => total + (l.cantidad * l.precio - l.descuento));

  List<_Linea> get _sinExistencia =>
      _lineas.where((l) => l.cantidad > l.disponible).toList();

  Future<void> _buscarRepuesto() async {
    final branchId = _branchId;
    if (branchId == null) return;

    final elegido = await showModalBottomSheet<StockItem>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _BuscarRepuesto(branchId: branchId),
    );

    if (elegido == null) return;

    setState(() {
      final ya = _lineas.where((l) => l.partId == elegido.partId).firstOrNull;
      if (ya != null) {
        ya.cantidad += 1;
      } else {
        _lineas.add(_Linea(
          partId: elegido.partId,
          nombre: elegido.partName,
          sku: elegido.sku,
          unidad: elegido.unit,
          disponible: elegido.quantity,
          precio: elegido.salePrice,
        ));
      }
    });
  }

  Future<void> _buscarCliente() async {
    final elegido = await showModalBottomSheet<Customer>(
      context: context,
      isScrollControlled: true,
      builder: (_) => const _BuscarCliente(),
    );

    if (elegido == null) return;

    setState(() {
      _cliente = elegido;
      // La factura sale con lo que tenga su ficha, y se puede cambiar para esta venta.
      _rtn.text = elegido.taxId ?? '';
      _aNombreDe.text = elegido.billingName ?? elegido.fullName;
    });
  }

  Future<void> _registrar(double tasa) async {
    final branchId = _branchId;
    if (branchId == null || _lineas.isEmpty || _sinExistencia.isNotEmpty) return;

    setState(() => _busy = true);
    try {
      final venta = await ref.read(saleRepositoryProvider).createCounterSale(
            branchId: branchId,
            paymentMethod: _metodo,
            customerId: _cliente?.id,
            notes: _nota.text.trim().isEmpty ? null : _nota.text.trim(),
            fiscal: _fiscal,
            customerTaxId: _fiscal && _rtn.text.trim().isNotEmpty ? _rtn.text.trim() : null,
            customerName: _fiscal && _aNombreDe.text.trim().isNotEmpty
                ? _aNombreDe.text.trim()
                : null,
            lines: [
              for (final l in _lineas)
                CounterSaleLine(
                  partId: l.partId,
                  quantity: l.cantidad,
                  unitPrice: l.precio,
                  discount: l.descuento,
                ),
            ],
          );

      // La existencia bajó y hay una venta más: las dos listas que la enseñan quedan viejas.
      ref.invalidate(stockProvider);
      setState(() => _hecha = venta);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e, 'No se pudo registrar la venta.'))),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _otra() {
    setState(() {
      _hecha = null;
      _lineas.clear();
      _cliente = null;
      _fiscal = false;
      _rtn.clear();
      _aNombreDe.clear();
      _nota.clear();
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final branches = ref.watch(branchOptionsProvider).value ?? const [];

    // La primera sucursal mientras no se elija otra: en un taller de una sola, elegirla sería
    // un paso que no decide nada.
    _branchId ??= branches.isEmpty ? null : branches.first.id;

    final tasa = ref.watch(taxRateProvider).value ?? 0;
    final rango = _branchId == null
        ? null
        : ref.watch(branchFiscalRangeProvider(_branchId!)).value;

    final impuesto = _fiscal ? _base * tasa / 100 : 0.0;
    final total = _base + impuesto;

    return Scaffold(
      appBar: AppBar(title: const Text('Vender repuesto')),
      body: _hecha != null
          ? _Hecha(venta: _hecha!, onOtra: _otra)
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 120),
              children: [
                if (branches.length > 1)
                  DropdownButtonFormField<String>(
                    initialValue: _branchId,
                    decoration: const InputDecoration(
                      labelText: 'Sucursal',
                      isDense: true,
                      border: OutlineInputBorder(),
                    ),
                    items: [
                      for (final branch in branches)
                        DropdownMenuItem(value: branch.id, child: Text(branch.name)),
                    ],
                    // Las existencias son de la sucursal: cambiarla invalida lo que había.
                    onChanged: _busy
                        ? null
                        : (value) => setState(() {
                              _branchId = value;
                              _lineas.clear();
                              _fiscal = false;
                            }),
                  ),

                const SizedBox(height: 16),
                Text('QUÉ SE VENDE', style: _rotulo(theme)),
                const SizedBox(height: 6),
                for (final linea in _lineas)
                  _LineaCard(
                    linea: linea,
                    onCambio: () => setState(() {}),
                    onQuitar: () => setState(() => _lineas.remove(linea)),
                  ),
                OutlinedButton.icon(
                  onPressed: _busy || _branchId == null ? null : _buscarRepuesto,
                  icon: const Icon(Icons.add),
                  label: Text(_lineas.isEmpty ? 'Buscar el repuesto' : 'Agregar otro'),
                ),
                if (_sinExistencia.isNotEmpty)
                  Padding(
                    padding: const EdgeInsets.only(top: 6),
                    child: Text(
                      'No hay tanto de: ${_sinExistencia.map((l) => l.nombre).join(', ')}. '
                      'Registre la entrada antes de venderlo.',
                      style: theme.textTheme.bodySmall
                          ?.copyWith(color: theme.colorScheme.error),
                    ),
                  ),

                const SizedBox(height: 20),
                Text('QUIÉN COMPRA', style: _rotulo(theme)),
                const SizedBox(height: 6),
                if (_cliente case final cliente?)
                  Card(
                    child: ListTile(
                      title: Text(cliente.fullName),
                      subtitle: Text(cliente.phone),
                      trailing: IconButton(
                        icon: const Icon(Icons.close),
                        onPressed: _busy ? null : () => setState(() => _cliente = null),
                      ),
                    ),
                  )
                else ...[
                  OutlinedButton.icon(
                    onPressed: _busy ? null : _buscarCliente,
                    icon: const Icon(Icons.person_search_outlined),
                    label: const Text('Buscar el cliente'),
                  ),
                  Padding(
                    padding: const EdgeInsets.only(top: 6),
                    child: Text(
                      'Opcional. Sin cliente la venta es a alguien de paso; con cliente le '
                      'queda en su historial y se le puede facturar con su RTN.',
                      style: theme.textTheme.bodySmall,
                    ),
                  ),
                ],

                const SizedBox(height: 20),
                Text('CÓMO PAGA', style: _rotulo(theme)),
                const SizedBox(height: 6),
                DropdownButtonFormField<PaymentMethod>(
                  initialValue: _metodo,
                  decoration: const InputDecoration(
                    labelText: 'Forma de pago',
                    isDense: true,
                    border: OutlineInputBorder(),
                  ),
                  items: [
                    for (final m in PaymentMethod.values)
                      DropdownMenuItem(value: m, child: Text(m.label)),
                  ],
                  onChanged: _busy ? null : (value) => setState(() => _metodo = value ?? _metodo),
                ),

                SwitchListTile(
                  value: _fiscal,
                  contentPadding: EdgeInsets.zero,
                  onChanged: _busy || !_puedeCai(rango)
                      ? null
                      : (value) => setState(() => _fiscal = value),
                  title: const Text('Factura con CAI'),
                  subtitle: Text(_leyendaCai(rango, tasa)),
                ),

                if (_fiscal) ...[
                  TextField(
                    controller: _aNombreDe,
                    textCapitalization: TextCapitalization.words,
                    decoration: const InputDecoration(
                      labelText: 'A nombre de',
                      isDense: true,
                      border: OutlineInputBorder(),
                    ),
                  ),
                  const SizedBox(height: 8),
                  TextField(
                    controller: _rtn,
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(
                      labelText: 'RTN del cliente',
                      helperText: 'Sin RTN: consumidor final',
                      isDense: true,
                      border: OutlineInputBorder(),
                    ),
                  ),
                  const SizedBox(height: 8),
                ],

                TextField(
                  controller: _nota,
                  decoration: const InputDecoration(
                    labelText: 'Nota',
                    isDense: true,
                    border: OutlineInputBorder(),
                  ),
                ),
              ],
            ),
      bottomNavigationBar: _hecha != null
          ? null
          : SafeArea(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            'Total',
                            style: theme.textTheme.bodyMedium,
                          ),
                        ),
                        Text(
                          money(total, 'HNL'),
                          style: theme.textTheme.titleLarge
                              ?.copyWith(fontFamily: GarajFonts.mono),
                        ),
                      ],
                    ),
                    if (tasa > 0)
                      Text(
                        _fiscal
                            ? 'Incluye ISV ${tasa.toStringAsFixed(0)}% '
                                '(${money(impuesto, 'HNL')}).'
                            : 'Sin ISV: solo la factura con CAI lo lleva. '
                                'Con factura: ${money(_base + _base * tasa / 100, 'HNL')}.',
                        style: theme.textTheme.bodySmall,
                      ),
                    const SizedBox(height: 8),
                    SizedBox(
                      width: double.infinity,
                      child: FilledButton(
                        onPressed: _busy || _lineas.isEmpty || _sinExistencia.isNotEmpty
                            ? null
                            : () => _registrar(tasa),
                        child: Text(_busy ? 'Registrando…' : 'Registrar la venta'),
                      ),
                    ),
                  ],
                ),
              ),
            ),
    );
  }

  bool _puedeCai(FiscalRange? rango) =>
      rango != null && !rango.isExpired && !rango.isExhausted;

  String _leyendaCai(FiscalRange? rango, double tasa) {
    if (rango == null) {
      return 'Esta sucursal no tiene CAI registrado. Se registra en el panel, en Taller.';
    }
    if (rango.isExpired) return 'El CAI de esta sucursal venció.';
    if (rango.isExhausted) return 'Se agotó el rango autorizado de esta sucursal.';
    return 'Consume el número ${rango.nextFiscalNumber} del rango autorizado'
        '${tasa > 0 ? ' y le suma el ISV ${tasa.toStringAsFixed(0)}%' : ''}.';
  }

  static TextStyle? _rotulo(ThemeData theme) => theme.textTheme.labelSmall?.copyWith(
        color: theme.colorScheme.onSurfaceVariant,
        letterSpacing: 0.6,
      );
}

/// Un renglón de la venta mientras se arma. El precio y el descuento se tocan: en el
/// mostrador se regatea.
class _Linea {
  _Linea({
    required this.partId,
    required this.nombre,
    required this.sku,
    required this.unidad,
    required this.disponible,
    required this.precio,
  });

  final String partId;
  final String nombre;
  final String sku;
  final String unidad;
  final double disponible;
  double precio;
  double cantidad = 1;
  double descuento = 0;
}

class _LineaCard extends StatelessWidget {
  const _LineaCard({required this.linea, required this.onCambio, required this.onQuitar});

  final _Linea linea;
  final VoidCallback onCambio;
  final VoidCallback onQuitar;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final total = (linea.cantidad * linea.precio - linea.descuento).clamp(0, double.infinity);

    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 8, 4, 10),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(linea.nombre, style: theme.textTheme.bodyLarge),
                      Text(
                        '${linea.sku} · quedan ${linea.disponible.toStringAsFixed(0)} '
                        '${linea.unidad}',
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: linea.cantidad > linea.disponible
                              ? theme.colorScheme.error
                              : theme.colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ],
                  ),
                ),
                Text(
                  money(total.toDouble(), 'HNL'),
                  style: theme.textTheme.titleSmall?.copyWith(fontFamily: GarajFonts.mono),
                ),
                IconButton(icon: const Icon(Icons.close), onPressed: onQuitar),
              ],
            ),
            Row(
              children: [
                Expanded(
                  child: _Campo(
                    label: 'Cantidad',
                    valor: linea.cantidad,
                    onCambio: (v) {
                      linea.cantidad = v;
                      onCambio();
                    },
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: _Campo(
                    label: 'Precio',
                    valor: linea.precio,
                    onCambio: (v) {
                      linea.precio = v;
                      onCambio();
                    },
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: _Campo(
                    label: 'Descuento',
                    valor: linea.descuento,
                    onCambio: (v) {
                      linea.descuento = v;
                      onCambio();
                    },
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _Campo extends StatefulWidget {
  const _Campo({required this.label, required this.valor, required this.onCambio});

  final String label;
  final double valor;
  final ValueChanged<double> onCambio;

  @override
  State<_Campo> createState() => _CampoState();
}

class _CampoState extends State<_Campo> {
  late final TextEditingController _controller =
      TextEditingController(text: _texto(widget.valor));

  static String _texto(double valor) =>
      valor == valor.roundToDouble() ? valor.toStringAsFixed(0) : valor.toString();

  @override
  void didUpdateWidget(_Campo old) {
    super.didUpdateWidget(old);
    // Solo cuando el valor cambió desde afuera —agregar dos veces el mismo repuesto—: si se
    // reescribiera en cada pulsación, el cursor saltaría al principio.
    if (widget.valor != old.valor && double.tryParse(_controller.text) != widget.valor) {
      _controller.text = _texto(widget.valor);
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => TextField(
        controller: _controller,
        keyboardType: const TextInputType.numberWithOptions(decimal: true),
        textAlign: TextAlign.right,
        decoration: InputDecoration(
          labelText: widget.label,
          isDense: true,
          border: const OutlineInputBorder(),
        ),
        onChanged: (texto) => widget.onCambio(double.tryParse(texto.trim()) ?? 0),
      );
}

/// El buscador de repuestos: las existencias de esa sucursal, con su precio y cuánto queda.
class _BuscarRepuesto extends ConsumerStatefulWidget {
  const _BuscarRepuesto({required this.branchId});

  final String branchId;

  @override
  ConsumerState<_BuscarRepuesto> createState() => _BuscarRepuestoState();
}

class _BuscarRepuestoState extends ConsumerState<_BuscarRepuesto> {
  String _texto = '';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final existencias = ref.watch(
      stockProvider(StockFilter(branchId: widget.branchId, search: _texto)),
    );

    return Padding(
      padding: EdgeInsets.only(
        left: 16,
        right: 16,
        top: 16,
        bottom: MediaQuery.of(context).viewInsets.bottom + 16,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextField(
            autofocus: true,
            decoration: const InputDecoration(
              hintText: 'Nombre o código del repuesto',
              prefixIcon: Icon(Icons.search),
              isDense: true,
              border: OutlineInputBorder(),
            ),
            onChanged: (valor) => setState(() => _texto = valor),
          ),
          const SizedBox(height: 12),
          SizedBox(
            height: 320,
            child: existencias.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (e, _) => Center(
                child: Text(apiErrorMessage(e, 'No se pudo buscar en la bodega.')),
              ),
              data: (items) => items.isEmpty
                  ? Center(
                      child: Text(
                        'Nada con ese nombre en esta sucursal.',
                        style: theme.textTheme.bodySmall,
                      ),
                    )
                  : ListView.builder(
                      itemCount: items.length,
                      itemBuilder: (_, i) {
                        final item = items[i];
                        return ListTile(
                          dense: true,
                          title: Text(item.partName),
                          subtitle: Text(
                            '${item.sku} · ${money(item.salePrice, 'HNL')} · '
                            'quedan ${item.quantity.toStringAsFixed(0)} ${item.unit}',
                          ),
                          onTap: () => Navigator.of(context).pop(item),
                        );
                      },
                    ),
            ),
          ),
        ],
      ),
    );
  }
}

/// El buscador de clientes. Sin resultados no pasa nada: la venta se registra sin cliente.
class _BuscarCliente extends ConsumerStatefulWidget {
  const _BuscarCliente();

  @override
  ConsumerState<_BuscarCliente> createState() => _BuscarClienteState();
}

class _BuscarClienteState extends ConsumerState<_BuscarCliente> {
  String _texto = '';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final clientes = ref.watch(customerSearchProvider(_texto));

    return Padding(
      padding: EdgeInsets.only(
        left: 16,
        right: 16,
        top: 16,
        bottom: MediaQuery.of(context).viewInsets.bottom + 16,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextField(
            autofocus: true,
            decoration: const InputDecoration(
              hintText: 'Nombre o teléfono',
              prefixIcon: Icon(Icons.search),
              isDense: true,
              border: OutlineInputBorder(),
            ),
            onChanged: (valor) => setState(() => _texto = valor),
          ),
          const SizedBox(height: 12),
          SizedBox(
            height: 320,
            child: clientes.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (e, _) =>
                  Center(child: Text(apiErrorMessage(e, 'No se pudo buscar el cliente.'))),
              data: (items) => items.isEmpty
                  ? Center(
                      child: Text('Nadie con ese nombre.', style: theme.textTheme.bodySmall),
                    )
                  : ListView.builder(
                      itemCount: items.length,
                      itemBuilder: (_, i) => ListTile(
                        dense: true,
                        title: Text(items[i].fullName),
                        subtitle: Text(items[i].phone),
                        onTap: () => Navigator.of(context).pop(items[i]),
                      ),
                    ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Lo que queda después de registrar: el número, el total y por dónde salió.
class _Hecha extends StatelessWidget {
  const _Hecha({required this.venta, required this.onOtra});

  final Sale venta;
  final VoidCallback onOtra;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Venta ${venta.number}', style: theme.textTheme.titleMedium),
                const SizedBox(height: 4),
                Text(
                  money(venta.total, venta.currency),
                  style: theme.textTheme.headlineSmall
                      ?.copyWith(fontFamily: GarajFonts.mono),
                ),
                const SizedBox(height: 8),
                Text(
                  venta.fiscalNumber == null
                      ? 'Comprobante de entrega, sin CAI: no lleva ISV.'
                      : 'Factura fiscal ${venta.fiscalNumber} · CAI ${venta.fiscalCai}',
                  style: theme.textTheme.bodySmall,
                ),
                Text(
                  'Los repuestos ya salieron de la bodega.',
                  style: theme.textTheme.bodySmall,
                ),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 8,
                  children: [
                    FilledButton(onPressed: onOtra, child: const Text('Otra venta')),
                    // El comprobante se manda desde el registro, que es donde queda la venta
                    // y donde se va a buscar cuando el cliente lo pida otra vez.
                    OutlinedButton(
                      onPressed: () => Navigator.of(context).pop(),
                      child: const Text('Volver a Ventas'),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

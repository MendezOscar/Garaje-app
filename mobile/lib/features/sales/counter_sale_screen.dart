import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/customer_repository.dart';
import '../../core/api/dashboard_repository.dart';
import '../../core/api/expense_repository.dart';
import '../../core/api/inventory_repository.dart';
import '../../core/api/sale_repository.dart';
import '../../core/api/service_request_repository.dart';
import '../../core/api/tenant_repository.dart';
import '../../core/api/work_order_repository.dart';
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

  /// De qué vehículo fue el trabajo. Puesto, queda en el historial del carro igual que una
  /// orden; vacío, es la venta de mostrador de siempre.
  String? _vehiculoId;

  /// Días de garantía. Nace con la del taller —que llega después de la primera pintada— y
  /// se puede cambiar aquí. Va con controlador y no con `initialValue` justamente por eso:
  /// un `initialValue` se fija en la primera pintada y se quedaría en cero.
  final _garantia = TextEditingController();
  bool _garantiaPuesta = false;

  int get _garantiaDias => int.tryParse(_garantia.text.trim()) ?? 0;

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
    _garantia.dispose();
    super.dispose();
  }

  double get _base =>
      _lineas.fold<double>(0, (total, l) => total + (l.cantidad * l.precio - l.descuento));

  List<_Linea> get _sinExistencia => _lineas
      .where((l) => l.disponible != null && l.cantidad > l.disponible!)
      .toList();

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

  /// El trabajo: del catálogo de mano de obra, o escrito a mano con su precio. Es lo que
  /// convierte esto en un servicio rápido y no solo en la venta de una pieza.
  Future<void> _agregarTrabajo() async {
    final elegido = await showModalBottomSheet<_Linea>(
      context: context,
      isScrollControlled: true,
      builder: (_) => const _ElegirTrabajo(),
    );

    if (elegido == null) return;
    setState(() => _lineas.add(elegido));
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
      // Los vehículos son del cliente: cambiarlo invalida el que estuviera elegido.
      _vehiculoId = null;
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
            vehicleId: _vehiculoId,
            warrantyDays: _garantiaDias,
            lines: [
              for (final l in _lineas)
                CounterSaleLine(
                  partId: l.partId,
                  laborServiceId: l.laborServiceId,
                  // El trabajo escrito a mano manda su concepto; el del catálogo lo toma de ahí.
                  description: l.esTrabajo && l.laborServiceId == null ? l.nombre : null,
                  quantity: l.cantidad,
                  unitPrice: l.precio,
                  discount: l.descuento,
                ),
            ],
          );

      // La existencia bajó y hay una venta más. Sin invalidar el registro, la venta quedaba
      // guardada pero «Ventas» seguía mostrando la lista de antes, y parecía que no se había
      // guardado nada. Lo mismo la caja del día y el estado de resultados, que la cuentan.
      ref.invalidate(stockProvider);
      ref.invalidate(salesRegistryProvider);
      ref.invalidate(dashboardProvider);
      ref.invalidate(incomeStatementProvider);
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
      _vehiculoId = null;
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

    // La garantía del taller, la primera vez que llega. Después manda lo que se escriba.
    final garantiaDelTaller = ref.watch(defaultWarrantyDaysProvider).value;
    if (!_garantiaPuesta && garantiaDelTaller != null) {
      _garantia.text = '$garantiaDelTaller';
      _garantiaPuesta = true;
    }
    final rango = _branchId == null
        ? null
        : ref.watch(branchFiscalRangeProvider(_branchId!)).value;

    // El precio ya lleva el ISV adentro: el total es lo cobrado, y facturar solo lo desglosa.
    // Antes se sumaba encima y el cliente que pedía factura pagaba un 15% más.
    final total = _base;
    final impuesto = _fiscal && tasa > 0 ? total - total / (1 + tasa / 100) : 0.0;

    return Scaffold(
      appBar: AppBar(title: const Text('Venta rápida')),
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
                Text('QUÉ SE VENDE O SE HACE', style: _rotulo(theme)),
                const SizedBox(height: 6),
                for (final linea in _lineas)
                  _LineaCard(
                    linea: linea,
                    onCambio: () => setState(() {}),
                    onQuitar: () => setState(() => _lineas.remove(linea)),
                  ),
                Row(
                  children: [
                    Expanded(
                      child: OutlinedButton.icon(
                        onPressed: _busy || _branchId == null ? null : _buscarRepuesto,
                        icon: const Icon(Icons.inventory_2_outlined, size: 18),
                        label: const Text('Repuesto'),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: OutlinedButton.icon(
                        onPressed: _busy ? null : _agregarTrabajo,
                        icon: const Icon(Icons.build_outlined, size: 18),
                        label: const Text('Trabajo'),
                      ),
                    ),
                  ],
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
                        tooltip: 'Quitar el cliente',
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

                // Con vehículo el trabajo entra en el historial del carro, como una orden.
                if (_cliente case final cliente?)
                  Consumer(
                    builder: (context, ref, _) {
                      final vehiculos =
                          ref.watch(customerVehiclesProvider(cliente.id)).value ?? const [];

                      if (vehiculos.isEmpty) return const SizedBox.shrink();

                      return Padding(
                        padding: const EdgeInsets.only(top: 8),
                        child: DropdownButtonFormField<String?>(
                          initialValue: _vehiculoId,
                          decoration: const InputDecoration(
                            labelText: 'De qué vehículo',
                            isDense: true,
                            border: OutlineInputBorder(),
                          ),
                          items: [
                            const DropdownMenuItem(
                              value: null,
                              child: Text('Ninguno, es venta de mostrador'),
                            ),
                            for (final v in vehiculos)
                              DropdownMenuItem(
                                value: v.id,
                                child: Text(
                                  '${v.brand} ${v.model}'
                                  '${v.plate != null ? ' · ${v.plate}' : ''}',
                                ),
                              ),
                          ],
                          onChanged:
                              _busy ? null : (value) => setState(() => _vehiculoId = value),
                        ),
                      );
                    },
                  ),

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
                const SizedBox(height: 8),
                TextField(
                  controller: _garantia,
                  keyboardType: TextInputType.number,
                  decoration: const InputDecoration(
                    labelText: 'Garantía (días)',
                    isDense: true,
                    border: OutlineInputBorder(),
                    helperText: 'Se imprime en el comprobante con su fecha. Cero es sin garantía.',
                    helperMaxLines: 2,
                  ),
                ),
                const SizedBox(height: 6),
                // Se dice explícitamente porque «Tarjeta» y «Transferencia» se pueden leer como
                // que la app cobra. No cobra: anota cómo pagó el cliente en el mostrador, que es
                // lo mismo que se le declaró a Apple por la regla 3.1.
                Text(
                  'Aquí solo se anota cómo pagó el cliente. La app no procesa cobros ni pide '
                  'datos de tarjeta.',
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
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
                                '(${money(impuesto, 'HNL')}), ya dentro del precio.'
                            : 'El total es el mismo con factura: el ISV ya va en el precio.',
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

/// Un renglón de la venta mientras se arma: un repuesto de bodega o un trabajo. El precio y
/// el descuento se tocan porque en el mostrador se regatea.
///
/// `disponible` va en null en un trabajo: no hay existencia que alcance o falte.
class _Linea {
  _Linea({
    this.partId,
    this.laborServiceId,
    required this.nombre,
    required this.sku,
    required this.unidad,
    required this.disponible,
    required this.precio,
  });

  final String? partId;
  final String? laborServiceId;
  final String nombre;
  final String sku;
  final String unidad;
  final double? disponible;
  double precio;
  double cantidad = 1;
  double descuento = 0;

  bool get esTrabajo => partId == null;
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
                        linea.disponible is double
                            ? '${linea.sku} · quedan '
                                '${linea.disponible!.toStringAsFixed(0)} ${linea.unidad}'
                            : linea.sku.isEmpty
                                ? 'Trabajo'
                                : 'Trabajo · ${linea.sku}',
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: linea.disponible != null &&
                                  linea.cantidad > linea.disponible!
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
                IconButton(
                  tooltip: 'Quitar de la venta',
                  icon: const Icon(Icons.close),
                  onPressed: onQuitar,
                ),
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
                  ? Center(child: _SinResultados(texto: _texto))
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

/// Qué decir cuando la bodega no devuelve nada.
///
/// «No existe» y «no hay en esta sucursal» son dos cosas distintas y se resuelven distinto: una
/// dándolo de alta en el catálogo, la otra recibiendo mercadería. El buscador del mostrador
/// lista existencias, así que sin esta consulta al catálogo las dos se veían igual y quien está
/// en el mostrador no sabía cuál de las dos le pasó.
class _SinResultados extends ConsumerWidget {
  const _SinResultados({required this.texto});

  final String texto;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final estilo = theme.textTheme.bodySmall;

    if (texto.trim().isEmpty) {
      return Text('Escriba el nombre o el código del repuesto.', style: estilo);
    }

    final enCatalogo = ref.watch(partSearchProvider(texto.trim()));

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 24),
      child: enCatalogo.when(
        // Mientras se consulta se dice lo que ya se sabe con seguridad, sin adelantarse.
        loading: () => Text('Sin existencia en esta sucursal.', style: estilo),
        error: (_, __) => Text('Sin existencia en esta sucursal.', style: estilo),
        data: (partes) => partes.isEmpty
            ? Text(
                'No hay ningún repuesto con ese nombre ni en el catálogo del taller.',
                textAlign: TextAlign.center,
                style: estilo,
              )
            : Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    partes.length == 1
                        ? '«${partes.first.name}» está en el catálogo, pero no hay existencia '
                            'en esta sucursal.'
                        : 'Hay ${partes.length} repuestos con ese nombre en el catálogo, pero '
                            'sin existencia en esta sucursal.',
                    textAlign: TextAlign.center,
                    style: estilo,
                  ),
                  const SizedBox(height: 6),
                  Text(
                    'Recíbalo en bodega desde Inventario y podrá venderlo.',
                    textAlign: TextAlign.center,
                    style: estilo?.copyWith(color: theme.colorScheme.onSurfaceVariant),
                  ),
                ],
              ),
      ),
    );
  }
}

/// Elegir el trabajo que se va a cobrar: del catálogo de mano de obra, o escrito a mano.
///
/// El catálogo primero porque es lo que el taller cobra todos los días y ya trae su precio;
/// escribirlo queda para lo que no está en ninguna lista.
class _ElegirTrabajo extends ConsumerStatefulWidget {
  const _ElegirTrabajo();

  @override
  ConsumerState<_ElegirTrabajo> createState() => _ElegirTrabajoState();
}

class _ElegirTrabajoState extends ConsumerState<_ElegirTrabajo> {
  final _concepto = TextEditingController();
  final _precio = TextEditingController();

  @override
  void dispose() {
    _concepto.dispose();
    _precio.dispose();
    super.dispose();
  }

  void _libre() {
    final nombre = _concepto.text.trim();
    final precio = double.tryParse(_precio.text.trim().replaceAll(',', '.'));
    if (nombre.isEmpty || precio == null || precio <= 0) return;

    Navigator.pop(
      context,
      _Linea(
        nombre: nombre,
        sku: '',
        unidad: 'trabajo',
        disponible: null,
        precio: precio,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final servicios = ref.watch(laborServicesProvider).value ?? const [];

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
              Text('Qué trabajo se cobra', style: theme.textTheme.titleMedium),
              const SizedBox(height: 8),

              for (final servicio in servicios)
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  dense: true,
                  title: Text(servicio.name),
                  trailing: Text(
                    money(servicio.price, 'HNL'),
                    style: theme.textTheme.bodyMedium?.copyWith(fontFamily: GarajFonts.mono),
                  ),
                  onTap: () => Navigator.pop(
                    context,
                    _Linea(
                      laborServiceId: servicio.id,
                      nombre: servicio.name,
                      sku: '',
                      unidad: 'trabajo',
                      disponible: null,
                      precio: servicio.price,
                    ),
                  ),
                ),

              if (servicios.isEmpty)
                Text(
                  'El taller todavía no tiene trabajos en el catálogo. Se puede escribir aquí.',
                  style: theme.textTheme.bodySmall,
                ),

              const Divider(height: 24),
              Text('O escríbalo', style: theme.textTheme.labelLarge),
              const SizedBox(height: 8),
              TextField(
                controller: _concepto,
                autofocus: servicios.isEmpty,
                textCapitalization: TextCapitalization.sentences,
                maxLength: 200,
                decoration: const InputDecoration(
                  labelText: 'Qué se hizo',
                  hintText: 'Cambio de aceite, revisión de frenos…',
                ),
              ),
              TextField(
                controller: _precio,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                decoration: const InputDecoration(labelText: 'Precio', prefixText: 'L '),
                onSubmitted: (_) => _libre(),
              ),
              const SizedBox(height: 12),
              Align(
                alignment: Alignment.centerRight,
                child: FilledButton(onPressed: _libre, child: const Text('Agregar')),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/inventory_repository.dart';
import '../../core/api/service_request_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/current_user.dart';
import '../../core/models/inventory.dart';

/// La bodega desde el teléfono. Los repuestos llegan al mostrador, no a la oficina: la
/// entrada de una compra, el conteo de una gaveta y el traslado a la otra sucursal pasan
/// donde está el repuesto, con el teléfono en la mano.
///
/// El Técnico ve existencias —necesita saber si hay antes de prometer una reparación— pero
/// no mueve nada: la API le responde 403 y aquí no se le enseñan los botones.
class InventoryScreen extends ConsumerStatefulWidget {
  const InventoryScreen({super.key});

  @override
  ConsumerState<InventoryScreen> createState() => _InventoryScreenState();
}

class _InventoryScreenState extends ConsumerState<InventoryScreen> {
  StockFilter _filter = const StockFilter();
  bool _busy = false;

  bool get _isOwner {
    final auth = ref.read(authControllerProvider);
    return auth is AuthSignedIn && auth.user.role == AppRole.owner;
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
      ref.invalidate(stockProvider(_filter));
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

  /// Qué se le puede hacer a la existencia de un repuesto en una sucursal.
  Future<void> _openItem(StockItem item) async {
    final action = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        // Desplazable: con las cinco acciones del Dueño la hoja no cabe en la altura que
        // Material le da por defecto en un teléfono.
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ListTile(
                title: Text(item.partName),
                subtitle: Text(
                  '${item.sku} · ${item.branchName} · '
                  '${_quantity(item.quantity)} ${item.unit}'
                  '${item.location == null ? '' : ' · ${item.location}'}',
                ),
              ),
              const Divider(height: 1),
              if (_isOwner) ...[
                ListTile(
                  leading: const Icon(Icons.local_shipping_outlined),
                  title: const Text('Entrada por compra'),
                  onTap: () => Navigator.pop(context, 'receive'),
                ),
                ListTile(
                  leading: const Icon(Icons.fact_check_outlined),
                  title: const Text('Ajuste por conteo'),
                  subtitle: const Text('Se registra lo contado, no la diferencia'),
                  onTap: () => Navigator.pop(context, 'adjust'),
                ),
                ListTile(
                  leading: const Icon(Icons.swap_horiz),
                  title: const Text('Trasladar a otra sucursal'),
                  onTap: () => Navigator.pop(context, 'transfer'),
                ),
                ListTile(
                  leading: const Icon(Icons.tune),
                  title: const Text('Mínimo y ubicación'),
                  onTap: () => Navigator.pop(context, 'settings'),
                ),
                ListTile(
                  leading: const Icon(Icons.edit_outlined),
                  title: const Text('Editar el repuesto'),
                  onTap: () => Navigator.pop(context, 'edit'),
                ),
              ],
              ListTile(
                leading: const Icon(Icons.history),
                title: const Text('Kardex'),
                subtitle: const Text('Todo lo que entró y salió'),
                onTap: () => Navigator.pop(context, 'kardex'),
              ),
            ],
          ),
        ),
      ),
    );

    if (!mounted || action == null) return;

    switch (action) {
      case 'receive':
        await _receive(item);
      case 'adjust':
        await _adjust(item);
      case 'transfer':
        await _transfer(item);
      case 'settings':
        await _settings(item);
      case 'edit':
        await _editPart(item);
      case 'kardex':
        await _kardex(item);
    }
  }

  Future<void> _receive(StockItem item) async {
    final quantity = TextEditingController();
    final cost = TextEditingController(text: item.costPrice.toStringAsFixed(2));
    final reference = TextEditingController();

    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Entrada por compra'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text('${item.partName} · ${item.branchName}'),
            const SizedBox(height: 12),
            TextField(
              controller: quantity,
              autofocus: true,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              decoration: InputDecoration(labelText: 'Cantidad (${item.unit})'),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: cost,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              decoration: const InputDecoration(
                labelText: 'Costo unitario',
                prefixText: 'L ',
                helperText: 'Actualiza el costo de referencia del catálogo.',
              ),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: reference,
              decoration: const InputDecoration(
                labelText: 'Referencia',
                hintText: 'Nº de factura del proveedor',
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
    );

    if (ok != true) return;

    final amount = _parse(quantity.text);
    if (amount == null || amount <= 0) return;

    await _run(() => ref.read(inventoryRepositoryProvider).receive(
          branchId: item.branchId,
          partId: item.partId,
          quantity: amount,
          unitCost: _parse(cost.text),
          reference: reference.text.trim().isEmpty ? null : reference.text.trim(),
        ));
  }

  Future<void> _adjust(StockItem item) async {
    final counted = TextEditingController(text: _quantity(item.quantity));
    final reason = TextEditingController();

    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Ajuste por conteo'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              'El sistema dice ${_quantity(item.quantity)} ${item.unit}. '
              'Escriba lo que contó.',
            ),
            const SizedBox(height: 12),
            TextField(
              controller: counted,
              autofocus: true,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              decoration: InputDecoration(labelText: 'Contado (${item.unit})'),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: reason,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: 'Motivo',
                helperText: 'Queda en el kardex con su nombre y la hora.',
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
            child: const Text('Ajustar'),
          ),
        ],
      ),
    );

    if (ok != true) return;

    final value = _parse(counted.text);
    if (value == null || reason.text.trim().isEmpty) {
      _snack('Hacen falta la cantidad contada y el motivo.');
      return;
    }

    await _run(() => ref.read(inventoryRepositoryProvider).adjust(
          branchId: item.branchId,
          partId: item.partId,
          countedQuantity: value,
          reason: reason.text.trim(),
        ));
  }

  Future<void> _transfer(StockItem item) async {
    final branches = ref.read(branchOptionsProvider).value ?? const <BranchOption>[];
    final destinations = branches.where((b) => b.id != item.branchId).toList();

    if (destinations.isEmpty) {
      _snack('No hay otra sucursal a donde trasladar.');
      return;
    }

    final quantity = TextEditingController();
    var toBranchId = destinations.first.id;

    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setInner) => AlertDialog(
          title: const Text('Traslado'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                'Sale de ${item.branchName}, donde hay '
                '${_quantity(item.quantity)} ${item.unit}.',
              ),
              const SizedBox(height: 12),
              DropdownButtonFormField<String>(
                initialValue: toBranchId,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Hacia'),
                items: [
                  for (final branch in destinations)
                    DropdownMenuItem(value: branch.id, child: Text(branch.name)),
                ],
                onChanged: (value) => setInner(() => toBranchId = value ?? toBranchId),
              ),
              const SizedBox(height: 8),
              TextField(
                controller: quantity,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                decoration: InputDecoration(labelText: 'Cantidad (${item.unit})'),
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
              child: const Text('Trasladar'),
            ),
          ],
        ),
      ),
    );

    if (ok != true) return;

    final amount = _parse(quantity.text);
    if (amount == null || amount <= 0) return;

    await _run(() => ref.read(inventoryRepositoryProvider).transfer(
          fromBranchId: item.branchId,
          toBranchId: toBranchId,
          partId: item.partId,
          quantity: amount,
        ));
  }

  Future<void> _settings(StockItem item) async {
    final minimum = TextEditingController(text: _quantity(item.minQuantity));
    final location = TextEditingController(text: item.location ?? '');

    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Mínimo y ubicación'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              controller: minimum,
              autofocus: true,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              decoration: const InputDecoration(
                labelText: 'Mínimo de reposición',
                helperText: 'Debajo de esto aparece en la alerta.',
              ),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: location,
              decoration: const InputDecoration(
                labelText: 'Ubicación',
                hintText: 'Estante, gaveta…',
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
            child: const Text('Guardar'),
          ),
        ],
      ),
    );

    if (ok != true) return;

    await _run(() => ref.read(inventoryRepositoryProvider).saveSettings(
          branchId: item.branchId,
          partId: item.partId,
          minQuantity: _parse(minimum.text) ?? 0,
          location: location.text.trim().isEmpty ? null : location.text.trim(),
        ));
  }

  Future<void> _editPart([StockItem? item]) async {
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (context) => _PartForm(item: item),
    );

    if (saved == true) ref.invalidate(stockProvider(_filter));
  }

  Future<void> _kardex(StockItem item) => showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        showDragHandle: true,
        builder: (context) => _Kardex(item: item),
      );

  void _snack(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) {
    final stock = ref.watch(stockProvider(_filter));
    final branches = ref.watch(branchOptionsProvider).value ?? const <BranchOption>[];

    return Scaffold(
      appBar: AppBar(title: const Text('Inventario')),
      floatingActionButton: _isOwner
          ? FloatingActionButton.extended(
              onPressed: _busy ? null : () => _editPart(),
              icon: const Icon(Icons.add),
              label: const Text('Nuevo repuesto'),
            )
          : null,
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 12, 12, 0),
            child: TextField(
              decoration: const InputDecoration(
                hintText: 'Buscar por SKU, nombre o marca',
                prefixIcon: Icon(Icons.search),
                isDense: true,
              ),
              onChanged: (value) => setState(() => _filter = _filter.copyWith(search: value)),
            ),
          ),
          SizedBox(
            height: 56,
            child: ListView(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
              children: [
                FilterChip(
                  label: const Text('Bajo mínimo'),
                  selected: _filter.onlyBelowMinimum,
                  onSelected: (value) => setState(
                    () => _filter = _filter.copyWith(onlyBelowMinimum: value),
                  ),
                ),
                const SizedBox(width: 8),
                ChoiceChip(
                  label: const Text('Todas'),
                  selected: _filter.branchId == null,
                  onSelected: (_) => setState(
                    () => _filter = _filter.copyWith(clearBranch: true),
                  ),
                ),
                for (final branch in branches) ...[
                  const SizedBox(width: 8),
                  ChoiceChip(
                    label: Text(branch.name),
                    selected: _filter.branchId == branch.id,
                    onSelected: (_) => setState(
                      () => _filter = _filter.copyWith(branchId: branch.id),
                    ),
                  ),
                ],
              ],
            ),
          ),
          Expanded(
            child: RefreshIndicator(
              onRefresh: () async => ref.invalidate(stockProvider(_filter)),
              child: stock.when(
                loading: () => const Center(child: CircularProgressIndicator()),
                error: (e, _) => ListView(
                  children: [
                    const SizedBox(height: 100),
                    Center(
                      child: Padding(
                        padding: const EdgeInsets.all(24),
                        child: Text(apiErrorMessage(e, 'No se pudo cargar el inventario.')),
                      ),
                    ),
                  ],
                ),
                data: (items) => items.isEmpty
                    ? ListView(
                        children: const [
                          SizedBox(height: 100),
                          Center(
                            child: Padding(
                              padding: EdgeInsets.all(24),
                              child: Text('No hay existencias que mostrar.'),
                            ),
                          ),
                        ],
                      )
                    : ListView.separated(
                        padding: const EdgeInsets.fromLTRB(12, 4, 12, 88),
                        itemCount: items.length,
                        separatorBuilder: (_, __) => const SizedBox(height: 8),
                        itemBuilder: (context, i) => _StockCard(
                          item: items[i],
                          onTap: _busy ? null : () => _openItem(items[i]),
                        ),
                      ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _StockCard extends StatelessWidget {
  const _StockCard({required this.item, required this.onTap});

  final StockItem item;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(item.partName, style: theme.textTheme.titleSmall),
                    const SizedBox(height: 2),
                    Text(
                      '${item.sku}${item.brand == null ? '' : ' · ${item.brand}'}',
                      style: theme.textTheme.bodySmall,
                    ),
                    Text(
                      '${item.branchName}'
                      '${item.location == null ? '' : ' · ${item.location}'}',
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              Column(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Text(
                    '${_quantity(item.quantity)} ${item.unit}',
                    style: theme.textTheme.titleMedium?.copyWith(
                      color: item.isBelowMinimum ? theme.colorScheme.error : null,
                    ),
                  ),
                  if (item.isBelowMinimum)
                    Text(
                      'mínimo ${_quantity(item.minQuantity)}',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.error,
                      ),
                    ),
                  Text('L ${item.salePrice.toStringAsFixed(2)}',
                      style: theme.textTheme.bodySmall),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Alta y edición del catálogo. El SKU es único por taller: es con lo que se busca el
/// repuesto en el mostrador.
class _PartForm extends ConsumerStatefulWidget {
  const _PartForm({this.item});

  final StockItem? item;

  @override
  ConsumerState<_PartForm> createState() => _PartFormState();
}

class _PartFormState extends ConsumerState<_PartForm> {
  late final _sku = TextEditingController(text: widget.item?.sku ?? '');
  late final _name = TextEditingController(text: widget.item?.partName ?? '');
  late final _brand = TextEditingController(text: widget.item?.brand ?? '');
  late final _category = TextEditingController(text: widget.item?.category ?? '');
  late final _unit = TextEditingController(text: widget.item?.unit ?? 'unidad');
  late final _cost =
      TextEditingController(text: widget.item?.costPrice.toStringAsFixed(2) ?? '');
  late final _price =
      TextEditingController(text: widget.item?.salePrice.toStringAsFixed(2) ?? '');

  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    for (final c in [_sku, _name, _brand, _category, _unit, _cost, _price]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _save() async {
    if (_sku.text.trim().isEmpty || _name.text.trim().isEmpty) {
      setState(() => _error = 'El SKU y el nombre son obligatorios.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      await ref.read(inventoryRepositoryProvider).savePart(
            id: widget.item?.partId,
            sku: _sku.text.trim(),
            name: _name.text.trim(),
            brand: _brand.text.trim().isEmpty ? null : _brand.text.trim(),
            category: _category.text.trim().isEmpty ? null : _category.text.trim(),
            unit: _unit.text.trim().isEmpty ? 'unidad' : _unit.text.trim(),
            costPrice: _parse(_cost.text) ?? 0,
            salePrice: _parse(_price.text) ?? 0,
          );

      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      setState(() => _error = apiErrorMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.only(
        left: 16,
        right: 16,
        bottom: MediaQuery.of(context).viewInsets.bottom + 16,
      ),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              widget.item == null ? 'Nuevo repuesto' : 'Editar repuesto',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _sku,
              // El SKU identifica el repuesto en todo el sistema; cambiarlo en un repuesto
              // que ya tiene movimientos rompería la búsqueda de quien lo conoce por él.
              enabled: widget.item == null,
              decoration: const InputDecoration(labelText: 'SKU'),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _name,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(labelText: 'Nombre'),
            ),
            const SizedBox(height: 8),
            Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _brand,
                    decoration: const InputDecoration(labelText: 'Marca'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: TextField(
                    controller: _category,
                    decoration: const InputDecoration(labelText: 'Categoría'),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _unit,
                    decoration: const InputDecoration(labelText: 'Unidad'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: TextField(
                    controller: _cost,
                    keyboardType: const TextInputType.numberWithOptions(decimal: true),
                    decoration: const InputDecoration(labelText: 'Costo', prefixText: 'L '),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: TextField(
                    controller: _price,
                    keyboardType: const TextInputType.numberWithOptions(decimal: true),
                    decoration: const InputDecoration(labelText: 'Precio', prefixText: 'L '),
                  ),
                ),
              ],
            ),
            if (_error != null) ...[
              const SizedBox(height: 12),
              Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ],
            const SizedBox(height: 16),
            FilledButton(
              onPressed: _busy ? null : _save,
              child: Text(widget.item == null ? 'Crear' : 'Guardar'),
            ),
            const SizedBox(height: 8),
            Text(
              'La existencia no se escribe aquí: entra por compra, ajuste o traslado, y '
              'cada movimiento queda con su responsable.',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
        ),
      ),
    );
  }
}

/// El kardex del repuesto en su sucursal: cada movimiento con su autor y el saldo con el
/// que quedó. Es lo que contesta «¿y dónde se fueron los seis que había?».
class _Kardex extends ConsumerWidget {
  const _Kardex({required this.item});

  final StockItem item;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final movements = ref.watch(movementsProvider((item.partId, item.branchId)));

    return SizedBox(
      height: MediaQuery.of(context).size.height * 0.75,
      child: Column(
        children: [
          ListTile(
            title: Text(item.partName),
            subtitle: Text('${item.sku} · ${item.branchName}'),
          ),
          const Divider(height: 1),
          Expanded(
            child: movements.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (e, _) => Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Text(apiErrorMessage(e, 'No se pudo cargar el kardex.')),
                ),
              ),
              data: (list) => list.isEmpty
                  ? const Center(child: Text('Sin movimientos todavía.'))
                  : ListView.builder(
                      itemCount: list.length,
                      itemBuilder: (context, i) {
                        final movement = list[i];
                        final positive = movement.signedQuantity >= 0;

                        return ListTile(
                          dense: true,
                          title: Text(
                            movement.type.label +
                                (movement.workOrderNumber == null
                                    ? ''
                                    : ' · ${movement.workOrderNumber}') +
                                (movement.counterpartBranchName == null
                                    ? ''
                                    : ' · ${movement.counterpartBranchName}'),
                          ),
                          subtitle: Text(
                            '${_date(movement.movedAt)} · ${movement.movedByName}'
                            '${movement.reference == null ? '' : ' · ${movement.reference}'}'
                            '${movement.notes == null ? '' : ' · ${movement.notes}'}',
                          ),
                          trailing: Column(
                            mainAxisAlignment: MainAxisAlignment.center,
                            crossAxisAlignment: CrossAxisAlignment.end,
                            children: [
                              Text(
                                '${positive ? '+' : ''}${_quantity(movement.signedQuantity)}',
                                style: theme.textTheme.titleSmall?.copyWith(
                                  color: positive
                                      ? theme.colorScheme.primary
                                      : theme.colorScheme.error,
                                ),
                              ),
                              Text(
                                'queda ${_quantity(movement.resultingQuantity)}',
                                style: theme.textTheme.bodySmall,
                              ),
                            ],
                          ),
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

/// Sin decimales cuando no hacen falta: "3", no "3.00" —el taller cuenta piezas—, pero
/// "0.75" cuando se trata de litros.
String _quantity(double value) =>
    value == value.roundToDouble() ? value.toStringAsFixed(0) : value.toStringAsFixed(2);

double? _parse(String text) => double.tryParse(text.trim().replaceAll(',', '.'));

String _date(DateTime value) {
  final local = value.toLocal();
  final d = local.day.toString().padLeft(2, '0');
  final m = local.month.toString().padLeft(2, '0');
  final h = local.hour.toString().padLeft(2, '0');
  final min = local.minute.toString().padLeft(2, '0');
  return '$d/$m $h:$min';
}

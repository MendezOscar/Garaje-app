import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/inventory_repository.dart';
import '../../core/models/inventory.dart';
import '../../core/models/work_order.dart';

/// Repuestos consumidos en la orden. Para el técnico es la diferencia entre un inventario
/// que cuadra y uno que no: si no lo registra aquí, nadie lo registra.
class PartsSection extends ConsumerWidget {
  const PartsSection({
    required this.order,
    required this.canEdit,
    required this.busy,
    required this.onChanged,
    super.key,
  });

  final WorkOrderDetail order;
  final bool canEdit;
  final bool busy;
  final Future<void> Function() onChanged;

  Future<void> _add(BuildContext context, WidgetRef ref) async {
    final choice = await showModalBottomSheet<_Choice>(
      context: context,
      isScrollControlled: true,
      builder: (_) => const _PartPicker(),
    );

    if (choice == null) return;

    try {
      final repo = ref.read(inventoryRepositoryProvider);

      switch (choice) {
        case _FromCatalog(:final part, :final quantity):
          await repo.addPart(order.id, partId: part.id, quantity: quantity);
        case _Manual(:final description, :final quantity, :final unitPrice, :final unitCost):
          await repo.addManualPart(
            order.id,
            description: description,
            quantity: quantity,
            unitPrice: unitPrice,
            unitCost: unitCost,
          );
      }

      await onChanged();
    } catch (e) {
      if (context.mounted) {
        // El 409 de existencia insuficiente dice cuánto queda: se muestra tal cual.
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e, 'No se pudo cargar el repuesto.'))),
        );
      }
    }
  }

  Future<void> _remove(BuildContext context, WidgetRef ref, WorkOrderPart line) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('¿Quitar ${line.partName}?'),
        content: Text(
          line.partId == null
              // Nunca salió de la bodega, así que tampoco vuelve a ella.
              ? 'Se quita de la orden.'
              : 'Vuelve a la bodega y queda registrado en el kardex.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancelar')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Quitar')),
        ],
      ),
    );

    if (confirmed != true) return;

    try {
      await ref.read(inventoryRepositoryProvider).removePart(order.id, line.id);
      await onChanged();
    } catch (e) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e))),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'REPUESTOS',
            style: theme.textTheme.labelSmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
              letterSpacing: 0.6,
            ),
          ),
          const SizedBox(height: 8),

          if (order.parts.isEmpty)
            Text('Sin repuestos cargados.', style: theme.textTheme.bodySmall),

          for (final line in order.parts)
            ListTile(
              contentPadding: EdgeInsets.zero,
              dense: true,
              title: Text(line.partName),
              subtitle: Text(
                '${_quantity(line.quantity)} ${line.unit} × ${_money(line.unitPrice)}'
                '${line.partId == null ? ' · a mano' : ''}'
                '${line.taskTitle != null ? ' · ${line.taskTitle}' : ''}',
              ),
              trailing: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(_money(line.total), style: theme.textTheme.bodyMedium),
                  if (canEdit)
                    IconButton(
                      onPressed: busy ? null : () => _remove(context, ref, line),
                      icon: const Icon(Icons.close, size: 18),
                      tooltip: 'Quitar',
                    ),
                ],
              ),
            ),

          if (order.parts.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text('Total en repuestos', style: theme.textTheme.bodySmall),
                  Text(_money(order.partsTotal), style: theme.textTheme.titleSmall),
                ],
              ),
            ),

          if (canEdit)
            TextButton.icon(
              onPressed: busy ? null : () => _add(context, ref),
              icon: const Icon(Icons.add, size: 18),
              label: const Text('Cargar repuesto'),
            ),
        ],
      ),
    );
  }
}

/// Lo que devuelve el selector: o un repuesto del catálogo, o uno escrito a mano.
sealed class _Choice {
  const _Choice();
}

class _FromCatalog extends _Choice {
  const _FromCatalog(this.part, this.quantity);

  final Part part;
  final double quantity;
}

class _Manual extends _Choice {
  const _Manual({
    required this.description,
    required this.quantity,
    required this.unitPrice,
    this.unitCost,
  });

  final String description;
  final double quantity;
  final double unitPrice;
  final double? unitCost;
}

class _PartPicker extends ConsumerStatefulWidget {
  const _PartPicker();

  @override
  ConsumerState<_PartPicker> createState() => _PartPickerState();
}

class _PartPickerState extends ConsumerState<_PartPicker> {
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
            // A mano, arriba del listado: cuando el repuesto no está en el catálogo, buscarlo
            // primero es perder el tiempo.
            ListTile(
              leading: const Icon(Icons.edit_outlined),
              title: const Text('Cargar a mano'),
              subtitle: const Text('Lo que se compró de encargo y no está en el catálogo.'),
              onTap: _askManual,
            ),
            const Divider(height: 1),
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
                              ' · ${_money(part.salePrice)}',
                            ),
                            trailing: Text(
                              '${_quantity(part.totalQuantity)} ${part.unit}',
                              style: TextStyle(
                                color: part.isOutOfStock
                                    ? Theme.of(context).colorScheme.error
                                    : null,
                              ),
                            ),
                            // Se deja tocar aunque el saldo esté en cero: puede haber una
                            // entrada sin registrar, y el backend responde con el motivo.
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
          decoration: InputDecoration(
            labelText: 'Cantidad (${part.unit})',
            helperText: 'Disponible: ${_quantity(part.totalQuantity)} ${part.unit}',
          ),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancelar')),
          FilledButton(
            onPressed: () => Navigator.pop(context, double.tryParse(controller.text.replaceAll(',', '.'))),
            child: const Text('Cargar'),
          ),
        ],
      ),
    );

    if (quantity == null || quantity <= 0 || !mounted) return;
    Navigator.pop(context, _FromCatalog(part, quantity));
  }

  /// Repuesto que no está en el catálogo: el que se compró de encargo para esta orden. No
  /// descuenta existencias —nunca pasó por bodega— así que el precio hay que escribirlo.
  Future<void> _askManual() async {
    final concepto = TextEditingController();
    final cantidad = TextEditingController(text: '1');
    final precio = TextEditingController();
    final costo = TextEditingController();

    final manual = await showDialog<_Manual>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Repuesto a mano'),
        content: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: concepto,
                autofocus: true,
                textCapitalization: TextCapitalization.sentences,
                maxLength: 200,
                decoration: const InputDecoration(labelText: 'Qué repuesto es'),
              ),
              TextField(
                controller: cantidad,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                decoration: const InputDecoration(labelText: 'Cantidad'),
              ),
              const SizedBox(height: 8),
              TextField(
                controller: precio,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                decoration: const InputDecoration(
                  labelText: 'Precio al cliente',
                  prefixText: 'L ',
                ),
              ),
              const SizedBox(height: 8),
              TextField(
                controller: costo,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                decoration: const InputDecoration(
                  labelText: 'Costo (opcional)',
                  prefixText: 'L ',
                  helperText: 'Lo que le costó al taller. Sin él, el margen sale inflado.',
                  helperMaxLines: 2,
                ),
              ),
            ],
          ),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancelar')),
          FilledButton(
            onPressed: () {
              final texto = concepto.text.trim();
              final cant = double.tryParse(cantidad.text.replaceAll(',', '.'));
              final unit = double.tryParse(precio.text.replaceAll(',', '.'));
              if (texto.isEmpty || cant == null || cant <= 0 || unit == null) return;

              Navigator.pop(
                context,
                _Manual(
                  description: texto,
                  quantity: cant,
                  unitPrice: unit,
                  unitCost: double.tryParse(costo.text.replaceAll(',', '.')),
                ),
              );
            },
            child: const Text('Cargar'),
          ),
        ],
      ),
    );

    if (manual == null || !mounted) return;
    Navigator.pop(context, manual);
  }
}

String _money(double value) => 'L ${value.toStringAsFixed(2)}';

String _quantity(double value) =>
    value == value.roundToDouble() ? value.toStringAsFixed(0) : value.toStringAsFixed(2);

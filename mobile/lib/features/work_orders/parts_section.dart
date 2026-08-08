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
      await ref.read(inventoryRepositoryProvider).addPart(
            order.id,
            partId: choice.part.id,
            quantity: choice.quantity,
          );
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
        content: const Text('Vuelve a la bodega y queda registrado en el kardex.'),
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

class _Choice {
  const _Choice(this.part, this.quantity);

  final Part part;
  final double quantity;
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
    Navigator.pop(context, _Choice(part, quantity));
  }
}

String _money(double value) => 'L ${value.toStringAsFixed(2)}';

String _quantity(double value) =>
    value == value.roundToDouble() ? value.toStringAsFixed(0) : value.toStringAsFixed(2);

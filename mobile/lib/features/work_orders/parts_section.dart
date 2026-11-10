import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/inventory_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/inventory.dart';
import '../../core/models/work_order.dart';
import '../../core/widgets/garaj_states.dart';

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
      builder: (_) => _PartPicker(vehicleId: order.vehicleId),
    );

    if (choice == null) return;

    try {
      final repo = ref.read(inventoryRepositoryProvider);

      switch (choice) {
        case _FromCatalog(:final part, :final quantity):
          await repo.addPart(order.id, partId: part.id, quantity: quantity);
        case _Manual(
            :final description,
            :final quantity,
            :final unitPrice,
            :final unitCost,
            :final boughtOutside,
            :final supplierName,
          ):
          await repo.addManualPart(
            order.id,
            description: description,
            quantity: quantity,
            unitPrice: unitPrice,
            unitCost: unitCost,
            boughtOutside: boughtOutside,
            supplierName: supplierName,
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
          line.partId == null || line.porConfirmar
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

  /// Confirmar es lo único que saca el repuesto de la bodega. Hasta entonces la línea está
  /// en la orden y se le cotiza al cliente, pero el inventario no se tocó.
  Future<void> _confirm(BuildContext context, WidgetRef ref, WorkOrderPart line) async {
    try {
      await ref.read(inventoryRepositoryProvider).confirmPart(order.id, line.id);
      await onChanged();
    } catch (e) {
      if (context.mounted) {
        // El 409 dice por qué: ya estaba confirmado, no alcanza, o le falta el precio.
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e, 'No se pudo confirmar el repuesto.'))),
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

          // Arriba y lleno. Estaba abajo de la lista como enlace de texto, y en el taller no
          // se encontraba: la queja fue «no está el de agregar repuestos».
          if (canEdit) ...[
            SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                onPressed: busy ? null : () => _add(context, ref),
                icon: const Icon(Icons.add),
                label: const Text('Cargar un repuesto'),
              ),
            ),
            const SizedBox(height: 12),
          ],

          if (order.parts.isEmpty)
            Text('Sin repuestos cargados.', style: theme.textTheme.bodySmall),

          for (final line in order.parts)
            ListTile(
              contentPadding: EdgeInsets.zero,
              dense: true,
              title: Text(line.partName),
              subtitle: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    '${_quantity(line.quantity)} ${line.unit} × ${_money(line.unitPrice)}'
                    '${_origen(line)}'
                    '${line.taskTitle != null ? ' · ${line.taskTitle}' : ''}',
                  ),
                  if (line.porConfirmar)
                    Text(
                      _pendiente(line),
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.tertiary,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                ],
              ),
              trailing: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(_money(line.total), style: theme.textTheme.bodyMedium),
                  if (canEdit && line.porConfirmar)
                    IconButton(
                      onPressed: busy ? null : () => _confirm(context, ref, line),
                      icon: const Icon(Icons.check, size: 20),
                      tooltip: 'Confirmar',
                    ),
                  if (canEdit)
                    IconButton(
                      onPressed: busy ? null : () => _remove(context, ref, line),
                      icon: const Icon(Icons.close, size: 18),
                      tooltip: 'Quitar',
                    ),
                ],
              ),
            ),

          if (order.parts.any((p) => p.porConfirmar))
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(
                'Los repuestos por confirmar están en la orden y se le cotizan al cliente, '
                'pero todavía no salieron de bodega. Toque ✓ en cada uno cuando de verdad lo '
                'use: la orden no se entrega mientras quede alguno sin confirmar.',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
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
    this.boughtOutside = false,
    this.supplierName,
  });

  final String description;
  final double quantity;
  final double unitPrice;
  final double? unitCost;

  /// Comprado en una casa de repuestos: se le cobra al cliente pero no va en la factura.
  final bool boughtOutside;
  final String? supplierName;
}

class _PartPicker extends ConsumerStatefulWidget {
  const _PartPicker({required this.vehicleId});

  /// El vehículo de la orden, para poder ofrecer solo lo que le sirve.
  final String vehicleId;

  @override
  ConsumerState<_PartPicker> createState() => _PartPickerState();
}

class _PartPickerState extends ConsumerState<_PartPicker> {
  String _search = '';

  /// «Solo lo que le sirve a este vehículo».
  ///
  /// Arranca apagado y nunca se enciende solo: a qué vehículos aplica cada repuesto se llena
  /// a mano y va a estar incompleto mucho tiempo. Encendido por defecto escondería repuestos
  /// que sí sirven, y el que atiende no tendría cómo saber por qué.
  bool _soloLoQueAplica = false;

  BusquedaDeRepuesto get _busqueda => BusquedaDeRepuesto(
        _search,
        vehicleId: _soloLoQueAplica ? widget.vehicleId : null,
      );

  @override
  Widget build(BuildContext context) {
    final results = ref.watch(partSearchProvider(_busqueda));

    // El taller que apagó los precios para el técnico tampoco se los enseña aquí: elige el
    // repuesto por nombre y SKU. El servidor ya no los manda; esto es para no escribir «L 0».
    final sesion = ref.watch(authControllerProvider);
    final vePrecios = sesion is! AuthSignedIn || sesion.user.seesPrices;

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

            // Con el carro delante, dejar de adivinar qué filtro le va. Interruptor y no
            // automático: apagado se sigue viendo el catálogo entero.
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
              child: Align(
                alignment: Alignment.centerLeft,
                child: FilterChip(
                  selected: _soloLoQueAplica,
                  onSelected: (v) => setState(() => _soloLoQueAplica = v),
                  avatar: _soloLoQueAplica ? null : const Icon(Icons.directions_car, size: 18),
                  label: const Text('Solo lo que le sirve'),
                ),
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
                error: (e, _) => GarajError(
          message: apiErrorMessage(e, 'No se pudo cargar el catálogo.'),
          onRetry: () => ref.invalidate(partSearchProvider(_busqueda)),
        ),
                data: (parts) => parts.isEmpty
                    ? Center(
                        child: Padding(
                          padding: const EdgeInsets.all(24),
                          child: Text(
                            _soloLoQueAplica
                                ? 'Ningún repuesto dice servirle a este vehículo. Apague el '
                                    'filtro para ver todo el catálogo.'
                                : 'Sin resultados.',
                            textAlign: TextAlign.center,
                          ),
                        ),
                      )
                    : ListView.builder(
                        itemCount: parts.length,
                        itemBuilder: (context, i) {
                          final part = parts[i];

                          return ListTile(
                            title: Text(part.name),
                            subtitle: Text(
                              '${part.sku}'
                              '${part.brand != null ? ' · ${part.brand}' : ''}'
                              '${vePrecios ? ' · ${_money(part.salePrice)}' : ''}',
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
  ///
  /// Puede ser del taller aunque no esté en el catálogo, o comprado en una casa de repuestos.
  /// Esa diferencia se pregunta y no se adivina: la comprada afuera se le cobra al cliente
  /// igual, pero no entra en la factura del taller.
  Future<void> _askManual() async {
    // El técnico de un taller que apagó los precios no los pone: carga lo que puso y el
    // Dueño lo valora. El servidor hace lo mismo; aquí solo se deja de pedir lo que no va.
    final sesion = ref.read(authControllerProvider);
    final vePrecios = sesion is! AuthSignedIn || sesion.user.seesPrices;

    final concepto = TextEditingController();
    final cantidad = TextEditingController(text: '1');
    final precio = TextEditingController();
    final costo = TextEditingController();
    final casa = TextEditingController();
    var afuera = false;

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
              if (vePrecios) ...[
                TextField(
                  controller: precio,
                  keyboardType: const TextInputType.numberWithOptions(decimal: true),
                  decoration: const InputDecoration(
                    labelText: 'Precio al cliente',
                    prefixText: 'L ',
                  ),
                ),
                const SizedBox(height: 8),
              ] else
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  child: Text(
                    'El precio lo pone el dueño: cargue el repuesto y él lo valora antes de '
                    'facturar.',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ),
              if (vePrecios)
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
              // Solo esta parte se redibuja: el resto del diálogo no depende del interruptor.
              StatefulBuilder(
                builder: (context, redibujar) => Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    SwitchListTile(
                      value: afuera,
                      onChanged: (valor) => redibujar(() => afuera = valor),
                      contentPadding: EdgeInsets.zero,
                      title: const Text('Comprado en casa de repuestos'),
                      subtitle: const Text(
                        'Se le cobra al cliente, pero no va en la factura del taller.',
                      ),
                    ),
                    if (afuera)
                      TextField(
                        controller: casa,
                        textCapitalization: TextCapitalization.words,
                        maxLength: 120,
                        decoration: const InputDecoration(
                          labelText: '¿En cuál casa de repuestos? (opcional)',
                        ),
                      ),
                  ],
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
              if (texto.isEmpty || cant == null || cant <= 0) return;
              if (vePrecios && unit == null) return;

              Navigator.pop(
                context,
                _Manual(
                  description: texto,
                  quantity: cant,
                  unitPrice: unit ?? 0,
                  unitCost: double.tryParse(costo.text.replaceAll(',', '.')),
                  boughtOutside: afuera,
                  supplierName:
                      afuera && casa.text.trim().isNotEmpty ? casa.text.trim() : null,
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

/// De dónde salió el repuesto, que es lo que el taller distingue de un vistazo: lo comprado
/// en una casa de repuestos se le cobra al cliente pero no va en su factura.
String _origen(WorkOrderPart line) {
  if (!line.boughtOutside) return line.partId == null ? ' · a mano' : '';

  final casa = line.supplierName;
  return casa != null && casa.isNotEmpty ? ' · $casa' : ' · casa de repuestos';
}

/// Lo que dice una línea que todavía no salió de bodega. Con la existencia a la vista se sabe
/// de un vistazo si alcanza o si hay que comprarlo, sin abrir el inventario.
String _pendiente(WorkOrderPart line) {
  if (line.partId == null) {
    return line.unitPrice <= 0
        ? 'por confirmar · póngale precio en el panel'
        : 'por confirmar · se compra fuera';
  }

  final quedan = line.available;
  if (quedan == null) return 'por confirmar';

  return quedan >= line.quantity
      ? 'por confirmar · quedan ${_quantity(quedan)} en bodega'
      : 'por confirmar · hay que comprarlo, quedan ${_quantity(quedan)}';
}

String _money(double value) => 'L ${value.toStringAsFixed(2)}';

String _quantity(double value) =>
    value == value.roundToDouble() ? value.toStringAsFixed(0) : value.toStringAsFixed(2);

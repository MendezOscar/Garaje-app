import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/staff_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/current_user.dart';
import '../../core/models/work_order.dart';
import '../shared/status_chip.dart';
import 'parts_section.dart';
import 'photo_gallery.dart';
import 'quotes_section.dart';

/// Pantalla de trabajo del técnico: ver qué hay que hacer, marcar pasos y mover el estado.
class WorkOrderDetailScreen extends ConsumerStatefulWidget {
  const WorkOrderDetailScreen({required this.id, super.key});

  final String id;

  @override
  ConsumerState<WorkOrderDetailScreen> createState() => _WorkOrderDetailScreenState();
}

class _WorkOrderDetailScreenState extends ConsumerState<WorkOrderDetailScreen> {
  bool _busy = false;

  bool get _canEdit {
    final auth = ref.read(authControllerProvider);
    return auth is AuthSignedIn && auth.user.role != AppRole.customer;
  }

  bool get _isOwner {
    final auth = ref.read(authControllerProvider);
    return auth is AuthSignedIn && auth.user.role == AppRole.owner;
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
      ref.invalidate(workOrderDetailProvider(widget.id));
      // La lista muestra el estado y el avance de pasos: si no se invalida, al volver
      // atrás el técnico vería datos viejos de lo que él mismo acaba de cambiar.
      ref.invalidate(myWorkOrdersProvider);
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

  Future<void> _changeStatus(WorkOrderStatus status) async {
    final note = await _askForNote(status);
    if (note == null) return; // canceló

    await _run(() async {
      await ref.read(workOrderRepositoryProvider).changeStatus(
            widget.id,
            status,
            note: note.isEmpty ? null : note,
          );
    });
  }

  Future<String?> _askForNote(WorkOrderStatus status) async {
    final controller = TextEditingController();

    return showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Pasar a "${status.label}"'),
        content: TextField(
          controller: controller,
          autofocus: true,
          decoration: const InputDecoration(
            labelText: 'Nota (opcional)',
            helperText: 'El cliente la verá en el seguimiento.',
          ),
          maxLines: 2,
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancelar')),
          FilledButton(
            onPressed: () => Navigator.pop(context, controller.text.trim()),
            child: const Text('Confirmar'),
          ),
        ],
      ),
    );
  }

  Future<void> _addTask() async {
    final controller = TextEditingController();
    final price = TextEditingController();
    final services = ref.read(laborServicesProvider).value ?? const <LaborServiceOption>[];
    String? serviceId;

    final title = await showDialog<String>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setInner) => AlertDialog(
          title: const Text('Nuevo paso'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: controller,
                autofocus: true,
                decoration: const InputDecoration(labelText: '¿Qué hay que hacer?'),
              ),
              const SizedBox(height: 12),
              // El paso se cobra por el servicio del catálogo o por el precio que se escriba
              // aquí. Sin ninguno de los dos no entra en la factura.
              DropdownButtonFormField<String?>(
                initialValue: serviceId,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Mano de obra'),
                items: [
                  const DropdownMenuItem<String?>(value: null, child: Text('Sin servicio')),
                  for (final s in services)
                    DropdownMenuItem<String?>(
                      value: s.id,
                      child: Text('${s.name} · ${_money(s.price)}', overflow: TextOverflow.ellipsis),
                    ),
                ],
                onChanged: (value) => setInner(() {
                  serviceId = value;
                  // Al elegir del catálogo se propone su precio, pero se puede cambiar.
                  price.text = value == null
                      ? ''
                      : services.firstWhere((s) => s.id == value).price.toStringAsFixed(2);
                }),
              ),
              TextField(
                controller: price,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                decoration: const InputDecoration(
                  labelText: 'Precio',
                  prefixText: 'L ',
                  helperText: 'Déjelo vacío si el paso no se cobra.',
                ),
              ),
            ],
          ),
          actions: [
            TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancelar')),
            FilledButton(
              onPressed: () => Navigator.pop(context, controller.text.trim()),
              child: const Text('Agregar'),
            ),
          ],
        ),
      ),
    );

    if (title == null || title.isEmpty) return;
    await _run(() => ref.read(workOrderRepositoryProvider).addTask(
          widget.id,
          title,
          laborServiceId: serviceId,
          laborPrice: _parsePrice(price.text),
        ));
  }

  /// Cambia lo que se cobra por un paso ya creado: un servicio del catálogo, un precio a
  /// mano, o nada.
  Future<void> _changeTaskLabor(WorkOrderTask task) async {
    final services = ref.read(laborServicesProvider).value ?? const <LaborServiceOption>[];

    // Cadena vacía es "sin cobro" y `_custom` es "precio a mano": null queda para cuando se
    // sale del menú sin elegir.
    const custom = '_custom';

    final choice = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: ListView(
          shrinkWrap: true,
          children: [
            ListTile(
              leading: const Icon(Icons.edit_outlined),
              title: const Text('Precio a mano'),
              subtitle: const Text('Para el trabajo que no está en el catálogo'),
              onTap: () => Navigator.pop(context, custom),
            ),
            const Divider(height: 1),
            ListTile(
              title: const Text('Sin cobro de mano de obra'),
              selected: task.laborPrice == null,
              onTap: () => Navigator.pop(context, ''),
            ),
            for (final s in services)
              ListTile(
                title: Text(s.name),
                trailing: Text(_money(s.price)),
                selected: s.id == task.laborServiceId,
                onTap: () => Navigator.pop(context, s.id),
              ),
          ],
        ),
      ),
    );

    if (choice == null) return;

    if (choice == custom) {
      final price = await _askPrice(task);
      if (price == null) return;

      await _run(() => ref.read(workOrderRepositoryProvider).setTaskLabor(
            widget.id,
            task,
            laborServiceId: task.laborServiceId,
            // Cero es quitarle el precio a mano, no cobrar cero.
            laborPrice: price > 0 ? price : null,
          ));
      return;
    }

    await _run(() => ref.read(workOrderRepositoryProvider).setTaskLabor(
          widget.id,
          task,
          laborServiceId: choice.isEmpty ? null : choice,
        ));
  }

  Future<double?> _askPrice(WorkOrderTask task) async {
    final controller = TextEditingController(
      text: task.laborPrice?.toStringAsFixed(2) ?? '',
    );

    return showDialog<double>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(task.title),
        content: TextField(
          controller: controller,
          autofocus: true,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: const InputDecoration(labelText: 'Precio', prefixText: 'L '),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancelar')),
          FilledButton(
            onPressed: () => Navigator.pop(context, _parsePrice(controller.text) ?? 0),
            child: const Text('Guardar'),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final detail = ref.watch(workOrderDetailProvider(widget.id));

    // El catálogo de mano de obra se pide desde que se abre la orden: cuando el técnico va a
    // agregar un paso ya está en memoria. Al Cliente no se le pide: el backend se lo niega.
    if (_canEdit) ref.watch(laborServicesProvider);

    return Scaffold(
      appBar: AppBar(title: Text(detail.asData?.value.number ?? 'Orden')),
      body: detail.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Text(apiErrorMessage(e, 'No se pudo cargar la orden.')),
          ),
        ),
        data: (order) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(workOrderDetailProvider(widget.id)),
          child: ListView(
            padding: const EdgeInsets.all(16),
            children: [
              _Header(order: order),
              const SizedBox(height: 16),
              _Section(
                title: 'Motivo de ingreso',
                child: Text(order.description),
              ),
              _DiagnosisSection(
                order: order,
                canEdit: _canEdit,
                busy: _busy,
                onSave: (text) => _run(() async {
                  await ref.read(workOrderRepositoryProvider).saveDiagnosis(
                        widget.id,
                        description: order.description,
                        diagnosis: text.isEmpty ? null : text,
                        promisedAt: order.promisedAt,
                      );
                }),
              ),
              if (_isOwner)
                _AssignSection(
                  order: order,
                  busy: _busy,
                  technicians: ref.watch(technicianOptionsProvider).value ?? const [],
                  onAssign: (technicianId) => _run(() async {
                    await ref
                        .read(workOrderRepositoryProvider)
                        .assign(widget.id, technicianId);
                  }),
                )
              // Al Cliente le interesa saber quién tiene su moto; al Técnico no, que solo
              // ve las suyas y leería su propio nombre en cada orden.
              else if (!_canEdit && order.assignedTechnicianName != null)
                _Section(
                  title: 'Técnico responsable',
                  child: Text(order.assignedTechnicianName!),
                ),
              _TasksSection(
                order: order,
                canEdit: _canEdit,
                busy: _busy,
                onChangeLabor: _changeTaskLabor,
                onToggle: (task, value) => _run(() async {
                  await ref.read(workOrderRepositoryProvider).completeTask(
                        widget.id,
                        task.id,
                        isDone: value,
                      );
                }),
                onAdd: _addTask,
              ),
              PartsSection(
                order: order,
                canEdit: _canEdit,
                busy: _busy,
                onChanged: () async {
                  ref.invalidate(workOrderDetailProvider(widget.id));
                },
              ),
              PhotoGallery(workOrderId: order.id, canEdit: _canEdit),
              QuotesSection(workOrderId: order.id),
              if (_canEdit && order.allowedNextStatuses.isNotEmpty)
                _Section(
                  title: 'Cambiar estado',
                  child: Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    children: [
                      for (final next in order.allowedNextStatuses)
                        FilledButton.tonal(
                          onPressed: _busy ? null : () => _changeStatus(next),
                          child: Text(next.label),
                        ),
                    ],
                  ),
                ),
              _TimelineSection(entries: order.timeline),
              const SizedBox(height: 32),
            ],
          ),
        ),
      ),
    );
  }
}

class _Header extends StatelessWidget {
  const _Header({required this.order});

  final WorkOrderDetail order;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Icon(
              order.vehicleType == VehicleType.motorcycle
                  ? Icons.two_wheeler
                  : Icons.directions_car,
              color: theme.colorScheme.onSurfaceVariant,
            ),
            const SizedBox(width: 8),
            Expanded(child: Text(order.vehicleLabel, style: theme.textTheme.titleMedium)),
            StatusChip(status: order.status),
          ],
        ),
        const SizedBox(height: 6),
        Text(
          [
            if (order.plate != null) order.plate!,
            order.customerName,
            order.branchName,
            if (order.mileageIn != null) '${order.mileageIn} km',
          ].join(' · '),
          style: theme.textTheme.bodySmall,
        ),
      ],
    );
  }
}

class _Section extends StatelessWidget {
  const _Section({required this.title, required this.child});

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title.toUpperCase(),
            style: theme.textTheme.labelSmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
              letterSpacing: 0.6,
            ),
          ),
          const SizedBox(height: 8),
          child,
        ],
      ),
    );
  }
}

/// El diagnóstico se escribe aquí, junto al motivo de ingreso: se lee la queja del cliente y
/// debajo lo que el taller encontró. Es lo que después se copia a la cotización y lo que el
/// cliente ve en el seguimiento, así que tiene que poder escribirse desde el teléfono —que es
/// lo que el técnico tiene en la mano cuando lo sabe, no una computadora en la oficina.
class _DiagnosisSection extends StatefulWidget {
  const _DiagnosisSection({
    required this.order,
    required this.canEdit,
    required this.busy,
    required this.onSave,
  });

  final WorkOrderDetail order;
  final bool canEdit;
  final bool busy;
  final Future<void> Function(String text) onSave;

  @override
  State<_DiagnosisSection> createState() => _DiagnosisSectionState();
}

class _DiagnosisSectionState extends State<_DiagnosisSection> {
  late final TextEditingController _controller =
      TextEditingController(text: widget.order.diagnosis ?? '');

  @override
  void didUpdateWidget(_DiagnosisSection oldWidget) {
    super.didUpdateWidget(oldWidget);

    // Tras guardar, la pantalla se recarga con el texto ya persistido. Solo se pisa el
    // cuadro si el usuario no está escribiendo algo distinto encima.
    final saved = widget.order.diagnosis ?? '';
    if (saved != (oldWidget.order.diagnosis ?? '') && _controller.text != saved) {
      _controller.text = saved;
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (!widget.canEdit) {
      return widget.order.diagnosis == null
          ? const SizedBox.shrink()
          : _Section(title: 'Diagnóstico', child: Text(widget.order.diagnosis!));
    }

    return _Section(
      title: 'Diagnóstico',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          TextField(
            controller: _controller,
            maxLines: 4,
            minLines: 2,
            textCapitalization: TextCapitalization.sentences,
            decoration: const InputDecoration(
              hintText: 'Qué se encontró: causa, qué hay que cambiar, qué se recomienda…',
              border: OutlineInputBorder(),
            ),
            onChanged: (_) => setState(() {}),
          ),
          const SizedBox(height: 8),
          FilledButton.tonal(
            onPressed: widget.busy || _controller.text.trim() == (widget.order.diagnosis ?? '')
                ? null
                : () => widget.onSave(_controller.text.trim()),
            child: const Text('Guardar diagnóstico'),
          ),
        ],
      ),
    );
  }
}

/// Quién responde por la orden. Lo cambia solo el Dueño, y desde el teléfono porque el
/// reparto del trabajo se decide en el patio: llega una moto urgente y hay que mover a
/// alguien, y nadie va a subir a la oficina a hacerlo en la computadora.
class _AssignSection extends StatelessWidget {
  const _AssignSection({
    required this.order,
    required this.technicians,
    required this.busy,
    required this.onAssign,
  });

  final WorkOrderDetail order;
  final List<TechnicianOption> technicians;
  final bool busy;
  final void Function(String? technicianId) onAssign;

  @override
  Widget build(BuildContext context) {
    // Solo los de la sucursal de la orden: la API rechaza a los demás con un 400, y
    // ofrecerlos sería enseñar opciones que solo pueden dar error.
    final available = technicians.where((t) => t.worksAt(order.branchId)).toList();

    return _Section(
      title: 'Técnico responsable',
      child: DropdownButtonFormField<String?>(
        initialValue: available.any((t) => t.id == order.assignedTechnicianId)
            ? order.assignedTechnicianId
            : null,
        isExpanded: true,
        decoration: const InputDecoration(isDense: true),
        items: [
          const DropdownMenuItem(value: null, child: Text('Sin asignar')),
          for (final technician in available)
            DropdownMenuItem(value: technician.id, child: Text(technician.name)),
        ],
        onChanged: busy ? null : onAssign,
      ),
    );
  }
}

class _TasksSection extends StatelessWidget {
  const _TasksSection({
    required this.order,
    required this.canEdit,
    required this.busy,
    required this.onToggle,
    required this.onAdd,
    required this.onChangeLabor,
  });

  final WorkOrderDetail order;
  final bool canEdit;
  final bool busy;
  final void Function(WorkOrderTask task, bool value) onToggle;
  final VoidCallback onAdd;
  final void Function(WorkOrderTask task) onChangeLabor;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return _Section(
      title: 'Pasos de la reparación',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (order.tasks.isEmpty)
            Text('Todavía no hay pasos.', style: theme.textTheme.bodySmall),
          for (final task in order.tasks) ...[
            CheckboxListTile(
              value: task.isDone,
              onChanged: canEdit && !busy ? (v) => onToggle(task, v ?? false) : null,
              contentPadding: EdgeInsets.zero,
              controlAffinity: ListTileControlAffinity.leading,
              dense: true,
              title: Text(
                task.title,
                style: task.isDone
                    ? const TextStyle(decoration: TextDecoration.lineThrough)
                    : null,
              ),
              subtitle: task.technicianNotes != null || task.actualHours != null
                  ? Text([
                      if (task.actualHours != null) '${task.actualHours} h',
                      if (task.technicianNotes != null) task.technicianNotes!,
                    ].join(' · '))
                  : null,
              // Lo que se va a cobrar por el paso, a la vista: es la parte que más se olvida
              // y la razón más común de que la factura salga corta.
              secondary: canEdit
                  ? TextButton(
                      onPressed: busy ? null : () => onChangeLabor(task),
                      child: Text(
                        task.laborPrice != null ? _money(task.laborPrice!) : 'Sin cobro',
                        style: task.laborPrice == null
                            ? TextStyle(color: theme.colorScheme.onSurfaceVariant)
                            : null,
                      ),
                    )
                  : null,
            ),
            if (canEdit && task.laborServiceName != null)
              Padding(
                padding: const EdgeInsets.only(left: 48, bottom: 4),
                child: Text(task.laborServiceName!, style: theme.textTheme.bodySmall),
              ),
          ],
          if (canEdit && order.tasks.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text('Mano de obra', style: theme.textTheme.bodySmall),
                  Text(_money(order.laborTotal), style: theme.textTheme.titleSmall),
                ],
              ),
            ),
          if (canEdit)
            TextButton.icon(
              onPressed: busy ? null : onAdd,
              icon: const Icon(Icons.add, size: 18),
              label: const Text('Agregar paso'),
            ),
        ],
      ),
    );
  }
}

String _money(double value) => 'L ${value.toStringAsFixed(2)}';

/// Lo tecleado como precio. Vacío, cero o mal escrito es "sin precio", no cero lempiras.
double? _parsePrice(String text) {
  final value = double.tryParse(text.trim().replaceAll(',', '.'));
  return value != null && value > 0 ? value : null;
}

class _TimelineSection extends StatelessWidget {
  const _TimelineSection({required this.entries});

  final List<WorkOrderStatusEntry> entries;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return _Section(
      title: 'Línea de tiempo',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          for (final entry in entries)
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Padding(
                    padding: const EdgeInsets.only(top: 5, right: 10),
                    child: Container(
                      width: 8,
                      height: 8,
                      decoration: BoxDecoration(
                        color: theme.colorScheme.primary,
                        shape: BoxShape.circle,
                      ),
                    ),
                  ),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Text(entry.toStatus.label, style: theme.textTheme.bodyMedium),
                            if (!entry.isVisibleToCustomer) ...[
                              const SizedBox(width: 6),
                              Icon(
                                Icons.visibility_off_outlined,
                                size: 14,
                                color: theme.colorScheme.onSurfaceVariant,
                              ),
                            ],
                          ],
                        ),
                        Text(
                          '${_formatDate(entry.changedAt)} · ${entry.changedByName}',
                          style: theme.textTheme.bodySmall,
                        ),
                        if (entry.note != null)
                          Padding(
                            padding: const EdgeInsets.only(top: 2),
                            child: Text(entry.note!, style: theme.textTheme.bodySmall),
                          ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }

  static String _formatDate(DateTime value) {
    final local = value.toLocal();
    final d = local.day.toString().padLeft(2, '0');
    final m = local.month.toString().padLeft(2, '0');
    final h = local.hour.toString().padLeft(2, '0');
    final min = local.minute.toString().padLeft(2, '0');
    return '$d/$m $h:$min';
  }
}

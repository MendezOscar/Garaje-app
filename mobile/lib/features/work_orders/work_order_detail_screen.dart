import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_client.dart';
import '../../core/api/inventory_repository.dart';
import '../../core/api/job_template_repository.dart';
import '../../core/api/sale_repository.dart';
import '../../core/api/staff_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/current_user.dart';
import '../../core/models/work_order.dart';
import '../shared/status_chip.dart';
import 'invoice_section.dart';
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

  Future<void> _addTask(bool catalogLabor) async {
    final controller = TextEditingController();
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
              // En modo manual el paso va suelto: el precio es uno solo para toda la orden.
              if (catalogLabor) ...[
                const SizedBox(height: 12),
                DropdownButtonFormField<String?>(
                  initialValue: serviceId,
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: 'Mano de obra',
                    helperText: 'Sin servicio, el paso no se cobra.',
                  ),
                  items: [
                    const DropdownMenuItem<String?>(value: null, child: Text('Sin cobro')),
                    for (final s in services)
                      DropdownMenuItem<String?>(
                        value: s.id,
                        child: Text('${s.name} · ${_money(s.price)}',
                            overflow: TextOverflow.ellipsis),
                      ),
                  ],
                  onChanged: (value) => setInner(() => serviceId = value),
                ),
              ],
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
    await _run(() => ref
        .read(workOrderRepositoryProvider)
        .addTask(widget.id, title, laborServiceId: catalogLabor ? serviceId : null));
  }

  /// Arma la orden con un trabajo frecuente: anexa sus pasos y propone sus repuestos.
  ///
  /// Los repuestos no se cargan solos: cargarlos descuenta la bodega, y aquí el trabajo apenas
  /// empieza. Se ofrecen en una hoja para irlos cargando conforme se instalan.
  Future<void> _applyTemplate() async {
    final templates = ref.read(jobTemplatesProvider).value ?? const <JobTemplate>[];
    if (templates.isEmpty) return;

    final choice = await showModalBottomSheet<JobTemplate>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: ListView(
          shrinkWrap: true,
          children: [
            for (final t in templates)
              ListTile(
                title: Text(t.name),
                subtitle: Text(
                  '${t.taskCount} paso${t.taskCount == 1 ? '' : 's'} · '
                  '${t.partCount} repuesto${t.partCount == 1 ? '' : 's'}',
                ),
                trailing: Text(_money(t.total)),
                onTap: () => Navigator.pop(context, t),
              ),
          ],
        ),
      ),
    );

    if (choice == null) return;

    ApplyTemplateResult? result;
    await _run(() async {
      result = await ref.read(jobTemplateRepositoryProvider).apply(widget.id, choice.id);
    });

    final sugeridos = result?.suggestedParts ?? const <SuggestedPart>[];
    if (!mounted || sugeridos.isEmpty) return;

    await showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (_) => _SuggestedPartsSheet(workOrderId: widget.id, parts: sugeridos),
    );

    if (mounted) ref.invalidate(workOrderDetailProvider(widget.id));
  }

  /// Cambia el servicio del catálogo que le pone precio a un paso.
  Future<void> _changeTaskLabor(WorkOrderTask task) async {
    final services = ref.read(laborServicesProvider).value ?? const <LaborServiceOption>[];

    final choice = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: ListView(
          shrinkWrap: true,
          children: [
            ListTile(
              title: const Text('Sin cobro de mano de obra'),
              selected: task.laborServiceId == null,
              // Cadena vacía y no null: null es "se salió del menú sin elegir".
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

    await _run(() => ref.read(workOrderRepositoryProvider).setTaskLabor(
          widget.id,
          task,
          laborServiceId: choice.isEmpty ? null : choice,
        ));
  }

  /// Elige de dónde sale el precio de la mano de obra de la orden. En manual pide el total,
  /// porque cambiar de modo sin número dejaría la orden sin nada que cobrar.
  Future<void> _changeLaborMode(WorkOrderDetail order, LaborMode mode) async {
    if (order.laborMode == mode) return;

    double? total;
    if (mode == LaborMode.manual) {
      total = await _askTotal(order);
      if (total == null) return;
    }

    await _run(() => ref
        .read(workOrderRepositoryProvider)
        .setLaborMode(widget.id, mode, total: total));
  }

  Future<double?> _askTotal(WorkOrderDetail order) async {
    final controller = TextEditingController(
      text: order.manualLaborTotal?.toStringAsFixed(2) ?? '',
    );

    return showDialog<double>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Total de mano de obra'),
        content: TextField(
          controller: controller,
          autofocus: true,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: const InputDecoration(
            labelText: 'Total',
            prefixText: 'L ',
            helperText: 'Es lo que va a la factura por el trabajo.',
          ),
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
    if (_canEdit) ref.watch(jobTemplatesProvider);

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
              // Va arriba porque el primer mensaje se manda al recibir el vehículo, con el
              // cliente todavía en el mostrador.
              if (_canEdit) _NotifySection(order: order, isOwner: _isOwner),
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
                isOwner: _isOwner,
                hasTemplates: (ref.watch(jobTemplatesProvider).value ?? const []).isNotEmpty,
                onApplyTemplate: _applyTemplate,
                onAdd: () => _addTask(order.isCatalogLabor),
                onChangeMode: (mode) => _changeLaborMode(order, mode),
                onEditTotal: () async {
                  final total = await _askTotal(order);
                  if (total == null) return;
                  await _run(() => ref
                      .read(workOrderRepositoryProvider)
                      .setLaborMode(widget.id, LaborMode.manual, total: total));
                },
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
              // Cobrar es lo último del trabajo y pasa en el taller, con el cliente
              // enfrente: si solo estuviera en el web habría que subir a la computadora
              // con el vehículo ya entregado.
              if (_isOwner) InvoiceSection(order: order),
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
              _VehicleHistorySection(order: order),
              _TimelineSection(entries: order.timeline),
              const SizedBox(height: 32),
            ],
          ),
        ),
      ),
    );
  }
}

/// Los tres mensajes de WhatsApp que llevan al cliente al enlace de seguimiento.
///
/// El enlace es el mismo toda la reparación: se manda al recibir el vehículo, otra vez cuando
/// está listo y al final con la factura. Sirve al cliente que no va a instalar la app, que en
/// un taller son casi todos.
class _NotifySection extends ConsumerStatefulWidget {
  const _NotifySection({required this.order, required this.isOwner});

  final WorkOrderDetail order;
  final bool isOwner;

  @override
  ConsumerState<_NotifySection> createState() => _NotifySectionState();
}

class _NotifySectionState extends ConsumerState<_NotifySection> {
  bool _busy = false;

  Future<void> _send(String kind) async {
    setState(() => _busy = true);
    try {
      final url = await ref
          .read(workOrderRepositoryProvider)
          .trackingLink(widget.order.id, kind);

      // externalApplication: abre WhatsApp de verdad, no una vista web dentro de la app.
      final launched = await launchUrl(Uri.parse(url), mode: LaunchMode.externalApplication);
      if (!launched) _snack('No se pudo abrir WhatsApp.');
    } catch (e) {
      _snack(apiErrorMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _snack(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) {
    // Las ventas solo las ve el Dueño: al Técnico la API le responde 403, así que ni se
    // preguntan y él no ve el botón de la factura.
    final conFactura = widget.isOwner &&
        (ref.watch(workOrderSalesProvider(widget.order.id)).value ?? const [])
            .any((s) => !s.isVoided);

    return _Section(
      title: 'Avisar al cliente',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Le llega un enlace donde ve el avance, las fotos y —al cerrar— su factura. '
            'No necesita instalar nada.',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              FilledButton.icon(
                onPressed: _busy ? null : () => _send('received'),
                icon: const Icon(Icons.share_outlined, size: 18),
                label: const Text('Mandar el enlace'),
              ),
              FilledButton.tonal(
                onPressed: _busy ? null : () => _send('ready'),
                child: const Text('Ya está listo'),
              ),
              if (conFactura)
                FilledButton.tonal(
                  onPressed: _busy ? null : () => _send('invoice'),
                  child: const Text('Mandar la factura'),
                ),
            ],
          ),
        ],
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

/// Las demás órdenes del mismo vehículo, entregadas incluidas. Responde la pregunta del
/// mostrador cuando el cliente vuelve a los dos meses: qué se le hizo y cuándo.
/// Las visitas anteriores del vehículo, plegadas.
///
/// Un vehículo con años de taller trae veinte, y desplegadas empujan la línea de tiempo fuera
/// de la pantalla. Cerrado ya contesta lo que casi siempre se pregunta —cuántas veces vino y
/// cuándo la última—, así que abrirlo es para leer el detalle, no para enterarse.
class _VehicleHistorySection extends ConsumerStatefulWidget {
  const _VehicleHistorySection({required this.order});

  final WorkOrderDetail order;

  @override
  ConsumerState<_VehicleHistorySection> createState() => _VehicleHistorySectionState();
}

class _VehicleHistorySectionState extends ConsumerState<_VehicleHistorySection> {
  bool _abierto = false;

  @override
  Widget build(BuildContext context) {
    final order = widget.order;
    final history = ref.watch(vehicleHistoryProvider(order.vehicleId));
    final theme = Theme.of(context);

    return history.maybeWhen(
      data: (items) {
        final otras = items.where((o) => o.id != order.id).toList();
        if (otras.isEmpty) return const SizedBox.shrink();

        return _Section(
          title: 'Historial del vehículo',
          child: Column(
            children: [
              InkWell(
                onTap: () => setState(() => _abierto = !_abierto),
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 4),
                  child: Row(
                    children: [
                      Expanded(
                        child: Text(
                          '${otras.length} '
                          '${otras.length == 1 ? 'visita antes' : 'visitas antes'}, '
                          'la última el ${_fecha(otras.first.openedAt)}',
                          style: theme.textTheme.bodyMedium,
                        ),
                      ),
                      Icon(_abierto ? Icons.expand_less : Icons.expand_more),
                    ],
                  ),
                ),
              ),
              if (_abierto)
                for (final previa in otras)
                  ListTile(
                    contentPadding: EdgeInsets.zero,
                    visualDensity: VisualDensity.compact,
                    title: Row(
                      children: [
                        Text(previa.number, style: theme.textTheme.titleSmall),
                        const SizedBox(width: 8),
                        StatusChip(status: previa.status),
                      ],
                    ),
                    subtitle: Text(
                      '${_fecha(previa.openedAt)} · ${previa.description}',
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                    ),
                    trailing: const Icon(Icons.chevron_right),
                    // `push` y no `go`: se vuelve a esta orden con la flecha de atrás.
                    onTap: () => context.push('/ordenes/${previa.id}'),
                  ),
            ],
          ),
        );
      },
      // Mientras carga, o si falla, no se dibuja nada: es información de apoyo y no puede
      // ensuciar la pantalla de la orden que se está atendiendo.
      orElse: () => const SizedBox.shrink(),
    );
  }

  static String _fecha(DateTime value) {
    final d = value.toLocal();
    return '${d.day.toString().padLeft(2, '0')}/${d.month.toString().padLeft(2, '0')}/${d.year}';
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
    required this.isOwner,
    required this.busy,
    required this.onToggle,
    required this.onAdd,
    required this.onChangeLabor,
    required this.onChangeMode,
    required this.onEditTotal,
    required this.hasTemplates,
    required this.onApplyTemplate,
  });

  final WorkOrderDetail order;
  final bool canEdit;
  final bool isOwner;
  final bool busy;
  final bool hasTemplates;
  final VoidCallback onApplyTemplate;
  final void Function(WorkOrderTask task, bool value) onToggle;
  final VoidCallback onAdd;
  final void Function(WorkOrderTask task) onChangeLabor;
  final void Function(LaborMode mode) onChangeMode;
  final VoidCallback onEditTotal;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final catalog = order.isCatalogLabor;

    return _Section(
      title: 'Pasos de la reparación',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Las dos formas de cobrar la mano de obra son excluyentes: o cada paso lleva su
          // servicio del catálogo, o se cobra un total por toda la orden.
          if (isOwner)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: SegmentedButton<LaborMode>(
                segments: const [
                  ButtonSegment(value: LaborMode.catalog, label: Text('Catálogo')),
                  ButtonSegment(value: LaborMode.manual, label: Text('A mano')),
                ],
                selected: {order.laborMode},
                showSelectedIcon: false,
                onSelectionChanged: busy ? null : (s) => onChangeMode(s.first),
              ),
            ),
          // Antes de la lista: en una orden vacía, armarla de un toque es lo que hay que
          // ofrecer antes de que nadie empiece a teclear de pie y con las manos sucias.
          if (canEdit && hasTemplates)
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                onPressed: busy ? null : onApplyTemplate,
                icon: const Icon(Icons.bolt_outlined, size: 18),
                label: const Text('Aplicar trabajo frecuente'),
              ),
            ),
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
              secondary: canEdit && catalog
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
            if (canEdit && catalog && task.laborServiceName != null)
              Padding(
                padding: const EdgeInsets.only(left: 48, bottom: 4),
                child: Text(task.laborServiceName!, style: theme.textTheme.bodySmall),
              ),
          ],
          if (canEdit)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(
                    catalog ? 'Mano de obra' : 'Mano de obra (total)',
                    style: theme.textTheme.bodySmall,
                  ),
                  Row(
                    children: [
                      Text(_money(order.laborTotal), style: theme.textTheme.titleSmall),
                      if (isOwner && !catalog)
                        IconButton(
                          tooltip: 'Cambiar el total',
                          icon: const Icon(Icons.edit_outlined, size: 18),
                          onPressed: busy ? null : onEditTotal,
                        ),
                    ],
                  ),
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

/// Los repuestos que el trabajo lleva, para irlos cargando conforme se instalan.
///
/// Se carga uno a uno y no todos de golpe: cada carga descuenta la bodega, y si de uno no hay
/// existencia debe fallar solo esa línea —no el resto, y desde luego no los pasos, que ya
/// quedaron puestos—.
class _SuggestedPartsSheet extends ConsumerStatefulWidget {
  const _SuggestedPartsSheet({required this.workOrderId, required this.parts});

  final String workOrderId;
  final List<SuggestedPart> parts;

  @override
  ConsumerState<_SuggestedPartsSheet> createState() => _SuggestedPartsSheetState();
}

class _SuggestedPartsSheetState extends ConsumerState<_SuggestedPartsSheet> {
  final _cargados = <int>{};
  int? _cargando;

  Future<void> _cargar(int i, SuggestedPart part) async {
    setState(() => _cargando = i);
    try {
      await ref.read(inventoryRepositoryProvider).addPart(
            widget.workOrderId,
            partId: part.partId!,
            quantity: part.quantity,
          );
      if (mounted) setState(() => _cargados.add(i));
    } catch (e) {
      if (mounted) {
        // El 409 de existencia insuficiente dice cuánto queda: se muestra tal cual.
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e, 'No se pudo cargar el repuesto.'))),
        );
      }
    } finally {
      if (mounted) setState(() => _cargando = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return SafeArea(
      child: ListView(
        shrinkWrap: true,
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
        children: [
          Text('Repuestos de este trabajo', style: theme.textTheme.titleMedium),
          const SizedBox(height: 4),
          Text(
            'Cárguelos al usarlos, no ahora: al cargarlos salen de la bodega.',
            style: theme.textTheme.bodySmall,
          ),
          const SizedBox(height: 8),
          for (final (i, part) in widget.parts.indexed)
            ListTile(
              contentPadding: EdgeInsets.zero,
              dense: true,
              title: Text('${_cantidad(part.quantity)} ${part.unit} · ${part.partName}'),
              subtitle: part.partId == null
                  ? const Text('Fuera del catálogo: se carga aparte, con su precio')
                  : part.isShort
                      ? Text(
                          'Quedan ${_cantidad(part.available)}',
                          style: TextStyle(color: theme.colorScheme.error),
                        )
                      : null,
              trailing: part.partId == null
                  ? null
                  : _cargados.contains(i)
                      ? Icon(Icons.check, color: theme.colorScheme.primary)
                      : TextButton(
                          onPressed: _cargando != null ? null : () => _cargar(i, part),
                          child: const Text('Cargar'),
                        ),
            ),
        ],
      ),
    );
  }
}

/// Sin decimales cuando no hacen falta: «2 unidad» se lee mejor que «2.00 unidad».
String _cantidad(double value) =>
    value == value.roundToDouble() ? value.toStringAsFixed(0) : value.toStringAsFixed(2);

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

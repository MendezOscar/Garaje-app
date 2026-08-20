import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_client.dart';
import '../../core/api/inventory_repository.dart';
import '../../core/api/job_template_repository.dart';
import '../../core/api/media_repository.dart';
import '../../core/api/quote_repository.dart';
import '../../core/api/sale_repository.dart';
import '../../core/api/staff_repository.dart';
import '../../core/api/tenant_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/current_user.dart';
import '../../core/models/quote.dart';
import '../../core/sync/upload_queue.dart';
import '../../core/models/work_order.dart';
import '../../core/theme/garaj_brand.dart';
import '../shared/status_chip.dart';
import 'invoice_section.dart';
import 'photo_capture.dart';
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

  bool get _isTechnician {
    final auth = ref.read(authControllerProvider);
    return auth is AuthSignedIn && auth.user.role == AppRole.technician;
  }

  /// Días de atraso sobre la fecha prometida, o null si no hay promesa, todavía no vence o ya
  /// se entregó. Es lo primero que hay que saber al abrir la orden: si el cliente ya tiene
  /// motivo para llamar.
  static int? _atraso(WorkOrderDetail order) {
    final prometida = order.promisedAt;
    if (prometida == null) return null;
    if (order.status == WorkOrderStatus.delivered ||
        order.status == WorkOrderStatus.cancelled) {
      return null;
    }

    final dias = DateTime.now().difference(prometida).inDays;
    return dias >= 1 ? dias : null;
  }

  /// Abre una sección en su propia pantalla, con los datos vivos de la orden.
  ///
  /// El teléfono no da para doce secciones desplegadas: la de arriba se lee y las demás se
  /// visitan. La página vuelve a leer la orden del provider, así que lo que se cambia dentro
  /// —cargar un repuesto, guardar el diagnóstico— se ve sin volver atrás.
  void _abrirSeccion(Widget Function(WorkOrderDetail order) contenido) {
    Navigator.of(context).push(
      MaterialPageRoute(builder: (_) => _SeccionPagina(id: widget.id, contenido: contenido)),
    );
  }

  /// Quién responde por la orden. Se decide en el patio —llega una moto urgente y hay que
  /// mover a alguien—, así que se cambia desde el teléfono y no desde la computadora.
  Future<void> _asignarTecnico(WorkOrderDetail order) async {
    final tecnicos = (ref.read(technicianOptionsProvider).value ?? const <TechnicianOption>[])
        .where((t) => t.worksAt(order.branchId))
        .toList();

    final elegido = await showModalBottomSheet<String?>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: ListView(
          shrinkWrap: true,
          children: [
            ListTile(
              title: Text(
                'Técnico responsable',
                style: Theme.of(context).textTheme.titleMedium,
              ),
            ),
            ListTile(
              title: const Text('Sin asignar'),
              trailing: order.assignedTechnicianId == null ? const Icon(Icons.check) : null,
              onTap: () => Navigator.pop(context, ''),
            ),
            for (final tecnico in tecnicos)
              ListTile(
                title: Text(tecnico.name),
                trailing:
                    order.assignedTechnicianId == tecnico.id ? const Icon(Icons.check) : null,
                onTap: () => Navigator.pop(context, tecnico.id),
              ),
          ],
        ),
      ),
    );

    if (elegido == null) return;
    // Cadena vacía es «sin asignar»: null significaría que la hoja se cerró sin elegir.
    await _run(() => ref
        .read(workOrderRepositoryProvider)
        .assign(widget.id, elegido.isEmpty ? null : elegido));
  }

  /// Las dos formas de cobrar la mano de obra son excluyentes: o cada paso lleva su servicio
  /// del catálogo, o los pasos van sueltos y se cobra un total por toda la orden.
  Future<void> _comoSeCobra(WorkOrderDetail order) async {
    final modo = await showModalBottomSheet<LaborMode>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              title: Text(
                'Cómo se cobra la mano de obra',
                style: Theme.of(context).textTheme.titleMedium,
              ),
            ),
            ListTile(
              title: const Text('Con el catálogo'),
              subtitle: const Text('Cada paso lleva su servicio y su precio'),
              trailing: order.isCatalogLabor ? const Icon(Icons.check) : null,
              onTap: () => Navigator.pop(context, LaborMode.catalog),
            ),
            ListTile(
              title: const Text('A mano'),
              subtitle: const Text('Los pasos van sueltos y se cobra un total'),
              trailing: order.isCatalogLabor ? null : const Icon(Icons.check),
              onTap: () => Navigator.pop(context, LaborMode.manual),
            ),
          ],
        ),
      ),
    );

    if (modo == null) return;

    // Pasar a «a mano» pide el total en el mismo gesto: un total en cero no es un cobro, es
    // una factura corta esperando a que alguien se acuerde.
    if (modo == LaborMode.manual) {
      final total = await _askTotal(order);
      if (total == null) return;
      await _run(() => ref
          .read(workOrderRepositoryProvider)
          .setLaborMode(widget.id, LaborMode.manual, total: total));
      return;
    }

    if (order.laborMode != modo) await _changeLaborMode(order, modo);
  }

  /// Los tres mensajes de WhatsApp, en una hoja. Es el botón de la derecha de la barra fija:
  /// avisar es lo que se hace justo después de mover el estado, con el cliente esperando.
  void _avisar(WorkOrderDetail order) {
    showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (_) => Padding(
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
        child: _NotifySection(order: order, isOwner: _isOwner),
      ),
    );
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

  /// El estado siguiente «hacia adelante»: el menor de los permitidos que esté por encima del
  /// actual, sin contar los que detienen la orden ni la cancelación.
  static WorkOrderStatus? _siguienteNatural(
    WorkOrderDetail order,
    List<WorkOrderStatus> permitidos,
  ) {
    final candidatos = permitidos
        .where((s) => !s.isBlocked && s != WorkOrderStatus.cancelled)
        .toList()
      ..sort((a, b) => a.value.compareTo(b.value));

    if (candidatos.isEmpty) return null;

    return candidatos.firstWhere(
      (s) => s.value > order.status.value,
      orElse: () => candidatos.first,
    );
  }

  Future<void> _completeTask(WorkOrderTask task) =>
      _run(() => ref
          .read(workOrderRepositoryProvider)
          .completeTask(widget.id, task.id, isDone: true));

  Future<void> _tomarFoto() async {
    setState(() => _busy = true);
    try {
      final tomada = await capturarFoto(ref, ownerId: widget.id);
      if (tomada && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Foto guardada en la orden.')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e, 'No se pudo guardar la foto.'))),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// Detener el trabajo diciendo por qué.
  ///
  /// Antes esto era elegir «Esperando repuestos» entre nueve estados y, con suerte, escribir
  /// una nota. El motivo es lo que el mostrador necesita para comprar el repuesto o para
  /// llamar al cliente, así que aquí se pregunta primero y el estado sale de la respuesta.
  Future<void> _detener(WorkOrderDetail order) async {
    final opciones = [
      (
        WorkOrderStatus.waitingParts,
        'Falta un repuesto',
        'El mostrador lo ve y lo compra.',
        Icons.inventory_2_outlined,
      ),
      (
        WorkOrderStatus.waitingApproval,
        'Falta que el cliente apruebe',
        'Se le manda la cotización.',
        Icons.chat_bubble_outline,
      ),
    ].where((o) => order.allowedNextStatuses.contains(o.$1)).toList();

    if (opciones.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Esta orden no se puede detener en su estado actual.')),
      );
      return;
    }

    final controller = TextEditingController();
    var elegido = opciones.first.$1;

    final confirmado = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (context) => StatefulBuilder(
        builder: (context, setInner) => SafeArea(
          child: Padding(
            padding: EdgeInsets.fromLTRB(
              16,
              0,
              16,
              16 + MediaQuery.viewInsetsOf(context).bottom,
            ),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Detener el trabajo', style: Theme.of(context).textTheme.titleLarge),
                const SizedBox(height: 4),
                Text(
                  'El mostrador lo ve al instante y queda escrito por qué.',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                const SizedBox(height: 12),
                for (final (status, titulo, pie, icono) in opciones)
                  ListTile(
                    contentPadding: EdgeInsets.zero,
                    leading: Icon(icono),
                    title: Text(titulo),
                    subtitle: Text(pie),
                    selected: status == elegido,
                    trailing: status == elegido
                        ? Icon(Icons.check_circle, color: Theme.of(context).colorScheme.primary)
                        : null,
                    onTap: () => setInner(() => elegido = status),
                  ),
                const SizedBox(height: 8),
                TextField(
                  controller: controller,
                  maxLines: 2,
                  decoration: const InputDecoration(
                    labelText: 'Qué falta',
                    helperText: 'El cliente lo ve en su seguimiento.',
                  ),
                ),
                const SizedBox(height: 16),
                FilledButton(
                  onPressed: () => Navigator.pop(context, true),
                  child: const Text('Detener y avisar'),
                ),
              ],
            ),
          ),
        ),
      ),
    );

    if (confirmado != true) return;

    final nota = controller.text.trim();
    await _run(() => ref.read(workOrderRepositoryProvider).changeStatus(
          widget.id,
          elegido,
          note: nota.isEmpty ? null : nota,
        ));
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

    final cargada = detail.asData?.value;
    final siguientes = cargada == null || !_canEdit
        ? const <WorkOrderStatus>[]
        : cargada.allowedNextStatuses;

    // Para el Técnico la acción del día no es mover el estado, es marcar el paso que está
    // haciendo: el estado lo mueve el mostrador, o él mismo cuando ya no queda ningún paso.
    final pendientes = (cargada?.tasks ?? const <WorkOrderTask>[])
        .where((t) => !t.isDone)
        .toList()
      ..sort((a, b) => a.sequence.compareTo(b.sequence));
    final siguientePaso = _canEdit && !_isOwner && pendientes.isNotEmpty ? pendientes.first : null;

    // El botón fijo lleva el paso natural hacia adelante, no el primero de la lista: desde
    // «En proceso» el backend permite antes «Esperando repuestos», que es un frenazo y no un
    // avance. Detener y cancelar viven en el menú, que es donde se buscan a propósito.
    final natural = cargada == null ? null : _siguienteNatural(cargada, siguientes);

    return Scaffold(
      appBar: AppBar(
        title: Text(cargada?.number ?? 'Orden'),
        actions: [
          // El estado siguiente está fijo abajo; los otros caminos —devolverla a diagnóstico,
          // cancelarla, detenerla— son de vez en cuando y caben en el menú.
          if (cargada != null && _canEdit)
            PopupMenuButton<Object>(
              tooltip: 'Más acciones',
              onSelected: _busy
                  ? null
                  : (value) {
                      switch (value) {
                        case 'detener':
                          _detener(cargada);
                        case 'tecnico':
                          _asignarTecnico(cargada);
                        default:
                          _changeStatus(value as WorkOrderStatus);
                      }
                    },
              itemBuilder: (context) => [
                // Detener es lo que el técnico necesitaba y no tenía: hasta hoy elegía
                // «Esperando repuestos» en una lista de estados, sin decir qué falta.
                if (siguientes.any((s) => s.isBlocked))
                  const PopupMenuItem(
                    value: 'detener',
                    child: ListTile(
                      contentPadding: EdgeInsets.zero,
                      leading: Icon(Icons.pause_circle_outline),
                      title: Text('Detener el trabajo'),
                    ),
                  ),
                for (final next in siguientes)
                  if (next != natural && !next.isBlocked)
                    PopupMenuItem(value: next, child: Text('Pasar a ${next.label}')),
                // El reparto del trabajo se hace de vez en cuando y antes ocupaba una
                // sección fija arriba de los pasos.
                if (_isOwner)
                  const PopupMenuItem(value: 'tecnico', child: Text('Asignar técnico')),
              ],
            ),
        ],
      ),
      // La acción del día no se busca desplazando: se queda al alcance del pulgar. Antes
      // «Cambiar estado» era una sección más en una torre de doce, casi al final.
      bottomNavigationBar: siguientePaso == null && natural == null
          ? null
          : _ActionBar(
              // El Técnico ve el paso; el Dueño, el estado. Cada uno con el botón de la
              // derecha que usa: la cámara el que trabaja, el WhatsApp el que avisa.
              label: siguientePaso != null
                  ? 'Marcar «${siguientePaso.title}»'
                  : 'Pasar a ${natural!.label}',
              icon: _isOwner ? Icons.chat_outlined : Icons.photo_camera_outlined,
              busy: _busy,
              onMain: siguientePaso != null
                  ? () => _completeTask(siguientePaso)
                  : () => _changeStatus(natural!),
              onSecondary: _isOwner ? () => _avisar(cargada!) : _tomarFoto,
            ),
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
          // Lo de arriba es el trabajo: en qué va la orden y qué pasos faltan. Lo demás
          // —repuestos, fotos, cobro, historial— son renglones que llevan a su pantalla.
          // Antes eran doce secciones desplegadas una debajo de otra, y los pasos, que es lo
          // que se viene a ver, quedaban a tres pantallas de desplazamiento.
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 32),
            children: [
              _Header(
                order: order,
                atraso: _atraso(order),
                mostrarTecnico: !_isTechnician,
              ),
              const SizedBox(height: 14),
              // El orden es el del trabajo: se diagnostica, se ve por cuánto va, se arman
              // los pasos, se cargan los repuestos, se cotiza, se fotografía, se avisa y se
              // cobra. Lo que solo se consulta va al final.
              if (_canEdit)
                _FilaDiagnostico(
                  order: order,
                  onTap: () => _abrirSeccion(
                    (order) => _DiagnosisSection(
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
                  ),
                )
              else if (order.diagnosis != null)
                _Fila(
                  icono: Icons.assignment_outlined,
                  titulo: 'Diagnóstico',
                  detalle: order.diagnosis!,
                  onTap: () => _abrirSeccion(
                    (order) => _DiagnosisSection(
                      order: order,
                      canEdit: false,
                      busy: false,
                      onSave: (_) async {},
                    ),
                  ),
                ),
              if (_isOwner) _TotalCard(order: order),
              _TasksCard(
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
                onAdd: () => _addTask(order.isCatalogLabor),
                mostrarManoDeObra: _canEdit && !_isOwner,
                siguienteId: siguientePaso?.id,
                hasTemplates:
                    (ref.watch(jobTemplatesProvider).value ?? const []).isNotEmpty,
                onApplyTemplate: _applyTemplate,
                onComoSeCobra: _isOwner ? () => _comoSeCobra(order) : null,
              ),
              const SizedBox(height: 12),
              if (_canEdit || order.parts.isNotEmpty)
                _Fila(
                  icono: Icons.inventory_2_outlined,
                  titulo: 'Repuestos',
                  detalle: order.parts.isEmpty
                      ? 'Sin repuestos cargados'
                      : '${order.parts.length} '
                          '${order.parts.length == 1 ? 'línea' : 'líneas'}'
                          ' · ${_money(order.partsTotal)}',
                  onTap: () => _abrirSeccion(
                    (order) => PartsSection(
                      order: order,
                      canEdit: _canEdit,
                      busy: _busy,
                      onChanged: () async =>
                          ref.invalidate(workOrderDetailProvider(widget.id)),
                    ),
                  ),
                ),
              if (!_isTechnician)
                _FilaCotizaciones(
                  workOrderId: order.id,
                  isOwner: _isOwner,
                  onTap: () => _abrirSeccion(
                    (order) => QuotesSection(workOrderId: order.id),
                  ),
                ),
              _FilaFotos(
                workOrderId: order.id,
                onTap: () => _abrirSeccion(
                  (order) => PhotoGallery(ownerId: order.id, canEdit: _canEdit),
                ),
              ),
              // El Técnico también avisa: muchas veces es él quien entrega el vehículo. El
              // Dueño además lo tiene en la barra fija, que es donde cae la mano después de
              // mover el estado.
              if (_canEdit)
                _Fila(
                  icono: Icons.chat_outlined,
                  titulo: 'Avisar al cliente',
                  detalle: 'Mandarle el enlace de seguimiento por WhatsApp',
                  onTap: () => _avisar(order),
                ),
              // Cobrar es lo último del trabajo y pasa en el taller, con el cliente
              // enfrente: si solo estuviera en el web habría que subir a la computadora
              // con el vehículo ya entregado.
              if (_isOwner)
                _FilaCobro(
                  workOrderId: order.id,
                  onTap: () => _abrirSeccion((order) => InvoiceSection(order: order)),
                ),
              _FilaHistorial(
                order: order,
                onTap: () => _abrirSeccion(
                  (order) => _VehicleHistorySection(order: order, siempreAbierto: true),
                ),
              ),
              _Fila(
                icono: Icons.history_outlined,
                titulo: 'Línea de tiempo',
                detalle: '${order.timeline.length} '
                    '${order.timeline.length == 1 ? 'cambio de estado' : 'cambios de estado'}',
                onTap: () => _abrirSeccion(
                  (order) => _TimelineSection(entries: order.timeline),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// La acción de hoy, fija abajo: el paso que sigue para el Técnico, el estado para el Dueño.
class _ActionBar extends StatelessWidget {
  const _ActionBar({
    required this.label,
    required this.icon,
    required this.busy,
    required this.onMain,
    required this.onSecondary,
  });

  final String label;
  final IconData icon;
  final bool busy;
  final VoidCallback onMain;
  final VoidCallback onSecondary;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      decoration: BoxDecoration(
        color: theme.colorScheme.surface,
        border: Border(top: BorderSide(color: theme.dividerColor)),
      ),
      child: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 10, 16, 10),
          child: Row(
            children: [
              Expanded(
                child: FilledButton(
                  onPressed: busy ? null : onMain,
                  child: Text(label, maxLines: 1, overflow: TextOverflow.ellipsis),
                ),
              ),
              const SizedBox(width: 8),
              SizedBox(
                width: 52,
                height: 48,
                child: OutlinedButton(
                  onPressed: busy ? null : onSecondary,
                  style: OutlinedButton.styleFrom(
                    padding: EdgeInsets.zero,
                    shape: const RoundedRectangleBorder(
                      borderRadius: BorderRadius.all(Radius.circular(6)),
                    ),
                    side: BorderSide(color: theme.dividerColor),
                  ),
                  child: Icon(icon, size: 20),
                ),
              ),
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

/// El encabezado: en qué estado va, si está atrasada, de quién es y por qué entró.
///
/// El estado y el atraso van juntos y arriba porque son la pregunta con la que se abre la
/// orden. El motivo de ingreso va sin rótulo: es la primera frase de la pantalla y no
/// necesita que nadie le explique qué es.
class _Header extends StatelessWidget {
  const _Header({required this.order, required this.atraso, required this.mostrarTecnico});

  final WorkOrderDetail order;
  final int? atraso;

  /// Quién tiene el vehículo. Al Cliente le importa —es lo que preguntaría por teléfono— y al
  /// Dueño también; al Técnico no, que leería su propio nombre en cada orden.
  final bool mostrarTecnico;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Wrap(
          spacing: 8,
          runSpacing: 6,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            StatusChip(status: order.status),
            if (atraso != null)
              _ChipAtraso(dias: atraso!),
          ],
        ),
        const SizedBox(height: 10),
        Row(
          children: [
            Icon(
              order.vehicleType == VehicleType.motorcycle
                  ? Icons.two_wheeler
                  : Icons.directions_car,
              size: 20,
              color: theme.colorScheme.onSurfaceVariant,
            ),
            const SizedBox(width: 8),
            Flexible(
              child: Text(
                order.vehicleLabel,
                style: theme.textTheme.titleMedium,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
            if (order.plate != null) ...[
              const SizedBox(width: 8),
              _Placa(order.plate!),
            ],
          ],
        ),
        const SizedBox(height: 4),
        Text(
          [
            order.customerName,
            order.customerPhone,
            if (order.promisedAt != null) 'prometida ${_fechaCorta(order.promisedAt!)}',
          ].join(' · '),
          style: theme.textTheme.bodySmall,
        ),
        Text(
          [
            order.branchName,
            if (mostrarTecnico)
              'técnico ${order.assignedTechnicianName ?? 'sin asignar'}',
            if (order.mileageIn != null) '${order.mileageIn} km',
            'abierta el ${_fechaCorta(order.openedAt)}',
          ].join(' · '),
          style: theme.textTheme.bodySmall,
        ),
        const SizedBox(height: 10),
        Text(order.description, style: theme.textTheme.bodyMedium),
      ],
    );
  }

  static String _fechaCorta(DateTime value) {
    final d = value.toLocal();
    final dia = d.day.toString().padLeft(2, '0');
    final mes = d.month.toString().padLeft(2, '0');
    final hora = d.hour.toString().padLeft(2, '0');
    final min = d.minute.toString().padLeft(2, '0');
    return '$dia/$mes $hora:$min';
  }
}

/// La placa, en monoespaciada y con borde: es un dato que se compara con el vehículo que está
/// enfrente, letra por letra.
class _Placa extends StatelessWidget {
  const _Placa(this.plate);

  final String plate;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
      decoration: BoxDecoration(
        border: Border.all(color: theme.dividerColor),
        borderRadius: BorderRadius.circular(3),
      ),
      child: Text(
        plate,
        style: theme.textTheme.labelSmall?.merge(monoStyle),
      ),
    );
  }
}

class _ChipAtraso extends StatelessWidget {
  const _ChipAtraso({required this.dias});

  final int dias;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final rojo = theme.colorScheme.error;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: rojo.withValues(alpha: 0.16),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        'Atrasada $dias ${dias == 1 ? 'día' : 'días'}',
        style: theme.textTheme.labelSmall?.copyWith(color: rojo),
      ),
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
  const _VehicleHistorySection({required this.order, this.siempreAbierto = false});

  final WorkOrderDetail order;

  /// En su propia pantalla no hay nada que plegar: se entró a leer las visitas.
  final bool siempreAbierto;

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

        final abierto = widget.siempreAbierto || _abierto;

        return _Section(
          title: 'Historial del vehículo',
          child: Column(
            children: [
              if (!widget.siempreAbierto)
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
              if (abierto)
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

/// Los pasos, que es lo que se viene a ver: qué falta y cuánto lleva cada uno.
///
/// Antes esta tarjeta empezaba con el interruptor de cómo se cobra la mano de obra y el botón
/// de trabajos frecuentes, y los pasos —lo único que el técnico toca con el vehículo
/// enfrente— arrancaban a media pantalla. Los dos ajustes se fueron al menú.
class _TasksCard extends StatelessWidget {
  const _TasksCard({
    required this.order,
    required this.canEdit,
    required this.busy,
    required this.onToggle,
    required this.onAdd,
    required this.onChangeLabor,
    required this.mostrarManoDeObra,
    required this.siguienteId,
    required this.hasTemplates,
    required this.onApplyTemplate,
    required this.onComoSeCobra,
  });

  final WorkOrderDetail order;
  final bool canEdit;
  final bool busy;

  /// El Dueño ve el total abajo, con el ISV y los repuestos: repetir aquí la mano de obra
  /// serían dos cifras distintas de lo mismo en la misma pantalla.
  final bool mostrarManoDeObra;

  /// El paso que la barra fija ofrece marcar. Va en negrita: es el que toca ahora.
  final String? siguienteId;
  final bool hasTemplates;
  final void Function(WorkOrderTask task, bool value) onToggle;
  final VoidCallback onAdd;
  final VoidCallback onApplyTemplate;

  /// Cambiar si la mano de obra sale del catálogo o de un total a mano. Null para quien no
  /// puede: es del Dueño.
  final VoidCallback? onComoSeCobra;
  final void Function(WorkOrderTask task) onChangeLabor;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final catalogo = order.isCatalogLabor;
    final hechos = order.tasks.where((t) => t.isDone).length;

    return Container(
      decoration: BoxDecoration(
        color: theme.colorScheme.surface,
        border: Border.all(color: theme.dividerColor),
        borderRadius: BorderRadius.circular(10),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(14, 12, 14, 6),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    'PASOS',
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                      letterSpacing: 0.6,
                    ),
                  ),
                ),
                if (order.tasks.isNotEmpty)
                  Text(
                    '$hechos de ${order.tasks.length}',
                    style: theme.textTheme.bodySmall?.merge(monoStyle),
                  ),
              ],
            ),
          ),
          if (order.tasks.isEmpty)
            Padding(
              padding: const EdgeInsets.fromLTRB(14, 0, 14, 2),
              child: Text(
                canEdit
                    ? 'Todavía no hay pasos: agréguelos uno a uno o traiga un trabajo frecuente.'
                    : 'Todavía no hay pasos.',
                style: theme.textTheme.bodySmall,
              ),
            ),
          for (final task in order.tasks)
            _FilaPaso(
              task: task,
              catalogo: catalogo,
              canEdit: canEdit,
              busy: busy,
              tocaAhora: task.id == siguienteId,
              onToggle: (value) => onToggle(task, value),
              onChangeLabor: () => onChangeLabor(task),
            ),
          // Las dos maneras de armar la orden, donde se arman: uno a uno, o de golpe con un
          // trabajo frecuente. Estaban en el menú y allí nadie las encontraba.
          if (canEdit)
            Padding(
              padding: const EdgeInsets.fromLTRB(6, 2, 6, 2),
              child: Wrap(
                children: [
                  TextButton.icon(
                    onPressed: busy ? null : onAdd,
                    icon: const Icon(Icons.add, size: 18),
                    label: const Text('Agregar paso'),
                  ),
                  if (hasTemplates)
                    TextButton.icon(
                      onPressed: busy ? null : onApplyTemplate,
                      icon: const Icon(Icons.bolt_outlined, size: 18),
                      label: const Text('Trabajo frecuente'),
                    ),
                ],
              ),
            ),
          if (mostrarManoDeObra || onComoSeCobra != null)
            Padding(
              padding: const EdgeInsets.fromLTRB(14, 0, 14, 12),
              child: Row(
                children: [
                  Expanded(
                    child: Text(
                      // Al Dueño el monto ya se lo dice el total de abajo; lo que aquí hace
                      // falta es de dónde sale el precio, que es lo que se puede cambiar.
                      onComoSeCobra != null
                          ? 'Se cobra ${catalogo ? 'con el catálogo' : 'a mano'}'
                          : 'Mano de obra ${_money(order.laborTotal)}',
                      style: theme.textTheme.bodySmall,
                    ),
                  ),
                  if (onComoSeCobra != null)
                    InkWell(
                      onTap: busy ? null : onComoSeCobra,
                      child: Text(
                        'Cambiar',
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: theme.colorScheme.primary,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                ],
              ),
            ),
        ],
      ),
    );
  }
}

/// Un paso: 52 px de alto para que el dedo lo acierte, la casilla a la izquierda y debajo del
/// título quién lo hace y cuánto se cobra. El precio se toca para cambiarlo.
class _FilaPaso extends StatelessWidget {
  const _FilaPaso({
    required this.task,
    required this.catalogo,
    required this.canEdit,
    required this.busy,
    required this.tocaAhora,
    required this.onToggle,
    required this.onChangeLabor,
  });

  final WorkOrderTask task;
  final bool catalogo;
  final bool canEdit;
  final bool busy;
  final bool tocaAhora;
  final void Function(bool value) onToggle;
  final VoidCallback onChangeLabor;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.colorScheme.onSurfaceVariant;
    final precio = task.laborPrice != null ? _money(task.laborPrice!) : 'Sin cobro';

    return InkWell(
      onTap: canEdit && !busy ? () => onToggle(!task.isDone) : null,
      child: Container(
        constraints: const BoxConstraints(minHeight: 52),
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
        decoration: BoxDecoration(
          border: Border(top: BorderSide(color: theme.dividerColor)),
        ),
        child: Row(
          children: [
            _Casilla(marcada: task.isDone),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Text(
                    task.title,
                    style: task.isDone
                        ? theme.textTheme.bodyLarge?.copyWith(
                            color: muted,
                            decoration: TextDecoration.lineThrough,
                          )
                        : theme.textTheme.bodyLarge?.copyWith(
                            fontWeight: tocaAhora ? FontWeight.w600 : null,
                          ),
                  ),
                  const SizedBox(height: 1),
                  Row(
                    children: [
                      Flexible(
                        child: Text(
                          // El paso hecho cuenta cuándo se hizo; el que falta, quién lo tiene.
                          task.isDone && task.completedAt != null
                              ? 'hecho a las ${_hora(task.completedAt!)}'
                              : tocaAhora
                                  ? 'toca ahora'
                                  : task.assignedTechnicianName ?? 'Sin asignar',
                          style: theme.textTheme.bodySmall,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                      // El precio del paso es del taller: al Cliente se le cobra con la
                      // cotización y con la factura, no con un borrador que todavía se mueve.
                      if (canEdit) Text(' · ', style: theme.textTheme.bodySmall),
                      // Azul y tocable cuando se puede cambiar: es el dato que más se olvida
                      // y la razón más común de que la factura salga corta.
                      if (canEdit && catalogo)
                        InkWell(
                          onTap: busy ? null : onChangeLabor,
                          child: Text(
                            precio,
                            style: theme.textTheme.bodySmall?.copyWith(
                              color: theme.colorScheme.primary,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        )
                      else if (canEdit)
                        Text(precio, style: theme.textTheme.bodySmall),
                    ],
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Casilla extends StatelessWidget {
  const _Casilla({required this.marcada});

  final bool marcada;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      width: 22,
      height: 22,
      decoration: BoxDecoration(
        color: marcada ? theme.colorScheme.primary : null,
        border: marcada ? null : Border.all(color: theme.dividerColor, width: 1.5),
        borderRadius: BorderRadius.circular(4),
      ),
      child: marcada
          ? Icon(Icons.check, size: 16, color: theme.colorScheme.onPrimary)
          : null,
    );
  }
}

/// Por cuánto va la orden, con el ISV que se le va a aplicar al cerrar, y en qué quedó la
/// cotización. Antes había que bajar hasta la tarjeta de cierre para saberlo.
class _TotalCard extends ConsumerWidget {
  const _TotalCard({required this.order});

  final WorkOrderDetail order;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final tasa = ref.watch(taxRateProvider).value ?? 0;
    final base = order.laborTotal + order.partsTotal;
    final impuesto = base * tasa / 100;

    final cotizaciones = ref.watch(workOrderQuotesProvider(order.id)).value ?? const <Quote>[];
    final ultima = cotizaciones.isEmpty ? null : cotizaciones.first;

    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: theme.colorScheme.surface,
        border: Border.all(color: theme.dividerColor),
        borderRadius: BorderRadius.circular(10),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'TOTAL ESTIMADO',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                    letterSpacing: 0.6,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  _money(base + impuesto),
                  style: theme.textTheme.titleLarge?.merge(monoStyle),
                ),
                const SizedBox(height: 2),
                Text(
                  'Trabajo ${_money(order.laborTotal)} · '
                  'Repuestos ${_money(order.partsTotal)}'
                  '${tasa > 0 ? ' · ISV ${tasa.toStringAsFixed(0)}%' : ''}',
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ),
          ),
          if (ultima != null)
            Padding(
              padding: const EdgeInsets.only(left: 8, top: 2),
              child: _Etiqueta('Cotización ${ultima.status.label.toLowerCase()}'),
            ),
        ],
      ),
    );
  }
}

class _Etiqueta extends StatelessWidget {
  const _Etiqueta(this.texto);

  final String texto;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        texto,
        style: theme.textTheme.labelSmall?.copyWith(color: theme.colorScheme.onSurfaceVariant),
      ),
    );
  }
}

/// Un renglón que lleva a su pantalla, con el resumen que casi siempre contesta la pregunta
/// sin necesidad de entrar: cuántos repuestos, cuántas fotos, cuántas visitas antes.
class _Fila extends StatelessWidget {
  const _Fila({
    required this.icono,
    required this.titulo,
    required this.detalle,
    required this.onTap,
  });

  final IconData icono;
  final String titulo;
  final String detalle;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      decoration: BoxDecoration(
        color: theme.colorScheme.surface,
        border: Border.all(color: theme.dividerColor),
        borderRadius: BorderRadius.circular(10),
      ),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(10),
        child: Container(
          constraints: const BoxConstraints(minHeight: 56),
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
          child: Row(
            children: [
              Icon(icono, size: 20, color: theme.colorScheme.onSurfaceVariant),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Text(titulo, style: theme.textTheme.bodyLarge),
                    Text(
                      detalle,
                      style: theme.textTheme.bodySmall,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ],
                ),
              ),
              Icon(Icons.chevron_right, color: theme.colorScheme.onSurfaceVariant),
            ],
          ),
        ),
      ),
    );
  }
}

class _FilaDiagnostico extends StatelessWidget {
  const _FilaDiagnostico({required this.order, required this.onTap});

  final WorkOrderDetail order;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final escrito = order.diagnosis?.trim();

    return _Fila(
      icono: Icons.assignment_outlined,
      titulo: 'Diagnóstico',
      detalle: escrito == null || escrito.isEmpty
          ? 'Sin escribir: qué se encontró al revisar'
          : escrito,
      onTap: onTap,
    );
  }
}

class _FilaFotos extends ConsumerWidget {
  const _FilaFotos({required this.workOrderId, required this.onTap});

  final String workOrderId;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final fotos = ref.watch(workOrderMediaProvider(workOrderId)).value?.length;
    final pendientes = ref.watch(pendingUploadsForProvider(workOrderId)).length;

    return _Fila(
      icono: Icons.photo_camera_outlined,
      titulo: 'Fotos',
      detalle: switch (fotos) {
        null => 'del proceso',
        0 => pendientes > 0 ? '$pendientes por subir' : 'Todavía no hay fotos',
        final n => '$n del proceso'
            '${pendientes > 0 ? ' · $pendientes por subir' : ''}',
      },
      onTap: onTap,
    );
  }
}

class _FilaCotizaciones extends ConsumerWidget {
  const _FilaCotizaciones({
    required this.workOrderId,
    required this.isOwner,
    required this.onTap,
  });

  final String workOrderId;
  final bool isOwner;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final cotizaciones = ref.watch(workOrderQuotesProvider(workOrderId)).value;

    // Al Cliente no se le enseña un renglón vacío: si no hay nada que aprobar, no hay nada
    // que contarle.
    if (!isOwner && (cotizaciones == null || cotizaciones.isEmpty)) {
      return const SizedBox.shrink();
    }

    final ultima = (cotizaciones ?? const <Quote>[]).firstOrNull;

    return _Fila(
      icono: Icons.request_quote_outlined,
      titulo: 'Cotizaciones',
      detalle: ultima == null
          ? 'Ninguna todavía'
          : '${ultima.number} · ${ultima.status.label} · ${_money(ultima.total)}',
      onTap: onTap,
    );
  }
}

class _FilaCobro extends ConsumerWidget {
  const _FilaCobro({required this.workOrderId, required this.onTap});

  final String workOrderId;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final ventas = ref.watch(workOrderSalesProvider(workOrderId)).value ?? const <Sale>[];
    final vigentes = ventas.where((v) => !v.isVoided).toList();
    final deuda = vigentes.fold<double>(0, (suma, v) => suma + v.balance);

    return _Fila(
      icono: Icons.point_of_sale_outlined,
      titulo: vigentes.isEmpty ? 'Cerrar y facturar' : 'Venta',
      detalle: vigentes.isEmpty
          ? 'Cobrar, entregar y anotar el próximo servicio'
          : '${vigentes.first.number} · ${_money(vigentes.first.total)}'
              '${deuda > 0 ? ' · debe ${_money(deuda)}' : ' · pagada'}',
      onTap: onTap,
    );
  }
}

class _FilaHistorial extends ConsumerWidget {
  const _FilaHistorial({required this.order, required this.onTap});

  final WorkOrderDetail order;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final historial = ref.watch(vehicleHistoryProvider(order.vehicleId)).value;
    final otras = (historial ?? const []).where((o) => o.id != order.id).toList();

    // Mientras carga, o si falla, no se dibuja: es información de apoyo y no puede ensuciar
    // la pantalla de la orden que se está atendiendo.
    if (otras.isEmpty) return const SizedBox.shrink();

    return _Fila(
      icono: Icons.history_edu_outlined,
      titulo: 'Historial del vehículo',
      detalle: '${otras.length} ${otras.length == 1 ? 'visita antes' : 'visitas antes'}',
      onTap: onTap,
    );
  }
}

/// La pantalla de una sección, con la orden viva: lo que se cambia dentro —cargar un
/// repuesto, guardar el diagnóstico— se ve sin volver atrás.
class _SeccionPagina extends ConsumerWidget {
  const _SeccionPagina({required this.id, required this.contenido});

  final String id;
  final Widget Function(WorkOrderDetail order) contenido;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final detail = ref.watch(workOrderDetailProvider(id));

    return Scaffold(
      appBar: AppBar(title: Text(detail.value?.number ?? 'Orden')),
      body: detail.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Text(apiErrorMessage(e, 'No se pudo cargar la orden.')),
          ),
        ),
        data: (order) => ListView(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
          children: [contenido(order)],
        ),
      ),
    );
  }
}

String _hora(DateTime value) {
  final local = value.toLocal();
  return '${local.hour.toString().padLeft(2, '0')}:'
      '${local.minute.toString().padLeft(2, '0')}';
}

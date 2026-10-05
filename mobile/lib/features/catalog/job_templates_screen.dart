import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/inventory_repository.dart';
import '../../core/api/job_template_repository.dart';
import '../../core/api/work_order_repository.dart' show LaborServiceOption, laborServicesProvider;
import '../../core/theme/garaj_brand.dart';
import '../../core/widgets/garaj_skeleton.dart';
import '../../core/widgets/garaj_states.dart';
import '../reports/reports_screen.dart' show money;

/// Trabajos frecuentes: el cambio de aceite, las pastillas de adelante, lo que el taller
/// repite.
///
/// Aquí se miran, se crean, se renombran, se activan y se borran.
///
/// El mejor camino sigue siendo guardar una orden ya hecha como trabajo frecuente —desde la
/// orden, con «Guardar como frecuente»—, porque entonces los pasos, sus servicios y sus
/// repuestos ya están ahí y ya están bien. Pero el taller también arma trabajos de memoria
/// antes de haberlos hecho nunca, y para eso no hay orden de donde copiar.
class JobTemplatesScreen extends ConsumerStatefulWidget {
  const JobTemplatesScreen({super.key});

  @override
  ConsumerState<JobTemplatesScreen> createState() => _JobTemplatesScreenState();
}

class _JobTemplatesScreenState extends ConsumerState<JobTemplatesScreen> {
  bool _busy = false;

  Future<void> _crear() async {
    final servicios = await ref.read(laborServicesProvider.future);

    if (!mounted) return;
    final nuevo = await showModalBottomSheet<_NuevoTrabajo>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _FormularioTrabajo(servicios: servicios),
    );

    if (nuevo == null) return;

    setState(() => _busy = true);
    try {
      await ref.read(jobTemplateRepositoryProvider).create(
            name: nuevo.nombre,
            description: nuevo.descripcion,
            tasks: nuevo.pasos,
            parts: nuevo.repuestos,
          );

      ref.invalidate(jobTemplatesProvider);

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('«${nuevo.nombre}» guardado.')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e, 'No se pudo guardar el trabajo.'))),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _abrir(JobTemplate plantilla) async {
    await Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => _DetalleTrabajo(id: plantilla.id),
      ),
    );

    ref.invalidate(jobTemplatesProvider);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final plantillas = ref.watch(jobTemplatesProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Trabajos frecuentes')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _busy ? null : _crear,
        icon: const Icon(Icons.add),
        label: const Text('Nuevo'),
      ),
      body: plantillas.when(
        loading: () => const GarajSkeletonList(rows: 4),
        error: (e, _) => GarajError(
          message: apiErrorMessage(e, 'No se pudieron cargar los trabajos.'),
          onRetry: () => ref.invalidate(jobTemplatesProvider),
        ),
        data: (lista) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(jobTemplatesProvider),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 32),
            children: [
              if (lista.isEmpty) ...[
                Text(
                  'Todavía no hay trabajos frecuentes.',
                  style: theme.textTheme.bodyMedium,
                ),
                const SizedBox(height: 8),
                Text(
                  'Ármelos con «Nuevo», o mejor desde una orden ya hecha: ábrala, busque '
                  '«Guardar como frecuente», y se guarda con sus pasos y sus repuestos tal '
                  'como quedó el trabajo de verdad.',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ),
              ],

              for (final plantilla in lista)
                Card(
                  margin: const EdgeInsets.only(bottom: 8),
                  child: ListTile(
                    title: Text(plantilla.name),
                    subtitle: Text(
                      [
                        '${plantilla.taskCount} '
                            '${plantilla.taskCount == 1 ? 'paso' : 'pasos'}',
                        '${plantilla.partCount} '
                            '${plantilla.partCount == 1 ? 'repuesto' : 'repuestos'}',
                        if (plantilla.usageCount > 0)
                          'usado ${plantilla.usageCount} '
                              '${plantilla.usageCount == 1 ? 'vez' : 'veces'}',
                      ].join(' · '),
                    ),
                    trailing: Text(
                      money(plantilla.total, 'HNL'),
                      style: theme.textTheme.titleSmall?.copyWith(
                        fontFamily: GarajFonts.mono,
                      ),
                    ),
                    onTap: () => _abrir(plantilla),
                  ),
                ),

              if (lista.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Text(
                    'El total es a precios de hoy: sale del catálogo cada vez, no de lo que '
                    'costaba cuando se guardó.',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Lo que lleva el trabajo, y lo poco que se le puede cambiar desde el teléfono: el nombre,
/// la descripción y si está activo. Los pasos y los repuestos se cambian guardando otra orden
/// como frecuente, que es más rápido que editarlos uno por uno de pie.
class _DetalleTrabajo extends ConsumerStatefulWidget {
  const _DetalleTrabajo({required this.id});

  final String id;

  @override
  ConsumerState<_DetalleTrabajo> createState() => _DetalleTrabajoState();
}

class _DetalleTrabajoState extends ConsumerState<_DetalleTrabajo> {
  bool _busy = false;

  Future<void> _renombrar(JobTemplateDetail plantilla) async {
    final nombre = TextEditingController(text: plantilla.name);
    final descripcion = TextEditingController(text: plantilla.description ?? '');
    var activo = plantilla.isActive;

    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, redibujar) => AlertDialog(
          title: const Text('Corregir el trabajo'),
          content: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                TextField(
                  controller: nombre,
                  autofocus: true,
                  textCapitalization: TextCapitalization.sentences,
                  maxLength: 200,
                  decoration: const InputDecoration(labelText: 'Nombre'),
                ),
                TextField(
                  controller: descripcion,
                  textCapitalization: TextCapitalization.sentences,
                  maxLines: 2,
                  decoration: const InputDecoration(labelText: 'Descripción (opcional)'),
                ),
                SwitchListTile(
                  value: activo,
                  onChanged: (value) => redibujar(() => activo = value),
                  contentPadding: EdgeInsets.zero,
                  title: const Text('Activo'),
                  subtitle: const Text('Desactivado no se puede elegir en una orden.'),
                ),
              ],
            ),
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
      ),
    );

    if (ok != true || nombre.text.trim().isEmpty) return;

    await _run(() async {
      await ref.read(jobTemplateRepositoryProvider).rename(
            plantilla,
            name: nombre.text.trim(),
            description:
                descripcion.text.trim().isEmpty ? null : descripcion.text.trim(),
            isActive: activo,
          );
    });
  }

  Future<void> _borrar(JobTemplateDetail plantilla) async {
    final confirmado = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('¿Borrar ${plantilla.name}?'),
        content: const Text(
          'Las órdenes que ya lo usaron no cambian: se les anexó el trabajo, no una '
          'referencia. Esto solo lo quita de la lista.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancelar'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Borrar'),
          ),
        ],
      ),
    );

    if (confirmado != true) return;

    await _run(() async {
      await ref.read(jobTemplateRepositoryProvider).remove(plantilla.id);
      if (mounted) Navigator.pop(context);
    });
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
      ref.invalidate(jobTemplateDetailProvider(widget.id));
      ref.invalidate(jobTemplatesProvider);
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

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final detalle = ref.watch(jobTemplateDetailProvider(widget.id));

    return Scaffold(
      appBar: AppBar(
        title: Text(detalle.value?.name ?? 'Trabajo frecuente'),
        actions: [
          if (detalle.value case final plantilla?) ...[
            IconButton(
              tooltip: 'Corregir',
              icon: const Icon(Icons.edit_outlined),
              onPressed: _busy ? null : () => _renombrar(plantilla),
            ),
            IconButton(
              tooltip: 'Borrar',
              icon: const Icon(Icons.delete_outline),
              onPressed: _busy ? null : () => _borrar(plantilla),
            ),
          ],
        ],
      ),
      body: detalle.when(
        loading: () => const GarajSkeletonList(rows: 4),
        error: (e, _) => GarajError(
          message: apiErrorMessage(e, 'No se pudo cargar el trabajo.'),
          onRetry: () => ref.invalidate(jobTemplateDetailProvider(widget.id)),
        ),
        data: (plantilla) => ListView(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 32),
          children: [
            if (plantilla.description case final texto?)
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: Text(texto, style: theme.textTheme.bodyMedium),
              ),

            if (!plantilla.isActive)
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: Text(
                  'Desactivado: no se puede elegir en una orden nueva.',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: theme.colorScheme.error,
                  ),
                ),
              ),

            Text('PASOS', style: _rotulo(theme)),
            const SizedBox(height: 6),
            if (plantilla.tasks.isEmpty)
              Text('Sin pasos.', style: theme.textTheme.bodySmall)
            else
              for (final paso in plantilla.tasks)
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  dense: true,
                  title: Text(paso.title),
                  subtitle: paso.laborServiceName == null
                      ? const Text('Sin servicio: este paso no se cobra')
                      : Text(paso.laborServiceName!),
                  trailing: paso.price == null
                      ? null
                      : Text(
                          money(paso.price!, 'HNL'),
                          style: theme.textTheme.bodyMedium?.copyWith(
                            fontFamily: GarajFonts.mono,
                          ),
                        ),
                ),

            const SizedBox(height: 16),
            Text('REPUESTOS', style: _rotulo(theme)),
            const SizedBox(height: 6),
            if (plantilla.parts.isEmpty)
              Text('Sin repuestos.', style: theme.textTheme.bodySmall)
            else
              for (final repuesto in plantilla.parts)
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  dense: true,
                  title: Text(repuesto.partName),
                  subtitle: Text(
                    '${_cantidad(repuesto.quantity)} ${repuesto.unit ?? 'unidad'}'
                    '${repuesto.sku != null && repuesto.sku!.isNotEmpty ? ' · ${repuesto.sku}' : ''}',
                  ),
                  trailing: Text(
                    money(repuesto.total, 'HNL'),
                    style: theme.textTheme.bodyMedium?.copyWith(
                      fontFamily: GarajFonts.mono,
                    ),
                  ),
                ),

            const Divider(height: 24),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text('Total a precios de hoy', style: theme.textTheme.bodyMedium),
                Text(
                  money(plantilla.total, 'HNL'),
                  style: theme.textTheme.titleMedium?.copyWith(
                    fontFamily: GarajFonts.mono,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              'Para cambiarle los pasos o los repuestos, guarde otra orden como trabajo '
              'frecuente: es más rápido que editarlos uno por uno.',
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

TextStyle? _rotulo(ThemeData theme) => theme.textTheme.labelSmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
      letterSpacing: 0.6,
    );

String _cantidad(double value) =>
    value == value.roundToDouble() ? value.toStringAsFixed(0) : value.toStringAsFixed(2);

/// Lo que devuelve el formulario de un trabajo nuevo.
typedef _PasoNuevo = ({String title, String? laborServiceId, double? estimatedHours});

typedef _RepuestoNuevo = ({String? partId, String? description, double quantity});

class _NuevoTrabajo {
  const _NuevoTrabajo({
    required this.nombre,
    this.descripcion,
    required this.pasos,
    required this.repuestos,
  });

  final String nombre;
  final String? descripcion;
  final List<_PasoNuevo> pasos;
  final List<_RepuestoNuevo> repuestos;
}

/// Armar un trabajo frecuente de memoria: cómo se llama, qué pasos lleva y qué repuestos.
///
/// Los repuestos son los que el trabajo lleva siempre —el aceite, el filtro, los empaques—.
/// Los que dependen del carro que entre se agregan después, en la orden: al aplicar el
/// trabajo se proponen y se cargan uno a uno cuando de verdad se instalan.
class _FormularioTrabajo extends ConsumerStatefulWidget {
  const _FormularioTrabajo({required this.servicios});

  final List<LaborServiceOption> servicios;

  @override
  ConsumerState<_FormularioTrabajo> createState() => _FormularioTrabajoState();
}

class _FormularioTrabajoState extends ConsumerState<_FormularioTrabajo> {
  final _nombre = TextEditingController();
  final _descripcion = TextEditingController();
  final _pasos = <_PasoEnEdicion>[_PasoEnEdicion()];

  /// Los repuestos del trabajo, con el nombre que se enseña mientras se arma.
  final _repuestos = <_RepuestoNuevo>[];
  final _nombresDeRepuestos = <String>[];

  @override
  void dispose() {
    _nombre.dispose();
    _descripcion.dispose();
    for (final paso in _pasos) {
      paso.dispose();
    }
    super.dispose();
  }

  bool get _sePuedeGuardar =>
      _nombre.text.trim().isNotEmpty && _pasos.any((p) => p.titulo.text.trim().isNotEmpty);

  void _guardar() {
    Navigator.pop(
      context,
      _NuevoTrabajo(
        nombre: _nombre.text.trim(),
        descripcion: _descripcion.text.trim().isEmpty ? null : _descripcion.text.trim(),
        pasos: [
          for (final paso in _pasos)
            if (paso.titulo.text.trim().isNotEmpty)
              (
                title: paso.titulo.text.trim(),
                laborServiceId: paso.servicioId,
                estimatedHours: double.tryParse(paso.horas.text.trim()),
              ),
        ],
        repuestos: _repuestos,
      ),
    );
  }

  static String _cantidad(double v) =>
      v == v.roundToDouble() ? v.toInt().toString() : v.toString();

  /// Agrega un repuesto al trabajo: del catalogo, o escrito a mano cuando no esta.
  Future<void> _agregarRepuesto() async {
    final elegido = await showModalBottomSheet<_RepuestoElegido>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const _ElegirRepuesto(),
    );

    if (elegido == null) return;

    setState(() {
      _repuestos.add((
        partId: elegido.partId,
        description: elegido.partId == null ? elegido.nombre : null,
        quantity: elegido.cantidad,
      ));
      _nombresDeRepuestos.add(elegido.nombre);
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: EdgeInsets.only(
        left: GarajSpace.md,
        right: GarajSpace.md,
        top: GarajSpace.md,
        bottom: MediaQuery.viewInsetsOf(context).bottom + GarajSpace.md,
      ),
      child: SingleChildScrollView(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Nuevo trabajo frecuente', style: theme.textTheme.titleLarge),
            const SizedBox(height: GarajSpace.md),

            TextField(
              controller: _nombre,
              autofocus: true,
              textCapitalization: TextCapitalization.sentences,
              onChanged: (_) => setState(() {}),
              decoration: const InputDecoration(
                labelText: 'Cómo se llama',
                hintText: 'Cambio de aceite, frenos de adelante…',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: GarajSpace.sm),

            TextField(
              controller: _descripcion,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: 'Para qué sirve (opcional)',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: GarajSpace.lg),

            Text('PASOS', style: theme.textTheme.labelSmall),
            const SizedBox(height: GarajSpace.xs),
            Text(
              'Lo que hay que hacer, en orden. Si el paso sale del catálogo de mano de obra, '
              'elíjalo ahí y el precio lo pone el catálogo.',
              style: theme.textTheme.bodySmall?.copyWith(color: theme.hintColor),
            ),
            const SizedBox(height: GarajSpace.sm),

            for (final (i, paso) in _pasos.indexed) ...[
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: TextField(
                      controller: paso.titulo,
                      textCapitalization: TextCapitalization.sentences,
                      onChanged: (_) => setState(() {}),
                      decoration: InputDecoration(
                        labelText: 'Paso ${i + 1}',
                        border: const OutlineInputBorder(),
                        isDense: true,
                      ),
                    ),
                  ),
                  if (_pasos.length > 1)
                    IconButton(
                      tooltip: 'Quitar el paso',
                      icon: const Icon(Icons.close),
                      onPressed: () => setState(() {
                        _pasos.removeAt(i).dispose();
                      }),
                    ),
                ],
              ),
              const SizedBox(height: GarajSpace.xs),
              Row(
                children: [
                  Expanded(
                    flex: 3,
                    child: DropdownButtonFormField<String?>(
                      initialValue: paso.servicioId,
                      isExpanded: true,
                      decoration: const InputDecoration(
                        labelText: 'Del catálogo',
                        border: OutlineInputBorder(),
                        isDense: true,
                      ),
                      items: [
                        const DropdownMenuItem(value: null, child: Text('— ninguno —')),
                        for (final s in widget.servicios)
                          DropdownMenuItem(value: s.id, child: Text(s.name)),
                      ],
                      onChanged: (v) => setState(() {
                        paso.servicioId = v;
                        // Sin título escrito, el del catálogo sirve: es como se llama el
                        // trabajo en el taller.
                        if (v != null && paso.titulo.text.trim().isEmpty) {
                          paso.titulo.text =
                              widget.servicios.firstWhere((s) => s.id == v).name;
                        }
                      }),
                    ),
                  ),
                  const SizedBox(width: GarajSpace.sm),
                  Expanded(
                    child: TextField(
                      controller: paso.horas,
                      keyboardType: const TextInputType.numberWithOptions(decimal: true),
                      decoration: const InputDecoration(
                        labelText: 'Horas',
                        border: OutlineInputBorder(),
                        isDense: true,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: GarajSpace.md),
            ],

            OutlinedButton.icon(
              onPressed: () => setState(() => _pasos.add(_PasoEnEdicion())),
              icon: const Icon(Icons.add),
              label: const Text('Otro paso'),
            ),
            const SizedBox(height: GarajSpace.lg),

            Text('REPUESTOS', style: theme.textTheme.labelSmall),
            const SizedBox(height: GarajSpace.xs),
            Text(
              'Los que el trabajo lleva siempre: el aceite, el filtro, los empaques. Los que '
              'dependen del carro se agregan despues, en la orden.',
              style: theme.textTheme.bodySmall?.copyWith(color: theme.hintColor),
            ),
            const SizedBox(height: GarajSpace.sm),

            for (final (i, repuesto) in _repuestos.indexed)
              ListTile(
                contentPadding: EdgeInsets.zero,
                dense: true,
                title: Text(_nombresDeRepuestos[i]),
                subtitle: Text(_cantidad(repuesto.quantity)),
                trailing: IconButton(
                  tooltip: 'Quitar el repuesto',
                  icon: const Icon(Icons.close, size: 18),
                  onPressed: () => setState(() {
                    _repuestos.removeAt(i);
                    _nombresDeRepuestos.removeAt(i);
                  }),
                ),
              ),

            OutlinedButton.icon(
              onPressed: _agregarRepuesto,
              icon: const Icon(Icons.add),
              label: const Text('Agregar repuesto'),
            ),
            const SizedBox(height: GarajSpace.lg),

            Row(
              children: [
                Expanded(
                  child: OutlinedButton(
                    onPressed: () => Navigator.pop(context),
                    child: const Text('Cancelar'),
                  ),
                ),
                const SizedBox(width: GarajSpace.sm),
                Expanded(
                  child: FilledButton(
                    onPressed: _sePuedeGuardar ? _guardar : null,
                    child: const Text('Guardar'),
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

/// Un paso mientras se escribe. Lleva sus propios controladores para que al quitar un paso
/// del medio no se arrastre el texto del de abajo.
class _PasoEnEdicion {
  final titulo = TextEditingController();
  final horas = TextEditingController();
  String? servicioId;

  void dispose() {
    titulo.dispose();
    horas.dispose();
  }
}

/// Lo que devuelve el selector de repuesto: del catálogo o escrito a mano.
class _RepuestoElegido {
  const _RepuestoElegido({this.partId, required this.nombre, required this.cantidad});

  /// Null cuando se escribió a mano: es un repuesto que no está en el catálogo.
  final String? partId;
  final String nombre;
  final double cantidad;
}

/// Elegir el repuesto que el trabajo lleva siempre.
///
/// Del catálogo cuando está —así el precio y el descuento de bodega salen solos al aplicarlo—,
/// y escrito a mano cuando no: hay empaques que se compran de encargo y nunca entran a bodega.
class _ElegirRepuesto extends ConsumerStatefulWidget {
  const _ElegirRepuesto();

  @override
  ConsumerState<_ElegirRepuesto> createState() => _ElegirRepuestoState();
}

class _ElegirRepuestoState extends ConsumerState<_ElegirRepuesto> {
  final _cantidad = TextEditingController(text: '1');
  String _texto = '';

  @override
  void dispose() {
    _cantidad.dispose();
    super.dispose();
  }

  double get _cuantos => double.tryParse(_cantidad.text.trim()) ?? 0;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final catalogo = ref.watch(partSearchProvider(_texto));

    return Padding(
      padding: EdgeInsets.only(
        left: GarajSpace.md,
        right: GarajSpace.md,
        bottom: MediaQuery.viewInsetsOf(context).bottom + GarajSpace.md,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Repuesto del trabajo', style: theme.textTheme.titleLarge),
          const SizedBox(height: GarajSpace.md),

          Row(
            children: [
              Expanded(
                flex: 3,
                child: TextField(
                  autofocus: true,
                  onChanged: (v) => setState(() => _texto = v),
                  decoration: const InputDecoration(
                    labelText: 'Nombre o código',
                    prefixIcon: Icon(Icons.search),
                    isDense: true,
                    border: OutlineInputBorder(),
                  ),
                ),
              ),
              const SizedBox(width: GarajSpace.sm),
              Expanded(
                child: TextField(
                  controller: _cantidad,
                  keyboardType: const TextInputType.numberWithOptions(decimal: true),
                  onChanged: (_) => setState(() {}),
                  decoration: const InputDecoration(
                    labelText: 'Cuántos',
                    isDense: true,
                    border: OutlineInputBorder(),
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: GarajSpace.sm),

          SizedBox(
            height: 260,
            child: catalogo.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (e, _) => Center(
                child: Text(apiErrorMessage(e, 'No se pudo buscar el repuesto.')),
              ),
              data: (items) => items.isEmpty
                  ? Center(
                      child: Text(
                        _texto.trim().isEmpty
                            ? 'Escriba el nombre o el código.'
                            : 'Nada con ese nombre en el catálogo.',
                        style: theme.textTheme.bodySmall,
                      ),
                    )
                  : ListView.builder(
                      keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
                      itemCount: items.length,
                      itemBuilder: (_, i) => ListTile(
                        dense: true,
                        title: Text(items[i].name),
                        subtitle: Text('${items[i].sku} · ${money(items[i].salePrice, 'HNL')}'),
                        onTap: _cuantos <= 0
                            ? null
                            : () => Navigator.pop(
                                  context,
                                  _RepuestoElegido(
                                    partId: items[i].id,
                                    nombre: items[i].name,
                                    cantidad: _cuantos,
                                  ),
                                ),
                      ),
                    ),
            ),
          ),

          // A mano: lo que se compra de encargo y nunca entra a bodega.
          const Divider(height: 24),
          SizedBox(
            width: double.infinity,
            child: OutlinedButton.icon(
              onPressed: _texto.trim().isEmpty || _cuantos <= 0
                  ? null
                  : () => Navigator.pop(
                        context,
                        _RepuestoElegido(nombre: _texto.trim(), cantidad: _cuantos),
                      ),
              icon: const Icon(Icons.edit_outlined),
              label: Text(
                _texto.trim().isEmpty
                    ? 'No está en el catálogo: escriba el nombre arriba'
                    : 'Agregar «${_texto.trim()}» a mano',
              ),
            ),
          ),
        ],
      ),
    );
  }
}

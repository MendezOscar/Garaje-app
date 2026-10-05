import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/job_template_repository.dart';
import '../../core/theme/garaj_brand.dart';
import '../../core/widgets/garaj_skeleton.dart';
import '../../core/widgets/garaj_states.dart';
import '../reports/reports_screen.dart' show money;

/// Trabajos frecuentes: el cambio de aceite, las pastillas de adelante, lo que el taller
/// repite.
///
/// Aquí se miran, se renombran, se activan y se borran. Crearlos no se hace desde esta
/// pantalla a propósito: el camino bueno es guardar una orden ya hecha como trabajo frecuente
/// —desde la orden, con «Guardar como frecuente»—, porque entonces los pasos, sus servicios y
/// sus repuestos ya están ahí y ya están bien.
class JobTemplatesScreen extends ConsumerStatefulWidget {
  const JobTemplatesScreen({super.key});

  @override
  ConsumerState<JobTemplatesScreen> createState() => _JobTemplatesScreenState();
}

class _JobTemplatesScreenState extends ConsumerState<JobTemplatesScreen> {
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
                  'Se arman desde una orden ya hecha: abra una, busque «Guardar como '
                  'frecuente», y la próxima vez que entre el mismo trabajo se anexa completo '
                  'con un toque.',
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

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/labor_service_repository.dart';
import '../../core/api/work_order_repository.dart' show laborServicesProvider;
import '../../core/theme/garaj_brand.dart';
import '../../core/widgets/garaj_skeleton.dart';
import '../../core/widgets/garaj_states.dart';
import '../reports/reports_screen.dart' show money;

/// El catálogo de mano de obra: lo que el taller cobra por cada trabajo.
///
/// Un servicio se cobra de una de dos formas, y es la única decisión del formulario: precio
/// cerrado —se cobra lo mismo tarde lo que tarde— u horas por tarifa, que es lo que se usa
/// cuando el trabajo depende de cuánto pelee el carro.
class LaborServicesScreen extends ConsumerStatefulWidget {
  const LaborServicesScreen({super.key});

  @override
  ConsumerState<LaborServicesScreen> createState() => _LaborServicesScreenState();
}

class _LaborServicesScreenState extends ConsumerState<LaborServicesScreen> {
  String _busqueda = '';
  bool _busy = false;

  Future<void> _editar([LaborService? servicio]) async {
    final guardado = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _Formulario(servicio: servicio),
    );

    if (guardado != true) return;

    ref.invalidate(laborCatalogProvider);
    // El selector de los pasos lee de otro proveedor: si no se invalida, sigue enseñando el
    // precio viejo hasta que la app se reinicie.
    ref.invalidate(laborServicesProvider);
  }

  Future<void> _activar(LaborService servicio, bool activo) async {
    setState(() => _busy = true);
    try {
      await ref.read(laborServiceRepositoryProvider).save(
            id: servicio.id,
            code: servicio.code,
            name: servicio.name,
            description: servicio.description,
            category: servicio.category,
            standardHours: servicio.standardHours,
            hourlyRate: servicio.hourlyRate,
            isFixedPrice: servicio.isFixedPrice,
            fixedPrice: servicio.fixedPrice,
            isActive: activo,
          );

      ref.invalidate(laborCatalogProvider);
      ref.invalidate(laborServicesProvider);
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
    final catalogo = ref.watch(laborCatalogProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Mano de obra')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _busy ? null : () => _editar(),
        icon: const Icon(Icons.add),
        label: const Text('Trabajo'),
      ),
      body: catalogo.when(
        loading: () => const GarajSkeletonList(),
        error: (e, _) => GarajError(
          message: apiErrorMessage(e, 'No se pudo cargar el catálogo.'),
          onRetry: () => ref.invalidate(laborCatalogProvider),
        ),
        data: (lista) {
          final texto = _busqueda.trim().toLowerCase();
          final visibles = texto.isEmpty
              ? lista
              : lista
                  .where((s) =>
                      s.name.toLowerCase().contains(texto) ||
                      s.code.toLowerCase().contains(texto))
                  .toList();

          return RefreshIndicator(
            onRefresh: () async => ref.invalidate(laborCatalogProvider),
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 96),
              children: [
                TextField(
                  decoration: const InputDecoration(
                    labelText: 'Buscar por nombre o código',
                    prefixIcon: Icon(Icons.search),
                    isDense: true,
                  ),
                  onChanged: (value) => setState(() => _busqueda = value),
                ),
                const SizedBox(height: 12),

                if (lista.isEmpty)
                  Text(
                    'El catálogo está vacío. Agregue los trabajos que el taller repite: el '
                    'cambio de aceite, la revisión de frenos, lo de todos los días.',
                    style: theme.textTheme.bodySmall,
                  )
                else if (visibles.isEmpty)
                  Text('Nada con ese nombre.', style: theme.textTheme.bodySmall),

                for (final servicio in visibles)
                  Card(
                    margin: const EdgeInsets.only(bottom: 8),
                    child: ListTile(
                      title: Text(
                        servicio.name,
                        style: servicio.isActive
                            ? null
                            : TextStyle(
                                color: theme.colorScheme.onSurfaceVariant,
                                decoration: TextDecoration.lineThrough,
                              ),
                      ),
                      subtitle: Text(
                        [
                          servicio.code,
                          if (servicio.isFixedPrice)
                            'precio cerrado'
                          else
                            '${servicio.standardHours.toStringAsFixed(1)} h × '
                                '${money(servicio.hourlyRate, 'HNL')}',
                          if (servicio.category != null) servicio.category!,
                          if (!servicio.isActive) 'desactivado',
                        ].join(' · '),
                      ),
                      trailing: Text(
                        money(servicio.price, 'HNL'),
                        style: theme.textTheme.titleSmall?.copyWith(
                          fontFamily: GarajFonts.mono,
                        ),
                      ),
                      onTap: _busy ? null : () => _editar(servicio),
                      onLongPress: _busy
                          ? null
                          : () => _activar(servicio, !servicio.isActive),
                    ),
                  ),

                if (lista.isNotEmpty)
                  Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Text(
                      'Toque para corregir. Deje el dedo encima para activar o desactivar: un '
                      'trabajo desactivado no se puede elegir en una orden nueva, pero las '
                      'que ya lo llevan no cambian.',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ),
              ],
            ),
          );
        },
      ),
    );
  }
}

class _Formulario extends ConsumerStatefulWidget {
  const _Formulario({this.servicio});

  final LaborService? servicio;

  @override
  ConsumerState<_Formulario> createState() => _FormularioState();
}

class _FormularioState extends ConsumerState<_Formulario> {
  late final _codigo = TextEditingController(text: widget.servicio?.code ?? '');
  late final _nombre = TextEditingController(text: widget.servicio?.name ?? '');
  late final _categoria = TextEditingController(text: widget.servicio?.category ?? '');
  late final _horas = TextEditingController(
    text: (widget.servicio?.standardHours ?? 1).toString(),
  );
  late final _tarifa = TextEditingController(
    text: widget.servicio?.hourlyRate.toStringAsFixed(2) ?? '',
  );
  late final _cerrado = TextEditingController(
    text: widget.servicio?.fixedPrice.toStringAsFixed(2) ?? '',
  );

  late bool _precioCerrado = widget.servicio?.isFixedPrice ?? true;
  bool _busy = false;

  @override
  void dispose() {
    _codigo.dispose();
    _nombre.dispose();
    _categoria.dispose();
    _horas.dispose();
    _tarifa.dispose();
    _cerrado.dispose();
    super.dispose();
  }

  Future<void> _guardar() async {
    final nombre = _nombre.text.trim();
    final codigo = _codigo.text.trim();
    if (nombre.isEmpty || codigo.isEmpty) return;

    setState(() => _busy = true);
    try {
      await ref.read(laborServiceRepositoryProvider).save(
            id: widget.servicio?.id,
            code: codigo,
            name: nombre,
            category: _categoria.text.trim().isEmpty ? null : _categoria.text.trim(),
            standardHours: double.tryParse(_horas.text.replaceAll(',', '.')) ?? 1,
            hourlyRate: double.tryParse(_tarifa.text.replaceAll(',', '.')) ?? 0,
            isFixedPrice: _precioCerrado,
            fixedPrice: double.tryParse(_cerrado.text.replaceAll(',', '.')) ?? 0,
            isActive: widget.servicio?.isActive ?? true,
          );

      if (mounted) Navigator.pop(context, true);
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

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

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
              Text(
                widget.servicio == null ? 'Nuevo trabajo' : 'Corregir el trabajo',
                style: theme.textTheme.titleMedium,
              ),
              const SizedBox(height: 12),

              TextField(
                controller: _nombre,
                autofocus: widget.servicio == null,
                textCapitalization: TextCapitalization.sentences,
                maxLength: 200,
                decoration: const InputDecoration(
                  labelText: 'Qué trabajo es',
                  hintText: 'Cambio de aceite y filtro',
                ),
              ),

              TextField(
                controller: _codigo,
                textCapitalization: TextCapitalization.characters,
                maxLength: 30,
                decoration: const InputDecoration(
                  labelText: 'Código',
                  hintText: 'ACE-01',
                  helperText: 'Para encontrarlo rápido. Cualquier cosa corta sirve.',
                ),
              ),

              TextField(
                controller: _categoria,
                textCapitalization: TextCapitalization.sentences,
                maxLength: 100,
                decoration: const InputDecoration(labelText: 'Categoría (opcional)'),
              ),
              const SizedBox(height: 12),

              // La única decisión del formulario: cómo se cobra.
              SegmentedButton<bool>(
                segments: const [
                  ButtonSegment(value: true, label: Text('Precio cerrado')),
                  ButtonSegment(value: false, label: Text('Horas × tarifa')),
                ],
                selected: {_precioCerrado},
                showSelectedIcon: false,
                onSelectionChanged: (s) => setState(() => _precioCerrado = s.first),
              ),
              const SizedBox(height: 10),

              if (_precioCerrado)
                TextField(
                  controller: _cerrado,
                  keyboardType: const TextInputType.numberWithOptions(decimal: true),
                  decoration: const InputDecoration(
                    labelText: 'Precio',
                    prefixText: 'L ',
                    helperText: 'Se cobra lo mismo tarde lo que tarde.',
                    helperMaxLines: 2,
                  ),
                )
              else
                Row(
                  children: [
                    Expanded(
                      child: TextField(
                        controller: _horas,
                        keyboardType: const TextInputType.numberWithOptions(decimal: true),
                        decoration: const InputDecoration(labelText: 'Horas'),
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: TextField(
                        controller: _tarifa,
                        keyboardType: const TextInputType.numberWithOptions(decimal: true),
                        decoration: const InputDecoration(
                          labelText: 'Por hora',
                          prefixText: 'L ',
                        ),
                      ),
                    ),
                  ],
                ),

              const SizedBox(height: 16),
              Align(
                alignment: Alignment.centerRight,
                child: FilledButton(
                  onPressed: _busy ? null : _guardar,
                  child: Text(_busy ? 'Guardando…' : 'Guardar'),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

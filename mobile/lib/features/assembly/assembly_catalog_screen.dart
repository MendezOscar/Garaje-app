import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/assembly_repository.dart';
import '../../core/api/service_request_repository.dart' show branchOptionsProvider;
import '../../core/models/assembly.dart';
import '../../core/theme/garaj_brand.dart';
import '../reports/reports_screen.dart' show money;

/// El catálogo del contrato de armado: con quién se trabaja, qué modelos se arman, en qué
/// bodegas, y la tabla de precios de cada contratista.
///
/// Es del Dueño entero: de aquí salen los dos números que lleva cada moto. Se copian al
/// registrarla, así que subir una tarifa hoy no reescribe lo que ya se cerró.
class AssemblyCatalogScreen extends ConsumerWidget {
  const AssemblyCatalogScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => DefaultTabController(
        length: 3,
        child: Scaffold(
          appBar: AppBar(
            title: const Text('Catálogo del armado'),
            bottom: const TabBar(
              tabs: [
                Tab(text: 'Contratistas'),
                Tab(text: 'Modelos'),
                Tab(text: 'Bodegas'),
              ],
            ),
          ),
          body: const TabBarView(
            children: [_Contratistas(), _Modelos(), _Bodegas()],
          ),
        ),
      );
}

// ---------------------------------------------------------------------------
// Contratistas
// ---------------------------------------------------------------------------

class _Contratistas extends ConsumerWidget {
  const _Contratistas();

  Future<void> _editar(
    BuildContext context,
    WidgetRef ref, {
    AssemblyContractor? contratista,
  }) async {
    final guardado = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _ContratistaSheet(contratista: contratista),
    );

    if (guardado == true) ref.invalidate(assemblyContractorsProvider);
  }

  Future<void> _tarifas(
    BuildContext context,
    WidgetRef ref,
    AssemblyContractor contratista,
  ) async {
    final guardadas = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _TarifasSheet(contratista: contratista),
    );

    if (guardadas == true) ref.invalidate(assemblyContractorsProvider);
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final contratistas = ref.watch(assemblyContractorsProvider);

    return Scaffold(
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _editar(context, ref),
        icon: const Icon(Icons.add),
        label: const Text('Contratista'),
      ),
      body: contratistas.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => _Error(e: e, texto: 'No se pudieron cargar los contratistas.'),
        data: (items) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(assemblyContractorsProvider),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 96),
            children: [
              const _GastoPorMoto(),
              const SizedBox(height: 12),
              if (items.isEmpty)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 32),
                  child: Text(
                    'Todavía no hay contratistas. Agregue el primero para poder registrar '
                    'motos.',
                    textAlign: TextAlign.center,
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
              for (final c in items)
                Card(
                  margin: const EdgeInsets.only(bottom: 8),
                  child: ListTile(
                    contentPadding: const EdgeInsets.fromLTRB(14, 6, 10, 6),
                    title: Text(c.name),
                    subtitle: Text(
                      [
                        if (!c.isActive) 'dado de baja',
                        c.rates.isEmpty
                            ? 'sin tarifas'
                            : '${c.rates.length} '
                                '${c.rates.length == 1 ? 'modelo' : 'modelos'} con tarifa',
                        if (c.notes != null && c.notes!.trim().isNotEmpty) c.notes!.trim(),
                      ].join(' · '),
                      style: theme.textTheme.bodySmall,
                    ),
                    onTap: () => _editar(context, ref, contratista: c),
                    trailing: TextButton(
                      onPressed: () => _tarifas(context, ref, c),
                      child: const Text('Tarifas'),
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

/// Lo que al taller le cuesta armar una moto aparte del pago al técnico: transporte a la
/// bodega, herramienta, lo que el armado se lleve y no dependa del modelo.
class _GastoPorMoto extends ConsumerStatefulWidget {
  const _GastoPorMoto();

  @override
  ConsumerState<_GastoPorMoto> createState() => _GastoPorMotoState();
}

class _GastoPorMotoState extends ConsumerState<_GastoPorMoto> {
  bool _guardando = false;

  Future<void> _editar(double actual) async {
    final controller = TextEditingController(text: actual.toStringAsFixed(2));

    final nuevo = await showDialog<double>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Gasto por moto'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Text(
              'Lo que al taller le cuesta armar una, aparte del pago al técnico. Se copia a '
              'cada moto al registrarla: subirlo hoy no cambia lo que ya se cerró.',
            ),
            const SizedBox(height: 12),
            TextField(
              controller: controller,
              autofocus: true,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              decoration: const InputDecoration(labelText: 'Gasto', isDense: true),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancelar'),
          ),
          FilledButton(
            onPressed: () =>
                Navigator.pop(context, double.tryParse(controller.text.trim())),
            child: const Text('Guardar'),
          ),
        ],
      ),
    );

    if (nuevo == null) return;

    setState(() => _guardando = true);
    try {
      await ref.read(assemblyRepositoryProvider).saveExpensePerMotorcycle(nuevo);
      ref.invalidate(assemblyExpenseProvider);
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(apiErrorMessage(e, 'No se pudo guardar el gasto.'))),
      );
    } finally {
      if (mounted) setState(() => _guardando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final gasto = ref.watch(assemblyExpenseProvider);

    return Card(
      child: ListTile(
        contentPadding: const EdgeInsets.fromLTRB(14, 6, 10, 6),
        title: const Text('Gasto del taller por moto'),
        subtitle: Text(
          'Aparte del pago al técnico. Es lo que descuenta de lo que queda.',
          style: theme.textTheme.bodySmall,
        ),
        trailing: _guardando
            ? const SizedBox(
                height: 20,
                width: 20,
                child: CircularProgressIndicator(strokeWidth: 2),
              )
            : Text(
                money(gasto.value ?? 0, 'HNL'),
                style: theme.textTheme.titleSmall?.copyWith(fontFamily: GarajFonts.mono),
              ),
        onTap: gasto.value == null ? null : () => _editar(gasto.value!),
      ),
    );
  }
}

class _ContratistaSheet extends ConsumerStatefulWidget {
  const _ContratistaSheet({this.contratista});

  final AssemblyContractor? contratista;

  @override
  ConsumerState<_ContratistaSheet> createState() => _ContratistaSheetState();
}

class _ContratistaSheetState extends ConsumerState<_ContratistaSheet> {
  late final _nombre = TextEditingController(text: widget.contratista?.name ?? '');
  late final _notas = TextEditingController(text: widget.contratista?.notes ?? '');

  late bool _activo = widget.contratista?.isActive ?? true;
  late String? _branchId = widget.contratista?.branchId;

  bool _guardando = false;
  String? _error;

  @override
  void dispose() {
    _nombre.dispose();
    _notas.dispose();
    super.dispose();
  }

  Future<void> _guardar() async {
    if (_nombre.text.trim().isEmpty) {
      setState(() => _error = 'El nombre es obligatorio.');
      return;
    }

    setState(() {
      _guardando = true;
      _error = null;
    });

    try {
      await ref.read(assemblyRepositoryProvider).saveContractor(
            id: widget.contratista?.id,
            name: _nombre.text.trim(),
            notes: _notas.text.trim().isEmpty ? null : _notas.text.trim(),
            isActive: _activo,
            branchId: _branchId,
          );

      if (!mounted) return;
      Navigator.pop(context, true);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = apiErrorMessage(e, 'No se pudo guardar el contratista.');
        _guardando = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final sucursales = ref.watch(branchOptionsProvider).value ?? const [];

    return Padding(
      padding: EdgeInsets.fromLTRB(
        16,
        0,
        16,
        MediaQuery.of(context).viewInsets.bottom + 24,
      ),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              widget.contratista == null ? 'Nuevo contratista' : 'Contratista',
              style: theme.textTheme.titleMedium,
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _nombre,
              textCapitalization: TextCapitalization.words,
              decoration: const InputDecoration(labelText: 'Nombre', isDense: true),
            ),
            if (sucursales.length > 1) ...[
              const SizedBox(height: 12),
              DropdownButtonFormField<String>(
                initialValue:
                    sucursales.any((s) => s.id == _branchId) ? _branchId : null,
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: 'Sucursal que lleva el contrato',
                  isDense: true,
                ),
                items: [
                  for (final s in sucursales)
                    DropdownMenuItem(value: s.id, child: Text(s.name)),
                ],
                onChanged: (v) => setState(() => _branchId = v),
              ),
              Padding(
                padding: const EdgeInsets.only(top: 6),
                child: Text(
                  'De aquí sale la sucursal de cada moto: al técnico no se le pregunta, que '
                  'está en la bodega del contratista.',
                  style: theme.textTheme.bodySmall,
                ),
              ),
            ],
            const SizedBox(height: 12),
            TextField(
              controller: _notas,
              maxLines: 2,
              decoration: const InputDecoration(labelText: 'Nota', isDense: true),
            ),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              value: _activo,
              onChanged: (v) => setState(() => _activo = v),
              title: const Text('Activo'),
              subtitle: Text(
                'Dado de baja deja de salir al registrar una moto. Lo ya registrado se queda.',
                style: theme.textTheme.bodySmall,
              ),
            ),
            if (_error != null)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  _error!,
                  style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error),
                ),
              ),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: _guardando ? null : _guardar,
              child: Text(_guardando ? 'Guardando…' : 'Guardar'),
            ),
          ],
        ),
      ),
    );
  }
}

/// La tabla de un contratista: lo que paga por cada modelo y lo que se le paga al técnico.
///
/// Se manda completa y reemplaza lo que había: es una tabla que se llena de un tirón al
/// abrir el contrato y se repasa cuando se renegocia.
class _TarifasSheet extends ConsumerStatefulWidget {
  const _TarifasSheet({required this.contratista});

  final AssemblyContractor contratista;

  @override
  ConsumerState<_TarifasSheet> createState() => _TarifasSheetState();
}

class _TarifasSheetState extends ConsumerState<_TarifasSheet> {
  /// Un par de controladores por modelo: el cobro y el pago.
  final _cobro = <String, TextEditingController>{};
  final _pago = <String, TextEditingController>{};

  bool _guardando = false;
  String? _error;

  @override
  void dispose() {
    for (final c in _cobro.values) {
      c.dispose();
    }
    for (final c in _pago.values) {
      c.dispose();
    }
    super.dispose();
  }

  TextEditingController _controlador(
    Map<String, TextEditingController> mapa,
    String modelId,
    double inicial,
  ) =>
      mapa.putIfAbsent(
        modelId,
        () => TextEditingController(text: inicial == 0 ? '' : inicial.toStringAsFixed(2)),
      );

  Future<void> _guardar(List<AssemblyModel> modelos) async {
    setState(() {
      _guardando = true;
      _error = null;
    });

    // Solo las filas con cobro: un modelo sin precio no es una tarifa en cero, es un modelo
    // que este contratista no manda a armar.
    final rates = <AssemblyRate>[];
    for (final m in modelos) {
      final cobro = double.tryParse(_cobro[m.id]?.text.trim() ?? '');
      if (cobro == null) continue;

      rates.add(
        AssemblyRate(
          modelId: m.id,
          modelName: m.name,
          charge: cobro,
          technicianPay: double.tryParse(_pago[m.id]?.text.trim() ?? '') ?? 0,
        ),
      );
    }

    try {
      await ref.read(assemblyRepositoryProvider).saveRates(widget.contratista.id, rates);
      if (!mounted) return;
      Navigator.pop(context, true);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = apiErrorMessage(e, 'No se pudieron guardar las tarifas.');
        _guardando = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final modelos = ref.watch(assemblyModelsProvider);

    return Padding(
      padding: EdgeInsets.fromLTRB(
        16,
        0,
        16,
        MediaQuery.of(context).viewInsets.bottom + 24,
      ),
      child: modelos.when(
        loading: () => const Padding(
          padding: EdgeInsets.all(32),
          child: Center(child: CircularProgressIndicator()),
        ),
        error: (e, _) => Padding(
          padding: const EdgeInsets.all(24),
          child: Text(apiErrorMessage(e, 'No se pudieron cargar los modelos.')),
        ),
        data: (todos) {
          final activos = todos.where((m) => m.isActive).toList();
          final tarifas = {for (final r in widget.contratista.rates) r.modelId: r};

          return SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  'Tarifas de ${widget.contratista.name}',
                  style: theme.textTheme.titleMedium,
                ),
                Padding(
                  padding: const EdgeInsets.only(top: 4, bottom: 12),
                  child: Text(
                    'Deje en blanco el modelo que este contratista no manda a armar. Los '
                    'cambios valen para las motos que se registren de ahora en adelante.',
                    style: theme.textTheme.bodySmall,
                  ),
                ),
                if (activos.isEmpty)
                  Padding(
                    padding: const EdgeInsets.symmetric(vertical: 24),
                    child: Text(
                      'No hay modelos todavía. Agréguelos en la pestaña Modelos.',
                      textAlign: TextAlign.center,
                      style: theme.textTheme.bodyMedium,
                    ),
                  ),
                for (final m in activos)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 10),
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.end,
                      children: [
                        Expanded(
                          flex: 3,
                          child: Text(m.name, style: theme.textTheme.bodyMedium),
                        ),
                        Expanded(
                          flex: 2,
                          child: TextField(
                            controller: _controlador(
                              _cobro,
                              m.id,
                              tarifas[m.id]?.charge ?? 0,
                            ),
                            keyboardType:
                                const TextInputType.numberWithOptions(decimal: true),
                            decoration: const InputDecoration(
                              labelText: 'Cobro',
                              isDense: true,
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        Expanded(
                          flex: 2,
                          child: TextField(
                            controller: _controlador(
                              _pago,
                              m.id,
                              tarifas[m.id]?.technicianPay ?? 0,
                            ),
                            keyboardType:
                                const TextInputType.numberWithOptions(decimal: true),
                            decoration: const InputDecoration(
                              labelText: 'Pago',
                              isDense: true,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                if (_error != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Text(
                      _error!,
                      style:
                          theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error),
                    ),
                  ),
                const SizedBox(height: 12),
                FilledButton(
                  onPressed: _guardando || activos.isEmpty
                      ? null
                      : () => _guardar(activos),
                  child: Text(_guardando ? 'Guardando…' : 'Guardar las tarifas'),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Modelos y bodegas
// ---------------------------------------------------------------------------

class _Modelos extends ConsumerWidget {
  const _Modelos();

  Future<void> _editar(BuildContext context, WidgetRef ref, {AssemblyModel? modelo}) async {
    final nombre = TextEditingController(text: modelo?.name ?? '');
    final prefijo = TextEditingController(text: modelo?.vinPrefix ?? '');
    var activo = modelo?.isActive ?? true;

    final guardar = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setInner) => AlertDialog(
          title: Text(modelo == null ? 'Nuevo modelo' : 'Modelo'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              TextField(
                controller: nombre,
                autofocus: true,
                textCapitalization: TextCapitalization.characters,
                decoration: const InputDecoration(labelText: 'Nombre', isDense: true),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: prefijo,
                textCapitalization: TextCapitalization.characters,
                decoration: const InputDecoration(
                  labelText: 'Prefijo del VIN',
                  helperText: 'Opcional. Con él, la serie propone el modelo sola.',
                  isDense: true,
                ),
              ),
              SwitchListTile(
                contentPadding: EdgeInsets.zero,
                value: activo,
                onChanged: (v) => setInner(() => activo = v),
                title: const Text('Activo'),
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
      ),
    );

    if (guardar != true || nombre.text.trim().isEmpty) return;

    try {
      await ref.read(assemblyRepositoryProvider).saveModel(
            id: modelo?.id,
            name: nombre.text.trim(),
            isActive: activo,
            vinPrefix: prefijo.text.trim().isEmpty ? null : prefijo.text.trim(),
          );
      ref.invalidate(assemblyModelsProvider);
      ref.invalidate(assemblyPickListsProvider);
    } catch (e) {
      if (!context.mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(apiErrorMessage(e, 'No se pudo guardar el modelo.'))),
      );
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final modelos = ref.watch(assemblyModelsProvider);

    return Scaffold(
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _editar(context, ref),
        icon: const Icon(Icons.add),
        label: const Text('Modelo'),
      ),
      body: modelos.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => _Error(e: e, texto: 'No se pudieron cargar los modelos.'),
        data: (items) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(assemblyModelsProvider),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 96),
            children: [
              for (final m in items)
                Card(
                  margin: const EdgeInsets.only(bottom: 8),
                  child: ListTile(
                    contentPadding: const EdgeInsets.fromLTRB(14, 2, 10, 2),
                    title: Text(m.name),
                    subtitle: Text(
                      [
                        if (!m.isActive) 'dado de baja',
                        if (m.vinPrefix != null && m.vinPrefix!.isNotEmpty)
                          'VIN ${m.vinPrefix}',
                      ].join(' · '),
                      style: theme.textTheme.bodySmall,
                    ),
                    onTap: () => _editar(context, ref, modelo: m),
                  ),
                ),
              if (items.isEmpty)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 32),
                  child: Text(
                    'Todavía no hay modelos.',
                    textAlign: TextAlign.center,
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Bodegas extends ConsumerWidget {
  const _Bodegas();

  Future<void> _editar(
    BuildContext context,
    WidgetRef ref, {
    AssemblyWarehouse? bodega,
  }) async {
    final nombre = TextEditingController(text: bodega?.name ?? '');
    var activa = bodega?.isActive ?? true;

    final guardar = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setInner) => AlertDialog(
          title: Text(bodega == null ? 'Nueva bodega' : 'Bodega'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              TextField(
                controller: nombre,
                autofocus: true,
                textCapitalization: TextCapitalization.words,
                decoration: const InputDecoration(labelText: 'Nombre', isDense: true),
              ),
              SwitchListTile(
                contentPadding: EdgeInsets.zero,
                value: activa,
                onChanged: (v) => setInner(() => activa = v),
                title: const Text('Activa'),
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
      ),
    );

    if (guardar != true || nombre.text.trim().isEmpty) return;

    try {
      await ref.read(assemblyRepositoryProvider).saveWarehouse(
            id: bodega?.id,
            name: nombre.text.trim(),
            isActive: activa,
          );
      ref.invalidate(assemblyWarehousesProvider);
      ref.invalidate(assemblyPickListsProvider);
    } catch (e) {
      if (!context.mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(apiErrorMessage(e, 'No se pudo guardar la bodega.'))),
      );
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final bodegas = ref.watch(assemblyWarehousesProvider);

    return Scaffold(
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _editar(context, ref),
        icon: const Icon(Icons.add),
        label: const Text('Bodega'),
      ),
      body: bodegas.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => _Error(e: e, texto: 'No se pudieron cargar las bodegas.'),
        data: (items) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(assemblyWarehousesProvider),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 96),
            children: [
              for (final b in items)
                Card(
                  margin: const EdgeInsets.only(bottom: 8),
                  child: ListTile(
                    contentPadding: const EdgeInsets.fromLTRB(14, 2, 10, 2),
                    title: Text(b.name),
                    subtitle: b.isActive
                        ? null
                        : Text('dada de baja', style: theme.textTheme.bodySmall),
                    onTap: () => _editar(context, ref, bodega: b),
                  ),
                ),
              if (items.isEmpty)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 32),
                  child: Text(
                    'Todavía no hay bodegas.',
                    textAlign: TextAlign.center,
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Error extends StatelessWidget {
  const _Error({required this.e, required this.texto});

  final Object e;
  final String texto;

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Text(apiErrorMessage(e, texto), textAlign: TextAlign.center),
        ),
      );
}

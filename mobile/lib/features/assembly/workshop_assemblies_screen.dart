import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/assembly_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/assembly.dart';
import '../../core/theme/garaj_brand.dart';
import '../reports/reports_screen.dart' show money;

/// Las motos armadas del taller, como las ve quien factura.
///
/// Es la lista con los dos precios: lo que se le cobra al contratista y lo que se le paga al
/// técnico. De aquí sale lo que hay que facturar este mes y lo que hay que pagar, y aquí se
/// corrige la serie mal tecleada antes de que entre en un corte.
class WorkshopAssembliesScreen extends ConsumerStatefulWidget {
  const WorkshopAssembliesScreen({super.key});

  @override
  ConsumerState<WorkshopAssembliesScreen> createState() => _WorkshopAssembliesScreenState();
}

class _WorkshopAssembliesScreenState extends ConsumerState<WorkshopAssembliesScreen> {
  AssemblyFilter _filtro = AssemblyFilter(from: _inicioDeMes(DateTime.now()));

  final _serie = TextEditingController();
  Timer? _espera;

  static DateTime _inicioDeMes(DateTime d) => DateTime(d.year, d.month);

  @override
  void dispose() {
    _espera?.cancel();
    _serie.dispose();
    super.dispose();
  }

  /// Se espera a que deje de teclear: cada pulsación sería una consulta, y la lista
  /// parpadeando mientras se escribe una serie de 17 caracteres no deja leer nada.
  void _buscar(String value) {
    _espera?.cancel();
    _espera = Timer(const Duration(milliseconds: 400), () {
      if (mounted) setState(() => _filtro = _filtro.copyWith(serial: value, page: 1));
    });
  }

  void _periodo(DateTime? desde, DateTime? hasta) => setState(() {
        _filtro = AssemblyFilter(
          from: desde,
          to: hasta,
          contractorId: _filtro.contractorId,
          technicianUserId: _filtro.technicianUserId,
          serial: _filtro.serial,
          onlyUnbilled: _filtro.onlyUnbilled,
          onlyUnpaid: _filtro.onlyUnpaid,
        );
      });

  Future<void> _corregir(AssemblyRow moto) async {
    final guardada = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _CorregirSheet(moto: moto),
    );

    if (guardada == true) ref.invalidate(assembliesProvider(_filtro));
  }

  Future<void> _borrar(AssemblyRow moto) async {
    final confirmado = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('¿Borrar esta moto?'),
        content: Text(
          'Se quita del taller la ${moto.serial}. No se puede deshacer: si fue un error de '
          'tecleo, mejor corríjala.',
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

    try {
      await ref.read(assemblyRepositoryProvider).remove(moto.id);
      ref.invalidate(assembliesProvider(_filtro));
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Moto borrada.')),
      );
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(apiErrorMessage(e, 'No se pudo borrar la moto.'))),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final pagina = ref.watch(assembliesProvider(_filtro));
    final listas = ref.watch(assemblyPickListsProvider).value;

    // Borrar una moto es del Dueño: el Encargado la corrige, pero no la hace desaparecer.
    // Lo decide el servidor; aquí solo se evita ofrecer un botón que va a dar 403.
    final auth = ref.watch(authControllerProvider);
    final esDueno = auth is AuthSignedIn && auth.user.esDueno;
    final hoy = DateTime.now();
    final mesPasado = DateTime(hoy.year, hoy.month - 1);

    return Scaffold(
      appBar: AppBar(
        title: const Text('Motos del taller'),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(104),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
            child: Column(
              children: [
                TextField(
                  controller: _serie,
                  onChanged: _buscar,
                  textInputAction: TextInputAction.search,
                  textCapitalization: TextCapitalization.characters,
                  decoration: InputDecoration(
                    hintText: 'Serie',
                    prefixIcon: const Icon(Icons.search),
                    isDense: true,
                    border: const OutlineInputBorder(),
                    suffixIcon: _serie.text.isEmpty
                        ? null
                        : IconButton(
                            tooltip: 'Limpiar',
                            icon: const Icon(Icons.close),
                            onPressed: () {
                              _serie.clear();
                              _buscar('');
                            },
                          ),
                  ),
                ),
                const SizedBox(height: 8),
                SizedBox(
                  height: 40,
                  child: ListView(
                    scrollDirection: Axis.horizontal,
                    children: [
                      _Chip(
                        label: 'Este mes',
                        selected: _filtro.from == _inicioDeMes(hoy) && _filtro.to == null,
                        onSelected: () => _periodo(_inicioDeMes(hoy), null),
                      ),
                      _Chip(
                        label: 'Mes pasado',
                        selected: _filtro.from == mesPasado,
                        onSelected: () => _periodo(
                          mesPasado,
                          DateTime(hoy.year, hoy.month, 0),
                        ),
                      ),
                      _Chip(
                        label: 'Todo',
                        selected: _filtro.from == null,
                        onSelected: () => _periodo(null, null),
                      ),
                      const VerticalDivider(width: 16),
                      _Chip(
                        label: 'Sin facturar',
                        selected: _filtro.onlyUnbilled,
                        onSelected: () => setState(
                          () => _filtro = _filtro.copyWith(
                            onlyUnbilled: !_filtro.onlyUnbilled,
                            page: 1,
                          ),
                        ),
                      ),
                      _Chip(
                        label: 'Sin pagar',
                        selected: _filtro.onlyUnpaid,
                        onSelected: () => setState(
                          () => _filtro =
                              _filtro.copyWith(onlyUnpaid: !_filtro.onlyUnpaid, page: 1),
                        ),
                      ),
                      if (listas != null) ...[
                        const VerticalDivider(width: 16),
                        _Chip(
                          label: 'Todos los contratistas',
                          selected: _filtro.contractorId == null,
                          onSelected: () => setState(
                            () => _filtro =
                                _filtro.copyWith(limpiarContratista: true, page: 1),
                          ),
                        ),
                        for (final c in listas.contractors)
                          _Chip(
                            label: c.name,
                            selected: _filtro.contractorId == c.id,
                            onSelected: () => setState(
                              () => _filtro = _filtro.copyWith(contractorId: c.id, page: 1),
                            ),
                          ),
                        if (listas.technicians.isNotEmpty) ...[
                          const VerticalDivider(width: 16),
                          _Chip(
                            label: 'Todos los técnicos',
                            selected: _filtro.technicianUserId == null,
                            onSelected: () => setState(
                              () => _filtro = _filtro.copyWith(limpiarTecnico: true, page: 1),
                            ),
                          ),
                          for (final t in listas.technicians)
                            _Chip(
                              label: t.name,
                              selected: _filtro.technicianUserId == t.id,
                              onSelected: () => setState(
                                () => _filtro =
                                    _filtro.copyWith(technicianUserId: t.id, page: 1),
                              ),
                            ),
                        ],
                      ],
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
      body: pagina.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Text(
              apiErrorMessage(e, 'No se pudieron cargar las motos.'),
              textAlign: TextAlign.center,
            ),
          ),
        ),
        data: (p) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(assembliesProvider(_filtro)),
          child: ListView.builder(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
            itemCount: p.items.length + 2,
            itemBuilder: (context, i) {
              if (i == 0) return _Totales(pagina: p);

              if (i == p.items.length + 1) {
                if (p.items.isEmpty) {
                  return Padding(
                    padding: const EdgeInsets.symmetric(vertical: 32),
                    child: Text(
                      'Nada con esos filtros.',
                      textAlign: TextAlign.center,
                      style: theme.textTheme.bodyMedium,
                    ),
                  );
                }

                if (!p.hayMas && p.page == 1) return const SizedBox(height: 8);

                // Páginas y no un «ver más» que apila: con una tanda de mil, apilar deja al
                // teléfono cargando una lista que nadie recorre con el dedo.
                return _Paginas(
                  pagina: p.page,
                  de: (p.total / p.pageSize).ceil(),
                  onCambiar: (pagina) =>
                      setState(() => _filtro = _filtro.copyWith(page: pagina)),
                );
              }

              return _MotoCard(
                moto: p.items[i - 1],
                puedeBorrar: esDueno,
                onCorregir: _corregir,
                onBorrar: _borrar,
              );
            },
          ),
        ),
      ),
    );
  }
}

/// Las cuatro cifras del filtro completo, no las de la página: es el reporte del taller.
///
/// «Queda» no sale de aquí para nadie más: lo que el taller gana con un contratista no va en
/// la hoja que recibe ese contratista, ni en la del técnico.
class _Totales extends StatelessWidget {
  const _Totales({required this.pagina});

  final AssemblyPage pagina;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${pagina.total} ${pagina.total == 1 ? 'moto' : 'motos'}',
              style: theme.textTheme.titleSmall,
            ),
            const SizedBox(height: 8),
            _Linea(titulo: 'Se cobra', valor: pagina.totalCharge),
            _Linea(titulo: 'Se paga', valor: pagina.totalPay),
            if (pagina.totalExpense > 0)
              _Linea(titulo: 'Gasto del taller', valor: pagina.totalExpense),
            const Divider(height: 18),
            _Linea(titulo: 'Queda', valor: pagina.margin, fuerte: true),
          ],
        ),
      ),
    );
  }
}

class _Linea extends StatelessWidget {
  const _Linea({required this.titulo, required this.valor, this.fuerte = false});

  final String titulo;
  final double valor;
  final bool fuerte;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final estilo = fuerte
        ? theme.textTheme.titleMedium?.copyWith(fontFamily: GarajFonts.mono)
        : theme.textTheme.bodyMedium?.copyWith(fontFamily: GarajFonts.mono);

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 2),
      child: Row(
        children: [
          Expanded(
            child: Text(
              titulo,
              style: fuerte ? theme.textTheme.titleSmall : theme.textTheme.bodyMedium,
            ),
          ),
          Text(money(valor, 'HNL'), style: estilo),
        ],
      ),
    );
  }
}

/// El pie para moverse entre páginas. Los totales de arriba son del filtro entero, así que
/// cambiar de página no cambia ninguna cifra: solo qué motos se están leyendo.
class _Paginas extends StatelessWidget {
  const _Paginas({required this.pagina, required this.de, required this.onCambiar});

  final int pagina;
  final int de;
  final ValueChanged<int> onCambiar;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          IconButton(
            tooltip: 'Anterior',
            icon: const Icon(Icons.chevron_left),
            onPressed: pagina <= 1 ? null : () => onCambiar(pagina - 1),
          ),
          Text('Página $pagina de $de', style: theme.textTheme.bodySmall),
          IconButton(
            tooltip: 'Siguiente',
            icon: const Icon(Icons.chevron_right),
            onPressed: pagina >= de ? null : () => onCambiar(pagina + 1),
          ),
        ],
      ),
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip({required this.label, required this.selected, required this.onSelected});

  final String label;
  final bool selected;
  final VoidCallback onSelected;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(right: 8),
        child: ChoiceChip(
          label: Text(label),
          selected: selected,
          onSelected: (_) => onSelected(),
        ),
      );
}

class _MotoCard extends StatelessWidget {
  const _MotoCard({
    required this.moto,
    required this.puedeBorrar,
    required this.onCorregir,
    required this.onBorrar,
  });

  final AssemblyRow moto;
  final bool puedeBorrar;
  final Future<void> Function(AssemblyRow moto) onCorregir;
  final Future<void> Function(AssemblyRow moto) onBorrar;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      child: ListTile(
        contentPadding: const EdgeInsets.fromLTRB(14, 6, 6, 6),
        title: Text(moto.serial, style: const TextStyle(fontFamily: GarajFonts.mono)),
        subtitle: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${moto.modelName} · ${_dia(moto.assembledOn)}',
              style: theme.textTheme.bodySmall,
            ),
            Text(
              '${moto.technicianName} · ${moto.warehouseName}',
              style: theme.textTheme.bodySmall,
            ),
            Text(
              '${moto.contractorName} · paga ${money(moto.technicianPay, 'HNL')}',
              style: theme.textTheme.bodySmall,
            ),
            if (moto.billed || moto.paid)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Wrap(
                  spacing: 6,
                  children: [
                    if (moto.billed) const _Sello(texto: 'facturada'),
                    if (moto.paid) const _Sello(texto: 'pagada'),
                  ],
                ),
              ),
          ],
        ),
        trailing: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              money(moto.charge, 'HNL'),
              style: theme.textTheme.titleSmall?.copyWith(fontFamily: GarajFonts.mono),
            ),
            PopupMenuButton<String>(
              tooltip: 'Más',
              onSelected: (v) => v == 'corregir' ? onCorregir(moto) : onBorrar(moto),
              itemBuilder: (context) => [
                // Una moto que ya entró en un corte no se toca: el corte congeló lo que
                // incluyó, y cambiarla por detrás dejaría la factura sin cuadrar. Primero
                // se reabre el corte.
                PopupMenuItem(
                  value: 'corregir',
                  enabled: !moto.billed,
                  child: const Text('Corregir'),
                ),
                if (puedeBorrar)
                  PopupMenuItem(
                    value: 'borrar',
                    enabled: !moto.billed && !moto.paid,
                    child: const Text('Borrar'),
                  ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _Sello extends StatelessWidget {
  const _Sello({required this.texto});

  final String texto;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(texto, style: theme.textTheme.labelSmall),
    );
  }
}

/// Corregir una moto: el error de tecleo de la serie, el modelo equivocado, o el pago que no
/// sale de ninguna tabla.
class _CorregirSheet extends ConsumerStatefulWidget {
  const _CorregirSheet({required this.moto});

  final AssemblyRow moto;

  @override
  ConsumerState<_CorregirSheet> createState() => _CorregirSheetState();
}

class _CorregirSheetState extends ConsumerState<_CorregirSheet> {
  late final _serie = TextEditingController(text: widget.moto.serial);
  late final _notas = TextEditingController(text: widget.moto.notes ?? '');
  late final _cobro = TextEditingController(text: widget.moto.charge.toStringAsFixed(2));
  late final _pago =
      TextEditingController(text: widget.moto.technicianPay.toStringAsFixed(2));

  late String _modelId = widget.moto.modelId;
  late String _contractorId = widget.moto.contractorId;
  late String _warehouseId = widget.moto.warehouseId;
  late String _technicianId = widget.moto.technicianUserId;
  late DateTime _fecha = widget.moto.assembledOn;

  bool _guardando = false;
  String? _error;

  @override
  void dispose() {
    _serie.dispose();
    _notas.dispose();
    _cobro.dispose();
    _pago.dispose();
    super.dispose();
  }

  Future<void> _elegirFecha() async {
    final elegida = await showDatePicker(
      context: context,
      initialDate: _fecha,
      firstDate: DateTime(DateTime.now().year - 2),
      lastDate: DateTime.now(),
    );

    if (elegida != null) setState(() => _fecha = elegida);
  }

  Future<void> _guardar() async {
    setState(() {
      _guardando = true;
      _error = null;
    });

    try {
      await ref.read(assemblyRepositoryProvider).correct(
            widget.moto.id,
            serial: _serie.text.trim(),
            modelId: _modelId,
            contractorId: _contractorId,
            warehouseId: _warehouseId,
            technicianUserId: _technicianId,
            assembledOn: _fecha,
            notes: _notas.text.trim().isEmpty ? null : _notas.text.trim(),
            charge: double.tryParse(_cobro.text.trim()),
            technicianPay: double.tryParse(_pago.text.trim()),
          );

      if (!mounted) return;
      Navigator.pop(context, true);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = apiErrorMessage(e, 'No se pudo corregir la moto.');
        _guardando = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final listas = ref.watch(assemblyPickListsProvider).value;

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
            Text('Corregir la moto', style: theme.textTheme.titleMedium),
            const SizedBox(height: 12),
            TextField(
              controller: _serie,
              textCapitalization: TextCapitalization.characters,
              decoration: const InputDecoration(labelText: 'Serie', isDense: true),
            ),
            const SizedBox(height: 12),
            if (listas == null)
              const Center(child: CircularProgressIndicator())
            else ...[
              _Selector(
                etiqueta: 'Modelo',
                valor: _modelId,
                opciones: listas.models,
                onChanged: (v) => setState(() => _modelId = v),
              ),
              const SizedBox(height: 12),
              _Selector(
                etiqueta: 'Contratista',
                valor: _contractorId,
                opciones: listas.contractors,
                onChanged: (v) => setState(() => _contractorId = v),
              ),
              const SizedBox(height: 12),
              _Selector(
                etiqueta: 'Bodega',
                valor: _warehouseId,
                opciones: listas.warehouses,
                onChanged: (v) => setState(() => _warehouseId = v),
              ),
              if (listas.technicians.isNotEmpty) ...[
                const SizedBox(height: 12),
                _Selector(
                  etiqueta: 'Técnico',
                  valor: _technicianId,
                  opciones: listas.technicians,
                  onChanged: (v) => setState(() => _technicianId = v),
                ),
              ],
            ],
            const SizedBox(height: 12),
            InkWell(
              onTap: _elegirFecha,
              child: InputDecorator(
                decoration: const InputDecoration(labelText: 'Día en que se armó', isDense: true),
                child: Text(_dia(_fecha)),
              ),
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _cobro,
                    keyboardType: const TextInputType.numberWithOptions(decimal: true),
                    decoration: const InputDecoration(labelText: 'Se cobra', isDense: true),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: TextField(
                    controller: _pago,
                    keyboardType: const TextInputType.numberWithOptions(decimal: true),
                    decoration: const InputDecoration(labelText: 'Se le paga', isDense: true),
                  ),
                ),
              ],
            ),
            Padding(
              padding: const EdgeInsets.only(top: 6),
              child: Text(
                'Los dos vienen de la tarifa del contratista. Cámbielos solo para esta moto, '
                'cuando lo acordado no es lo de la tabla.',
                style: theme.textTheme.bodySmall,
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _notas,
              maxLines: 2,
              decoration: const InputDecoration(labelText: 'Nota', isDense: true),
            ),
            if (_error != null)
              Padding(
                padding: const EdgeInsets.only(top: 12),
                child: Text(
                  _error!,
                  style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error),
                ),
              ),
            const SizedBox(height: 16),
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

class _Selector extends StatelessWidget {
  const _Selector({
    required this.etiqueta,
    required this.valor,
    required this.opciones,
    required this.onChanged,
  });

  final String etiqueta;
  final String valor;
  final List<AssemblyName> opciones;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    // El catálogo pudo cambiar desde que se registró la moto —un modelo que se dio de baja—.
    // Si el valor de la moto ya no está en la lista, el desplegable nace vacío en vez de
    // reventar, y quien corrige elige uno de los de hoy.
    final existe = opciones.any((o) => o.id == valor);

    return DropdownButtonFormField<String>(
      initialValue: existe ? valor : null,
      isExpanded: true,
      decoration: InputDecoration(labelText: etiqueta, isDense: true),
      items: [
        for (final o in opciones)
          DropdownMenuItem(value: o.id, child: Text(o.name, overflow: TextOverflow.ellipsis)),
      ],
      onChanged: (v) {
        if (v != null) onChanged(v);
      },
    );
  }
}

String _dia(DateTime value) =>
    '${value.day.toString().padLeft(2, '0')}/${value.month.toString().padLeft(2, '0')}/'
    '${value.year}';

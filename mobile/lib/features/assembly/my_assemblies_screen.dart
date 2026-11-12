import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/api/assembly_repository.dart';
import '../../core/models/assembly.dart';

/// Lo que uno lleva armado, y lo que suma para él.
///
/// El número de arriba es el que de verdad importa: cuántas van en el periodo. Al propio
/// técnico no le viaja lo que gana por moto —eso se liquida en su corte—, así que su
/// pantalla cuenta motos y no lempiras.
///
/// Lleva filtros y páginas porque hay contratos que se arman por tandas: cien motos en un
/// día es un martes normal, y una lista de mil no se recorre con el dedo.
class MyAssembliesScreen extends ConsumerStatefulWidget {
  const MyAssembliesScreen({super.key});

  @override
  ConsumerState<MyAssembliesScreen> createState() => _MyAssembliesScreenState();
}

class _MyAssembliesScreenState extends ConsumerState<MyAssembliesScreen> {
  AssemblyFilter _filtro = AssemblyFilter(from: _inicioDeMes(DateTime.now()));

  final _serie = TextEditingController();
  Timer? _espera;

  static DateTime _inicioDeMes(DateTime d) => DateTime(d.year, d.month);

  /// «Todo» se manda como una fecha vieja y no como vacío: vacío le dice al servidor que
  /// use su valor por omisión, que es el mes corriente, y el chip estaría mintiendo.
  static final _desdeSiempre = DateTime(2000);

  @override
  void dispose() {
    _espera?.cancel();
    _serie.dispose();
    super.dispose();
  }

  /// Se espera a que deje de teclear: una serie son 17 caracteres, y con cada uno la lista
  /// parpadearía sin dejar leer nada.
  void _buscar(String value) {
    _espera?.cancel();
    _espera = Timer(const Duration(milliseconds: 400), () {
      if (mounted) setState(() => _filtro = _filtro.copyWith(serial: value, page: 1));
    });
  }

  /// Cambiar de periodo siempre vuelve a la primera página: quedarse en la siete de un mes
  /// que ahora tiene dos páginas es mirar una lista vacía sin entender por qué.
  void _periodo(DateTime desde, DateTime? hasta) => setState(() {
        _filtro = AssemblyFilter(
          from: desde,
          to: hasta,
          warehouseId: _filtro.warehouseId,
          serial: _filtro.serial,
        );
      });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final datos = ref.watch(myAssembliesProvider(_filtro));
    final bodegas = ref.watch(assemblyPickListsProvider).value?.warehouses ?? const [];

    final hoy = DateTime.now();
    final mesPasado = DateTime(hoy.year, hoy.month - 1);

    return Scaffold(
      appBar: AppBar(
        title: const Text('Mis motos armadas'),
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
                        onSelected: () =>
                            _periodo(mesPasado, DateTime(hoy.year, hoy.month, 0)),
                      ),
                      _Chip(
                        label: 'Todo',
                        selected: _filtro.from == _desdeSiempre,
                        onSelected: () => _periodo(_desdeSiempre, null),
                      ),
                      if (bodegas.length > 1) ...[
                        const VerticalDivider(width: 16),
                        _Chip(
                          label: 'Todas las bodegas',
                          selected: _filtro.warehouseId == null,
                          onSelected: () => setState(
                            () => _filtro = _filtro.copyWith(limpiarBodega: true, page: 1),
                          ),
                        ),
                        for (final b in bodegas)
                          _Chip(
                            label: b.name,
                            selected: _filtro.warehouseId == b.id,
                            onSelected: () => setState(
                              () => _filtro = _filtro.copyWith(warehouseId: b.id, page: 1),
                            ),
                          ),
                      ],
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => context.push('/armar-moto'),
        icon: const Icon(Icons.add),
        label: const Text('Armé una moto'),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(myAssembliesProvider(_filtro)),
        child: datos.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(
            children: [
              Padding(
                padding: const EdgeInsets.all(24),
                child: Text(apiErrorMessage(e), textAlign: TextAlign.center),
              ),
            ],
          ),
          data: (d) => ListView(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
            children: [
              _Resumen(datos: d, todo: _filtro.from == _desdeSiempre),
              const SizedBox(height: 16),
              if (d.items.isEmpty)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 32),
                  child: Text(
                    _filtro.serial?.trim().isNotEmpty == true ||
                            _filtro.warehouseId != null
                        ? 'Nada con esos filtros.'
                        : 'Todavía no ha registrado ninguna en ese periodo.',
                    textAlign: TextAlign.center,
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
              for (final m in d.items) _Fila(moto: m),
              if (d.hasMore || d.page > 1)
                _Paginas(
                  pagina: d.page,
                  de: (d.count / _filtro.pageSize).ceil(),
                  onCambiar: (p) => setState(() => _filtro = _filtro.copyWith(page: p)),
                ),
            ],
          ),
        ),
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

/// El pie para moverse entre páginas.
///
/// Páginas y no un «ver más» que va apilando: en una tanda de mil, apilar deja al teléfono
/// cargando una lista que nadie va a recorrer con el dedo, y perder el sitio al volver.
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

class _Resumen extends StatelessWidget {
  const _Resumen({required this.datos, required this.todo});

  final MyAssemblies datos;

  /// Sin periodo: el rótulo no puede decir «del 1 al 11» cuando se están viendo tres años.
  final bool todo;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              todo ? 'Todo lo que lleva armado' : 'Del ${_dia(datos.from)} al ${_dia(datos.to)}',
              style: theme.textTheme.bodySmall,
            ),
            const SizedBox(height: 8),
            Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('${datos.count}', style: theme.textTheme.headlineMedium),
                      Text(
                        datos.count == 1 ? 'moto armada' : 'motos armadas',
                        style: theme.textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
                // Sin monto cuando el servidor no lo manda, que es el caso del propio
                // técnico: lo que gana por moto se liquida en su corte, no en esta pantalla.
                // Nulo o cero: el cero es el disfraz con el que viaja mientras queden
                // compilaciones viejas en la calle, que se caen con nulo.
                if (datos.pay case final total? when total > 0)
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.end,
                    children: [
                      Text(
                        'L ${total.toStringAsFixed(2)}',
                        style: theme.textTheme.headlineMedium?.copyWith(
                          color: theme.colorScheme.primary,
                        ),
                      ),
                      Text('le corresponde', style: theme.textTheme.bodySmall),
                    ],
                  ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  static String _dia(DateTime d) =>
      '${d.day.toString().padLeft(2, '0')}/${d.month.toString().padLeft(2, '0')}';
}

class _Fila extends StatelessWidget {
  const _Fila({required this.moto});

  final MotorcycleAssembly moto;

  @override
  Widget build(BuildContext context) => ListTile(
        contentPadding: EdgeInsets.zero,
        leading: const Icon(Icons.two_wheeler),
        title: Text(moto.serial, style: const TextStyle(fontFamily: 'monospace')),
        subtitle: Text(
          '${moto.modelName} · ${moto.warehouseName} · '
          '${moto.assembledOn.day.toString().padLeft(2, '0')}/'
          '${moto.assembledOn.month.toString().padLeft(2, '0')}',
        ),
        trailing: (moto.technicianPay ?? 0) <= 0
            ? null
            : Text('L ${moto.technicianPay!.toStringAsFixed(2)}'),
      );
}

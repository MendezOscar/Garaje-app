import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_blue_plus/flutter_blue_plus.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/vehicle_scan_repository.dart';
import '../../core/models/media.dart';
import '../../core/scanner/elm327.dart';
import 'photo_capture.dart';

/// Leer el vehículo con el adaptador OBD2, desde la orden.
///
/// Se entra desde la orden a propósito: así no hay que preguntar de qué carro es la lectura
/// —que es donde se equivoca uno con el informe compartido desde la tableta— y el técnico ya
/// tiene delante la placa que está trabajando.
class ScanVehicleScreen extends ConsumerStatefulWidget {
  const ScanVehicleScreen({
    required this.workOrderId,
    required this.workOrderNumber,
    required this.vehicleLabel,
    super.key,
  });

  final String workOrderId;
  final String workOrderNumber;
  final String vehicleLabel;

  @override
  ConsumerState<ScanVehicleScreen> createState() => _ScanVehicleScreenState();
}

enum _Paso { adaptadores, leyendo, resultado }

class _ScanVehicleScreenState extends ConsumerState<ScanVehicleScreen> {
  final _notas = TextEditingController();
  final _kilometraje = TextEditingController();

  StreamSubscription<List<ScanResult>>? _busqueda;
  StreamSubscription<BluetoothAdapterState>? _estadoBluetooth;

  SesionObd? _sesion;
  LecturaObd? _lectura;

  _Paso _paso = _Paso.adaptadores;
  List<ScanResult> _encontrados = const [];
  BluetoothAdapterState _bluetooth = BluetoothAdapterState.unknown;
  bool _buscando = false;
  bool _guardando = false;
  String? _error;

  @override
  void initState() {
    super.initState();

    _estadoBluetooth = FlutterBluePlus.adapterState.listen((estado) {
      if (!mounted) return;
      setState(() => _bluetooth = estado);
      if (estado == BluetoothAdapterState.on && _paso == _Paso.adaptadores && !_buscando) {
        _buscar();
      }
    });
  }

  @override
  void dispose() {
    _busqueda?.cancel();
    _estadoBluetooth?.cancel();
    _notas.dispose();
    _kilometraje.dispose();
    // Soltar el adaptador al salir: si queda conectado, el siguiente intento —o el teléfono
    // del compañero— no lo encuentra.
    unawaited(_sesion?.cerrar());
    unawaited(SesionObd.pararBusqueda());
    super.dispose();
  }

  Future<void> _buscar() async {
    setState(() {
      _buscando = true;
      _error = null;
      _encontrados = const [];
    });

    await _busqueda?.cancel();
    // Si quedó una búsqueda corriendo, arrancar otra encima falla: se para primero.
    await SesionObd.pararBusqueda();

    _busqueda = SesionObd.buscar().listen(
      (resultados) {
        if (!mounted) return;
        setState(() {
          // Sin nombre no se puede elegir nada: un aparato que no se presenta no se le
          // puede ofrecer al técnico como «conéctese a este».
          _encontrados = resultados
              .where((r) => r.device.platformName.trim().isNotEmpty)
              .toList();
        });
      },
      onError: (Object e) {
        if (!mounted) return;
        setState(() {
          _buscando = false;
          _error = 'No se pudo buscar adaptadores. Revise que el Bluetooth esté encendido.';
        });
      },
    );

    // La búsqueda se para sola por tiempo; esto solo apaga el indicador de la pantalla.
    await Future<void>.delayed(const Duration(seconds: 12));
    if (mounted) setState(() => _buscando = false);
  }

  Future<void> _leer(BluetoothDevice dispositivo) async {
    setState(() {
      _paso = _Paso.leyendo;
      _error = null;
    });

    try {
      final sesion = await SesionObd.conectar(dispositivo);
      _sesion = sesion;

      final lectura = await sesion.leer();
      if (!mounted) return;

      setState(() {
        _lectura = lectura;
        _paso = _Paso.resultado;
      });

      // El adaptador ya dio lo que tenía que dar; se suelta antes de guardar para no
      // dejarlo tomado mientras el técnico escribe las observaciones.
      await sesion.cerrar();
      _sesion = null;
    } on ErrorObd catch (e) {
      if (!mounted) return;
      await _sesion?.cerrar();
      _sesion = null;
      setState(() {
        _paso = _Paso.adaptadores;
        _error = e.mensaje;
      });
    } catch (e) {
      if (!mounted) return;
      await _sesion?.cerrar();
      _sesion = null;
      setState(() {
        _paso = _Paso.adaptadores;
        _error = 'No se pudo conectar con el adaptador. Inténtelo otra vez.';
      });
    }
  }

  Future<void> _guardar() async {
    final lectura = _lectura;
    if (lectura == null) return;

    setState(() => _guardando = true);

    try {
      await ref.read(vehicleScanRepositoryProvider).create(
            workOrderId: widget.workOrderId,
            scannedAt: DateTime.now(),
            codigos: lectura.codigos,
            vin: lectura.vin,
            mileage: int.tryParse(_kilometraje.text.replaceAll(RegExp(r'[^0-9]'), '')),
            deviceName: lectura.adaptador,
            protocol: lectura.protocolo,
            notes: _notas.text.trim().isEmpty ? null : _notas.text.trim(),
          );

      ref.invalidate(vehicleScansProvider(widget.workOrderId));
      // El servidor dejó el informe adjunto a la orden: la galería tiene que enterarse.
      ref.invalidate(mediaProviderDe(MediaOwnerType.workOrder, widget.workOrderId));

      if (!mounted) return;

      Navigator.of(context).pop(true);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Lectura guardada. El informe quedó en la orden.')),
      );
    } catch (e) {
      if (!mounted) return;
      setState(() => _guardando = false);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(apiErrorMessage(e, 'No se pudo guardar la lectura.'))),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Escanear vehículo'),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(26),
          child: Padding(
            padding: const EdgeInsets.only(left: 16, bottom: 10),
            child: Align(
              alignment: Alignment.centerLeft,
              child: Text(
                '${widget.workOrderNumber} · ${widget.vehicleLabel}',
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ),
          ),
        ),
      ),
      body: switch (_paso) {
        _Paso.adaptadores => _listaDeAdaptadores(),
        _Paso.leyendo => _leyendo(),
        _Paso.resultado => _resultado(),
      },
    );
  }

  Widget _listaDeAdaptadores() {
    final theme = Theme.of(context);

    if (_bluetooth == BluetoothAdapterState.unauthorized) {
      return const _Aviso(
        icono: Icons.bluetooth_disabled,
        titulo: 'Falta el permiso de Bluetooth',
        detalle: 'Déle permiso a GarajApp para usar Bluetooth desde los ajustes del teléfono '
            'y vuelva a entrar a esta pantalla.',
      );
    }

    if (_bluetooth == BluetoothAdapterState.off) {
      return const _Aviso(
        icono: Icons.bluetooth_disabled,
        titulo: 'El Bluetooth está apagado',
        detalle: 'Enciéndalo para que el teléfono encuentre el adaptador del vehículo.',
      );
    }

    return Column(
      children: [
        Card(
          margin: const EdgeInsets.fromLTRB(16, 12, 16, 0),
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Antes de conectar', style: theme.textTheme.titleSmall),
                const SizedBox(height: 6),
                const Text(
                  '1. Enchufe el adaptador en el conector de diagnóstico, debajo del timón.\n'
                  '2. Ponga la llave en contacto. No hace falta arrancar.\n'
                  '3. Elija el adaptador de la lista.',
                ),
              ],
            ),
          ),
        ),
        if (_error != null)
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
            child: Text(_error!, style: TextStyle(color: theme.colorScheme.error)),
          ),
        if (_buscando) const Padding(
          padding: EdgeInsets.only(top: 12),
          child: LinearProgressIndicator(),
        ),
        Expanded(
          child: _encontrados.isEmpty
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(24),
                    child: Text(
                      _buscando
                          ? 'Buscando adaptadores…'
                          : 'No apareció ningún adaptador. Revise que esté enchufado y que su '
                              'luz esté encendida, y busque otra vez.',
                      textAlign: TextAlign.center,
                    ),
                  ),
                )
              : ListView.separated(
                  itemCount: _encontrados.length,
                  separatorBuilder: (_, __) => const Divider(height: 1),
                  itemBuilder: (context, i) {
                    final resultado = _encontrados[i];

                    return ListTile(
                      leading: const Icon(Icons.bluetooth),
                      title: Text(resultado.device.platformName),
                      subtitle: Text(resultado.device.remoteId.str),
                      trailing: const Icon(Icons.chevron_right),
                      onTap: () => _leer(resultado.device),
                    );
                  },
                ),
        ),
        SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: FilledButton.icon(
              onPressed: _buscando ? null : _buscar,
              icon: const Icon(Icons.search),
              label: const Text('Buscar adaptadores'),
            ),
          ),
        ),
      ],
    );
  }

  Widget _leyendo() => const Center(
        child: Padding(
          padding: EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              CircularProgressIndicator(),
              SizedBox(height: 20),
              Text(
                'Leyendo el vehículo…',
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
              ),
              SizedBox(height: 8),
              Text(
                'No apague la llave ni desenchufe el adaptador. Puede tardar medio minuto.',
                textAlign: TextAlign.center,
              ),
            ],
          ),
        ),
      );

  Widget _resultado() {
    final lectura = _lectura!;
    final theme = Theme.of(context);

    return Column(
      children: [
        Expanded(
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
            children: [
              if (lectura.codigos.isEmpty)
                Card(
                  color: theme.colorScheme.secondaryContainer,
                  child: const Padding(
                    padding: EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'El vehículo no reportó ningún código',
                          style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
                        ),
                        SizedBox(height: 6),
                        Text(
                          'Las tres memorias salieron limpias. Vale la pena guardarlo: es lo '
                          'que se le enseña al cliente que llegó diciendo que el carro anda mal.',
                        ),
                      ],
                    ),
                  ),
                )
              else ...[
                Text(
                  lectura.codigos.length == 1
                      ? '1 código encontrado'
                      : '${lectura.codigos.length} códigos encontrados',
                  style: theme.textTheme.titleMedium,
                ),
                const SizedBox(height: 8),
                ...lectura.codigos.map(
                  (codigo) => Card(
                    child: ListTile(
                      title: Text(
                        codigo.code,
                        style: const TextStyle(fontWeight: FontWeight.w700),
                      ),
                      subtitle: Text(
                        codigo.description ??
                            'Código propio del fabricante: su significado lo da el escáner '
                                'de la marca.',
                      ),
                      trailing: Text(
                        codigo.status.etiqueta,
                        style: theme.textTheme.bodySmall,
                      ),
                      isThreeLine: true,
                    ),
                  ),
                ),
              ],
              if (lectura.vin != null)
                Padding(
                  padding: const EdgeInsets.only(top: 12),
                  child: Text('VIN leído del vehículo: ${lectura.vin}',
                      style: theme.textTheme.bodySmall),
                ),
              if (lectura.protocolo != null)
                Text('Protocolo: ${lectura.protocolo}', style: theme.textTheme.bodySmall),
              const SizedBox(height: 16),
              TextField(
                controller: _kilometraje,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(
                  labelText: 'Kilometraje (opcional)',
                  // El OBD2 no entrega el odómetro: lo lee el técnico del tablero.
                  helperText: 'El vehículo no lo reporta; se toma del tablero.',
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _notas,
                maxLines: 3,
                maxLength: 1000,
                decoration: const InputDecoration(
                  labelText: 'Observaciones (opcional)',
                  helperText: 'Sale en el informe que recibe el cliente.',
                ),
              ),
            ],
          ),
        ),
        SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              children: [
                if (_guardando) const Padding(
                  padding: EdgeInsets.only(bottom: 12),
                  child: LinearProgressIndicator(),
                ),
                FilledButton(
                  onPressed: _guardando ? null : _guardar,
                  child: const Text('Guardar en la orden'),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

class _Aviso extends StatelessWidget {
  const _Aviso({required this.icono, required this.titulo, required this.detalle});

  final IconData icono;
  final String titulo;
  final String detalle;

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(icono, size: 48),
              const SizedBox(height: 16),
              Text(titulo,
                  style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
                  textAlign: TextAlign.center),
              const SizedBox(height: 8),
              Text(detalle, textAlign: TextAlign.center),
            ],
          ),
        ),
      );
}

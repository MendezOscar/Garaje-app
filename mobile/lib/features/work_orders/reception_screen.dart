import 'dart:convert';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/work_order_repository.dart';

/// Cómo entró el vehículo: combustible, los golpes que ya traía, lo que el cliente deja
/// adentro, y su firma.
///
/// Se llena con el carro delante y el cliente al lado, así que todo está en una sola hoja que
/// se desliza: mandarlo a cuatro pantallas sería garantizar que se llene a medias. La firma va
/// al final porque es lo último que pasa: primero se anota, después se le enseña y firma.
class ReceptionScreen extends ConsumerStatefulWidget {
  const ReceptionScreen({required this.workOrderId, super.key});

  final String workOrderId;

  @override
  ConsumerState<ReceptionScreen> createState() => _ReceptionScreenState();
}

class _ReceptionScreenState extends ConsumerState<ReceptionScreen> {
  final _damages = TextEditingController();
  final _belongings = TextEditingController();
  final _notes = TextEditingController();
  final _deliveredBy = TextEditingController();
  final _mileage = TextEditingController();

  final _firma = GlobalKey();
  final _trazos = <List<Offset>>[];

  FuelLevel _combustible = FuelLevel.unknown;
  bool _cargada = false;
  bool _busy = false;

  @override
  void dispose() {
    _damages.dispose();
    _belongings.dispose();
    _notes.dispose();
    _deliveredBy.dispose();
    _mileage.dispose();
    super.dispose();
  }

  void _llenar(VehicleReception hoja) {
    _combustible = hoja.fuelLevel;
    _damages.text = hoja.damages ?? '';
    _belongings.text = hoja.belongings ?? '';
    _notes.text = hoja.notes ?? '';
    _deliveredBy.text = hoja.deliveredByName ?? '';
    _mileage.text = hoja.mileageIn?.toString() ?? '';
  }

  /// La firma dibujada, en PNG y base64. Null si no se firmó nada: así el servidor deja la
  /// que ya hubiera en vez de borrarla cada vez que se corrige la hoja.
  Future<String?> _firmaEnBase64() async {
    if (_trazos.isEmpty) return null;

    final limite = _firma.currentContext?.findRenderObject();
    if (limite is! RenderRepaintBoundary) return null;

    final imagen = await limite.toImage(pixelRatio: 2);
    final datos = await imagen.toByteData(format: ui.ImageByteFormat.png);
    if (datos == null) return null;

    return base64Encode(datos.buffer.asUint8List());
  }

  Future<void> _guardar() async {
    setState(() => _busy = true);
    try {
      await ref.read(workOrderRepositoryProvider).saveReception(
            widget.workOrderId,
            fuelLevel: _combustible,
            damages: _vacioEsNulo(_damages.text),
            belongings: _vacioEsNulo(_belongings.text),
            notes: _vacioEsNulo(_notes.text),
            deliveredByName: _vacioEsNulo(_deliveredBy.text),
            mileageIn: int.tryParse(_mileage.text.trim()),
            signature: await _firmaEnBase64(),
          );

      ref.invalidate(receptionProvider(widget.workOrderId));
      ref.invalidate(workOrderDetailProvider(widget.workOrderId));

      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e, 'No se pudo guardar la recepción.'))),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final hoja = ref.watch(receptionProvider(widget.workOrderId));

    // Lo que ya estaba escrito, una sola vez: después manda lo que se esté escribiendo.
    if (!_cargada && hoja.hasValue) {
      if (hoja.value case final guardada?) _llenar(guardada);
      _cargada = true;
    }

    return Scaffold(
      appBar: AppBar(
        title: const Text('Recepción del vehículo'),
        actions: [
          TextButton(
            onPressed: _busy ? null : _guardar,
            child: const Text('Guardar'),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 12, 16, 32),
        children: [
          Text(
            'Lo que se anote aquí es lo que vale cuando después se discuta si un golpe venía '
            'o no. Se llena delante del cliente.',
            style: theme.textTheme.bodySmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 16),

          DropdownButtonFormField<FuelLevel>(
            initialValue: _combustible,
            decoration: const InputDecoration(
              labelText: 'Combustible',
              border: OutlineInputBorder(),
            ),
            items: [
              for (final nivel in FuelLevel.values)
                DropdownMenuItem(value: nivel, child: Text(nivel.label)),
            ],
            onChanged: _busy
                ? null
                : (value) => setState(() => _combustible = value ?? _combustible),
          ),
          const SizedBox(height: 12),

          TextField(
            controller: _mileage,
            keyboardType: TextInputType.number,
            decoration: const InputDecoration(
              labelText: 'Kilometraje de entrada',
              border: OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 12),

          TextField(
            controller: _damages,
            maxLines: 3,
            textCapitalization: TextCapitalization.sentences,
            decoration: const InputDecoration(
              labelText: 'Golpes y rayones que ya traía',
              hintText: 'Rayón en la puerta del copiloto, golpe en el bómper trasero…',
              border: OutlineInputBorder(),
              alignLabelWithHint: true,
            ),
          ),
          const SizedBox(height: 6),
          Text(
            'Las fotos van en la orden, en «Fotos». Una del golpe vale más que describirlo.',
            style: theme.textTheme.bodySmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 12),

          TextField(
            controller: _belongings,
            maxLines: 2,
            textCapitalization: TextCapitalization.sentences,
            decoration: const InputDecoration(
              labelText: 'Qué deja adentro',
              hintText: 'Llanta de repuesto, gato, documentos…',
              border: OutlineInputBorder(),
              alignLabelWithHint: true,
            ),
          ),
          const SizedBox(height: 12),

          TextField(
            controller: _notes,
            maxLines: 2,
            textCapitalization: TextCapitalization.sentences,
            decoration: const InputDecoration(
              labelText: 'Otras notas',
              border: OutlineInputBorder(),
              alignLabelWithHint: true,
            ),
          ),
          const SizedBox(height: 20),

          Text('QUIÉN LO ENTREGA', style: theme.textTheme.labelSmall),
          const SizedBox(height: 6),
          TextField(
            controller: _deliveredBy,
            textCapitalization: TextCapitalization.words,
            decoration: const InputDecoration(
              labelText: 'Nombre de quien entrega',
              helperText: 'No siempre es el dueño: lo trae un empleado, el hijo, el chofer.',
              helperMaxLines: 2,
              border: OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 16),

          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text('FIRMA', style: theme.textTheme.labelSmall),
              if (_trazos.isNotEmpty)
                TextButton(
                  onPressed: () => setState(_trazos.clear),
                  child: const Text('Borrar'),
                ),
            ],
          ),
          const SizedBox(height: 6),

          // El lienzo: se firma con el dedo, que es lo que hay en el patio del taller.
          RepaintBoundary(
            key: _firma,
            child: Container(
              height: 180,
              decoration: BoxDecoration(
                color: Colors.white,
                border: Border.all(color: theme.dividerColor),
                borderRadius: BorderRadius.circular(10),
              ),
              child: GestureDetector(
                onPanStart: (d) => setState(() => _trazos.add([d.localPosition])),
                onPanUpdate: (d) => setState(() {
                  if (_trazos.isNotEmpty) _trazos.last.add(d.localPosition);
                }),
                child: CustomPaint(
                  painter: _Firma(_trazos),
                  size: Size.infinite,
                ),
              ),
            ),
          ),
          const SizedBox(height: 6),

          if (hoja.value?.signatureUrl != null && _trazos.isEmpty)
            Text(
              'Ya hay una firma guardada. Firme de nuevo solo si hay que reemplazarla.',
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            )
          else
            Text(
              'El cliente firma aquí con el dedo, después de leer lo anotado.',
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),

          const SizedBox(height: 24),
          FilledButton(
            onPressed: _busy ? null : _guardar,
            child: Text(_busy ? 'Guardando…' : 'Guardar la recepción'),
          ),
        ],
      ),
    );
  }
}

String? _vacioEsNulo(String value) => value.trim().isEmpty ? null : value.trim();

/// Dibuja los trazos de la firma. Negro sobre blanco y sin suavizados: lo que se va a
/// guardar es una imagen chica que tiene que leerse impresa.
class _Firma extends CustomPainter {
  const _Firma(this.trazos);

  final List<List<Offset>> trazos;

  @override
  void paint(Canvas canvas, Size size) {
    final pincel = Paint()
      ..color = Colors.black
      ..strokeWidth = 2.5
      ..strokeCap = StrokeCap.round
      ..style = PaintingStyle.stroke;

    for (final trazo in trazos) {
      if (trazo.length < 2) {
        if (trazo.isNotEmpty) canvas.drawPoints(ui.PointMode.points, trazo, pincel);
        continue;
      }

      final camino = Path()..moveTo(trazo.first.dx, trazo.first.dy);
      for (final punto in trazo.skip(1)) {
        camino.lineTo(punto.dx, punto.dy);
      }
      canvas.drawPath(camino, pincel);
    }
  }

  @override
  bool shouldRepaint(_Firma oldDelegate) => true;
}

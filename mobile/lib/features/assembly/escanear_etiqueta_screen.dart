import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import '../../core/assembly/serie_de_la_etiqueta.dart';

/// Apuntar a la etiqueta de la caja y sacar de ahí la serie.
///
/// La caja trae el VIN en código de barras, y leer un código hecho para máquinas no se
/// equivoca como se equivoca alguien tecleando diecisiete caracteres en una bodega. Se lee
/// en el propio teléfono: sin servidor, sin señal y sin costo por moto.
///
/// Lo leído se devuelve a la pantalla de registro para que caiga en el campo, **no se
/// guarda**: la confirma quien tiene la caja delante.
class EscanearEtiquetaScreen extends StatefulWidget {
  const EscanearEtiquetaScreen({super.key});

  @override
  State<EscanearEtiquetaScreen> createState() => _EscanearEtiquetaScreenState();
}

class _EscanearEtiquetaScreenState extends State<EscanearEtiquetaScreen> {
  // Solo los formatos que llevan estas etiquetas. Acotarlo hace la lectura más rápida y
  // evita que un QR de otra caja se cuele.
  final _controlador = MobileScannerController(
    formats: const [
      BarcodeFormat.code39,
      BarcodeFormat.code128,
      BarcodeFormat.dataMatrix,
    ],
    detectionSpeed: DetectionSpeed.normal,
  );

  /// Lo que se ha visto en esta sesión, para no volver a avisar del mismo.
  final _vistos = <String>{};

  /// Dos series distintas dentro del recuadro: hay más de una caja a la vista.
  bool _hayDosCajas = false;

  bool _devuelto = false;

  /// Lo que pasó al leer una foto de la galería, para decirlo abajo.
  String? _deLaFoto;

  /// El tamaño del recuadro de la guía. Es también el área que de verdad se lee.
  static const _guia = Size(280, 140);

  @override
  void dispose() {
    _controlador.dispose();
    super.dispose();
  }

  void _alLeer(BarcodeCapture captura) {
    if (_devuelto) return;

    final codigos = captura.barcodes
        .map((b) => b.rawValue)
        .whereType<String>()
        .toList();

    for (final c in codigos) {
      _vistos.add(normalizarSerie(c));
    }

    // En una bodega las cajas están pegadas. Si en el recuadro entran dos series distintas,
    // no se escoge una: se registraría una moto que no es la que se armó, y eso no da error
    // —queda guardado y nadie se entera hasta que el contratista reclame—.
    final series = codigos.map(normalizarSerie).where(pareceSerie).toSet();

    if (series.length > 1) {
      if (mounted) setState(() => _hayDosCajas = true);
      return;
    }

    final serie = series.isEmpty ? null : series.first;

    if (serie == null) {
      // Se vio algo, pero no el VIN: en una caja hay cuatro códigos y tres no sirven. Se
      // sigue leyendo sin decir nada, que es lo que pasa mientras uno mueve el teléfono.
      if (mounted) setState(() => _hayDosCajas = false);
      return;
    }

    _devuelto = true;
    Navigator.of(context).pop(serie);
  }

  /// Leer el código de una foto que ya está en el teléfono.
  ///
  /// Es para el dueño: a él las cajas le llegan por WhatsApp, no las tiene delante. Antes
  /// tenía que leer la serie de la imagen y teclear diecisiete caracteres; ahora la abre y
  /// sale lo mismo que saldría apuntándole con la cámara.
  Future<void> _deUnaFoto() async {
    final elegida = await ImagePicker().pickImage(source: ImageSource.gallery);
    if (elegida == null || !mounted) return;

    setState(() => _deLaFoto = 'Leyendo la foto…');

    final leido = await _controlador.analyzeImage(elegida.path);
    if (!mounted) return;

    final codigos = leido?.barcodes
            .map((b) => b.rawValue)
            .whereType<String>()
            .toList() ??
        const <String>[];

    final serie = serieDeLaEtiqueta(codigos);

    if (serie == null) {
      setState(() => _deLaFoto = codigos.isEmpty
          ? 'En esa foto no se ve ningún código. Pruebe con una más cerca de la etiqueta.'
          : 'En esa foto se leyeron ${codigos.length} códigos y ninguno es el VIN.');
      return;
    }

    _devuelto = true;
    Navigator.of(context).pop(serie);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(title: const Text('Escanear la etiqueta')),
      body: Stack(
        fit: StackFit.expand,
        children: [
          LayoutBuilder(
            builder: (context, medidas) => MobileScanner(
              controller: _controlador,
              onDetect: _alLeer,

              // El recuadro de la guía es de verdad: fuera de él no se lee. Sin esto, la
              // cámara agarraría la etiqueta de la caja de al lado sin avisar.
              scanWindow: Rect.fromCenter(
                center: Offset(medidas.maxWidth / 2, medidas.maxHeight / 2),
                width: _guia.width,
                height: _guia.height,
              ),
              errorBuilder: (context, error) => Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Icon(Icons.no_photography_outlined, size: 40),
                      const SizedBox(height: 12),
                      Text(
                        error.errorCode ==
                                MobileScannerErrorCode.permissionDenied
                            ? 'La app no tiene permiso para usar la cámara. Se puede dar en '
                                  'los ajustes del teléfono, o teclear la serie a mano.'
                            : 'No se pudo abrir la cámara. Teclee la serie a mano.',
                        textAlign: TextAlign.center,
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),

          // La guía: una caja lleva cuatro códigos, y hay que decir a cuál apuntar.
          IgnorePointer(
            child: Center(
              child: Container(
                width: _guia.width,
                height: _guia.height,
                decoration: BoxDecoration(
                  border: Border.all(
                    color: _hayDosCajas
                        ? Colors.amber
                        : Colors.white.withValues(alpha: 0.9),
                    width: _hayDosCajas ? 3 : 2,
                  ),
                  borderRadius: BorderRadius.circular(12),
                ),
              ),
            ),
          ),

          Align(
            alignment: Alignment.bottomCenter,
            child: SafeArea(
              child: Container(
                margin: const EdgeInsets.all(16),
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(
                  color: theme.colorScheme.surface.withValues(alpha: 0.92),
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      'Apunte al código de barras del VIN',
                      style: theme.textTheme.titleSmall,
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      _deLaFoto ??
                          (_hayDosCajas
                          ? 'Se ven dos cajas distintas en el recuadro. Acérquese hasta que '
                                'solo entre la etiqueta de la que acaba de armar.'
                          : _vistos.isEmpty
                          ? 'Es el de diecisiete caracteres. Los otros de la caja '
                                '—envío, SKU, número de motor— no sirven.'
                          : 'Ya se leyeron ${_vistos.length} códigos de la caja, pero '
                                'ninguno es el VIN. Acérquese al que dice VIN.'),
                      style: theme.textTheme.bodySmall,
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: 10),
                    Wrap(
                      alignment: WrapAlignment.center,
                      children: [
                        TextButton.icon(
                          onPressed: _deUnaFoto,
                          icon: const Icon(Icons.photo_library_outlined, size: 18),
                          label: const Text('Leer de una foto'),
                        ),
                        TextButton(
                          onPressed: () => Navigator.of(context).pop(),
                          child: const Text('Mejor la tecleo'),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

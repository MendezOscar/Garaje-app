import 'package:flutter/material.dart';
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
    formats: const [BarcodeFormat.code39, BarcodeFormat.code128, BarcodeFormat.dataMatrix],
    detectionSpeed: DetectionSpeed.normal,
  );

  /// Lo que se ha visto en esta sesión, para no volver a avisar del mismo.
  final _vistos = <String>{};

  bool _devuelto = false;

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

    final serie = serieDeLaEtiqueta(codigos);
    if (serie == null) {
      // Se vio algo, pero no el VIN: en una caja hay cuatro códigos y tres no sirven. Se
      // sigue leyendo sin decir nada, que es lo que pasa mientras uno mueve el teléfono.
      if (mounted) setState(() {});
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
          MobileScanner(
            controller: _controlador,
            onDetect: _alLeer,
            errorBuilder: (context, error) => Center(
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Icon(Icons.no_photography_outlined, size: 40),
                    const SizedBox(height: 12),
                    Text(
                      error.errorCode == MobileScannerErrorCode.permissionDenied
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

          // La guía: una caja lleva cuatro códigos, y hay que decir a cuál apuntar.
          IgnorePointer(
            child: Center(
              child: Container(
                width: 280,
                height: 140,
                decoration: BoxDecoration(
                  border: Border.all(color: Colors.white.withValues(alpha: 0.9), width: 2),
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
                      _vistos.isEmpty
                          ? 'Es el de diecisiete caracteres. Los otros de la caja —envío, '
                              'SKU, número de motor— no sirven.'
                          : 'Ya se leyeron ${_vistos.length} códigos de la caja, pero '
                              'ninguno es el VIN. Acérquese al que dice VIN.',
                      style: theme.textTheme.bodySmall,
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: 10),
                    TextButton(
                      onPressed: () => Navigator.of(context).pop(),
                      child: const Text('Mejor la tecleo'),
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

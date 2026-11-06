import 'dart:async';
import 'dart:io';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:receive_sharing_intent/receive_sharing_intent.dart';

/// El informe que alguien acaba de compartir a GarajApp y todavía no tiene orden.
///
/// Nace de «Compartir → GarajApp» en el escáner —la LAUNCH es una tableta Android y arma su
/// PDF ahí mismo— o en cualquier aplicación que mande un PDF. Se queda aquí hasta que el
/// técnico diga a qué orden va: el escáner no lo sabe, solo sabe que leyó un carro.
class InformeCompartido {
  const InformeCompartido({required this.archivo, this.nombre});

  final File archivo;

  /// Cómo se llamaba el archivo al llegar. Es lo que se guarda y lo que después se lee en la
  /// lista de adjuntos, porque un PDF no tiene miniatura por la que reconocerlo.
  final String? nombre;
}

/// Lo compartido que está esperando orden. Nulo mientras no haya nada pendiente.
final informeCompartidoProvider =
    NotifierProvider<InformeCompartidoNotifier, InformeCompartido?>(
  InformeCompartidoNotifier.new,
);

class InformeCompartidoNotifier extends Notifier<InformeCompartido?> {
  @override
  InformeCompartido? build() => null;

  void recibir(InformeCompartido informe) => state = informe;

  /// Se limpia al adjuntarlo o al cancelar: si se quedara, volvería a abrirse la pantalla
  /// la próxima vez que la app pase a primer plano.
  void limpiar() => state = null;
}

/// Solo PDF. Las imágenes compartidas no entran por aquí: para las fotos está la cámara
/// dentro de la orden, que además las comprime y las encola cuando no hay señal.
InformeCompartido? _pdfDe(List<SharedMediaFile> compartidos) {
  for (final compartido in compartidos) {
    final ruta = compartido.path;
    final esPdf = (compartido.mimeType ?? '').toLowerCase() == 'application/pdf' ||
        ruta.toLowerCase().endsWith('.pdf');

    if (!esPdf) continue;

    final archivo = File(ruta);
    if (!archivo.existsSync()) continue;

    return InformeCompartido(archivo: archivo, nombre: Uri.parse(ruta).pathSegments.last);
  }

  return null;
}

/// Engancha la app a la bandeja de compartir del sistema.
///
/// Dos vías, y hacen falta las dos: el flujo atiende lo que llega con la app ya abierta, y
/// `getInitialMedia` lo que la abrió desde cero —que es el caso normal en el taller—.
StreamSubscription<List<SharedMediaFile>> escucharCompartidos(
  void Function(InformeCompartido) alRecibir,
) {
  final suscripcion = ReceiveSharingIntent.instance.getMediaStream().listen(
    (compartidos) {
      final informe = _pdfDe(compartidos);
      if (informe != null) alRecibir(informe);
    },
    // Un fallo leyendo lo compartido no puede tumbar la app: el técnico está en medio de un
    // trabajo. Se ignora y el informe se vuelve a compartir.
    onError: (_) {},
  );

  unawaited(_atenderLoQueAbrioLaApp(alRecibir));

  return suscripcion;
}

Future<void> _atenderLoQueAbrioLaApp(void Function(InformeCompartido) alRecibir) async {
  try {
    final informe = _pdfDe(await ReceiveSharingIntent.instance.getInitialMedia());
    if (informe != null) alRecibir(informe);

    // Hay que decirle al plugin que ya se atendió, o lo vuelve a entregar al reabrir.
    ReceiveSharingIntent.instance.reset();
  } catch (_) {
    // Lo mismo que arriba: se pierde el informe, no la app. Se comparte otra vez.
  }
}

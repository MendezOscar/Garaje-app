/// Sacar el VIN de los códigos de barras de la etiqueta de la caja.
///
/// Una caja de moto trae varios códigos pegados —el del envío, el del SKU, el número de
/// motor, el VIN— y a veces el mismo VIN repetido dos veces. Hay que escoger uno solo, y sin
/// preguntarle al técnico, que para eso está leyendo un código.
///
/// La regla que los distingue sale del propio estándar y se comprobó contra las etiquetas de
/// verdad del taller: **diecisiete caracteres, y ni I ni O ni Q**. Esas tres letras el
/// estándar no las usa justamente para que no se confundan con 1 y 0.
///
/// En una etiqueta real eso deja fuera el número de motor —`LC163FML279200Q6`, dieciséis
/// caracteres y con Q—, el del envío (`34005571`) y el del SKU (`L062403880`).
library;

/// Los caracteres que el estándar permite: el alfabeto sin I, O ni Q, y los dígitos.
final _permitidos = RegExp(r'^[A-HJ-NPR-Z0-9]{17}$');

/// Todo lo que no sea letra o dígito. Los lectores devuelven el contenido tal cual, y el
/// Code 39 de estas etiquetas viene envuelto en asteriscos: `*LLCLPP200VE100410*`.
final _separadores = RegExp(r'[^A-Za-z0-9]');

/// Normaliza como el resto del sistema: mayúsculas y sin separadores.
String normalizarSerie(String crudo) =>
    crudo.replaceAll(_separadores, '').toUpperCase();

/// Si lo leído tiene forma de VIN.
bool pareceSerie(String crudo) => _permitidos.hasMatch(normalizarSerie(crudo));

/// El VIN de entre todos los códigos que se vean en la etiqueta, o null si ninguno lo es.
///
/// Devuelve el primero con forma de VIN. Que el mismo venga repetido no importa: es el
/// mismo número. Que haya dos distintos con forma de VIN en una misma caja no pasa —el VIN
/// identifica el chasis y la caja lleva una moto—, y si pasara, el técnico lo corrige: lo
/// leído se escribe en el campo, no se guarda solo.
String? serieDeLaEtiqueta(Iterable<String> codigos) {
  for (final codigo in codigos) {
    final limpio = normalizarSerie(codigo);
    if (_permitidos.hasMatch(limpio)) return limpio;
  }

  return null;
}

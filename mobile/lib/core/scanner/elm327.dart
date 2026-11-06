import 'dart:async';
import 'dart:convert';

import 'package:flutter_blue_plus/flutter_blue_plus.dart';

import '../models/vehicle_scan.dart';
import 'dtc_catalogo.dart';

/// Un código recién leído del vehículo, antes de subirlo.
class CodigoLeido {
  const CodigoLeido({required this.code, required this.status, this.description});

  final String code;
  final DtcStatus status;
  final String? description;

  Map<String, dynamic> toJson() =>
      {'code': code, 'status': status.value, 'description': description};
}

/// Lo que salió de una lectura completa del vehículo.
class LecturaObd {
  const LecturaObd({
    required this.codigos,
    this.vin,
    this.protocolo,
    this.adaptador,
  });

  final List<CodigoLeido> codigos;
  final String? vin;
  final String? protocolo;
  final String? adaptador;
}

/// Lo que puede salir mal, dicho como se le enseña al técnico en la pantalla.
class ErrorObd implements Exception {
  const ErrorObd(this.mensaje);

  final String mensaje;

  @override
  String toString() => mensaje;
}

/// La conversación con un adaptador OBD2 por Bluetooth.
///
/// El adaptador es un ELM327: se le mandan comandos en texto terminados en retorno de carro
/// y contesta texto terminado en `>`. Encima de eso van los modos del OBD2: 03 las fallas
/// confirmadas, 07 las pendientes, 0A las permanentes y 09 02 el número de chasis.
///
/// Solo Bluetooth LE. Los adaptadores azules baratos son Bluetooth clásico y el iPhone no
/// los ve —Apple solo deja hablar por serie con equipos certificados—, así que soportarlos
/// significaría una función que existe en Android y no en iPhone. Se eligió un adaptador LE
/// para que el taller vea lo mismo en cualquier teléfono.
class SesionObd {
  SesionObd._(this._dispositivo, this._escritura);

  final BluetoothDevice _dispositivo;
  final BluetoothCharacteristic _escritura;
  StreamSubscription<List<int>>? _notificaciones;

  final _buffer = StringBuffer();
  Completer<String>? _esperando;

  /// Los UUID que usan casi todos los adaptadores LE del mercado. Se prueban primero y, si
  /// el aparato trae otros, se busca cualquier pareja de características que sirva: hay
  /// demasiados fabricantes como para llevar una lista cerrada.
  static final _serviciosConocidos = [
    Guid('0000fff0-0000-1000-8000-00805f9b34fb'),
    Guid('0000ffe0-0000-1000-8000-00805f9b34fb'),
    Guid('0000ffff-0000-1000-8000-00805f9b34fb'),
  ];

  /// Busca adaptadores encendidos alrededor. Devuelve lo que va apareciendo.
  static Stream<List<ScanResult>> buscar({
    Duration duracion = const Duration(seconds: 12),
  }) {
    // Sin filtrar por servicio: muchos adaptadores no anuncian el suyo en la publicidad y
    // filtrando no aparecerían nunca. Se filtra después, al conectar.
    unawaited(FlutterBluePlus.startScan(timeout: duracion));
    return FlutterBluePlus.onScanResults;
  }

  static Future<void> pararBusqueda() async {
    if (FlutterBluePlus.isScanningNow) await FlutterBluePlus.stopScan();
  }

  /// Conecta, busca por dónde se habla con el aparato y lo deja listo para recibir comandos.
  static Future<SesionObd> conectar(BluetoothDevice dispositivo) async {
    await FlutterBluePlus.stopScan();
    await dispositivo.connect(timeout: const Duration(seconds: 20));

    final servicios = await dispositivo.discoverServices();

    final candidatos = [
      ...servicios.where((s) => _serviciosConocidos.contains(s.uuid)),
      ...servicios.where((s) => !_serviciosConocidos.contains(s.uuid)),
    ];

    for (final servicio in candidatos) {
      BluetoothCharacteristic? escritura;
      BluetoothCharacteristic? lectura;

      for (final c in servicio.characteristics) {
        if (c.properties.notify || c.properties.indicate) lectura ??= c;
        if (c.properties.write || c.properties.writeWithoutResponse) escritura ??= c;
      }

      if (escritura == null || lectura == null) continue;

      final sesion = SesionObd._(dispositivo, escritura);

      final suscripcion = lectura.onValueReceived.listen(sesion._recibir);
      sesion._notificaciones = suscripcion;
      dispositivo.cancelWhenDisconnected(suscripcion);

      await lectura.setNotifyValue(true);

      return sesion;
    }

    await dispositivo.disconnect();
    throw const ErrorObd(
      'Ese aparato no parece un adaptador OBD2: no tiene por dónde conversar. '
      'Asegúrese de elegir el adaptador y no otro equipo Bluetooth.',
    );
  }

  void _recibir(List<int> datos) {
    _buffer.write(String.fromCharCodes(datos));

    final texto = _buffer.toString();
    if (!texto.contains('>')) return;

    final respuesta = texto.substring(0, texto.indexOf('>'));
    _buffer.clear();

    final esperando = _esperando;
    _esperando = null;
    if (esperando != null && !esperando.isCompleted) esperando.complete(respuesta);
  }

  /// Manda un comando y espera su respuesta. El adaptador contesta con `>` cuando terminó.
  Future<String> _comando(String comando, {Duration espera = const Duration(seconds: 6)}) async {
    _buffer.clear();
    final completer = Completer<String>();
    _esperando = completer;

    final bytes = utf8.encode('$comando\r');

    await _escritura.write(
      bytes,
      withoutResponse: _escritura.properties.writeWithoutResponse,
    );

    try {
      return await completer.future.timeout(espera);
    } on TimeoutException {
      _esperando = null;
      throw const ErrorObd(
        'El adaptador dejó de contestar. Revise que esté bien metido en el conector '
        'y que la llave esté en contacto.',
      );
    }
  }

  /// Deja el adaptador hablando como nos sirve y negocia el protocolo con el vehículo.
  Future<String?> _preparar() async {
    // Reinicio: el adaptador puede venir configurado de otra aplicación.
    await _comando('ATZ', espera: const Duration(seconds: 10));

    // Sin eco, sin saltos de línea, sin espacios y sin cabeceras: lo que vuelve es solo la
    // respuesta del vehículo, que es mucho más fácil de leer sin equivocarse.
    await _comando('ATE0');
    await _comando('ATL0');
    await _comando('ATS0');
    await _comando('ATH0');

    // Protocolo automático: el adaptador prueba hasta que el vehículo contesta.
    await _comando('ATSP0');

    // Esta es la que de verdad despierta la conversación con el carro; hasta aquí todo fue
    // con el adaptador. Si no contesta, no hay vehículo escuchando.
    final prueba = await _comando('0100', espera: const Duration(seconds: 12));

    if (_sinRespuesta(prueba)) {
      throw const ErrorObd(
        'El vehículo no contesta. Ponga la llave en contacto —sin arrancar basta— y '
        'vuelva a intentar.',
      );
    }

    final protocolo = await _comando('ATDP');
    return _limpiar(protocolo).isEmpty ? null : _limpiar(protocolo);
  }

  /// La lectura completa: las tres memorias de fallas y el número de chasis.
  Future<LecturaObd> leer() async {
    final protocolo = await _preparar();

    final codigos = <String, CodigoLeido>{};

    for (final (modo, marca, estado) in const [
      ('03', '43', DtcStatus.stored),
      ('07', '47', DtcStatus.pending),
      ('0A', '4A', DtcStatus.permanent),
    ]) {
      // Que una memoria no conteste no echa a perder la lectura: los vehículos viejos no
      // tienen la permanente, y es normal que 0A no exista.
      try {
        final respuesta = await _comando(modo, espera: const Duration(seconds: 10));

        for (final code in _codigosDe(respuesta, marca)) {
          // El mismo código puede salir confirmado y pendiente. Vale el más grave, que es
          // el confirmado: es el que ya encendió la luz.
          codigos.putIfAbsent(
            code,
            () => CodigoLeido(code: code, status: estado, description: descripcionDtc(code)),
          );
        }
      } on ErrorObd {
        continue;
      }
    }

    String? vin;
    try {
      vin = _vinDe(await _comando('0902', espera: const Duration(seconds: 10)));
    } on ErrorObd {
      // El VIN es un extra: hay vehículos que no lo publican. La lectura vale igual.
      vin = null;
    }

    final lista = codigos.values.toList()
      ..sort((a, b) => a.code.compareTo(b.code));

    return LecturaObd(
      codigos: lista,
      vin: vin,
      protocolo: protocolo,
      adaptador: _dispositivo.platformName.isEmpty ? null : _dispositivo.platformName,
    );
  }

  Future<void> cerrar() async {
    await _notificaciones?.cancel();
    if (_dispositivo.isConnected) await _dispositivo.disconnect();
  }

  // ---------------------------------------------------------------------------------------
  // Lectura de las respuestas
  // ---------------------------------------------------------------------------------------

  static bool _sinRespuesta(String respuesta) {
    final texto = respuesta.toUpperCase();
    return texto.contains('NO DATA') ||
        texto.contains('UNABLE') ||
        texto.contains('ERROR') ||
        (texto.contains('SEARCHING') && _limpiar(respuesta).isEmpty);
  }

  /// Deja la respuesta en hexadecimal corrido: sin saltos, sin espacios y sin los números de
  /// trama que el adaptador pone delante cuando la respuesta no cabe en un mensaje.
  static String _limpiar(String respuesta) {
    final lineas = respuesta
        .split(RegExp(r'[\r\n]+'))
        .map((l) => l.trim())
        .where((l) => l.isNotEmpty)
        .toList();

    final buffer = StringBuffer();

    for (var linea in lineas) {
      final mayus = linea.toUpperCase();
      if (mayus.startsWith('SEARCHING') || mayus.startsWith('BUS INIT')) continue;

      // «0:», «1:» … numeran las tramas de una respuesta larga.
      linea = linea.replaceFirst(RegExp(r'^[0-9A-F]:'), '');
      linea = linea.replaceAll(RegExp(r'[^0-9A-Fa-f]'), '');

      buffer.write(linea.toUpperCase());
    }

    return buffer.toString();
  }

  /// Saca los códigos de la respuesta de un modo de fallas.
  static List<String> _codigosDe(String respuesta, String marca) {
    if (_sinRespuesta(respuesta)) return [];

    var hex = _limpiar(respuesta);
    final inicio = hex.indexOf(marca);
    if (inicio < 0) return [];

    hex = hex.substring(inicio + 2);

    // En CAN viene primero cuántas fallas hay; en los protocolos viejos no. Se reconoce
    // porque ese byte cuadra con los pares que siguen, y si no cuadra se lee todo como
    // pares: colar un byte de más convertiría P0301 en un código inventado.
    if (hex.length >= 2) {
      final cuantos = int.tryParse(hex.substring(0, 2), radix: 16);
      if (cuantos != null && cuantos > 0 && (hex.length - 2) >= cuantos * 4) {
        hex = hex.substring(2);
      }
    }

    final codigos = <String>[];

    for (var i = 0; i + 4 <= hex.length; i += 4) {
      final a = int.tryParse(hex.substring(i, i + 2), radix: 16);
      final b = int.tryParse(hex.substring(i + 2, i + 4), radix: 16);
      if (a == null || b == null) continue;

      final code = _codigoDe(a, b);
      if (code != null && !codigos.contains(code)) codigos.add(code);
    }

    return codigos;
  }

  /// Dos bytes se convierten en un código según la norma: los dos primeros bits dicen el
  /// sistema —P, C, B o U— y lo que queda son los cuatro dígitos.
  static String? _codigoDe(int a, int b) {
    // Relleno: el vehículo completa el mensaje con ceros cuando sobran pares.
    if (a == 0 && b == 0) return null;

    const letras = ['P', 'C', 'B', 'U'];
    final letra = letras[(a >> 6) & 0x03];
    final primero = (a >> 4) & 0x03;
    final segundo = (a & 0x0F).toRadixString(16).toUpperCase();
    final tercero = ((b >> 4) & 0x0F).toRadixString(16).toUpperCase();
    final cuarto = (b & 0x0F).toRadixString(16).toUpperCase();

    return '$letra$primero$segundo$tercero$cuarto';
  }

  /// El número de chasis, que el vehículo manda en varias tramas como texto.
  static String? _vinDe(String respuesta) {
    if (_sinRespuesta(respuesta)) return null;

    final hex = _limpiar(respuesta);
    final inicio = hex.indexOf('4902');
    if (inicio < 0) return null;

    // Tras «4902» va un byte de secuencia —casi siempre 01— y después el texto.
    var cuerpo = hex.substring(inicio + 4);
    if (cuerpo.startsWith('01')) cuerpo = cuerpo.substring(2);

    final bytes = <int>[];
    for (var i = 0; i + 2 <= cuerpo.length; i += 2) {
      final valor = int.tryParse(cuerpo.substring(i, i + 2), radix: 16);
      if (valor == null) continue;
      // Solo lo imprimible: las tramas traen ceros de relleno delante del texto.
      if (valor >= 0x20 && valor <= 0x7E) bytes.add(valor);
    }

    final vin = String.fromCharCodes(bytes).replaceAll(RegExp(r'[^A-HJ-NPR-Z0-9]'), '');

    // Un VIN tiene diecisiete caracteres. Si no salen, es que la respuesta vino partida y
    // no se armó bien: mejor no decir nada que decir un chasis equivocado.
    return vin.length == 17 ? vin : null;
  }
}

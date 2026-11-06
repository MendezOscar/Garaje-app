/// Qué significa cada código de falla, en español.
///
/// Vive en la app y no en el servidor a propósito: se escanea con el carro en la rampa, que
/// es justo donde peor anda la señal. El técnico tiene que leer «fallo de combustión en el
/// cilindro 3» ahí mismo, no cuando vuelva a la oficina. Lo que se resuelve aquí se manda
/// después al servidor junto con el código, y queda copiado en la lectura.
///
/// No pretende ser la base de datos de un escáner de marca. Son los códigos de la norma
/// —los que significan lo mismo en cualquier vehículo— y, cuando uno no está en la tabla, se
/// dice la zona que el propio código señala. Los códigos propios del fabricante no se
/// adivinan: esa información la vende cada marca con su equipo, y ponerle un texto inventado
/// a un código del que no sabemos nada es peor que dejarlo sin descripción.
library;

/// La descripción del código, o null si no se puede decir nada honesto de él.
String? descripcionDtc(String codigo) {
  final code = codigo.trim().toUpperCase();
  if (code.length != 5) return null;

  final exacta = _tabla[code];
  if (exacta != null) return exacta;

  final porPatron = _porPatron(code);
  if (porPatron != null) return porPatron;

  // Código propio del fabricante: no hay nada que decir sin la información de la marca.
  if (!esGenerico(code)) return null;

  return _zona(code);
}

/// Si el código es de la norma —el segundo carácter es 0— o propio del fabricante.
bool esGenerico(String codigo) =>
    codigo.length == 5 && codigo[1] == '0';

/// Los códigos que solo cambian en el número: cilindros, inyectores, bobinas, sensores.
String? _porPatron(String code) {
  final numero = int.tryParse(code.substring(1));
  if (numero == null) return null;

  if (code[0] == 'P') {
    if (numero >= 301 && numero <= 312) {
      return 'Fallo de combustión detectado en el cilindro ${numero - 300}.';
    }
    if (numero >= 201 && numero <= 212) {
      return 'Circuito del inyector del cilindro ${numero - 200}.';
    }
    if (numero >= 351 && numero <= 362) {
      return 'Circuito de la bobina de encendido del cilindro ${numero - 350}.';
    }
    if (numero >= 261 && numero <= 292) {
      final cilindro = ((numero - 261) ~/ 3) + 1;
      return 'Caudal del inyector del cilindro $cilindro fuera de rango.';
    }
  }

  return null;
}

/// La zona del vehículo que señala el código, cuando no está en la tabla. Sale de cómo está
/// repartida la numeración en la norma SAE J2012.
String _zona(String code) {
  final numero = int.tryParse(code.substring(1)) ?? 0;

  switch (code[0]) {
    case 'P':
      if (numero < 100) return 'Sistema de combustible y aire: medición y control.';
      if (numero < 200) return 'Medición de aire y combustible: sensores de admisión y mezcla.';
      if (numero < 300) return 'Alimentación de combustible: inyectores y bomba.';
      if (numero < 400) return 'Encendido o fallos de combustión.';
      if (numero < 500) return 'Control de emisiones: recirculación de gases, catalizador o evaporativo.';
      if (numero < 600) return 'Velocidad, marcha mínima y alimentación eléctrica del motor.';
      if (numero < 700) return 'Computadora del motor y sus circuitos de salida.';
      if (numero < 1000) return 'Transmisión.';
      return 'Sistema de motor y transmisión.';
    case 'C':
      return 'Chasis: frenos, suspensión o dirección.';
    case 'B':
      return 'Carrocería: bolsas de aire, aire acondicionado, luces o accesorios.';
    case 'U':
      return 'Comunicación entre las computadoras del vehículo.';
    default:
      return 'Sistema no identificado.';
  }
}

/// Los códigos de la norma, con lo que significan dicho en una línea.
const Map<String, String> _tabla = {
  // Distribución y sincronización
  'P0011': 'Posición del árbol de levas adelantada respecto a lo esperado (banco 1).',
  'P0012': 'Posición del árbol de levas atrasada respecto a lo esperado (banco 1).',
  'P0014': 'Posición del árbol de levas de escape fuera de lo esperado (banco 1).',
  'P0016': 'El cigüeñal y el árbol de levas no van sincronizados (banco 1).',
  'P0017': 'El cigüeñal y el árbol de levas de escape no van sincronizados (banco 1).',
  'P0018': 'El cigüeñal y el árbol de levas no van sincronizados (banco 2).',
  'P0021': 'Posición del árbol de levas adelantada respecto a lo esperado (banco 2).',
  'P0022': 'Posición del árbol de levas atrasada respecto a lo esperado (banco 2).',

  // Calentadores de los sensores de oxígeno
  'P0030': 'Circuito del calentador del sensor de oxígeno 1, banco 1.',
  'P0031': 'Baja señal en el calentador del sensor de oxígeno 1, banco 1.',
  'P0032': 'Alta señal en el calentador del sensor de oxígeno 1, banco 1.',
  'P0036': 'Circuito del calentador del sensor de oxígeno 2, banco 1.',
  'P0037': 'Baja señal en el calentador del sensor de oxígeno 2, banco 1.',
  'P0038': 'Alta señal en el calentador del sensor de oxígeno 2, banco 1.',
  'P0050': 'Circuito del calentador del sensor de oxígeno 1, banco 2.',
  'P0053': 'Resistencia del calentador del sensor de oxígeno fuera de rango (banco 1).',
  'P0056': 'Circuito del calentador del sensor de oxígeno 2, banco 2.',

  // Admisión
  'P0068': 'El flujo de aire medido no concuerda con la posición del acelerador.',
  'P0070': 'Circuito del sensor de temperatura del aire exterior.',
  'P0087': 'Presión de combustible más baja de lo necesario.',
  'P0088': 'Presión de combustible más alta de lo necesario.',
  'P0089': 'El regulador de presión de combustible no está trabajando bien.',
  'P0090': 'Circuito de la válvula reguladora de presión de combustible.',
  'P0097': 'Baja señal del sensor de temperatura del aire de admisión 2.',
  'P0100': 'Circuito del sensor de flujo de aire (MAF).',
  'P0101': 'El sensor de flujo de aire (MAF) reporta un valor fuera de lo esperado.',
  'P0102': 'Señal baja del sensor de flujo de aire (MAF).',
  'P0103': 'Señal alta del sensor de flujo de aire (MAF).',
  'P0105': 'Circuito del sensor de presión del múltiple (MAP).',
  'P0106': 'El sensor de presión del múltiple (MAP) reporta un valor fuera de lo esperado.',
  'P0107': 'Señal baja del sensor de presión del múltiple (MAP).',
  'P0108': 'Señal alta del sensor de presión del múltiple (MAP).',
  'P0110': 'Circuito del sensor de temperatura del aire de admisión.',
  'P0111': 'El sensor de temperatura del aire de admisión reporta un valor fuera de rango.',
  'P0112': 'Señal baja del sensor de temperatura del aire de admisión.',
  'P0113': 'Señal alta del sensor de temperatura del aire de admisión.',
  'P0115': 'Circuito del sensor de temperatura del refrigerante.',
  'P0116': 'El sensor de temperatura del refrigerante reporta un valor fuera de rango.',
  'P0117': 'Señal baja del sensor de temperatura del refrigerante.',
  'P0118': 'Señal alta del sensor de temperatura del refrigerante.',
  'P0119': 'Señal intermitente del sensor de temperatura del refrigerante.',
  'P0120': 'Circuito del sensor de posición del acelerador (TPS).',
  'P0121': 'El sensor de posición del acelerador no concuerda con lo esperado.',
  'P0122': 'Señal baja del sensor de posición del acelerador.',
  'P0123': 'Señal alta del sensor de posición del acelerador.',
  'P0125': 'El motor tarda demasiado en alcanzar temperatura para trabajar en ciclo cerrado.',
  'P0128': 'El termostato no deja que el motor alcance su temperatura normal.',

  // Sensores de oxígeno
  'P0130': 'Circuito del sensor de oxígeno 1, banco 1.',
  'P0131': 'Señal baja del sensor de oxígeno 1, banco 1.',
  'P0132': 'Señal alta del sensor de oxígeno 1, banco 1.',
  'P0133': 'Respuesta lenta del sensor de oxígeno 1, banco 1.',
  'P0134': 'Sin actividad en el sensor de oxígeno 1, banco 1.',
  'P0135': 'Circuito del calentador del sensor de oxígeno 1, banco 1.',
  'P0136': 'Circuito del sensor de oxígeno 2, banco 1.',
  'P0137': 'Señal baja del sensor de oxígeno 2, banco 1.',
  'P0138': 'Señal alta del sensor de oxígeno 2, banco 1.',
  'P0139': 'Respuesta lenta del sensor de oxígeno 2, banco 1.',
  'P0140': 'Sin actividad en el sensor de oxígeno 2, banco 1.',
  'P0141': 'Circuito del calentador del sensor de oxígeno 2, banco 1.',
  'P0150': 'Circuito del sensor de oxígeno 1, banco 2.',
  'P0151': 'Señal baja del sensor de oxígeno 1, banco 2.',
  'P0152': 'Señal alta del sensor de oxígeno 1, banco 2.',
  'P0153': 'Respuesta lenta del sensor de oxígeno 1, banco 2.',
  'P0154': 'Sin actividad en el sensor de oxígeno 1, banco 2.',
  'P0155': 'Circuito del calentador del sensor de oxígeno 1, banco 2.',
  'P0156': 'Circuito del sensor de oxígeno 2, banco 2.',
  'P0157': 'Señal baja del sensor de oxígeno 2, banco 2.',
  'P0158': 'Señal alta del sensor de oxígeno 2, banco 2.',
  'P0161': 'Circuito del calentador del sensor de oxígeno 2, banco 2.',

  // Mezcla
  'P0170': 'La corrección de combustible se salió de rango (banco 1).',
  'P0171': 'Mezcla demasiado pobre: entra más aire o menos combustible del debido (banco 1).',
  'P0172': 'Mezcla demasiado rica: entra más combustible del debido (banco 1).',
  'P0173': 'La corrección de combustible se salió de rango (banco 2).',
  'P0174': 'Mezcla demasiado pobre (banco 2).',
  'P0175': 'Mezcla demasiado rica (banco 2).',
  'P0180': 'Circuito del sensor de temperatura del combustible.',
  'P0190': 'Circuito del sensor de presión del riel de combustible.',
  'P0191': 'El sensor de presión del riel reporta un valor fuera de lo esperado.',
  'P0193': 'Señal alta del sensor de presión del riel de combustible.',

  // Combustible y sobrealimentación
  'P0200': 'Circuito de los inyectores.',
  'P0217': 'El motor se sobrecalentó.',
  'P0219': 'El motor pasó de las revoluciones permitidas.',
  'P0221': 'El segundo sensor de posición del acelerador no concuerda con lo esperado.',
  'P0222': 'Señal baja del segundo sensor de posición del acelerador.',
  'P0223': 'Señal alta del segundo sensor de posición del acelerador.',
  'P0230': 'Circuito de la bomba de combustible.',
  'P0234': 'El turbo está dando más presión de la debida.',
  'P0299': 'El turbo no está dando la presión que debería.',

  // Encendido y fallos de combustión
  'P0300': 'Fallos de combustión en varios cilindros, sin uno fijo.',
  'P0313': 'Fallo de combustión con poco combustible en el tanque.',
  'P0315': 'La computadora no tiene aprendida la variación del cigüeñal.',
  'P0316': 'Fallos de combustión en los primeros segundos de arranque.',
  'P0320': 'Circuito de la señal de revoluciones del motor.',
  'P0325': 'Circuito del sensor de detonación (banco 1).',
  'P0326': 'El sensor de detonación reporta un valor fuera de rango (banco 1).',
  'P0327': 'Señal baja del sensor de detonación (banco 1).',
  'P0328': 'Señal alta del sensor de detonación (banco 1).',
  'P0330': 'Circuito del sensor de detonación (banco 2).',
  'P0335': 'Circuito del sensor de posición del cigüeñal.',
  'P0336': 'El sensor de posición del cigüeñal reporta un valor fuera de rango.',
  'P0337': 'Señal baja del sensor de posición del cigüeñal.',
  'P0339': 'Señal intermitente del sensor de posición del cigüeñal.',
  'P0340': 'Circuito del sensor de posición del árbol de levas.',
  'P0341': 'El sensor del árbol de levas reporta un valor fuera de lo esperado.',
  'P0342': 'Señal baja del sensor de posición del árbol de levas.',
  'P0343': 'Señal alta del sensor de posición del árbol de levas.',
  'P0345': 'Circuito del sensor de posición del árbol de levas (banco 2).',
  'P0350': 'Circuito primario o secundario de las bobinas de encendido.',
  'P0380': 'Circuito de las bujías de precalentamiento (motor diésel).',

  // Emisiones
  'P0401': 'La recirculación de gases de escape (EGR) no está pasando suficiente flujo.',
  'P0402': 'La recirculación de gases de escape (EGR) está pasando demasiado flujo.',
  'P0403': 'Circuito de la válvula de recirculación de gases (EGR).',
  'P0404': 'La válvula EGR no se mueve como debería.',
  'P0405': 'Señal baja del sensor de posición de la válvula EGR.',
  'P0411': 'El sistema de aire secundario no está dando el flujo esperado.',
  'P0420': 'El catalizador del banco 1 ya no está limpiando los gases como debería.',
  'P0421': 'El catalizador de arranque del banco 1 trabaja por debajo del umbral.',
  'P0430': 'El catalizador del banco 2 ya no está limpiando los gases como debería.',
  'P0440': 'Falla general en el sistema de vapores de gasolina (evaporativo).',
  'P0441': 'El sistema de vapores de gasolina no está purgando bien.',
  'P0442': 'Fuga pequeña en el sistema de vapores de gasolina.',
  'P0443': 'Circuito de la válvula de purga del sistema de vapores.',
  'P0446': 'Circuito de ventilación del sistema de vapores de gasolina.',
  'P0447': 'Circuito abierto en la ventilación del sistema de vapores.',
  'P0449': 'Circuito de la válvula de ventilación del canister.',
  'P0451': 'El sensor de presión del tanque reporta un valor fuera de rango.',
  'P0455': 'Fuga grande en el sistema de vapores de gasolina; suele ser la tapa del tanque.',
  'P0456': 'Fuga muy pequeña en el sistema de vapores de gasolina.',
  'P0457': 'Fuga en el sistema de vapores: la tapa del tanque quedó suelta.',
  'P0461': 'El sensor de nivel de combustible reporta un valor fuera de rango.',
  'P0462': 'Señal baja del sensor de nivel de combustible.',
  'P0463': 'Señal alta del sensor de nivel de combustible.',
  'P0480': 'Circuito del ventilador de enfriamiento 1.',
  'P0481': 'Circuito del ventilador de enfriamiento 2.',

  // Velocidad, marcha mínima y alimentación
  'P0500': 'Circuito del sensor de velocidad del vehículo.',
  'P0501': 'El sensor de velocidad reporta un valor fuera de lo esperado.',
  'P0505': 'El control de marcha mínima no está trabajando bien.',
  'P0506': 'La marcha mínima está más baja de lo que debería.',
  'P0507': 'La marcha mínima está más alta de lo que debería.',
  'P0520': 'Circuito del sensor de presión de aceite.',
  'P0521': 'El sensor de presión de aceite reporta un valor fuera de rango.',
  'P0522': 'Señal baja del sensor de presión de aceite.',
  'P0523': 'Señal alta del sensor de presión de aceite.',
  'P0532': 'Señal baja del sensor de presión del aire acondicionado.',
  'P0562': 'Voltaje del sistema bajo: revise batería y alternador.',
  'P0563': 'Voltaje del sistema alto: revise el regulador del alternador.',
  'P0571': 'Circuito del interruptor del pedal de freno.',

  // Computadora
  'P0600': 'Falla de comunicación interna de la computadora del motor.',
  'P0601': 'Error de memoria en la computadora del motor.',
  'P0602': 'La computadora del motor no está programada.',
  'P0603': 'Se perdió la memoria guardada de la computadora del motor.',
  'P0606': 'Falla interna del procesador de la computadora del motor.',
  'P0620': 'Circuito de control del alternador.',
  'P0622': 'Circuito del campo del alternador.',
  'P0630': 'El VIN no está programado en la computadora del motor.',
  'P0645': 'Circuito del relé del embrague del aire acondicionado.',
  'P0685': 'Circuito del relé de alimentación de la computadora del motor.',

  // Transmisión
  'P0700': 'La computadora de la transmisión reportó una falla.',
  'P0705': 'Circuito del sensor de posición de la palanca de cambios.',
  'P0706': 'El sensor de posición de la palanca reporta un valor fuera de rango.',
  'P0710': 'Circuito del sensor de temperatura del aceite de la transmisión.',
  'P0711': 'El sensor de temperatura del aceite de la transmisión está fuera de rango.',
  'P0715': 'Circuito del sensor de velocidad de entrada de la transmisión.',
  'P0716': 'El sensor de velocidad de entrada reporta un valor fuera de rango.',
  'P0720': 'Circuito del sensor de velocidad de salida de la transmisión.',
  'P0721': 'El sensor de velocidad de salida reporta un valor fuera de rango.',
  'P0730': 'La relación de cambio no es la que debería.',
  'P0731': 'La primera velocidad no está entrando como debe.',
  'P0732': 'La segunda velocidad no está entrando como debe.',
  'P0733': 'La tercera velocidad no está entrando como debe.',
  'P0734': 'La cuarta velocidad no está entrando como debe.',
  'P0735': 'La quinta velocidad no está entrando como debe.',
  'P0740': 'El embrague del convertidor de par no está trabajando.',
  'P0741': 'El embrague del convertidor de par patina o no se aplica.',
  'P0742': 'El embrague del convertidor de par se quedó aplicado.',
  'P0743': 'Circuito eléctrico del embrague del convertidor de par.',
  'P0745': 'Circuito del solenoide de control de presión de la transmisión.',
  'P0746': 'El solenoide de control de presión se quedó pegado.',
  'P0750': 'Circuito del solenoide de cambio A.',
  'P0751': 'El solenoide de cambio A se quedó pegado.',
  'P0755': 'Circuito del solenoide de cambio B.',
  'P0756': 'El solenoide de cambio B se quedó pegado.',
  'P0760': 'Circuito del solenoide de cambio C.',
  'P0765': 'Circuito del solenoide de cambio D.',
  'P0795': 'Circuito del solenoide de control de presión C.',

  // Comunicación entre módulos
  'U0001': 'Falla en la red de comunicación del vehículo (bus CAN).',
  'U0100': 'Se perdió la comunicación con la computadora del motor.',
  'U0101': 'Se perdió la comunicación con la computadora de la transmisión.',
  'U0121': 'Se perdió la comunicación con el módulo de frenos (ABS).',
  'U0140': 'Se perdió la comunicación con el módulo de carrocería.',
  'U0155': 'Se perdió la comunicación con el tablero de instrumentos.',
  'U0164': 'Se perdió la comunicación con el módulo del aire acondicionado.',
  'U0401': 'Datos inválidos recibidos de la computadora del motor.',

  // Chasis
  'C0035': 'Circuito del sensor de velocidad de la rueda delantera izquierda.',
  'C0040': 'Circuito del sensor de velocidad de la rueda delantera derecha.',
  'C0045': 'Circuito del sensor de velocidad de la rueda trasera izquierda.',
  'C0050': 'Circuito del sensor de velocidad de la rueda trasera derecha.',
  'C0051': 'Circuito del sensor de ángulo de la dirección.',
  'C0121': 'Circuito del relé de válvulas del sistema de frenos.',
  'C0265': 'Circuito del relé del módulo de frenos (ABS).',
};

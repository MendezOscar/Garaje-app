import 'package:flutter_test/flutter_test.dart';
import 'package:garaj_app/core/assembly/serie_de_la_etiqueta.dart';

// Los códigos de estas pruebas están copiados de fotos reales de cajas de Italika que mandó
// el taller. No son inventados: es lo que el lector va a ver de verdad en la bodega.
void main() {
  test('de una etiqueta con cuatro códigos saca el VIN y no los otros', () {
    // Caja de una FT125 negra. En la etiqueta hay: el código del envío, el del SKU, el
    // número de motor y el VIN —este último impreso dos veces, arriba y al pie—.
    final codigos = [
      '34005571',
      'L062403880',
      'LC157FMI112743E6',
      '*LLCLPP200VE100410*',
      'LLCLPP200VE100410',
    ];

    expect(serieDeLaEtiqueta(codigos), 'LLCLPP200VE100410');
  });

  test('los asteriscos del Code 39 no entran en la serie', () {
    // El lector devuelve el contenido tal cual, y estas etiquetas vienen envueltas.
    expect(serieDeLaEtiqueta(['*LLCLPLPC8VA100156*']), 'LLCLPLPC8VA100156');
  });

  test('el número de motor no pasa por VIN', () {
    // Dieciséis caracteres y con Q: el estándar del VIN no usa Q. Si esta regla cediera, la
    // moto quedaría registrada con el número del motor y la serie de verdad se perdería.
    expect(pareceSerie('LC163FML279200Q6'), isFalse);
    expect(serieDeLaEtiqueta(['LC163FML279200Q6']), isNull);
  });

  test('ni el del envío ni el del SKU', () {
    expect(serieDeLaEtiqueta(['34007693', 'L064158942']), isNull);
  });

  test('las tres cajas que mandó el taller se leen bien', () {
    expect(serieDeLaEtiqueta(['*LLCLPLPC8VA100156*']), 'LLCLPLPC8VA100156');
    expect(serieDeLaEtiqueta(['LRPRPMV00VA000206']), 'LRPRPMV00VA000206');
    expect(serieDeLaEtiqueta(['LLCLPP200VE100410']), 'LLCLPP200VE100410');
  });

  test('un código con I, O o Q se descarta aunque mida diecisiete', () {
    // Son las tres letras que el estándar deja fuera para que no se confundan con 1 y 0.
    expect(pareceSerie('LLCLPP2O0VE100410'), isFalse);
    expect(pareceSerie('ILCLPP200VE100410'), isFalse);
    expect(pareceSerie('QLCLPP200VE100410'), isFalse);
  });

  test('lo que no mide diecisiete tampoco', () {
    expect(pareceSerie('LLCLPP200VE10041'), isFalse);
    expect(pareceSerie('LLCLPP200VE1004100'), isFalse);
  });
}

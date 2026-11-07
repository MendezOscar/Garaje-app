import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:garaj_app/core/router/app_router.dart';
import 'package:go_router/go_router.dart';

// Las dos maneras en que una entrada del menú termina abriendo el inicio sin decir por qué:
//
//  1. Dos rutas con el mismo camino. go_router se queda con la primera y nadie avisa. Pasó de
//     verdad: `/taller` era a la vez el inicio del Dueño y los ajustes del taller.
//  2. Una ruta declarada pero fuera de las listas del redirect, que manda al inicio todo lo
//     que no reconoce. También pasó: `/historial` se agregó al menú y al árbol de rutas, pero
//     no a las listas, y el renglón abría la pantalla de inicio.
//
// Ninguna de las dos necesita simulador ni API: se leen el árbol y las listas y se comparan.
void main() {
  test('ninguna ruta se repite', () {
    final caminos = <String>[];

    void recorrer(List<RouteBase> rutas, String prefijo) {
      for (final ruta in rutas) {
        if (ruta is GoRoute) {
          final camino = ruta.path.startsWith('/') ? ruta.path : '$prefijo/${ruta.path}';
          caminos.add(camino);
          recorrer(ruta.routes, camino);
        } else {
          recorrer(ruta.routes, prefijo);
        }
      }
    }

    recorrer(garajRoutes(), '');

    final repetidos = caminos.toSet().where((c) => caminos.where((o) => o == c).length > 1);
    expect(repetidos, isEmpty, reason: 'estas rutas están declaradas dos veces');
  });

  // Las rutas a las que de verdad navega la aplicación, leídas del código y no escritas a
  // mano aquí.
  //
  // La versión anterior llevaba la lista a mano, y por eso no sirvió: se agregaron
  // «/armar-moto» y «/motos-armadas» al menú, nadie tocó esta lista, la prueba siguió en
  // verde y los tres botones del armado abrían la pantalla de inicio. Una lista que hay que
  // acordarse de actualizar no protege de olvidarse de actualizar una lista.
  Set<String> rutasQueLaAppAbre() {
    final destinos = <String>{};

    // `route: '/x'` en las filas del menú, y `context.push('/x')` o `.go('/x')` en el resto.
    final patrones = [
      RegExp(r"""route:\s*'(/[a-z0-9\-/]+)'"""),
      RegExp(r"""\.(?:push|go|replace)\(\s*'(/[a-z0-9\-/]+)'"""),
    ];

    for (final archivo in Directory('lib').listSync(recursive: true)) {
      if (archivo is! File || !archivo.path.endsWith('.dart')) continue;

      final codigo = archivo.readAsStringSync();

      for (final patron in patrones) {
        for (final hallazgo in patron.allMatches(codigo)) {
          destinos.add(hallazgo.group(1)!);
        }
      }
    }

    return destinos;
  }

  test('todas las rutas a las que navega la app existen en el router', () {
    final declaradas = garajRoutes().whereType<GoRoute>().map((r) => r.path).toSet();

    for (final ruta in rutasQueLaAppAbre()) {
      expect(declaradas, contains(ruta), reason: '$ruta no está declarada en el router');
    }
  });

  test('el redirect deja pasar todas las rutas a las que navega la app', () {
    // Las que valen para cualquier perfil porque la pantalla decide adentro qué enseña, más
    // los inicios de cada perfil y las pantallas sin sesión, que el redirect trata aparte.
    const deCualquiera = {
      '/avisos',
      '/nueva-cita',
      '/ordenes',
      '/adjuntar-informe',
      '/pedir-cita',
      '/',
      '/login',
      '/bienvenida',
      '/taller',
      '/mis-asignaciones',
      '/mis-vehiculos',
      '/plataforma',
    };

    final permitidas = {
      ...rutasDelDueno,
      ...rutasDelMando,
      ...rutasDelTaller,
      ...deCualquiera,
    };

    for (final ruta in rutasQueLaAppAbre()) {
      expect(
        permitidas,
        contains(ruta),
        reason: '$ruta no está en las listas del redirect: la app la abre y cae en el inicio',
      );
    }
  });

  test('lo del Encargado y lo del Dueño no se mezclan', () {
    // El Encargado manda en su sucursal, pero no en el taller: lo que configura el taller
    // entero —los catálogos, los gastos, el personal, los ajustes— no puede estar también en
    // la lista que él abre, porque entonces la pantalla se abriría para fallar con un 403.
    expect(rutasDelDueno.intersection(rutasDelMando), isEmpty);

    for (final ruta in const ['/ajustes', '/usuarios', '/mano-de-obra', '/trabajos-frecuentes',
        '/resultados']) {
      expect(rutasDelDueno, contains(ruta), reason: '$ruta es del Dueño');
      expect(rutasDelMando, isNot(contains(ruta)));
    }

    for (final ruta in const ['/ventas', '/caja', '/por-cobrar', '/reclamos', '/reportes']) {
      expect(rutasDelMando, contains(ruta), reason: '$ruta también la abre el Encargado');
    }
  });
}

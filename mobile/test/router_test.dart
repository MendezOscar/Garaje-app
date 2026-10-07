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

  test('todas las rutas del menú existen en el router', () {
    final declaradas = garajRoutes().whereType<GoRoute>().map((r) => r.path).toSet();

    // Las que el menú «Más» usa. Si alguien renombra una ruta y olvida el menú, el toque
    // termina en el inicio del perfil sin decir por qué.
    const delMenu = [
      '/requerimientos',
      '/recordatorios',
      '/reclamos',
      '/por-cobrar',
      '/ventas',
      '/reportes',
      '/resultados',
      '/clientes',
      '/historial',
      '/mano-de-obra',
      '/trabajos-frecuentes',
      '/inventario',
      '/usuarios',
      '/ajustes',
      '/avisos',
      '/ordenes',
      '/nueva-cita',
    ];

    for (final ruta in delMenu) {
      expect(declaradas, contains(ruta), reason: '$ruta no está declarada en el router');
    }
  });

  test('el redirect deja pasar todas las rutas del menú', () {
    // Lo que el redirect deja pasar: lo del Dueño, lo del taller, y las que valen para
    // cualquier perfil porque la pantalla decide adentro qué enseña.
    const deCualquiera = {'/avisos', '/nueva-cita', '/ordenes'};
    final permitidas = {...rutasDelDueno, ...rutasDelTaller, ...deCualquiera};

    const delMenu = [
      '/requerimientos',
      '/recordatorios',
      '/reclamos',
      '/por-cobrar',
      '/ventas',
      '/reportes',
      '/resultados',
      '/clientes',
      '/historial',
      '/mano-de-obra',
      '/trabajos-frecuentes',
      '/inventario',
      '/usuarios',
      '/ajustes',
      '/avisos',
      '/ordenes',
      '/nueva-cita',
    ];

    for (final ruta in delMenu) {
      expect(
        permitidas,
        contains(ruta),
        reason: '$ruta no está en las listas del redirect: el menú la abre y cae en el inicio',
      );
    }
  });
}

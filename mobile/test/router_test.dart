import 'package:flutter_test/flutter_test.dart';
import 'package:garaj_app/core/router/app_router.dart';
import 'package:go_router/go_router.dart';

// Dos rutas con el mismo camino es una falla que no se ve: go_router se queda con la primera
// y la entrada del menú lleva a otra pantalla sin decir nada. Pasó de verdad —`/taller` era a
// la vez el inicio del Dueño y los ajustes del taller— y la entrada de Ajustes abría el
// inicio. Esta prueba no necesita simulador ni API: lee el árbol de rutas y compara.
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
}

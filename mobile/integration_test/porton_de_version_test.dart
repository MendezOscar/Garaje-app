import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garaj_app/core/api/app_version_repository.dart';
import 'package:garaj_app/core/auth/auth_controller.dart';
import 'package:garaj_app/core/auth/token_store.dart';
import 'package:garaj_app/core/theme/garaj_brand.dart';
import 'package:garaj_app/core/widgets/porton_de_version.dart';
import 'package:integration_test/integration_test.dart';

// El portón de versión contra la API de verdad. Lo que se mira es lo que decide el servidor,
// no una comparación hecha aquí.
//
// Se corre tres veces, una por cada configuración de la API, porque las tres maneras de
// equivocarse son distintas: no parar a quien hay que parar, molestar a quien está al día, y
// —la peor— dejar sin app a un taller que trabaja.
//
//   # 1. Parado: API con AppVersion__MinimumBuild=999
//   flutter test integration_test/porton_de_version_test.dart -d <sim> \
//     --dart-define=API_URL=http://localhost:5199 --dart-define=PORTON=bloquear
//
//   # 2. Avisado: API con MinimumBuild=0 y RecommendedBuild=999
//   … --dart-define=PORTON=avisar
//
//   # 3. Al día: API sin los números puestos
//   … --dart-define=PORTON=pasa
/// Qué tiene configurada la API con la que se está corriendo.
const esperado = String.fromEnvironment('PORTON', defaultValue: 'bloquear');

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  Future<void> montar(WidgetTester tester, ProviderContainer c) async {
    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: c,
        child: MaterialApp(
          theme: garajTheme,
          builder: (context, child) =>
              PortonDeVersion(child: child ?? const SizedBox.shrink()),
          home: const Scaffold(body: Center(child: Text('La pantalla de siempre'))),
        ),
      ),
    );

    // La consulta de versión es una petición de verdad: hay que darle tiempo de red.
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();
  }

  testWidgets('el portón hace lo que dice la API ($esperado)', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);

    final version = await container.read(versionDeLaAppProvider.future);
    await montar(tester, container);

    switch (esperado) {
      case 'bloquear':
        expect(version.bloquea, isTrue, reason: 'la API no está con el mínimo en alto');
        expect(find.text('Hay que actualizar la app'), findsOneWidget);
        expect(find.text('Abrir la tienda'), findsOneWidget);

        // La pantalla que estaba debajo no se ve: es un portón, no un aviso.
        expect(find.text('La pantalla de siempre'), findsNothing);

        // Y dice qué tiene y qué hace falta, que es lo que se pregunta por teléfono.
        expect(find.textContaining('hace falta ${version.compilacionMinima}'), findsOneWidget);

      case 'avisar':
        expect(version.avisa, isTrue, reason: 'la API no está con la recomendada en alto');

        // La franja se ve, y debajo se sigue trabajando: esto no para a nadie.
        expect(find.text('Actualizar'), findsOneWidget);
        expect(find.text('La pantalla de siempre'), findsOneWidget);
        expect(find.text('Hay que actualizar la app'), findsNothing);

        // Y se puede cerrar: insistir en cada pantalla castiga a quien ya decidió después.
        await tester.tap(find.text('Ahora no'));
        await tester.pumpAndSettle();
        expect(find.text('Actualizar'), findsNothing);
        expect(find.text('La pantalla de siempre'), findsOneWidget);

      default:
        // Lo normal, y lo que más importa: con los números sin poner, el portón no existe.
        expect(version.bloquea, isFalse);
        expect(version.avisa, isFalse);
        expect(find.text('La pantalla de siempre'), findsOneWidget);
        expect(find.text('Hay que actualizar la app'), findsNothing);
        expect(find.text('Actualizar'), findsNothing);
    }
  });

  testWidgets('entrar: la API para a la vieja y deja pasar a la que sirve', (tester) async {
    await TokenStore().clear();

    final container = ProviderContainer();
    addTearDown(container.dispose);

    Future<void> entrar() => container.read(authControllerProvider.notifier).login(
          'dueno@tallerdemo.hn',
          'Garaj123!',
        );

    if (esperado == 'bloquear') {
      // El 426 para la petición antes de mirar siquiera la contraseña, y llega como error a
      // quien la pidió.
      await expectLater(entrar(), throwsA(isA<DioException>()));

      expect(
        container.read(authControllerProvider),
        isNot(isA<AuthSignedIn>()),
        reason: 'la API no debería dejar entrar a una app por debajo del mínimo',
      );

      // Y ese rechazo prende el portón, que es lo que entera al que ya tenía la app abierta.
      await tester.pump(const Duration(seconds: 3));
      expect(container.read(versionDeLaAppProvider).value?.bloquea, isTrue);
      return;
    }

    // Con el portón abierto —o solo avisando— se entra como siempre. Esto es lo que no puede
    // fallar: el portón no está para estorbarle al taller que trabaja.
    await entrar();
    expect(container.read(authControllerProvider), isA<AuthSignedIn>());
  });
}

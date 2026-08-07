import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garaj_app/core/auth/token_store.dart';
import 'package:garaj_app/main.dart';
import 'package:integration_test/integration_test.dart';

/// Prueba de humo del login contra la API real. No usa mocks a propósito: lo que puede
/// fallar aquí es justamente la integración —ATS bloqueando HTTP, la URL mal apuntada,
/// el claim de rol, el redirect del router—, y eso un mock no lo detecta.
///
/// Requiere la API corriendo con los datos del seeder:
///   `flutter test integration_test/login_test.dart -d <simulador>`
///   `  --dart-define=API_URL=http://localhost:5080`
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  setUp(() async {
    // Cada caso arranca sin sesión: si no, el router saltaría directo al home.
    await TokenStore().clear();
  });

  Future<void> signIn(WidgetTester tester, String email) async {
    await tester.pumpWidget(const ProviderScope(child: GarajApp()));

    // El arranque muestra el splash mientras se decide si hay sesión guardada.
    await tester.pumpAndSettle();
    expect(find.text('Ingresar'), findsOneWidget);

    await tester.enterText(find.byType(TextFormField).first, email);
    await tester.enterText(find.byType(TextFormField).last, 'Garaj123!');
    await tester.tap(find.text('Ingresar'));

    // pumpAndSettle no espera respuestas de red: hay que darle tiempo real al round-trip.
    await tester.pump(const Duration(seconds: 5));
    await tester.pumpAndSettle();
  }

  testWidgets('el Dueño entra y aterriza en Taller con sus dos sucursales', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    expect(find.text('Taller'), findsOneWidget);
    expect(find.text('Óscar Méndez'), findsOneWidget);
    expect(find.text('Perfil: Dueño'), findsOneWidget);
    expect(find.text('Taller: Taller Garaj'), findsOneWidget);
    expect(find.text('Sucursales: Matriz, Sucursal Sur'), findsOneWidget);
  });

  testWidgets('el Técnico aterriza en sus asignaciones y solo ve su sucursal', (tester) async {
    await signIn(tester, 'tecnico1@garaj.test');

    expect(find.text('Mis asignaciones'), findsOneWidget);
    expect(find.text('Luis Cabrera'), findsOneWidget);
    expect(find.text('Sucursales: Matriz'), findsOneWidget);
  });

  testWidgets('el Cliente aterriza en sus vehículos', (tester) async {
    await signIn(tester, 'cliente@garaj.test');

    expect(find.text('Mis vehículos'), findsOneWidget);
    expect(find.text('María Torres'), findsOneWidget);
  });

  testWidgets('una contraseña incorrecta muestra el error y no navega', (tester) async {
    await tester.pumpWidget(const ProviderScope(child: GarajApp()));
    await tester.pumpAndSettle();

    await tester.enterText(find.byType(TextFormField).first, 'owner@garaj.test');
    await tester.enterText(find.byType(TextFormField).last, 'clave-incorrecta');
    await tester.tap(find.text('Ingresar'));
    await tester.pump(const Duration(seconds: 5));
    await tester.pumpAndSettle();

    expect(find.text('Credenciales inválidas.'), findsOneWidget);
    expect(find.text('Taller'), findsNothing);
  });
}

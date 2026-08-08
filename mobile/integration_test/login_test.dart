import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garaj_app/core/auth/token_store.dart';
import 'package:garaj_app/main.dart';
import 'package:integration_test/integration_test.dart';

// Prueba de humo contra la API real. No usa mocks a propósito: lo que puede fallar aquí es
// justamente la integración —ATS bloqueando HTTP, la URL mal apuntada, el claim de rol, el
// redirect del router, el alcance por perfil—, y eso un mock no lo detecta.
//
// Requiere la API corriendo con los datos del seeder:
//   `flutter test integration_test/login_test.dart -d <simulador>`
//   `  --dart-define=API_URL=http://localhost:5080`
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

    // pumpAndSettle no espera respuestas de red: hay que darle tiempo real al round-trip
    // del login y al de la lista de órdenes que carga después.
    await tester.pump(const Duration(seconds: 6));
    await tester.pumpAndSettle();
  }

  testWidgets('el Dueño ve las órdenes de todo el taller', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    expect(find.text('Taller'), findsOneWidget);
    expect(find.text('Óscar Méndez · Taller Garaj'), findsOneWidget);
    // El seeder deja una orden abierta en cada sucursal.
    expect(find.textContaining('MTZ-'), findsOneWidget);
    expect(find.textContaining('SPS-'), findsOneWidget);
  });

  testWidgets('el Técnico ve solo lo suyo y puede abrir la orden', (tester) async {
    await signIn(tester, 'tecnico1@garaj.test');

    expect(find.text('Mis asignaciones'), findsOneWidget);
    expect(find.textContaining('MTZ-'), findsOneWidget);
    // La orden de la otra sucursal es de otro técnico: no debe aparecer.
    expect(find.textContaining('SPS-'), findsNothing);

    await tester.tap(find.textContaining('MTZ-').first);
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    expect(find.text('MOTIVO DE INGRESO'), findsOneWidget);
    expect(find.text('PASOS DE LA REPARACIÓN'), findsOneWidget);
    expect(find.text('REPUESTOS'), findsOneWidget);
    expect(find.text('FOTOS DEL PROCESO'), findsOneWidget);
    // Puede documentar y consumir: cámara, galería y catálogo.
    expect(find.text('Cargar repuesto'), findsOneWidget);
    expect(find.byTooltip('Tomar foto'), findsOneWidget);
    expect(find.byTooltip('Elegir de la galería'), findsOneWidget);

    // El resto queda bajo el pliegue: el ListView no construye lo que no se ve.
    await tester.scrollUntilVisible(find.text('LÍNEA DE TIEMPO'), 300);
    expect(find.text('LÍNEA DE TIEMPO'), findsOneWidget);
    // Al Técnico sí se le ofrecen transiciones; al Cliente no.
    expect(find.text('CAMBIAR ESTADO'), findsOneWidget);
  });

  testWidgets('el Cliente sigue su vehículo pero no cambia estados', (tester) async {
    await signIn(tester, 'cliente@garaj.test');

    expect(find.text('Mis vehículos'), findsOneWidget);
    expect(find.textContaining('MTZ-'), findsOneWidget);

    await tester.tap(find.textContaining('MTZ-').first);
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    // Ve lo que le hicieron, pero ninguna de las acciones del taller.
    expect(find.text('REPUESTOS'), findsOneWidget);
    expect(find.text('FOTOS DEL PROCESO'), findsOneWidget);
    expect(find.text('Cargar repuesto'), findsNothing);
    expect(find.text('Agregar paso'), findsNothing);
    expect(find.byTooltip('Tomar foto'), findsNothing);

    await tester.scrollUntilVisible(find.text('LÍNEA DE TIEMPO'), 300);
    expect(find.text('LÍNEA DE TIEMPO'), findsOneWidget);
    expect(find.text('CAMBIAR ESTADO'), findsNothing);
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

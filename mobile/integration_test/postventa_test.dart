import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garaj_app/core/auth/auth_controller.dart';
import 'package:garaj_app/core/auth/token_store.dart';
import 'package:garaj_app/core/theme/garaj_brand.dart';
import 'package:garaj_app/features/expenses/expenses_screen.dart';
import 'package:garaj_app/features/work_orders/vehicle_history_lookup_screen.dart';
import 'package:integration_test/integration_test.dart';

// Las pantallas nuevas contra la API de verdad: el historial por placa y el estado de
// resultados de meses anteriores. Las reglas del servidor —plazos para anular y reabrir— se
// prueban aparte, en backend/tests/smoke/fase14_postventa.py.
//
//   flutter test integration_test/postventa_test.dart -d <simulador>
//     --dart-define=API_URL=http://localhost:5199
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  Future<ProviderContainer> conSesion() async {
    await TokenStore().clear();

    final container = ProviderContainer();
    await container.read(authControllerProvider.notifier).login(
          'dueno@tallerdemo.hn',
          'Garaj123!',
        );

    expect(
      container.read(authControllerProvider),
      isA<AuthSignedIn>(),
      reason: 'hace falta la API local en el 5199 con la demostración sembrada',
    );

    return container;
  }

  Future<void> montar(WidgetTester tester, ProviderContainer c, Widget pantalla) async {
    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: c,
        child: MaterialApp(theme: garajTheme, home: pantalla),
      ),
    );

    // pumpAndSettle no espera la red: hay que darle tiempo real al primer viaje.
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();
  }

  testWidgets('el historial encuentra las visitas de un vehículo', (tester) async {
    final container = await conSesion();
    addTearDown(container.dispose);

    await montar(tester, container, const VehicleHistoryLookupScreen());

    // Antes de buscar, la pantalla explica para qué sirve en vez de quedarse en blanco.
    expect(find.text('Busque un vehículo'), findsOneWidget);

    await tester.enterText(find.byType(TextField), 'Marvin');
    await tester.testTextInput.receiveAction(TextInputAction.search);
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    // El cliente de la demostración tiene más de un vehículo: primero se elige cuál.
    expect(find.textContaining('vehículos con esa búsqueda'), findsOneWidget);

    await tester.tap(find.byType(ListTile).first);
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    // Y entonces sí, sus visitas, con el número de orden a la vista.
    expect(find.textContaining('Ver todos'), findsOneWidget);
    expect(find.byType(Card), findsWidgets);
  });

  testWidgets('una búsqueda sin resultados lo dice', (tester) async {
    final container = await conSesion();
    addTearDown(container.dispose);

    await montar(tester, container, const VehicleHistoryLookupScreen());

    await tester.enterText(find.byType(TextField), 'ZZZ9999');
    await tester.testTextInput.receiveAction(TextInputAction.search);
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    expect(find.text('Ninguna visita con esa placa ni ese cliente'), findsOneWidget);
  });

  testWidgets('el estado de resultados se mueve a meses anteriores', (tester) async {
    final container = await conSesion();
    addTearDown(container.dispose);

    await montar(tester, container, const ExpensesScreen());

    expect(find.text('ESTE MES'), findsOneWidget);

    // La flecha de atrás: el mes anterior tiene su propio rótulo, y el botón «Hoy» aparece
    // para volver.
    await tester.tap(find.byTooltip('Mes anterior'));
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    expect(find.text('ESTE MES'), findsNothing);
    expect(find.text('Hoy'), findsOneWidget);

    await tester.tap(find.text('Hoy'));
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    expect(find.text('ESTE MES'), findsOneWidget);
  });
}

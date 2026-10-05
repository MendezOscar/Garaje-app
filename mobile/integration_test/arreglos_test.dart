import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garaj_app/core/api/claim_repository.dart';
import 'package:garaj_app/core/auth/auth_controller.dart';
import 'package:garaj_app/core/auth/token_store.dart';
import 'package:garaj_app/core/theme/garaj_brand.dart';
import 'package:garaj_app/features/catalog/job_templates_screen.dart';
import 'package:garaj_app/features/expenses/expenses_screen.dart';
import 'package:garaj_app/features/sales/counter_sale_screen.dart';
import 'package:garaj_app/features/work_orders/work_order_detail_screen.dart';
import 'package:garaj_app/features/workshop/workshop_settings_screen.dart';
import 'package:integration_test/integration_test.dart';

// Las fallas que reportó el taller, cada una con su caso, contra la API de verdad: lo que
// falló fue justamente la integración —un proveedor autoDispose que nadie estaba mirando, una
// lista que no se invalidaba, una pantalla sin botón—, y eso un mock no lo ve.
//
// Se monta la pantalla sola y no la aplicación entera a propósito: navegar el menú desde el
// login hace la prueba frágil por razones que no tienen nada que ver con lo que se prueba, y
// los proveedores, el cliente HTTP y la sesión son los de verdad igual.
//
//   flutter test integration_test/arreglos_test.dart -d <simulador>
//     --dart-define=API_URL=http://localhost:5199
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  /// Un contenedor con la sesión del Dueño abierta, como la tendría la aplicación corriendo.
  Future<ProviderContainer> conSesion() async {
    await TokenStore().clear();

    final container = ProviderContainer();
    await container.read(authControllerProvider.notifier).login(
          'dueno@local.test',
          'Local123!',
        );

    expect(
      container.read(authControllerProvider),
      isA<AuthSignedIn>(),
      reason: 'hace falta la API local en el puerto 5199 con el taller de pruebas',
    );

    return container;
  }

  /// Monta una pantalla sola, con la sesión abierta y el tema de la aplicación.
  Future<void> montar(WidgetTester tester, ProviderContainer container, Widget pantalla) async {
    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp(theme: garajTheme, home: pantalla),
      ),
    );

    // pumpAndSettle no espera la red: hay que darle tiempo real al primer viaje.
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();
  }

  testWidgets('el botón de gasto abre el formulario', (tester) async {
    final container = await conSesion();
    addTearDown(container.dispose);

    await montar(tester, container, const ExpensesScreen());

    // El botón existía y no hacía nada: el proveedor de sucursales venía vacío porque nadie
    // lo estaba mirando, y la función se iba por un `return` silencioso.
    await tester.tap(find.widgetWithText(FloatingActionButton, 'Gasto'));
    await tester.pump(const Duration(seconds: 3));
    await tester.pumpAndSettle();

    expect(find.text('Registrar un gasto'), findsOneWidget);
  });

  testWidgets('la venta de un trabajo queda registrada', (tester) async {
    final container = await conSesion();
    addTearDown(container.dispose);

    await montar(tester, container, const CounterSaleScreen());

    // Un trabajo escrito a mano, que es el caso que falló.
    // Por el texto y no por el tipo: el botón es un `OutlinedButton.icon`, que no es un
    // `OutlinedButton` a secas para los buscadores.
    await tester.tap(find.text('Trabajo').first);
    await tester.pump(const Duration(seconds: 3));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.widgetWithText(TextField, 'Qué se hizo'),
      'Prueba automática',
    );
    await tester.enterText(find.widgetWithText(TextField, 'Precio'), '150');
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(FilledButton, 'Agregar'));
    await tester.pump(const Duration(seconds: 2));
    await tester.pumpAndSettle();

    await tester.scrollUntilVisible(
      find.text('Registrar la venta'),
      150,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.text('Registrar la venta'));
    await tester.pump(const Duration(seconds: 6));
    await tester.pumpAndSettle();

    expect(find.textContaining('Venta VTA-'), findsOneWidget);
  });

  testWidgets('se puede armar un trabajo frecuente desde el teléfono', (tester) async {
    final container = await conSesion();
    addTearDown(container.dispose);

    await montar(tester, container, const JobTemplatesScreen());

    // Antes no había por dónde: la pantalla solo listaba, renombraba y borraba.
    await tester.tap(find.widgetWithText(FloatingActionButton, 'Nuevo'));
    await tester.pump(const Duration(seconds: 3));
    await tester.pumpAndSettle();

    // Los repuestos del trabajo. Faltaban: solo estaba el de agregar paso.
    expect(find.text('Agregar repuesto'), findsOneWidget);

    final nombre = 'Prueba ${DateTime.now().millisecondsSinceEpoch}';
    await tester.enterText(find.widgetWithText(TextField, 'Cómo se llama'), nombre);
    await tester.enterText(find.widgetWithText(TextField, 'Paso 1'), 'Revisar el motor');
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(FilledButton, 'Guardar'));
    await tester.pump(const Duration(seconds: 6));
    await tester.pumpAndSettle();

    expect(find.text(nombre), findsWidgets);
  });

  testWidgets('la orden de un reclamo pide decidir la garantía', (tester) async {
    final container = await conSesion();
    addTearDown(container.dispose);

    // La orden de reclamo del taller de pruebas: la que se abrió desde el reclamo, sin
    // cerrarlo. Si no existe, la prueba no tiene nada que mirar y lo dice.
    final reclamos = await container.read(claimRepositoryProvider).list();
    final conOrden = reclamos.firstWhere(
      (c) => c.repairWorkOrderId != null,
      orElse: () => throw StateError(
        'hace falta un reclamo con su orden de reparación en el taller de pruebas',
      ),
    );

    await montar(
      tester,
      container,
      WorkOrderDetailScreen(id: conOrden.repairWorkOrderId!),
    );

    expect(find.textContaining('Viene del reclamo'), findsOneWidget);

    // La decisión: o ya está tomada, o la pantalla la pide.
    final pide = find.text('¿La cubre la garantía?').evaluate().isNotEmpty;
    final tomada = find.textContaining('La paga el taller').evaluate().isNotEmpty ||
        find.textContaining('se le cobra al cliente').evaluate().isNotEmpty;

    expect(pide || tomada, isTrue);
  });

  testWidgets('se puede registrar un cliente que no está en el padrón', (tester) async {
    final container = await conSesion();
    addTearDown(container.dispose);

    await montar(tester, container, const CounterSaleScreen());

    await tester.tap(find.text('Buscar el cliente'));
    await tester.pump(const Duration(seconds: 3));
    await tester.pumpAndSettle();

    // Antes había que salirse a Clientes, crearlo y volver a empezar la venta.
    await tester.tap(find.text('¿No está registrado? Agregarlo'));
    await tester.pump(const Duration(seconds: 2));
    await tester.pumpAndSettle();

    expect(find.text('Cliente nuevo'), findsOneWidget);
    expect(find.widgetWithText(TextField, 'Nombre y apellido'), findsOneWidget);
    expect(find.widgetWithText(TextField, 'Teléfono'), findsOneWidget);
  });

  testWidgets('el paso nuevo se puede cobrar con precio a mano', (tester) async {
    final container = await conSesion();
    addTearDown(container.dispose);

    // La orden del reclamo del taller de pruebas sirve: lo que se mira es el formulario.
    final reclamos = await container.read(claimRepositoryProvider).list(onlyOpen: false);
    final conOrden = reclamos.firstWhere(
      (c) => c.repairWorkOrderId != null,
      orElse: () => throw StateError('hace falta una orden en el taller de pruebas'),
    );

    await montar(
      tester,
      container,
      WorkOrderDetailScreen(id: conOrden.repairWorkOrderId!),
    );

    // La sección de repuestos existe y se llega a ella desde la orden: la queja fue que no
    // estaba. Hay que desplazarse hasta el renglón: lo que no se ha dibujado todavía no está
    // en el árbol, y el buscador no lo vería.
    await tester.scrollUntilVisible(
      find.text('Repuestos'),
      150,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.text('Repuestos'), findsOneWidget);

    // Los pasos van dentro de la pantalla de la orden, no detrás de un renglón.
    await tester.scrollUntilVisible(
      find.text('Agregar paso'),
      150,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.text('Agregar paso'));
    await tester.pump(const Duration(seconds: 3));
    await tester.pumpAndSettle();

    // Los tres caminos. Antes solo se podía elegir del catálogo o dejarlo sin cobro: un
    // trabajo que no estuviera en el catálogo no tenía forma de llevar precio.
    expect(find.text('Nuevo paso'), findsOneWidget);
    expect(find.text('Precio a mano'), findsOneWidget);
    expect(find.text('Del catálogo'), findsOneWidget);
    expect(find.text('Sin cobro'), findsOneWidget);

    // Del catálogo no se pregunta el nombre: lo pone el servicio elegido. Escribirlo otra vez
    // era escribir dos veces lo mismo.
    expect(find.widgetWithText(TextField, '¿Qué hay que hacer?'), findsNothing);

    // Con precio a mano sí, porque ahí el nombre es lo único que da el concepto.
    await tester.tap(find.text('Precio a mano'));
    await tester.pumpAndSettle();

    expect(find.widgetWithText(TextField, '¿Qué hay que hacer?'), findsOneWidget);
    expect(find.widgetWithText(TextField, 'Precio'), findsOneWidget);
  });

  testWidgets('los ajustes del taller se leen y se guardan desde el teléfono',
      (tester) async {
    final container = await conSesion();
    addTearDown(container.dispose);

    await montar(tester, container, const WorkshopSettingsScreen());

    // Lo que llegó del servidor, ya en el formulario.
    expect(find.text('Taller Local'), findsOneWidget);
    expect(find.text('IDENTIDAD DEL TALLER'), findsOneWidget);
    expect(find.text('CÓMO COBRA EL TALLER'), findsOneWidget);

    // El `scrollable` explícito: la pantalla tiene más de un desplazable y, sin decirle
    // cuál, `scrollUntilVisible` revienta con «too many elements».
    await tester.scrollUntilVisible(
      find.widgetWithText(FilledButton, 'Guardar'),
      150,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Guardar'));
    await tester.pump(const Duration(seconds: 5));
    await tester.pumpAndSettle();

    expect(find.text('Ajustes guardados.'), findsOneWidget);
  });
}

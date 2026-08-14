import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garaj_app/core/auth/token_store.dart';
import 'package:garaj_app/core/onboarding/onboarding_controller.dart';
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
    // Cada caso arranca sin sesión: si no, el router saltaría directo al home. Y con la
    // bienvenida ya vista, porque si no el router la interpondría antes del login y todos
    // los casos de abajo tendrían que atravesarla.
    await TokenStore().clear();
    await OnboardingStore().markSeen();
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

  /// Abre una pantalla del menú «Más» de la bandeja. Reportes, Clientes, Usuarios e
  /// Inventario dejaron de ser iconos sueltos: no cabían seis en la barra de un teléfono.
  Future<void> openFromMenu(WidgetTester tester, String option) async {
    await tester.tap(find.byTooltip('Más'));
    await tester.pumpAndSettle();

    await tester.tap(find.text(option));
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();
  }

  testWidgets('la primera instalación abre en la bienvenida', (tester) async {
    await OnboardingStore().reset();

    await tester.pumpWidget(const ProviderScope(child: GarajApp()));
    await tester.pumpAndSettle();

    expect(find.text('El trabajo, paso a paso'), findsOneWidget);
    expect(find.text('Ingresar'), findsNothing);

    // Tres pantallas: se avanza hasta la última y el botón cambia de texto.
    await tester.tap(find.text('Siguiente'));
    await tester.pumpAndSettle();
    expect(find.text('Con fotos de todo'), findsOneWidget);

    await tester.tap(find.text('Siguiente'));
    await tester.pumpAndSettle();
    expect(find.text('Cotizar y cobrar'), findsOneWidget);
    expect(find.text('Entrar'), findsOneWidget);

    await tester.tap(find.text('Entrar'));
    await tester.pumpAndSettle();
    expect(find.text('Ingresar'), findsOneWidget);

    // Y no vuelve a aparecer: es de primera instalación, no de cada arranque.
    await tester.pumpWidget(const ProviderScope(child: GarajApp()));
    await tester.pumpAndSettle();
    expect(find.text('El trabajo, paso a paso'), findsNothing);
    expect(find.text('Ingresar'), findsOneWidget);
  });

  testWidgets('«Saltar» también la da por vista', (tester) async {
    await OnboardingStore().reset();

    await tester.pumpWidget(const ProviderScope(child: GarajApp()));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Saltar'));
    await tester.pumpAndSettle();

    expect(find.text('Ingresar'), findsOneWidget);
    expect(await OnboardingStore().hasSeen(), isTrue);
  });

  testWidgets('el Dueño ve las órdenes de todo el taller', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    expect(find.text('Taller'), findsOneWidget);
    expect(find.text('Óscar Méndez · Taller Garaj'), findsOneWidget);
    // El seeder deja una orden abierta en cada sucursal.
    expect(find.textContaining('MTZ-'), findsOneWidget);
    expect(find.textContaining('SPS-'), findsOneWidget);
    // El resumen de ingresos es solo suyo; los otros perfiles no lo ven.
    expect(find.text('INGRESOS'), findsOneWidget);
    expect(find.text('Hoy'), findsOneWidget);
    // La campana está en las tres bandejas. El botón de alta también, pero al taller le
    // dice "Recibir vehículo": el Dueño registra el ingreso, no pide cita.
    expect(find.byTooltip('Avisos'), findsOneWidget);
    expect(find.text('Recibir vehículo'), findsOneWidget);
    expect(find.text('Pedir cita'), findsNothing);
  });

  testWidgets('la sesión guardada entra sin volver a pedir la contraseña', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    // Segundo arranque con el token ya en el llavero: es la app abierta a la mañana
    // siguiente. Faltaba esta prueba y por eso pasó desapercibido que la app validaba la
    // sesión sin mandar la cabecera Authorization, recibía 401 y devolvía al login.
    await tester.pumpWidget(const ProviderScope(child: GarajApp()));
    await tester.pump(const Duration(seconds: 6));
    await tester.pumpAndSettle();

    expect(find.text('Ingresar'), findsNothing);
    expect(find.text('Taller'), findsOneWidget);
    expect(find.text('Óscar Méndez · Taller Garaj'), findsOneWidget);
  });

  testWidgets('el Técnico ve solo lo suyo y puede abrir la orden', (tester) async {
    await signIn(tester, 'tecnico1@garaj.test');

    expect(find.text('Mis asignaciones'), findsOneWidget);
    expect(find.textContaining('MTZ-'), findsOneWidget);
    expect(find.text('INGRESOS'), findsNothing);
    expect(find.byTooltip('Avisos'), findsOneWidget);
    expect(find.text('Recibir vehículo'), findsOneWidget);
    expect(find.text('Pedir cita'), findsNothing);
    // La orden de la otra sucursal es de otro técnico: no debe aparecer.
    expect(find.textContaining('SPS-'), findsNothing);

    await tester.tap(find.textContaining('MTZ-').first);
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    expect(find.text('MOTIVO DE INGRESO'), findsOneWidget);
    expect(find.text('DIAGNÓSTICO'), findsOneWidget);
    expect(find.text('PASOS DE LA REPARACIÓN'), findsOneWidget);

    // El Técnico también manda el enlace de seguimiento: muchas veces es él quien entrega el
    // carro. La factura no: las ventas son del Dueño.
    expect(find.text('AVISAR AL CLIENTE'), findsOneWidget);
    expect(find.text('Mandar el enlace'), findsOneWidget);
    expect(find.text('Mandar la factura'), findsNothing);

    // Lo de más abajo hay que traerlo a la vista: el ListView no construye lo que no se ve.
    // Y hay que decirle por cuál desplazarse: el cuadro del diagnóstico también es
    // desplazable, así que el buscador encontraría dos y no sabría por cuál decidirse.
    final page = find.byType(Scrollable).first;

    await tester.scrollUntilVisible(find.text('FOTOS DEL PROCESO'), 300, scrollable: page);
    // Puede documentar y consumir: cámara, galería y catálogo.
    expect(find.text('Cargar repuesto'), findsOneWidget);
    expect(find.byTooltip('Tomar foto'), findsOneWidget);
    expect(find.byTooltip('Elegir de la galería'), findsOneWidget);

    await tester.scrollUntilVisible(find.text('LÍNEA DE TIEMPO'), 300, scrollable: page);
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
    expect(find.text('Cargar repuesto'), findsNothing);
    expect(find.text('Agregar paso'), findsNothing);
    // El enlace de seguimiento lo manda el taller: a él ya le llegó por WhatsApp.
    expect(find.text('AVISAR AL CLIENTE'), findsNothing);
    // Ve quién tiene su vehículo, que es lo que preguntaría por teléfono.
    expect(find.text('TÉCNICO RESPONSABLE'), findsOneWidget);

    final page = find.byType(Scrollable).first;
    await tester.scrollUntilVisible(find.text('FOTOS DEL PROCESO'), 300, scrollable: page);
    expect(find.byTooltip('Tomar foto'), findsNothing);

    await tester.scrollUntilVisible(find.text('LÍNEA DE TIEMPO'), 300, scrollable: page);
    expect(find.text('LÍNEA DE TIEMPO'), findsOneWidget);
    expect(find.text('CAMBIAR ESTADO'), findsNothing);
  });

  testWidgets('el Cliente pide una cita desde su teléfono', (tester) async {
    await signIn(tester, 'cliente@garaj.test');

    expect(find.text('Pedir cita'), findsOneWidget);
    await tester.tap(find.text('Pedir cita'));
    await tester.pumpAndSettle();

    // Los vehículos y las sucursales los trae la API: hay que darle tiempo de red.
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    expect(find.text('¿Qué necesita?'), findsOneWidget);
    expect(find.text('FOTOS'), findsOneWidget);
    expect(find.byTooltip('Tomar foto'), findsOneWidget);

    // Sin motivo no se envía: es el único dato que el taller no puede adivinar.
    await tester.tap(find.text('Enviar al taller'));
    await tester.pumpAndSettle();
    expect(find.text('Cuéntenos qué necesita.'), findsOneWidget);
  });

  testWidgets('el Dueño abre los reportes desde la bandeja', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await openFromMenu(tester, 'Reportes');

    expect(find.text('Reportes'), findsOneWidget);
    expect(find.text('TOTAL FACTURADO'), findsOneWidget);
    // Los filtros que solo existen aquí: rango, agrupación y el reparto por técnico.
    expect(find.text('30 días'), findsOneWidget);
    // El filtro por técnico es lo nuevo. El reparto de abajo depende de que haya ventas
    // en el rango, así que no se comprueba aquí: eso lo cubre el humo del backend.
    expect(find.text('Todos los técnicos'), findsOneWidget);

    // El detalle de lo que está por cobrar se mudó a su propia pantalla: aquí queda el
    // total, y ninguna factura suelta con su botón de abonar.
    expect(find.text('Abonar'), findsNothing);
  });

  testWidgets('el Dueño registra un abono desde Por cobrar', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await tester.tap(find.byTooltip('Por cobrar'));
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    expect(find.text('Por cobrar'), findsWidgets);
    // Lo que distingue a esta pantalla de un reporte: se busca y se filtra.
    expect(find.text('Cliente, teléfono o número'), findsOneWidget);
    expect(find.text('Vencidas'), findsOneWidget);
    expect(find.text('Por vencer'), findsOneWidget);

    // El humo de la Fase 8 deja facturas con saldo. Sin ellas no hay nada que cobrar y
    // este caso no tiene nada que comprobar.
    if (find.text('Abonar').evaluate().isEmpty) return;

    await tester.tap(find.text('Abonar').first);
    await tester.pumpAndSettle();

    expect(find.text('Abono'), findsOneWidget);
    expect(find.text('Registrar abono'), findsOneWidget);
    // Propone el saldo completo, que es lo que pasa la mayoría de las veces.
    expect(find.widgetWithText(TextField, 'Cuánto abona'), findsOneWidget);
  });

  testWidgets('Por cobrar filtra por vencimiento', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await tester.tap(find.byTooltip('Por cobrar'));
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Vencidas'));
    await tester.pump(const Duration(seconds: 3));
    await tester.pumpAndSettle();

    // O trae vencidas, o dice que con esos filtros no hay nada. Lo que no puede es
    // quedarse en blanco ni reventar.
    final hayResultados = find.text('Abonar').evaluate().isNotEmpty;
    expect(
      hayResultados || find.text('Nada con esos filtros.').evaluate().isNotEmpty,
      isTrue,
    );
  });

  testWidgets('el Dueño aprueba y asigna desde la bandeja', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await tester.tap(find.byTooltip('Requerimientos'));
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    // El seeder deja uno pendiente y otro ya convertido en orden.
    expect(find.text('Aprobar y asignar'), findsWidgets);

    // Aprobar abre la hoja para elegir técnico sin salir de la bandeja.
    await tester.tap(find.text('Aprobar y asignar').first);
    await tester.pumpAndSettle();

    expect(find.text('¿Quién lo atiende?'), findsOneWidget);
    expect(find.text('Asignar después'), findsOneWidget);

    await tester.tap(find.text('Cancelar'));
    await tester.pumpAndSettle();
    expect(find.text('¿Quién lo atiende?'), findsNothing);
  });

  testWidgets('el Técnico ve la bandeja pero no decide', (tester) async {
    await signIn(tester, 'tecnico1@garaj.test');

    await tester.tap(find.byTooltip('Requerimientos'));
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    expect(find.text('Requerimientos'), findsOneWidget);
    // Puede recibir vehículos, pero aprobar es del Dueño.
    expect(find.text('Recibir vehículo'), findsOneWidget);
    expect(find.text('Aprobar y asignar'), findsNothing);
  });

  testWidgets('el Dueño cotiza y cobra desde la orden', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await tester.tap(find.textContaining('MTZ-').first);
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    final page = find.byType(Scrollable).first;

    // La parte comercial de la orden, que hasta ahora solo estaba en el web.
    await tester.scrollUntilVisible(find.text('COTIZACIONES'), 300, scrollable: page);
    expect(find.text('COTIZACIONES'), findsOneWidget);

    // «Cerrar y facturar» mientras no se ha cobrado; «Factura» después. Se comprueba lo
    // común a los dos: qué de estos dos estados toca depende de por dónde vayan los humos.
    await tester.scrollUntilVisible(find.textContaining('FACTURA'), 300, scrollable: page);
    expect(find.textContaining('FACTURA'), findsWidgets);
  });

  testWidgets('el Dueño busca en el padrón de clientes', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await openFromMenu(tester, 'Clientes');

    expect(find.text('Clientes'), findsWidgets);
    expect(find.text('Nuevo cliente'), findsOneWidget);
    // El seeder deja tres clientes con sus vehículos.
    expect(find.textContaining('vehículo'), findsWidgets);
  });

  testWidgets('el Dueño ve la bodega y puede moverla', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await openFromMenu(tester, 'Inventario');

    expect(find.text('Inventario'), findsOneWidget);
    expect(find.text('Nuevo repuesto'), findsOneWidget);
    expect(find.text('Bajo mínimo'), findsOneWidget);

    // La ficha de una existencia ofrece los movimientos, que son cosa del Dueño.
    await tester.tap(find.byType(Card).first);
    await tester.pumpAndSettle();

    expect(find.text('Entrada por compra'), findsOneWidget);
    expect(find.text('Ajuste por conteo'), findsOneWidget);
    expect(find.text('Kardex'), findsOneWidget);
  });

  testWidgets('el Técnico ve existencias pero no las mueve', (tester) async {
    await signIn(tester, 'tecnico1@garaj.test');

    await openFromMenu(tester, 'Inventario');

    expect(find.text('Inventario'), findsOneWidget);
    // Ni alta de catálogo ni entradas: la API se lo negaría.
    expect(find.text('Nuevo repuesto'), findsNothing);

    await tester.tap(find.byType(Card).first);
    await tester.pumpAndSettle();

    expect(find.text('Entrada por compra'), findsNothing);
    expect(find.text('Kardex'), findsOneWidget);
  });

  testWidgets('la campana abre los avisos del usuario', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await tester.tap(find.byTooltip('Avisos'));
    await tester.pumpAndSettle();
    await tester.pump(const Duration(seconds: 3));
    await tester.pumpAndSettle();

    expect(find.text('Avisos'), findsWidgets);
    expect(find.text('Marcar todo'), findsOneWidget);
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

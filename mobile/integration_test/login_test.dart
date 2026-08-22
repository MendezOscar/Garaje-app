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
    // del login y al de la pantalla de inicio que carga después.
    await tester.pump(const Duration(seconds: 6));
    await tester.pumpAndSettle();
  }

  /// Cambia de destino en la barra de abajo.
  Future<void> goTo(WidgetTester tester, String destino) async {
    await tester.tap(find.text(destino).last);
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();
  }

  /// Abre una pantalla desde «Más», que es donde vive lo que no es de todos los días.
  /// Reportes, Clientes, Usuarios, Inventario y Por cobrar dejaron de ser iconos sueltos en
  /// la barra de arriba y entradas de un menú «⋯»: ahora son renglones agrupados.
  Future<void> openFromMore(WidgetTester tester, String option) async {
    await goTo(tester, 'Más');

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

  testWidgets('el Dueño entra a «Hoy», no a la bandeja', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    // Lo primero es cómo va el día, y arriba lo cobrado: lo facturado es otra cifra y no
    // es la pregunta de quien abre el teléfono a media tarde.
    expect(find.text('Hoy'), findsWidgets);
    expect(find.text('COBRADO HOY'), findsOneWidget);
    expect(find.text('FACTURADO'), findsOneWidget);
    expect(find.text('Taller Garaj'), findsWidgets);

    // Los cuatro destinos de la barra de abajo, que antes era una línea de texto con el
    // nombre del usuario.
    expect(find.text('Órdenes'), findsOneWidget);
    expect(find.text('Caja'), findsOneWidget);
    expect(find.text('Más'), findsOneWidget);

    // Recibir un vehículo es lo que más se hace en el mostrador: acción principal, no una
    // pantalla a dos saltos.
    expect(find.text('Recibir vehículo'), findsOneWidget);
    expect(find.text('Pedir una cita'), findsNothing);
    expect(find.byTooltip('Avisos'), findsOneWidget);

    // Las órdenes siguen estando, en su destino.
    await goTo(tester, 'Órdenes');
    expect(find.textContaining('MTZ-'), findsWidgets);
    // El seeder deja una orden abierta en cada sucursal.
    expect(find.textContaining('SPS-'), findsWidgets);
    // El resumen de ingresos ya no encabeza la bandeja: se mudó a Hoy.
    expect(find.text('INGRESOS'), findsNothing);
  });

  testWidgets('la caja del día es un destino, no una entrada de menú', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await goTo(tester, 'Caja');

    // Se abre todos los días al cerrar, con el efectivo en la mano.
    expect(find.text('Cierre de caja'), findsOneWidget);
    expect(find.byTooltip('Elegir el día'), findsOneWidget);
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
    expect(find.text('Hoy'), findsWidgets);
  });

  testWidgets('«Más» agrupa el taller y termina en la cuenta', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await goTo(tester, 'Más');

    expect(find.text('Requerimientos'), findsOneWidget);
    expect(find.text('Por cobrar'), findsOneWidget);
    expect(find.text('Reportes'), findsOneWidget);
    expect(find.text('Inventario'), findsOneWidget);
    expect(find.text('Usuarios'), findsOneWidget);

    // El grupo de la cuenta queda al final, a un desplazamiento: es donde el revisor de Apple
    // la busca, así que la prueba llega hasta él como llegaría él.
    await tester.scrollUntilVisible(
      find.text('Eliminar mi cuenta'),
      300,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.text('Salir'), findsOneWidget);
    expect(find.text('Eliminar mi cuenta'), findsOneWidget);

    await tester.tap(find.text('Eliminar mi cuenta'));
    await tester.pumpAndSettle();

    expect(find.text('¿Eliminar su cuenta?'), findsOneWidget);
    await tester.tap(find.text('Cancelar'));
    await tester.pumpAndSettle();
    expect(find.text('¿Eliminar su cuenta?'), findsNothing);
  });

  testWidgets('el Técnico entra a su cola de trabajo', (tester) async {
    await signIn(tester, 'tecnico1@garaj.test');

    expect(find.text('Mi trabajo'), findsWidgets);
    expect(find.textContaining('MTZ-'), findsWidgets);
    // La orden de la otra sucursal es de otro técnico: no debe aparecer.
    expect(find.textContaining('SPS-'), findsNothing);
    // Ni ingresos ni caja: el dinero del taller no es asunto suyo.
    expect(find.text('COBRADO HOY'), findsNothing);
    expect(find.text('Caja'), findsNothing);
    // Sus tres destinos.
    expect(find.text('Repuestos'), findsOneWidget);
    expect(find.text('Más'), findsOneWidget);
    expect(find.byTooltip('Buscar una orden'), findsOneWidget);
  });

  testWidgets('la orden entra por los pasos, no por una torre de secciones', (tester) async {
    await signIn(tester, 'tecnico1@garaj.test');

    await tester.tap(find.textContaining('MTZ-').first);
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    // Arriba, el trabajo: en qué va la orden y qué pasos faltan.
    expect(find.text('PASOS'), findsOneWidget);

    // Y lo demás son renglones que llevan a su pantalla, con el resumen a la vista. Antes
    // eran secciones desplegadas y los pasos quedaban a tres pantallas de desplazamiento.
    expect(find.text('Diagnóstico'), findsOneWidget);
    expect(find.text('Repuestos'), findsOneWidget);
    expect(find.text('Fotos'), findsOneWidget);

    // Lo que solo se consulta va al final, y hay que traerlo a la vista: el ListView no
    // construye lo que no se ve.
    await tester.scrollUntilVisible(
      find.text('Línea de tiempo'),
      300,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.text('Línea de tiempo'), findsOneWidget);

    // El diagnóstico ya no es un cuadro de texto siempre abierto en la pantalla principal.
    expect(find.text('Guardar diagnóstico'), findsNothing);
    // Ni el interruptor de cómo se cobra la mano de obra, que es un ajuste y está en el menú.
    expect(find.text('Catálogo'), findsNothing);

    // La acción del día está fija abajo, sin desplazar: marcar el paso que sigue o, si ya no
    // queda ninguno, mover el estado. Cuál de las dos depende de por dónde vaya la orden.
    final marcar = find.textContaining('Marcar «').evaluate().isNotEmpty;
    final pasar = find.textContaining('Pasar a ').evaluate().isNotEmpty;
    expect(marcar || pasar, isTrue);
  });

  testWidgets('el Técnico avisa al cliente y documenta desde la orden', (tester) async {
    await signIn(tester, 'tecnico1@garaj.test');

    await tester.tap(find.textContaining('MTZ-').first);
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    // El Técnico también manda el enlace de seguimiento: muchas veces es él quien entrega el
    // carro. La factura no: las ventas son del Dueño.
    await tester.tap(find.text('Avisar al cliente'));
    await tester.pumpAndSettle();

    expect(find.text('Mandar el enlace'), findsOneWidget);
    expect(find.text('Mandar la factura'), findsNothing);

    await tester.tapAt(const Offset(200, 60));
    await tester.pumpAndSettle();

    // Las fotos y los repuestos son su propia pantalla, con la cámara y el catálogo dentro.
    await tester.ensureVisible(find.text('Fotos'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Fotos'));
    await tester.pumpAndSettle();
    expect(find.byTooltip('Tomar foto'), findsOneWidget);
    expect(find.byTooltip('Elegir de la galería'), findsOneWidget);

    await tester.pageBack();
    await tester.pumpAndSettle();

    await tester.ensureVisible(find.text('Repuestos'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Repuestos'));
    await tester.pumpAndSettle();
    expect(find.text('Cargar repuesto'), findsOneWidget);
  });

  testWidgets('el Técnico puede detener el trabajo diciendo qué falta', (tester) async {
    await signIn(tester, 'tecnico1@garaj.test');

    await tester.tap(find.textContaining('MTZ-').first);
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    // El menú solo aparece si la orden admite transiciones; si no, no hay nada que probar.
    if (find.byTooltip('Más acciones').evaluate().isEmpty) return;

    await tester.tap(find.byTooltip('Más acciones'));
    await tester.pumpAndSettle();

    // Detener no está en el menú de toda orden: depende de su estado.
    if (find.text('Detener el trabajo').evaluate().isEmpty) return;

    await tester.tap(find.text('Detener el trabajo'));
    await tester.pumpAndSettle();

    // La hoja pregunta el motivo, que es lo que el mostrador necesita para comprar el
    // repuesto o para llamar al cliente. Antes solo se elegía un estado.
    expect(find.text('Detener el trabajo'), findsWidgets);
    expect(find.text('Qué falta'), findsOneWidget);
    expect(find.text('Detener y avisar'), findsOneWidget);

    // Se cierra sin detener nada: esta prueba no cambia el estado de la orden.
    await tester.tapAt(const Offset(200, 60));
    await tester.pumpAndSettle();
    expect(find.text('Detener y avisar'), findsNothing);
  });

  testWidgets('el Técnico ve existencias pero no las mueve', (tester) async {
    await signIn(tester, 'tecnico1@garaj.test');

    await goTo(tester, 'Repuestos');

    expect(find.text('Inventario'), findsWidgets);
    // Ni alta de catálogo ni entradas: la API se lo negaría.
    expect(find.text('Nuevo repuesto'), findsNothing);

    await tester.tap(find.byType(Card).first);
    await tester.pumpAndSettle();

    expect(find.text('Entrada por compra'), findsNothing);
    expect(find.text('Kardex'), findsOneWidget);
  });

  testWidgets('el Cliente entra a su vehículo, no a una bandeja', (tester) async {
    await signIn(tester, 'cliente@garaj.test');

    expect(find.text('Mi vehículo'), findsWidgets);
    expect(find.text('Historial'), findsOneWidget);
    // Su vehículo se cuenta en palabras, no con los estados internos del taller.
    expect(find.text('Ver el avance'), findsWidgets);
    expect(find.text('Pedir una cita'), findsOneWidget);
    // Nada del taller: ni caja, ni ingresos, ni folios encabezando la pantalla.
    expect(find.text('COBRADO HOY'), findsNothing);
    expect(find.text('Recibir vehículo'), findsNothing);
  });

  testWidgets('el Cliente sigue su vehículo pero no cambia estados', (tester) async {
    await signIn(tester, 'cliente@garaj.test');

    await tester.tap(find.text('Ver el avance').first);
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    // Ve en qué va y quién tiene su vehículo, que es lo que preguntaría por teléfono.
    expect(find.text('PASOS'), findsOneWidget);
    expect(find.textContaining('técnico '), findsOneWidget);

    // Pero ninguna de las acciones del taller.
    expect(find.text('Agregar paso'), findsNothing);
    expect(find.text('Cargar repuesto'), findsNothing);
    // Ni el dinero: el total con ISV y el cierre son del Dueño.
    expect(find.text('TOTAL ESTIMADO'), findsNothing);
    expect(find.text('Cerrar y facturar'), findsNothing);
    // El enlace de seguimiento lo manda el taller: a él ya le llegó por WhatsApp.
    expect(find.text('Avisar al cliente'), findsNothing);
    // Y no tiene barra de acciones: no mueve estados ni marca pasos.
    expect(find.textContaining('Pasar a '), findsNothing);
    expect(find.textContaining('Marcar «'), findsNothing);

    // Las fotos son suyas de ver, no de tomar.
    await tester.ensureVisible(find.text('Fotos'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Fotos'));
    await tester.pumpAndSettle();
    expect(find.byTooltip('Tomar foto'), findsNothing);
    expect(find.text('FOTOS DEL PROCESO'), findsOneWidget);
  });

  testWidgets('el Cliente tiene el historial de su vehículo', (tester) async {
    await signIn(tester, 'cliente@garaj.test');

    await goTo(tester, 'Historial');

    // Sin importes: al Cliente el backend no le da las facturas, van por el enlace público.
    expect(find.text('VISITAS'), findsOneWidget);
    expect(find.text('DESDE'), findsOneWidget);
    expect(find.textContaining('MTZ-'), findsWidgets);
  });

  testWidgets('el Cliente pide una cita desde su teléfono', (tester) async {
    await signIn(tester, 'cliente@garaj.test');

    await tester.tap(find.text('Pedir una cita'));
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

  testWidgets('el Dueño abre los reportes desde Más', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await openFromMore(tester, 'Reportes');

    expect(find.text('Reportes'), findsWidgets);
    expect(find.text('TOTAL FACTURADO'), findsOneWidget);
    // Los filtros que solo existen aquí: rango, agrupación y el reparto por técnico.
    expect(find.text('30 días'), findsOneWidget);
    expect(find.text('Todos los técnicos'), findsOneWidget);

    // El detalle de lo que está por cobrar vive en su propia pantalla.
    expect(find.text('Abonar'), findsNothing);
  });

  testWidgets('el Dueño registra un abono desde Por cobrar', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await openFromMore(tester, 'Por cobrar');

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

  testWidgets('el Dueño aprueba y asigna desde Requerimientos', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await openFromMore(tester, 'Requerimientos');

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

  testWidgets('el Técnico ve los requerimientos pero no decide', (tester) async {
    await signIn(tester, 'tecnico1@garaj.test');

    await openFromMore(tester, 'Requerimientos');

    expect(find.text('Requerimientos'), findsWidgets);
    // Puede recibir vehículos, pero aprobar es del Dueño.
    expect(find.text('Recibir vehículo'), findsOneWidget);
    expect(find.text('Aprobar y asignar'), findsNothing);
  });

  testWidgets('el Dueño cotiza y cobra desde la orden', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await goTo(tester, 'Órdenes');

    await tester.tap(find.textContaining('MTZ-').first);
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    // Por cuánto va la orden, sin abrir nada y sin desplazar: antes había que bajar hasta la
    // tarjeta de cierre para enterarse.
    expect(find.text('TOTAL ESTIMADO'), findsOneWidget);

    // De dónde sale el precio de la mano de obra se ve y se cambia en la tarjeta de pasos,
    // que es donde se aplica.
    expect(find.textContaining('Se cobra '), findsOneWidget);
    expect(find.text('Cambiar'), findsOneWidget);

    // Y los renglones van en el orden en que el trabajo los pide: primero el diagnóstico,
    // después los repuestos.
    expect(
      tester.getTopLeft(find.text('Diagnóstico')).dy,
      lessThan(tester.getTopLeft(find.text('Repuestos')).dy),
    );

    // La parte comercial, que hasta ahora solo estaba en el web, cada una en su renglón. El
    // ListView no construye lo que no se ve, así que hay que traerlas a la vista.
    final page = find.byType(Scrollable).first;
    await tester.scrollUntilVisible(find.text('Cotizaciones'), 300, scrollable: page);

    await tester.tap(find.text('Cotizaciones'));
    await tester.pumpAndSettle();
    expect(find.text('COTIZACIONES'), findsOneWidget);

    // Las fotos del daño se suben desde el teléfono, que es donde está el vehículo. Solo si
    // la orden ya tiene alguna cotización: sin cotización no hay dónde adjuntarlas.
    if (find.textContaining('COT-').evaluate().isNotEmpty) {
      expect(find.text('FOTOS DEL DAÑO'), findsWidgets);
    }

    await tester.pageBack();
    await tester.pumpAndSettle();

    // «Cerrar y facturar» mientras no se ha cobrado; «Venta» después. Cuál toca depende de
    // por dónde vayan los humos, así que se prueba lo común a los dos.
    await tester.scrollUntilVisible(
      find.text('Línea de tiempo'),
      300,
      scrollable: find.byType(Scrollable).first,
    );
    final cobro = find.text('Cerrar y facturar').evaluate().isNotEmpty
        ? 'Cerrar y facturar'
        : 'Venta';

    await tester.ensureVisible(find.text(cobro));
    await tester.pumpAndSettle();
    await tester.tap(find.text(cobro));
    await tester.pumpAndSettle();
    expect(find.textContaining('FACTURA'), findsWidgets);
  });

  testWidgets('el Dueño busca en el padrón de clientes', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await openFromMore(tester, 'Clientes');

    expect(find.text('Clientes'), findsWidgets);
    expect(find.text('Nuevo cliente'), findsOneWidget);
    // El seeder deja tres clientes con sus vehículos.
    expect(find.textContaining('vehículo'), findsWidgets);
  });

  testWidgets('el Dueño ve la bodega y puede moverla', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await openFromMore(tester, 'Inventario');

    expect(find.text('Inventario'), findsWidgets);
    expect(find.text('Nuevo repuesto'), findsOneWidget);
    expect(find.text('Bajo mínimo'), findsOneWidget);

    // La ficha de una existencia ofrece los movimientos, que son cosa del Dueño.
    await tester.tap(find.byType(Card).first);
    await tester.pumpAndSettle();

    expect(find.text('Entrada por compra'), findsOneWidget);
    expect(find.text('Ajuste por conteo'), findsOneWidget);
    expect(find.text('Kardex'), findsOneWidget);
  });

  testWidgets('el Dueño vende un repuesto y la venta queda en el registro', (tester) async {
    await signIn(tester, 'owner@garaj.test');

    await openFromMore(tester, 'Ventas');

    expect(find.text('Ventas'), findsWidgets);
    expect(find.text('Vender repuesto'), findsOneWidget);
    // El registro es de lo facturado, así que trae también las de contado y las anuladas si
    // se piden: son filtros, no una lista de cobranza.
    expect(find.text('Con anuladas'), findsOneWidget);

    await tester.tap(find.text('Vender repuesto'));
    await tester.pump(const Duration(seconds: 3));
    await tester.pumpAndSettle();

    expect(find.text('Buscar el repuesto'), findsOneWidget);
    // Sin nada en la venta no se puede registrar: el botón está, apagado.
    expect(
      tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Registrar la venta'))
          .onPressed,
      isNull,
    );

    await tester.tap(find.text('Buscar el repuesto'));
    await tester.pumpAndSettle();

    // Dentro de la hoja, no en la pantalla de atrás: ahí también hay campos de texto y un
    // SwitchListTile —el de la factura con CAI— que se lleva el primer `ListTile` del árbol.
    final hoja = find.byType(BottomSheet);
    await tester.enterText(
      find.descendant(of: hoja, matching: find.byType(TextField)),
      'aceite',
    );
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    // El primer resultado de la bodega de esa sucursal, con su precio.
    await tester.tap(
      find.descendant(of: hoja, matching: find.byType(ListTile)).first,
    );
    await tester.pumpAndSettle();

    expect(find.text('Cantidad'), findsOneWidget);
    expect(find.text('Agregar otro'), findsOneWidget);
    // Sin CAI no se cobra ISV, y la pantalla lo dice antes de registrar.
    expect(
      find.textContaining('Sin ISV: solo la factura con CAI lo lleva'),
      findsOneWidget,
    );

    await tester.tap(find.widgetWithText(FilledButton, 'Registrar la venta'));
    await tester.pump(const Duration(seconds: 6));
    await tester.pumpAndSettle();

    // Queda la venta con su número y dicho que el repuesto ya salió de la bodega.
    expect(find.textContaining('Venta VTA-'), findsOneWidget);
    expect(
      find.text('Comprobante de entrega, sin CAI: no lleva ISV.'),
      findsOneWidget,
    );
    expect(find.text('Los repuestos ya salieron de la bodega.'), findsOneWidget);

    // Y se puede anular desde el registro, que es lo que salva un cobro mal digitado: la
    // prueba lo hace para no dejar una venta inventada en la caja del día.
    await tester.tap(find.text('Volver a Ventas'));
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();

    await tester.tap(find.byType(PopupMenuButton<String>).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Anular'));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.descendant(of: find.byType(AlertDialog), matching: find.byType(TextField)),
      'Prueba de integración',
    );
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Anular'));
    await tester.pump(const Duration(seconds: 5));
    await tester.pumpAndSettle();

    expect(find.textContaining('anulada'), findsWidgets);
  });

  testWidgets('el Técnico no entra al registro de ventas', (tester) async {
    await signIn(tester, 'tecnico1@garaj.test');

    await goTo(tester, 'Más');

    // Las ventas son del negocio: el técnico no las ve ni tiene por dónde llegar.
    expect(find.text('Ventas'), findsNothing);
    expect(find.text('Vender repuesto'), findsNothing);
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
    expect(find.text('Hoy'), findsNothing);
  });
}

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garaj_app/core/api/quote_repository.dart';
import 'package:garaj_app/core/api/work_order_repository.dart';
import 'package:garaj_app/core/auth/auth_controller.dart';
import 'package:garaj_app/core/auth/token_store.dart';
import 'package:garaj_app/core/theme/garaj_brand.dart';
import 'package:garaj_app/features/catalog/job_templates_screen.dart';
import 'package:garaj_app/features/catalog/labor_services_screen.dart';
import 'package:garaj_app/features/claims/claims_screen.dart';
import 'package:garaj_app/features/customers/customers_screen.dart';
import 'package:garaj_app/features/customer/quote_screen.dart';
import 'package:garaj_app/features/expenses/expenses_screen.dart';
import 'package:garaj_app/features/inventory/inventory_screen.dart';
import 'package:garaj_app/features/login/login_screen.dart';
import 'package:garaj_app/features/notifications/notifications_screen.dart';
import 'package:garaj_app/features/receivables/receivables_screen.dart';
import 'package:garaj_app/features/reminders/service_reminders_screen.dart';
import 'package:garaj_app/features/reports/reports_screen.dart';
import 'package:garaj_app/features/sales/counter_sale_screen.dart';
import 'package:garaj_app/features/sales/sales_screen.dart';
import 'package:garaj_app/features/service_requests/new_service_request_screen.dart';
import 'package:garaj_app/features/service_requests/service_requests_screen.dart';
import 'package:garaj_app/features/shell/customer_shell.dart';
import 'package:garaj_app/features/shell/owner_shell.dart';
import 'package:garaj_app/features/shell/technician_shell.dart';
import 'package:garaj_app/features/users/users_screen.dart';
import 'package:garaj_app/features/work_orders/work_order_detail_screen.dart';
import 'package:garaj_app/features/workshop/workshop_settings_screen.dart';
import 'package:integration_test/integration_test.dart';

// Las capturas del manual, sacadas de la aplicación de verdad contra la API local con el
// taller de demostración sembrado. No son dibujos: si una pantalla cambia, se vuelve a correr
// esto y el manual queda al día.
//
// La prueba no afirma nada: deja cada pantalla quieta en el simulador y escribe en la salida
// «CAPTURA:<nombre>». Quien la dispara (tools/capturar-manual.sh) ve esa línea y saca la foto
// con `xcrun simctl io … screenshot`. Flutter no sabe tomarle una foto al simulador de iOS.
//
//   flutter test integration_test/capturas_manual_test.dart -d <simulador>
//     --dart-define=API_URL=http://localhost:5199
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  Future<ProviderContainer> sesion(String correo) async {
    await TokenStore().clear();
    final container = ProviderContainer();
    await container.read(authControllerProvider.notifier).login(correo, 'Garaj123!');
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
        child: MaterialApp(
          theme: garajTheme,
          debugShowCheckedModeBanner: false,
          home: pantalla,
        ),
      ),
    );
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();
  }

  /// Deja la pantalla quieta el tiempo que necesita el guion de afuera para fotografiarla.
  Future<void> foto(WidgetTester tester, String nombre) async {
    debugPrint('CAPTURA:$nombre');
    await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 2600)));
    await tester.pump();
  }

  testWidgets('las pantallas del dueño', (tester) async {
    final c = await sesion('dueno@tallerdemo.hn');
    addTearDown(c.dispose);

    // El armazón, para que se vea la barra de abajo con sus cuatro destinos.
    await montar(tester, c, const OwnerShell());
    await foto(tester, 'hoy');

    for (final destino in ['Órdenes', 'Caja', 'Más']) {
      await tester.tap(find.text(destino).last);
      await tester.pump(const Duration(seconds: 3));
      await tester.pumpAndSettle();
      await foto(tester, {'Órdenes': 'ordenes', 'Caja': 'caja', 'Más': 'mas'}[destino]!);
    }

    // El menú «Más» es largo: la segunda mitad merece su propia foto.
    await tester.drag(find.byType(ListView).first, const Offset(0, -420));
    await tester.pumpAndSettle();
    await foto(tester, 'mas-abajo');

    final pantallas = <String, Widget>{
      'requerimientos': const ServiceRequestsScreen(),
      'recordatorios': const ServiceRemindersScreen(),
      'reclamos': const ClaimsScreen(),
      'por-cobrar': const ReceivablesScreen(),
      'ventas': const SalesScreen(),
      'reportes': const ReportsScreen(),
      'resultados': const ExpensesScreen(),
      'clientes': const CustomersScreen(),
      'mano-de-obra': const LaborServicesScreen(),
      'trabajos-frecuentes': const JobTemplatesScreen(),
      'inventario': const InventoryScreen(),
      'usuarios': const UsersScreen(),
      'ajustes-taller': const WorkshopSettingsScreen(),
      'avisos': const NotificationsScreen(),
      'mostrador': const CounterSaleScreen(),
      'recibir-vehiculo': const NewServiceRequestScreen(),
    };

    for (final entrada in pantallas.entries) {
      await montar(tester, c, entrada.value);
      await foto(tester, entrada.key);
    }

    // El detalle de una orden abierta, que es donde se trabaja.
    final abiertas = await c.read(myWorkOrdersProvider.future);
    expect(abiertas, isNotEmpty, reason: 'la demostración debería dejar órdenes abiertas');
    await montar(tester, c, WorkOrderDetailScreen(id: abiertas.first.id));
    await foto(tester, 'orden-detalle');

    await tester.drag(find.byType(ListView).first, const Offset(0, -500));
    await tester.pumpAndSettle();
    await foto(tester, 'orden-detalle-abajo');

    await tester.drag(find.byType(ListView).first, const Offset(0, -600));
    await tester.pumpAndSettle();
    await foto(tester, 'orden-detalle-final');
  });

  testWidgets('las pantallas del técnico', (tester) async {
    final c = await sesion('tecnico1@tallerdemo.hn');
    addTearDown(c.dispose);

    await montar(tester, c, const TechnicianShell());
    await foto(tester, 'tecnico-mi-trabajo');

    await tester.tap(find.text('Repuestos').last);
    await tester.pump(const Duration(seconds: 3));
    await tester.pumpAndSettle();
    await foto(tester, 'tecnico-repuestos');
  });

  testWidgets('las pantallas del cliente', (tester) async {
    final c = await sesion('cliente1@tallerdemo.hn');
    addTearDown(c.dispose);

    await montar(tester, c, const CustomerShell());
    await foto(tester, 'cliente-mi-vehiculo');

    await tester.tap(find.text('Historial').last);
    await tester.pump(const Duration(seconds: 3));
    await tester.pumpAndSettle();
    await foto(tester, 'cliente-historial');

    final cotizaciones = await c.read(myQuotesProvider.future);
    if (cotizaciones.isNotEmpty) {
      await montar(tester, c, QuoteScreen(id: cotizaciones.first.id));
      await foto(tester, 'cliente-cotizacion');
    }
  });

  testWidgets('la entrada', (tester) async {
    await TokenStore().clear();
    await montar(tester, ProviderContainer(), const LoginScreen());
    await foto(tester, 'login');
  });
}

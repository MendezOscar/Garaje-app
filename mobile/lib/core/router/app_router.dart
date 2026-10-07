import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/claims/claims_screen.dart';
import '../../features/catalog/job_templates_screen.dart';
import '../../features/catalog/labor_services_screen.dart';
import '../../features/customers/customers_screen.dart';
import '../../features/expenses/expenses_screen.dart';
import '../../features/inventory/inventory_screen.dart';
import '../../features/login/login_screen.dart';
import '../../features/notifications/notifications_screen.dart';
import '../../features/onboarding/onboarding_screen.dart';
import '../../features/reminders/service_reminders_screen.dart';
import '../../features/reports/cash_close_screen.dart';
import '../../features/reports/reports_screen.dart';
import '../../features/receivables/receivables_screen.dart';
import '../../features/sales/counter_sale_screen.dart';
import '../../features/sales/sales_screen.dart';
import '../../features/users/users_screen.dart';
import '../../features/service_requests/new_service_request_screen.dart';
import '../../features/service_requests/service_requests_screen.dart';
import '../../features/customer/quote_screen.dart';
import '../../features/shell/customer_shell.dart';
import '../../features/shell/owner_shell.dart';
import '../../features/shell/technician_shell.dart';
import '../../features/splash/splash_screen.dart';
import '../../features/work_orders/vehicle_history_lookup_screen.dart';
import '../../features/work_orders/work_order_detail_screen.dart';
import '../../features/work_orders/work_order_list_screen.dart';
import '../../features/workshop/workshop_settings_screen.dart';
import '../auth/auth_controller.dart';
import '../models/current_user.dart';
import '../onboarding/onboarding_controller.dart';

final appRouterProvider = Provider<GoRouter>((ref) {
  // Puente entre Riverpod y go_router: al cambiar el estado de sesión —o al terminar la
  // bienvenida— se reevalúa el redirect, así la navegación ocurre sola sin que las
  // pantallas la empujen.
  final notifier = _RouterNotifier(ref);
  ref.onDispose(notifier.dispose);

  return GoRouter(
    initialLocation: '/',
    refreshListenable: notifier,
    routes: garajRoutes(),
    redirect: (context, state) {
      final auth = ref.read(authControllerProvider);
      final seenWelcome = ref.read(onboardingProvider);
      final location = state.matchedLocation;

      // Mientras no se sepa si hay sesión guardada o si ya se vio la bienvenida, se espera
      // en el splash. Adivinar significaría enseñar la bienvenida un fotograma a quien ya
      // la vio, o el login a quien tenía sesión.
      if (auth is AuthLoading || seenWelcome == null) {
        return location == '/' ? null : '/';
      }

      // Sin respuesta del servidor no se sabe si la sesión sirve. Se espera en el splash, que
      // ofrece reintentar, en lugar de mandar al login a quien la tiene buena.
      if (auth is AuthUnreachable) return location == '/' ? null : '/';

      // La bienvenida solo estorba a quien ya entró: si hay sesión, se da por vista.
      if (!seenWelcome && auth is AuthSignedOut) {
        return location == '/bienvenida' ? null : '/bienvenida';
      }

      if (auth is AuthSignedOut) return location == '/login' ? null : '/login';

      // El detalle es accesible desde cualquier perfil: el backend decide si el usuario
      // puede verla y devuelve 404 si no le corresponde. Los avisos y la petición de cita
      // están por encima del perfil: cada uno ve lo suyo dentro de la pantalla.
      if (location.startsWith('/ordenes') ||
          location.startsWith('/presupuesto/') ||
          location == '/avisos' ||
          location == '/nueva-cita') {
        return null;
      }

      // Los reportes, lo que está por cobrar, los usuarios y el padrón de clientes son del Dueño: la API responde
      // 403 a los demás, pero rebotarlos aquí evita enseñarles una pantalla que solo puede
      // fallar. Toda ruta que no esté en esta lista termina en el inicio del perfil.
      if (rutasDelDueno.contains(location)) {
        return (auth as AuthSignedIn).user.role == AppRole.owner
            ? null
            : homeRouteFor(auth.user.role);
      }

      if (rutasDelTaller.contains(location)) {
        return (auth as AuthSignedIn).user.role == AppRole.customer
            ? homeRouteFor(auth.user.role)
            : null;
      }

      final home = homeRouteFor((auth as AuthSignedIn).user.role);
      return location == home ? null : home;
    },
  );
});

/// Lo que solo abre el Dueño. La API responde 403 a los demás, pero rebotarlos aquí evita
/// enseñarles una pantalla que solo puede fallar.
///
/// **Toda ruta que no esté en estas dos listas termina en el inicio del perfil**, y sin decir
/// por qué: es lo que le pasó a «Historial» cuando se agregó al menú y no aquí. La prueba de
/// `test/router_test.dart` compara el menú contra estas listas justamente por eso.
const rutasDelDueno = {
  '/reportes',
  '/caja',
  '/recordatorios',
  '/reclamos',
  '/resultados',
  '/mano-de-obra',
  '/trabajos-frecuentes',
  '/por-cobrar',
  '/ventas',
  '/mostrador',
  '/usuarios',
  '/ajustes',
  '/clientes',
};

/// Lo del taller: el Dueño y el Técnico, el Cliente no.
///
/// El inventario lo necesita el Técnico para saber si hay existencia antes de prometer una
/// reparación —los movimientos se los niega el backend—, los requerimientos son la bandeja
/// del taller, y el historial es lo que se consulta en el mostrador cuando el cliente vuelve.
const rutasDelTaller = {
  '/inventario',
  '/requerimientos',
  '/historial',
};

/// El árbol de rutas, aparte del router.
///
/// Fuera del provider para que se pueda leer sin sesión ni almacenamiento seguro: así una
/// prueba comprueba que ninguna ruta se repite, que es una falla que no se ve —go_router se
/// queda con la primera y la entrada del menú abre otra pantalla sin decir nada—.
List<RouteBase> garajRoutes() => [
      GoRoute(path: '/', builder: (_, __) => const SplashScreen()),
      GoRoute(path: '/login', builder: (_, __) => const LoginScreen()),
      GoRoute(path: '/bienvenida', builder: (_, __) => const OnboardingScreen()),
      // El Dueño entra al armazón de cuatro destinos —Hoy, Órdenes, Caja, Más— y no a la
      // bandeja pelada: lo primero que quiere saber al abrir la app es cómo va el día.
      GoRoute(path: '/taller', builder: (_, __) => const OwnerShell()),
      // El Técnico entra a su cola de trabajo, no a una bandeja de taller; el Cliente, a su
      // vehículo. Los dos con su barra de abajo: la bandeja pelada era la misma para los tres.
      GoRoute(path: '/mis-asignaciones', builder: (_, __) => const TechnicianShell()),
      GoRoute(path: '/mis-vehiculos', builder: (_, __) => const CustomerShell()),
      // La bandeja con buscador sigue existiendo, pero como destino de la lupa: es donde se
      // busca una orden vieja, con el vehículo ya entregado.
      GoRoute(
        path: '/ordenes',
        builder: (_, __) => const WorkOrderListScreen(
          title: 'Buscar una orden',
          emptyMessage: 'No hay órdenes abiertas.',
        ),
      ),
      GoRoute(
        path: '/ordenes/:id',
        builder: (_, state) => WorkOrderDetailScreen(id: state.pathParameters['id']!),
      ),
      GoRoute(path: '/avisos', builder: (_, __) => const NotificationsScreen()),
      GoRoute(
        path: '/presupuesto/:id',
        builder: (_, state) => QuoteScreen(id: state.pathParameters['id']!),
      ),
      GoRoute(path: '/nueva-cita', builder: (_, __) => const NewServiceRequestScreen()),
      GoRoute(path: '/reportes', builder: (_, __) => const ReportsScreen()),
      GoRoute(path: '/caja', builder: (_, __) => const CashCloseScreen()),
      GoRoute(path: '/recordatorios', builder: (_, __) => const ServiceRemindersScreen()),
      GoRoute(path: '/reclamos', builder: (_, __) => const ClaimsScreen()),
      GoRoute(path: '/resultados', builder: (_, __) => const ExpensesScreen()),
      GoRoute(path: '/mano-de-obra', builder: (_, __) => const LaborServicesScreen()),
      GoRoute(path: '/trabajos-frecuentes', builder: (_, __) => const JobTemplatesScreen()),
      GoRoute(path: '/por-cobrar', builder: (_, __) => const ReceivablesScreen()),
      // El registro de ventas y la venta de mostrador. Pasa con el cliente enfrente, que es
      // donde está el teléfono y no la computadora.
      GoRoute(path: '/ventas', builder: (_, __) => const SalesScreen()),
      GoRoute(path: '/mostrador', builder: (_, __) => const CounterSaleScreen()),
      GoRoute(path: '/usuarios', builder: (_, __) => const UsersScreen()),

      // Los ajustes del taller: el ISV, la garantía, el bodegaje y si el técnico ve precios.
      // Son los que el Dueño cambia con el taller abierto, y el teléfono es lo que trae
      // encima.
      //
      // La ruta es `/ajustes` y no `/taller` porque `/taller` ya es el inicio del Dueño: con
      // las dos iguales, go_router se quedaba con la primera y la entrada del menú llevaba a
      // la pantalla de inicio.
      GoRoute(path: '/ajustes', builder: (_, __) => const WorkshopSettingsScreen()),
      GoRoute(path: '/clientes', builder: (_, __) => const CustomersScreen()),
      GoRoute(
        path: '/historial',
        builder: (_, __) => const VehicleHistoryLookupScreen(),
      ),
      GoRoute(path: '/inventario', builder: (_, __) => const InventoryScreen()),
      GoRoute(path: '/requerimientos', builder: (_, __) => const ServiceRequestsScreen()),
    ];

String homeRouteFor(AppRole role) => switch (role) {
      AppRole.owner => '/taller',
      AppRole.technician => '/mis-asignaciones',
      AppRole.customer => '/mis-vehiculos',
    };

class _RouterNotifier extends ChangeNotifier {
  _RouterNotifier(Ref ref) {
    _subscriptions = [
      ref.listen<AuthState>(authControllerProvider, (_, __) => notifyListeners()),
      ref.listen<bool?>(onboardingProvider, (_, __) => notifyListeners()),
    ];
  }

  late final List<ProviderSubscription<Object?>> _subscriptions;

  @override
  void dispose() {
    for (final subscription in _subscriptions) {
      subscription.close();
    }
    super.dispose();
  }
}

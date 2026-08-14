import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/customers/customers_screen.dart';
import '../../features/inventory/inventory_screen.dart';
import '../../features/login/login_screen.dart';
import '../../features/notifications/notifications_screen.dart';
import '../../features/onboarding/onboarding_screen.dart';
import '../../features/reports/reports_screen.dart';
import '../../features/receivables/receivables_screen.dart';
import '../../features/users/users_screen.dart';
import '../../features/service_requests/new_service_request_screen.dart';
import '../../features/service_requests/service_requests_screen.dart';
import '../../features/splash/splash_screen.dart';
import '../../features/work_orders/work_order_detail_screen.dart';
import '../../features/work_orders/work_order_list_screen.dart';
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
    routes: [
      GoRoute(path: '/', builder: (_, __) => const SplashScreen()),
      GoRoute(path: '/login', builder: (_, __) => const LoginScreen()),
      GoRoute(path: '/bienvenida', builder: (_, __) => const OnboardingScreen()),
      GoRoute(
        path: '/taller',
        builder: (_, __) => const WorkOrderListScreen(
          title: 'Taller',
          emptyMessage: 'No hay órdenes abiertas en el taller.',
        ),
      ),
      GoRoute(
        path: '/mis-asignaciones',
        builder: (_, __) => const WorkOrderListScreen(
          title: 'Mis asignaciones',
          emptyMessage: 'No tienes órdenes asignadas ahora mismo.',
        ),
      ),
      GoRoute(
        path: '/mis-vehiculos',
        builder: (_, __) => const WorkOrderListScreen(
          title: 'Mis vehículos',
          emptyMessage: 'No tienes vehículos en el taller.',
        ),
      ),
      GoRoute(
        path: '/ordenes/:id',
        builder: (_, state) => WorkOrderDetailScreen(id: state.pathParameters['id']!),
      ),
      GoRoute(path: '/avisos', builder: (_, __) => const NotificationsScreen()),
      GoRoute(path: '/nueva-cita', builder: (_, __) => const NewServiceRequestScreen()),
      GoRoute(path: '/reportes', builder: (_, __) => const ReportsScreen()),
      GoRoute(path: '/por-cobrar', builder: (_, __) => const ReceivablesScreen()),
      GoRoute(path: '/usuarios', builder: (_, __) => const UsersScreen()),
      GoRoute(path: '/clientes', builder: (_, __) => const CustomersScreen()),
      GoRoute(path: '/inventario', builder: (_, __) => const InventoryScreen()),
      GoRoute(path: '/requerimientos', builder: (_, __) => const ServiceRequestsScreen()),
    ],
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

      // La bienvenida solo estorba a quien ya entró: si hay sesión, se da por vista.
      if (!seenWelcome && auth is AuthSignedOut) {
        return location == '/bienvenida' ? null : '/bienvenida';
      }

      if (auth is AuthSignedOut) return location == '/login' ? null : '/login';

      // El detalle es accesible desde cualquier perfil: el backend decide si el usuario
      // puede verla y devuelve 404 si no le corresponde. Los avisos y la petición de cita
      // están por encima del perfil: cada uno ve lo suyo dentro de la pantalla.
      if (location.startsWith('/ordenes/') ||
          location == '/avisos' ||
          location == '/nueva-cita') {
        return null;
      }

      // Los reportes, lo que está por cobrar, los usuarios y el padrón de clientes son del Dueño: la API responde
      // 403 a los demás, pero rebotarlos aquí evita enseñarles una pantalla que solo puede
      // fallar. Toda ruta que no esté en esta lista termina en el inicio del perfil.
      if (location == '/reportes' ||
          location == '/por-cobrar' ||
          location == '/usuarios' ||
          location == '/clientes') {
        return (auth as AuthSignedIn).user.role == AppRole.owner
            ? null
            : homeRouteFor(auth.user.role);
      }

      // El inventario también lo ve el Técnico: necesita saber si hay existencia antes de
      // prometer una reparación. Los movimientos se los niega el backend.
      if (location == '/inventario') {
        return (auth as AuthSignedIn).user.role == AppRole.customer
            ? homeRouteFor(auth.user.role)
            : null;
      }

      // La bandeja de requerimientos es del taller. Al Cliente no le corresponde: los suyos
      // los ve dentro de sus vehículos.
      if (location == '/requerimientos') {
        return (auth as AuthSignedIn).user.role == AppRole.customer
            ? homeRouteFor(auth.user.role)
            : null;
      }

      final home = homeRouteFor((auth as AuthSignedIn).user.role);
      return location == home ? null : home;
    },
  );
});

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

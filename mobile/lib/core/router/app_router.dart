import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/login/login_screen.dart';
import '../../features/notifications/notifications_screen.dart';
import '../../features/service_requests/new_service_request_screen.dart';
import '../../features/splash/splash_screen.dart';
import '../../features/work_orders/work_order_detail_screen.dart';
import '../../features/work_orders/work_order_list_screen.dart';
import '../auth/auth_controller.dart';
import '../models/current_user.dart';

final appRouterProvider = Provider<GoRouter>((ref) {
  // Puente entre Riverpod y go_router: al cambiar el estado de sesión se reevalúa el
  // redirect, así el login y el logout navegan solos sin que las pantallas lo hagan.
  final notifier = _AuthRouterNotifier(ref);
  ref.onDispose(notifier.dispose);

  return GoRouter(
    initialLocation: '/',
    refreshListenable: notifier,
    routes: [
      GoRoute(path: '/', builder: (_, __) => const SplashScreen()),
      GoRoute(path: '/login', builder: (_, __) => const LoginScreen()),
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
    ],
    redirect: (context, state) {
      final auth = ref.read(authControllerProvider);
      final location = state.matchedLocation;

      if (auth is AuthLoading) return location == '/' ? null : '/';
      if (auth is AuthSignedOut) return location == '/login' ? null : '/login';

      // El detalle es accesible desde cualquier perfil: el backend decide si el usuario
      // puede verla y devuelve 404 si no le corresponde. Los avisos y la petición de cita
      // están por encima del perfil: cada uno ve lo suyo dentro de la pantalla.
      if (location.startsWith('/ordenes/') ||
          location == '/avisos' ||
          location == '/nueva-cita') {
        return null;
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

class _AuthRouterNotifier extends ChangeNotifier {
  _AuthRouterNotifier(Ref ref) {
    _subscription = ref.listen<AuthState>(
      authControllerProvider,
      (_, __) => notifyListeners(),
    );
  }

  late final ProviderSubscription<AuthState> _subscription;

  @override
  void dispose() {
    _subscription.close();
    super.dispose();
  }
}

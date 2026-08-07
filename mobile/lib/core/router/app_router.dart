import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/customer/customer_home_screen.dart';
import '../../features/login/login_screen.dart';
import '../../features/owner/owner_home_screen.dart';
import '../../features/splash/splash_screen.dart';
import '../../features/technician/technician_home_screen.dart';
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
      GoRoute(path: '/taller', builder: (_, __) => const OwnerHomeScreen()),
      GoRoute(path: '/mis-asignaciones', builder: (_, __) => const TechnicianHomeScreen()),
      GoRoute(path: '/mis-vehiculos', builder: (_, __) => const CustomerHomeScreen()),
    ],
    redirect: (context, state) {
      final auth = ref.read(authControllerProvider);
      final location = state.matchedLocation;

      if (auth is AuthLoading) return location == '/' ? null : '/';
      if (auth is AuthSignedOut) return location == '/login' ? null : '/login';

      final home = homeRouteFor((auth as AuthSignedIn).user.role);

      // Un técnico que llegue a la ruta del dueño (deep link, push) cae en la suya.
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

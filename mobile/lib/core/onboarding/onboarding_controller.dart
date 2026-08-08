import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Recuerda si ya se vio el recorrido de bienvenida.
///
/// Va en las preferencias del sistema y no en el almacén seguro: no es un secreto, y debe
/// desaparecer al desinstalar la aplicación —quien la vuelve a instalar en un teléfono nuevo
/// merece la bienvenida otra vez—.
///
/// La clave lleva versión. El día que el recorrido cambie de verdad, subirla lo vuelve a
/// mostrar sin tener que pedirle a nadie que borre datos.
class OnboardingStore {
  static const _key = 'onboarding_visto_v1';

  Future<bool> hasSeen() async =>
      (await SharedPreferences.getInstance()).getBool(_key) ?? false;

  Future<void> markSeen() async =>
      (await SharedPreferences.getInstance()).setBool(_key, true);

  /// Para pruebas y para el «volver a ver» de ajustes.
  Future<void> reset() async =>
      (await SharedPreferences.getInstance()).remove(_key);
}

/// `null` mientras se lee el disco: el router lo usa para quedarse en el splash en vez de
/// enseñar la bienvenida por un fotograma a quien ya la vio.
final onboardingProvider =
    NotifierProvider<OnboardingController, bool?>(OnboardingController.new);

class OnboardingController extends Notifier<bool?> {
  final _store = OnboardingStore();

  @override
  bool? build() {
    _load();
    return null;
  }

  Future<void> _load() async {
    state = await _store.hasSeen();
  }

  Future<void> complete() async {
    await _store.markSeen();
    state = true;
  }
}

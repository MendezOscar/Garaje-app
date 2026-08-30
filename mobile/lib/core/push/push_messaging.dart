import 'dart:async';
import 'dart:io';

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../api/notification_repository.dart';
import '../router/app_router.dart';

/// Registro del aparato en FCM y qué hacer cuando llega un aviso.
///
/// Todo esto es opcional a propósito. El aviso **siempre** queda guardado en la API y se ve
/// en la campana; el push solo hace que el teléfono suene sin abrir la app. Por eso, si no
/// hay proyecto de Firebase configurado —faltan `google-services.json` o
/// `GoogleService-Info.plist`—, la inicialización falla, se anota y la app sigue funcionando
/// completa. Ver [docs/push.md](../../../../docs/push.md).
class PushMessaging {
  PushMessaging(this._ref);

  final Ref _ref;

  /// En las preferencias del sistema, no en el almacén seguro: no es un secreto y debe irse
  /// al desinstalar, como la marca de la bienvenida.
  static const _yaSePregunto = 'avisos_preguntado_v1';

  bool _firebaseReady = false;
  String? _token;
  final _subscriptions = <StreamSubscription<dynamic>>[];

  /// Arranca al entrar a la sesión: pide permiso, registra el aparato y se queda escuchando.
  ///
  /// Nada de aquí puede tumbar la entrada a la app, así que todo va dentro de un try: en el
  /// simulador de iOS, por ejemplo, `getToken` falla porque no hay APNs, y eso no es motivo
  /// para dejar a nadie fuera.
  Future<void> start() async {
    try {
      await _start();
    } catch (error) {
      debugPrint('Push no disponible: $error');
    }
  }

  Future<void> _start() async {
    if (!await _initializeFirebase()) return;

    final messaging = FirebaseMessaging.instance;

    if (!await _pedirPermisoSiHaceFalta(messaging)) return;

    // Sin esto, en iOS un aviso que llega con la app abierta no se ve por ninguna parte.
    await messaging.setForegroundNotificationPresentationOptions(
      alert: true,
      badge: true,
      sound: true,
    );

    await _register(await messaging.getToken());

    // El token se reenvía en cada arranque —FCM lo rota por su cuenta y el viejo deja de
    // entregar sin avisar— y también si rota con la app abierta.
    _subscriptions.add(messaging.onTokenRefresh.listen(_register));

    // Con la app abierta el sistema no la interrumpe: lo que corresponde es que el globo de
    // la campana se ponga al día solo.
    _subscriptions.add(FirebaseMessaging.onMessage.listen((_) => _refreshBell()));

    // Tocar el aviso lleva a lo que el aviso trata.
    _subscriptions.add(FirebaseMessaging.onMessageOpenedApp.listen(_openFrom));

    // Y si la app estaba cerrada, el mensaje que la abrió llega por aquí.
    final initial = await messaging.getInitialMessage();
    if (initial != null) _openFrom(initial);
  }

  /// Al salir. Se da de baja el aparato **antes** de tirar la sesión: en un taller el
  /// teléfono se comparte, y si no, el siguiente en entrar recibiría los avisos del anterior.
  Future<void> stop() async {
    for (final subscription in _subscriptions) {
      await subscription.cancel();
    }
    _subscriptions.clear();

    final token = _token;
    _token = null;
    if (token == null) return;

    try {
      await _ref.read(notificationRepositoryProvider).unregisterDevice(token);
    } catch (error) {
      // Cerrar sesión no puede fallar porque el aparato no se pudo dar de baja.
      debugPrint('No se pudo dar de baja el aparato: $error');
    }
  }

  /// ¿Deja el sistema que el teléfono suene? La campana funciona igual, pero Avisos lo dice.
  Future<bool> avisosDelSistemaActivos() async {
    if (!await _initializeFirebase()) return false;

    try {
      return _concedido(await FirebaseMessaging.instance.getNotificationSettings());
    } catch (error) {
      debugPrint('No se pudo leer el permiso de avisos: $error');
      return false;
    }
  }

  /// Volver a pedirlo desde Avisos, para quien dijo «ahora no» la primera vez.
  Future<bool> pedirPermisoOtraVez() async {
    if (!await _initializeFirebase()) return false;

    final messaging = FirebaseMessaging.instance;
    if (!_concedido(await messaging.requestPermission())) return false;

    await _register(await messaging.getToken());
    return true;
  }

  /// El diálogo del sistema se pide **después** de explicar para qué sirve, no encima de la
  /// pantalla de inicio nada más entrar: quien no entendió qué gana, dice que no, y en Android
  /// el sistema no vuelve a preguntar nunca más.
  ///
  /// Quien dice «ahora no» no vuelve a ver esto en cada arranque —para eso está la marca en
  /// las preferencias—; lo reactiva desde Avisos cuando quiera.
  Future<bool> _pedirPermisoSiHaceFalta(FirebaseMessaging messaging) async {
    if (_concedido(await messaging.getNotificationSettings())) return true;

    final preferencias = await SharedPreferences.getInstance();
    if (preferencias.getBool(_yaSePregunto) ?? false) return false;
    await preferencias.setBool(_yaSePregunto, true);

    if (!await _explicarAntesDePedir()) return false;

    return _concedido(await messaging.requestPermission());
  }

  Future<bool> _explicarAntesDePedir() async {
    final context =
        _ref.read(appRouterProvider).routerDelegate.navigatorKey.currentContext;
    if (context == null) return false;

    final quiere = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('¿Le avisamos?'),
        content: const Text(
          'Le suena el teléfono cuando le asignen una orden, cuando un cliente responda una '
          'cotización o cuando un vehículo quede listo.\n\n'
          'Si no lo activa, los avisos igual le llegan: los ve al abrir la campana.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Ahora no'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Activar avisos'),
          ),
        ],
      ),
    );

    return quiere ?? false;
  }

  bool _concedido(NotificationSettings settings) =>
      settings.authorizationStatus == AuthorizationStatus.authorized ||
      settings.authorizationStatus == AuthorizationStatus.provisional;

  Future<bool> _initializeFirebase() async {
    if (_firebaseReady) return true;

    try {
      await Firebase.initializeApp();
      _firebaseReady = true;
    } catch (error) {
      // El caso normal mientras no exista el proyecto de Firebase. No es un fallo de la app.
      debugPrint('Push apagado: no hay proyecto de Firebase configurado ($error)');
      _firebaseReady = false;
    }

    return _firebaseReady;
  }

  Future<void> _register(String? token) async {
    if (token == null) return;

    try {
      await _ref
          .read(notificationRepositoryProvider)
          .registerDevice(token, Platform.isIOS ? 'ios' : 'android');
      _token = token;
    } catch (error) {
      // Sin registro no hay push, pero la sesión no depende de esto.
      debugPrint('No se pudo registrar el aparato para push: $error');
    }
  }

  void _refreshBell() {
    _ref.invalidate(unreadCountProvider);
    _ref.invalidate(notificationsProvider);
  }

  /// La misma navegación que hace la campana: si el aviso trae una orden, se abre la orden.
  void _openFrom(RemoteMessage message) {
    _refreshBell();

    final workOrderId = message.data['workOrderId'] as String?;
    if (workOrderId == null || workOrderId.isEmpty) return;

    _ref.read(appRouterProvider).push('/ordenes/$workOrderId');
  }
}

/// Se crea una vez y se mantiene viva mientras la app lo esté. Quien la enciende y la apaga
/// es la sesión, desde [GarajApp].
final pushMessagingProvider = Provider<PushMessaging>((ref) => PushMessaging(ref));

/// Para que Avisos pueda decir que el teléfono no va a sonar, y ofrecer encenderlo.
final avisosDelSistemaProvider = FutureProvider<bool>(
  (ref) => ref.read(pushMessagingProvider).avisosDelSistemaActivos(),
);

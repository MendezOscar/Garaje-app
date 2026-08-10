import 'dart:async';
import 'dart:io';

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

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

    // En Android 13+ esto abre el diálogo del sistema; en iOS, el de siempre. Denegarlo no
    // rompe nada: el usuario sigue viendo sus avisos en la campana.
    final settings = await messaging.requestPermission();
    if (settings.authorizationStatus == AuthorizationStatus.denied) return;

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

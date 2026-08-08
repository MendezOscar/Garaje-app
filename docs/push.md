# Notificaciones push

La aplicación funciona sin esto. Los avisos se guardan siempre y se ven en la campana; el
push solo hace que el teléfono suene sin tener que abrir la app. Mientras no haya proyecto de
Firebase configurado, el envío queda apagado y nada más falla.

## Estado

| Pieza | Estado |
| --- | --- |
| Avisos guardados y campana (API, web, móvil) | Listo y verificado |
| Registro de dispositivos (`/api/notifications/devices`) | Listo y verificado |
| Envío por FCM desde la API | Escrito, **sin probar**: hace falta el proyecto de Firebase |
| Recepción en la app Flutter | **Pendiente**: hace falta el proyecto de Firebase |

Lo que falta no es código: son los archivos de configuración que solo genera una cuenta de
Firebase. Sin `google-services.json` la compilación de Android falla, así que los paquetes de
Firebase **no** están agregados todavía; agregarlos antes de tener el proyecto rompería el
build para todos.

## Lo que hay que hacer en Firebase

1. Crear un proyecto en <https://console.firebase.google.com> (por ejemplo `garaj-app`).
2. Agregar una app Android con el paquete `com.example.garaj_app` y descargar
   `google-services.json` → `mobile/android/app/`.
3. Agregar una app iOS con el bundle id del proyecto y descargar `GoogleService-Info.plist`
   → `mobile/ios/Runner/`.
4. Para iOS, subir la clave de APNs (Apple Developer → Keys → Apple Push Notifications
   service) en Firebase → Configuración → Cloud Messaging. **Sin esto el push no llega a
   ningún iPhone**, aunque todo lo demás esté bien.
5. Configuración del proyecto → Cuentas de servicio → Generar nueva clave privada. Baja un
   JSON: esa es la credencial de la API.

## Configuración de la API

Dos variables de entorno, en el panel de Render:

```
Push__ProjectId          garaj-app
Push__ServiceAccountJson {"type":"service_account", … }   ← el JSON entero, en una línea
```

El JSON de la cuenta de servicio **es una credencial**: nunca en `appsettings.json` ni en el
repositorio. Con esas dos variables presentes, `IPushSender.IsConfigured` pasa a true y los
avisos empiezan a empujarse solos; sin ellas, la API arranca igual y solo se salta el envío.

Se habla con FCM por HTTP directo (`fcm.googleapis.com/v1/projects/{id}/messages:send`) en
lugar de con el SDK de administración de Firebase: el SDK trae media docena de dependencias
para hacer exactamente ese POST. Lo único que no es trivial es el token OAuth2 de la cuenta
de servicio, y de eso se encarga `Google.Apis.Auth`.

Un token que FCM rechaza por muerto (404 UNREGISTERED, o 400 por formato) se borra de
`device_tokens` en el momento: no se recupera reintentando, y sin borrarlo se seguiría
empujando a un teléfono desinstalado durante meses.

## Lo que falta en Flutter

Con los archivos del paso 2 y 3 en su sitio:

```yaml
# mobile/pubspec.yaml
firebase_core: ^3.6.0
firebase_messaging: ^15.1.3
```

Y al arrancar la sesión, pedir permiso, leer el token y registrarlo contra la API —el
endpoint ya existe y está probado:

```dart
await Firebase.initializeApp();
await FirebaseMessaging.instance.requestPermission();
final token = await FirebaseMessaging.instance.getToken();
if (token != null) {
  await ref.read(notificationRepositoryProvider)
      .registerDevice(token, Platform.isIOS ? 'ios' : 'android');
}
```

El token hay que reenviarlo **en cada arranque**, no solo la primera vez: FCM lo rota por su
cuenta y un token viejo deja de entregar sin avisar. `onTokenRefresh` cubre el caso de que
rote con la app abierta.

El payload que manda la API lleva los datos en `data` además de en `notification`, para que
al tocar el aviso la app sepa a qué pantalla ir:

| Clave | Contenido |
| --- | --- |
| `type` | `NotificationType` como entero |
| `workOrderId` | Id de la orden, o cadena vacía |
| `quoteId` | Id de la cotización, o cadena vacía |
| `serviceRequestId` | Id del requerimiento, o cadena vacía |

La navegación es la misma que ya usa la campana: si viene `workOrderId`, a `/ordenes/{id}`.

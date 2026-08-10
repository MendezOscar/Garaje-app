# Notificaciones push

La aplicación funciona sin esto. Los avisos se guardan siempre y se ven en la campana; el
push solo hace que el teléfono suene sin tener que abrir la app. Mientras no haya proyecto de
Firebase configurado, el envío queda apagado y nada más falla.

## Estado

| Pieza | Estado |
| --- | --- |
| Avisos guardados y campana (API, web, móvil) | Listo y verificado |
| Registro y baja de dispositivos (`/api/notifications/devices`) | Listo y verificado (humo de la Fase 6) |
| Envío por FCM desde la API | Escrito, **sin probar contra FCM**: hace falta el proyecto |
| Recepción en la app Flutter | Escrita y compilando en iOS y Android; **sin probar** por lo mismo |

Ya no falta código de la app: falta el proyecto de Firebase. La app **se compila y funciona
sin él** —lo comprobado en iOS y en Android—, y en ese caso simplemente no registra el
aparato: los avisos se guardan igual y se ven en la campana.

Cómo se sostienen las dos cosas a la vez:

- **Android.** `app/build.gradle.kts` aplica el plugin de Firebase **solo si**
  `google-services.json` existe. Sin esa condición, el plugin rompe la compilación de
  cualquiera que no tenga el archivo.
- **iOS.** No hay condición posible en el proyecto de Xcode, pero tampoco hace falta: los
  pods de Firebase compilan sin `GoogleService-Info.plist`. Lo que falla es
  `Firebase.initializeApp()` en tiempo de ejecución, y eso está atrapado.
- **Dart.** [push_messaging.dart](../mobile/lib/core/push/push_messaging.dart) trata la
  ausencia de proyecto como el caso normal: lo anota y sigue. Nada de la sesión depende de
  que el push funcione.

Un cambio que sí hubo que hacer: **el mínimo de iOS subió a 15.0** (lo exige
`firebase_core` 4.x). Cubre del iPhone 6s en adelante.

## Lo que hay que hacer en Firebase

1. Crear un proyecto en <https://console.firebase.google.com> (por ejemplo `garaj-app`).
2. Agregar una app Android con el paquete **`com.garaj.garaj_app`** y descargar
   `google-services.json` → `mobile/android/app/`.
3. Agregar una app iOS con el bundle id **`com.garaj.garajApp`** y descargar
   `GoogleService-Info.plist` → `mobile/ios/Runner/` (arrastrándolo dentro de Xcode, para que
   quede incluido en el bundle; copiarlo con el Finder no lo agrega al proyecto).
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

## En el proyecto de Xcode, una vez

Push en iOS necesita el permiso `aps-environment`, que se concede activando la capacidad en
Xcode: **Runner → Signing & Capabilities → + Capability → Push Notifications**. Xcode crea el
archivo de entitlements y lo enlaza solo.

No viene hecho en el repositorio a propósito: activarlo exige que el App ID de la cuenta de
Apple tenga la capacidad habilitada, y sin eso la firma de una compilación para teléfono
falla. Es decir, dejarlo puesto de antemano rompería las compilaciones a dispositivo de quien
no tenga todavía la cuenta configurada.

En el simulador **no se puede probar el push**: no hay APNs. `getToken()` falla ahí y la app
lo trata como «sin push», así que se prueba en un teléfono de verdad.

## Lo que ya hace la app

[push_messaging.dart](../mobile/lib/core/push/push_messaging.dart), encendido por la sesión
desde [main.dart](../mobile/lib/main.dart):

| Momento | Qué hace |
| --- | --- |
| Al entrar | Inicializa Firebase, pide permiso y registra el token contra la API |
| Si FCM rota el token | Lo vuelve a registrar (`onTokenRefresh`) |
| Aviso con la app abierta | Refresca el globo de la campana; en iOS también lo muestra |
| Al tocar el aviso | Abre `/ordenes/{id}` si el aviso trae orden |
| App cerrada, se abre desde el aviso | Lo mismo, por `getInitialMessage` |
| Al salir | **Da de baja el aparato antes** de tirar la sesión |

Dos decisiones que conviene conocer:

- El token se reenvía **en cada arranque**, no solo la primera vez: FCM lo rota por su cuenta
  y un token viejo deja de entregar sin avisar.
- La baja al salir ocurre **antes** de borrar el token de sesión, porque el endpoint pide
  autenticación. En un taller el teléfono se comparte, y sin esa baja el siguiente en entrar
  recibiría los avisos del anterior.
- No hay manejador de mensajes en segundo plano (`onBackgroundMessage`). No hace falta: la
  API manda `notification` además de `data`, así que el sistema muestra el aviso con la app
  cerrada sin que corra código nuestro.

## Cómo comprobar que funciona

Con las variables en la API y los archivos en la app, instalada en un **teléfono de verdad**:

1. Entrar con el Cliente en el teléfono y dejar la app cerrada.
2. Desde el web, como Dueño, cambiar el estado de una orden de sus vehículos —a «Lista», por
   ejemplo— dejando la nota visible al cliente.
3. El teléfono debe sonar. Al tocar el aviso, la app abre esa orden.

Si el aviso aparece en la campana pero el teléfono no suena, el problema está entre FCM y el
aparato —lo más común es la clave de APNs sin subir—, no en la API: el log de la API deja un
warning con la respuesta de FCM cuando el envío falla.

El envío va **dentro de la petición** que originó el aviso: son dos o tres aparatos por
taller y montar una cola en segundo plano para eso sería desproporcionado. Si algún día un
taller tiene veinte teléfonos, esto se saca a un trabajo aparte.

El payload que manda la API lleva los datos en `data` además de en `notification`, para que
al tocar el aviso la app sepa a qué pantalla ir:

| Clave | Contenido |
| --- | --- |
| `type` | `NotificationType` como entero |
| `workOrderId` | Id de la orden, o cadena vacía |
| `quoteId` | Id de la cotización, o cadena vacía |
| `serviceRequestId` | Id del requerimiento, o cadena vacía |

La navegación es la misma que ya usa la campana: si viene `workOrderId`, a `/ordenes/{id}`.

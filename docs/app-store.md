# Publicar en la App Store

Para Google Play, ver [play-store.md](play-store.md): los textos son otros —los límites no
coinciden— y hay dos formularios que Apple no tiene.

Todo lo que App Store Connect va a pedir, escrito de antemano. Se preparó **antes** de tener la
membresía a propósito: cuando Apple apruebe, publicar es llenar formularios con esto al lado, no
inventar textos con prisa.

## Ficha de la app

| Campo | Valor |
| --- | --- |
| Nombre (30) | `GarajApp — Taller mecánico` |
| Subtítulo (30) | `Órdenes, repuestos y cobros` |
| Categoría | Negocios · secundaria: Productividad |
| Clasificación por edad | 4+ |
| Idioma principal | Español (México) — es-MX es el que más se acerca a Honduras |
| Derechos de autor | `2026 Óscar Armando Cruz Méndez` — año y titular, sin el símbolo © |
| URL de soporte | `https://www.garajeapp.com/soporte` |
| URL de política de privacidad | `https://www.garajeapp.com/privacidad` |

> Van sobre **garajeapp.com**, que es el dominio que está en el aire. `garajapp.hn` no existe:
> no está registrado y no resuelve. Si algún día se registra, se cambian estas dos filas y los
> enlaces de las dos páginas, no el resto.

Las dos responden hoy y son estáticas (`web/public/soporte.html` y `privacidad.html`), así que
no dependen de que la API esté arriba: si el backend se cae, el enlace que Apple revisa sigue
abriendo.

### Palabras clave (100 caracteres, separadas por coma, sin espacios)

```
taller,mecanico,motos,ordenes,servicio,repuestos,inventario,cotizacion,factura,honduras
```

Sin acentos y sin repetir palabras que ya están en el nombre o el subtítulo: Apple las indexa
igual y repetirlas desperdicia caracteres.

### Texto promocional (menos de 170)

```
El taller desde el teléfono: reciba el vehículo, arme la reparación, cobre y avísele al
cliente por WhatsApp. Sin papeles sueltos y sin perder de vista lo que se debe.
```

Son 167 caracteres. El límite es «menos de 170», así que 170 exactos también se rechazan.
Este campo se puede cambiar sin mandar la app a revisión de nuevo.

### Descripción

```
GarajApp es el sistema de un taller mecánico de autos y motos, hecho para trabajar desde el
patio y no desde un escritorio.

RECIBIR Y REPARAR
· Reciba el vehículo en el mostrador con placa, kilometraje y el motivo de ingreso.
· Arme la reparación paso por paso, con la mano de obra de su catálogo y sus precios.
· Los trabajos que se repiten —el cambio de aceite, las pastillas— se guardan como trabajo
  frecuente y se aplican de un toque.
· Fotos del proceso desde el teléfono, aunque no haya señal: se suben solas cuando vuelve.

EL CLIENTE SABE EN QUÉ VA
· Un enlace por WhatsApp donde ve el avance, las fotos y al final su factura.
· No necesita instalar nada ni tener cuenta.

REPUESTOS SIN SORPRESAS
· Las existencias salen de los movimientos, no de un número que alguien edita.
· Al cargar un repuesto a la orden se descuenta de la bodega en ese momento.
· Aviso de lo que está bajo mínimo, por sucursal.

COTIZAR Y COBRAR
· Cotización con precios de su catálogo, aprobada por el cliente desde su teléfono.
· Ventas al contado o al crédito, con abonos y estado de cuenta.
· Cierre de caja del día: lo que de verdad entró, que no es lo mismo que lo facturado.
· Facturación con CAI para los talleres inscritos en el SAR de Honduras.

SABER CÓMO VA EL NEGOCIO
· Ingresos del día, la semana y el mes, separando repuestos de mano de obra.
· Cuánto produjo cada técnico y cuánto está por cobrar.

VARIAS SUCURSALES
· Cada sucursal con su bodega, sus correlativos y su gente.
· El técnico ve lo suyo; el dueño ve todo.

GarajApp es para talleres. Cada taller ve solo sus datos.
```

### Novedades de esta versión (primera)

```
Primera versión.
```

### Publicación

**Publicar esta versión automáticamente**: en cuanto Apple aprueba, la app sale a la tienda.
La alternativa —publicación manual— sirve si se quiere revisar la ficha ya aprobada antes de
que salga; para la primera versión no hace falta.

## Capturas

En [`app-store/capturas-6.9/`](app-store/capturas-6.9/), tomadas del simulador de iPhone 16 Pro
Max con la base de demostración (`docs/demo.md`), que es la que se ve vivida.

| # | Pantalla | Por qué esa |
| --- | --- | --- |
| 1 | Hoy | Lo primero que abre el taller: lo cobrado, lo que urge y el patio |
| 2 | Detalle de una orden | Los pasos y el total; el resto de la orden, en renglones |
| 3 | Reportes | La gráfica de ingresos: es lo que convence al dueño |
| 4 | Inventario | Existencias por sucursal |
| 5 | Cierre de caja | Lo cobrado en el día |

**Tamaño: 1320 × 2868 (6,9").** Con ese juego basta: Apple escala para los demás iPhone. Si
algún día se sube iPad, hace falta un juego aparte de 13".

Para volver a generarlas, con la API local sembrada con datos de demostración:

```bash
xcrun simctl boot "iPhone 16 Pro Max"
cd mobile && flutter test integration_test/<recorrido>.dart \
  -d "iPhone 16 Pro Max" --dart-define=API_URL=http://localhost:5080
xcrun simctl io booted screenshot captura.png
```

> El diálogo de permiso de notificaciones se planta encima y sale en la captura. En el recorrido
> se anula reemplazando `pushMessagingProvider`; y si quedó uno pendiente de una corrida
> anterior sigue ahí aunque se cierre la app —hasta se queda sobre el escritorio del simulador—,
> así que antes de repetir hay que quitar la app:
> `xcrun simctl uninstall <simulador> com.garaj.garajApp`.

## Información para el revisor

Sin esto la revisión se rechaza por «no pudimos entrar», que es el rechazo más común y el más
tonto.

```
La aplicación es para talleres mecánicos. Las cuentas las crea el dueño del taller para su
personal; no hay registro público, así que abajo va una cuenta de demostración con datos de
prueba.

Usuario: dueno@tallerdemo.hn
Contraseña: Garaj123!

Es el perfil Dueño, que ve todas las funciones. El taller de demostración tiene seis semanas de
historia para que se puedan revisar los reportes y el cierre de caja.

Para borrar la cuenta: pestaña «Más» abajo a la derecha → Cuenta → Eliminar mi cuenta. Se
confirma en un diálogo y la cuenta queda eliminada de inmediato.

La aplicación no recopila datos de menores ni contenido generado por usuarios que se comparta
públicamente. Las fotos que se toman quedan asociadas a la orden de trabajo del taller.
```

> Si el revisor borra la cuenta de demostración, hay que volver a crearla antes de responderle:
> desde **Plataforma → Talleres → el taller → Crear otro acceso de Dueño**.

**Antes de mandar a revisar hay que confirmar que esa cuenta existe en producción**, que es a
donde apunta la compilación que se sube. Ojo con esto:

> **No corra `POST /api/demo/seed` en producción.** Borra la base entera, y ahí ya hay un taller
> real. El taller de demostración para el revisor se crea desde **Plataforma → Talleres → Nuevo
> taller** —que no borra nada— y se le cargan a mano unas órdenes, o se le apunta a un ambiente
> aparte. Ver [deployment.md](deployment.md#puesta-en-marcha-del-primer-cliente).

## Privacidad de la app (el cuestionario)

Lo que la app maneja, para responder sin dudar:

| Dato | Se recopila | Para qué | Vinculado a la persona |
| --- | --- | --- | --- |
| Nombre y correo | Sí | Funcionalidad de la app (la cuenta del usuario) | Sí |
| Teléfono | Sí | Funcionalidad (avisar al cliente por WhatsApp) | Sí |
| Fotos | Sí | Funcionalidad (evidencia de la reparación) | Sí |
| Identificador de dispositivo | Sí | Funcionalidad (avisos push) | Sí |
| Ubicación, contactos, salud, publicidad | No | — | — |

Nada se usa para publicidad ni para seguimiento entre apps: la respuesta a «Tracking» es **no**.

## Lista antes de mandar

- [x] Membresía del Apple Developer Program activa, a nombre de Óscar Armando Cruz Méndez.
- [x] App ID `com.garaj.garajApp` registrado y la app creada en App Store Connect.
- [x] `flutter build ipa --dart-define=API_URL=https://<api de producción>` — que **no** apunte a
      localhost ni a pruebas.
- [x] `--build-name` y `--build-number` puestos al compilar; el número no se puede repetir.
- [x] Las dos URL (soporte y privacidad) responden.
- [x] La cuenta del revisor entra **en producción**: `Taller Demo`, tres sucursales y meses de
      historia. Quedó **sin fecha de pago**, así que la suscripción nunca lo deja en solo lectura
      a media revisión.
- [x] Cumplimiento de exportación: usa HTTPS estándar → exento.
- [x] **Borrar la cuenta desde la app**: hecho, como **Eliminar mi cuenta**, en la pestaña
      **Más → Cuenta** de los tres perfiles: un diálogo y dos toques, sin trámites ni correos.
      Ver [api.md](api.md#borrar-la-propia-cuenta).

## Estado (26 de agosto de 2026)

| Paso | Estado |
| --- | --- |
| Membresía (equipo `3B4RXN8W7G`) | Aprobada |
| Avisos push (clave APNs, entitlement) | Probados en un iPhone de verdad |
| Compilación 1.0.0 (2) subida | Sí |
| Ficha, capturas 6.9", información del revisor | Completas |
| Clasificación por edades | 4+ |
| Privacidad de la app | Publicada |
| Precio | Gratis, país base Honduras |
| **Envío** | Rechazado el 27 de agosto por falta de información (2.1); respondido el 28 con notas y video; **reenviado el 30 de agosto** |

Apple suele contestar en 24–48 horas. La versión quedó en **publicación automática**: si aprueban,
sale a la tienda sin volver a tocar nada.

### Rechazo del 27 de agosto: directriz 2.1, «información necesaria»

No encontraron un defecto: el campo **Notas** iba vacío y pidieron un video. Se responde sin
subir compilación nueva, desde **Revisión de apps → Ver envío → Responder al equipo de revisión
de apps**, y el mismo texto se deja en *Información de revisión de la app → Notas* al final de la
página de la versión, que es donde lo van a buscar en los envíos siguientes.

**Responder no lo devuelve a la cola.** El envío se queda en «Problemas sin resolver» y el
elemento en «Listo para revisión» hasta que se pulsa **«Volver a enviar a revisión de apps»**, en
la página del envío. Conviene dar un día o dos por si el revisor retoma el caso con la respuesta, y
si no se mueve, pulsarlo.

Los siete puntos que piden, y lo que se les contestó:

| Lo que piden | Respuesta |
| --- | --- |
| Grabación en un aparato físico | Video de la app en el iPhone, desde abrirla hasta eliminar la cuenta |
| Modelos y versiones probadas | El iPhone donde se probó, con su versión de iOS |
| Funciones, público y problema que resuelve | Herramienta de trabajo para talleres, sin registro público |
| Cómo entrar y configurar | Credenciales de los **tres perfiles** y qué mirar en cada pestaña |
| Servicios externos | Render, Supabase, Cloudflare R2, Firebase/APNs y los enlaces salientes a WhatsApp |
| Diferencias regionales | Ninguna; solo español y lempiras porque el producto es para Honduras |
| Industria regulada o material de terceros | No aplica; el correlativo de factura lo escribe el propio taller |

Dos cosas que hay que dejar en su lugar antes de responder:

- **Credenciales de cada tipo de cuenta.** Apple las pide para los tres perfiles, no solo el
  Dueño: `dueno@`, `tecnico1@` y `cliente1@tallerdemo.hn`. Hay que confirmar que las tres entran
  **en producción**; si alguna no, el Dueño le cambia la contraseña desde **Usuarios**.
- **Las cadenas de propósito.** Citan la 5.1.1 en el correo estándar. La de red local hablaba de
  «mientras se desarrolla la app», que es una frase de desarrollo en una app de tienda: se
  quitaron `NSLocalNetworkUsageDescription` y `NSBonjourServices` del
  [Info.plist](../mobile/ios/Runner/Info.plist), donde quedó un comentario con las dos claves
  para volver a ponerlas cuando haya que depurar en un iPhone conectado. Solo afecta a
  `flutter run` sobre el aparato; la compilación de publicación nunca pide ese permiso. Aplica
  desde la próxima compilación, no a la 1.0.0 (2) que ya está en revisión.

### Grabar el video: lo que costó tiempo

- **No hay que narrarlo.** Se revisa sin sonido; lo que explica va en las Notas.
- **La sesión sobrevive a borrar la app**: el token vive en el llavero de iOS, que no se borra al
  eliminarla. Para que se vea el inicio de sesión, primero **Más → Cerrar sesión**.
- **El permiso de cámara tampoco se reinicia siempre.** El camino que funciona es
  **Ajustes → General → Transferir o restablecer iPhone → Restablecer → Restablecer ubicación y
  privacidad**. Apagar la cámara en Ajustes no sirve: el permiso queda denegado y la app deja de
  preguntar.
- **Repase las contraseñas del técnico y del cliente antes de grabar.** Una que no entra obliga a
  repetir el video entero.
- Instale la compilación desde **TestFlight → Pruebas internas** (no pasan por revisión, aparece
  en minutos), no desde Xcode: así prueba lo mismo que verá el revisor.

### Lo que faltó y frenó el envío

Dos cosas que App Store Connect no pide en la página de la versión, sino en **Información de la
app**, y sin las cuales el botón de enviar se niega:

- **Derechos sobre el contenido**: pregunta si la app muestra contenido de terceros. Va **No**.
- **Privacidad de la app**: llenar el cuestionario no basta, hay que pulsar **Publicar**. Y la
  primera pregunta —«¿recopilas datos?»— va **Sí**: mandar nombres, teléfonos y fotos al servidor
  propio cuenta como recopilación.

La **declaración de comerciante de la UE** no bloquea nada: si no se contesta, lo único que pasa
es que la app no se muestra en las tiendas europeas.

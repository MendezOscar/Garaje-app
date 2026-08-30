# Lo que reportan los verificadores

La prueba cerrada de Google Play exige 12 verificadores durante 14 días seguidos. El equipo manda
un PDF diario; aquí queda cada punto con su clasificación, para no perderlos y para responder el
cuestionario de acceso a producción, que pregunta **qué cambió la app por lo que dijeron los
verificadores**. Una respuesta genérica ahí es la causa más común de que Google lo niegue.

Los PDF originales están fuera del repositorio, en `~/dev/Pruebas-cerrada-garajapp`.

## Estado

| # | Fecha | Quién | Qué reportó | Tipo | Decisión |
| --- | --- | --- | --- | --- | --- |
| 1 | 27 ago 2026 | Eiborth Gómez (@henkivzla) | La contraseña no se puede ver al escribirla | Falta real | **Hecho**: botón de ojo en el campo |
| 2 | 27 ago 2026 | Eiborth Gómez | El correo no se valida hasta enviar el formulario | Mejora | No se aplica por ahora |
| 3 | 27 ago 2026 | Eiborth Gómez | No hay «¿Olvidó su contraseña?» | Falta real | **Hecho a medias**: se dice a quién pedirla; el flujo por correo queda pendiente |
| 4 | 27 ago 2026 | Eiborth Gómez | No hay registro para talleres nuevos | Por diseño | No se aplica |
| 5 | 27 ago 2026 | Eiborth Gómez | El login sale oscuro después del onboarding azul | No es defecto | Se deja: la app sigue el tema del teléfono |
| 6 | 27 ago 2026 | Eiborth Gómez | El indicador de carga del splash se ve huérfano | Cosmético | **Hecho**: 40 px → 24 px |
| 7 | 28 ago 2026 | Eiborth Gómez | El permiso de avisos se pide sin explicar para qué | Mejora real | **Hecho**: diálogo propio antes del del sistema, y aviso en Avisos si quedó apagado |
| 8 | 28 ago 2026 | Eiborth Gómez | «Marcar todo» sigue visible sin avisos | Falta real | **Hecho**: solo aparece si hay avisos |
| 9 | 28 ago 2026 | Eiborth Gómez | Revisar contraste AA en modo oscuro | Comprobado | El oscuro pasa; el claro no llegaba y **se corrigió**: `#6B7480` → `#646E7A` |
| 10 | 28 ago 2026 | Eiborth Gómez | La pantalla Hoy está llena de ceros | No es defecto | Taller recién creado; se le siembran datos |
| 11 | 28 ago 2026 | Eiborth Gómez | Que permisos, política y Data safety concuerden | Comprobado | Concuerdan |
| 12 | 29 ago 2026 | Eiborth Gómez | «Elija el vehículo y la sucursal» con ambos ya elegidos | **Defecto real** | Pendiente |
| 13 | 29 ago 2026 | Eiborth Gómez | Buscar «aveo» no encuentra el vehículo Aveo | No reproducido | Falta el paso exacto |
| 14 | 29 ago 2026 | Eiborth Gómez | El botón se puede pulsar sin vehículo elegido | Falta real | Pendiente |
| 15 | 29 ago 2026 | Eiborth Gómez | El kilometraje admite cualquier cosa | Falta real | Pendiente |
| 16 | 29 ago 2026 | Eiborth Gómez | Revisar consistencia claro/oscuro | Comprobado | La app sigue el tema del sistema |

## Día 1 — 27 de agosto de 2026

Auditoría de UX del arranque: splash, los tres pasos del onboarding y el login. **No tocó ninguna
función del taller todavía** —órdenes, inventario, caja—, así que el reporte dice poco sobre si la
app funciona; dice cómo se ve la puerta de entrada.

Lo que anotó a favor: el logo centrado y la tipografía del splash, el botón «Saltar» y los puntos
de página del onboarding, el contraste del texto blanco sobre el azul, que el botón cambie de
«Siguiente» a «Entrar» en el último paso, y los campos del login con su subtítulo.

### 1. La contraseña no se puede ver al escribirla

Cierto: el campo va con `obscureText: true` y sin botón para revelarla
([login_screen.dart:95](../mobile/lib/features/login/login_screen.dart#L95)). En un taller, con
las manos sucias y contraseñas que le dictó el dueño, escribir a ciegas es justo donde la gente se
traba.

Es un `suffixIcon` con un `bool` de estado: unas diez líneas.

### 2. El correo no se valida hasta enviar el formulario

El validador solo comprueba que no esté vacío
([login_screen.dart:85](../mobile/lib/features/login/login_screen.dart#L85)), así que un correo mal
escrito se descubre cuando el servidor contesta «credenciales inválidas», que es un mensaje que no
ayuda. Con `autovalidateMode: onUserInteraction` y una comprobación de formato se avisa antes.

Barato, pero de valor menor: el error del servidor ya cierra el paso.

### 3. No hay «¿Olvidó su contraseña?»

Es una falta real y la más cara de las tres. Hoy no existe endpoint de recuperación: `AuthController`
solo tiene login, refresh, logout, me y borrar cuenta
([AuthController.cs](../backend/src/Garaj.Api/Controllers/AuthController.cs)). Hacerlo bien pide
enviar correo, y no hay proveedor de correo contratado.

Quien se queda afuera hoy tiene salida, pero nadie se la dice:

- El **técnico** y el **cliente**: el Dueño les pone una contraseña nueva desde **Usuarios**
  ([users_screen.dart:70](../mobile/lib/features/users/users_screen.dart#L70)).
- El **Dueño**: solo nosotros, desde el panel de plataforma.

Por eso conviene partirlo en dos: ahora, una línea bajo el botón —«¿Olvidó su contraseña? Pídasela
al dueño de su taller»— con el enlace de soporte; después, el flujo por correo cuando haya
proveedor.

### 4. No hay registro para talleres nuevos

**No se aplica, y a propósito.** GarajApp no tiene registro público: las cuentas las crea el dueño
del taller para su personal. Es exactamente lo que se le declaró a Apple en las notas del revisor
([app-store.md](app-store.md#información-para-el-revisor)); agregar un registro abierto
contradiría eso y llenaría la base de talleres fantasma.

Lo que sí cabe, si se quiere: un enlace discreto «¿Su taller aún no usa GarajApp?» hacia
garajeapp.com, que es donde se pide una cuenta.

### 5. El login sale oscuro después del onboarding azul

No es un defecto: la app sigue el tema del teléfono, y el del verificador está en modo oscuro. El
onboarding es azul de marca a propósito, en las dos apariencias.

Queda como decisión de diseño: dejarlo así, o pintar el login con el azul de marca para que el
arranque se vea de una pieza. Lo segundo obliga a fijar los colores del formulario a mano en las
dos apariencias.

### 6. El indicador de carga del splash se ve huérfano

Cosmético. El `CircularProgressIndicator` va 40 px debajo del logotipo
([splash_screen.dart:35](../mobile/lib/features/splash/splash_screen.dart#L35)); bajando ese
espacio se pega al conjunto y deja de flotar. Un número.

## Día 2 — 28 de agosto de 2026

Lo que sigue al inicio de sesión: el permiso de avisos, la pantalla **Hoy** en modo oscuro y la
pantalla de **Avisos** vacía. Su conclusión: «aprobado con observaciones», sin nada que por sí
solo haga que Google rechace.

A favor anotó la jerarquía de Hoy, el acceso directo a «Recibir vehículo», la navegación inferior
y que el estado vacío de Avisos se entiende.

### 7. El permiso de avisos se pide sin explicar para qué

Cierto y es el hallazgo más útil de los dos días. Hoy el diálogo del sistema sale apenas entra el
usuario, encima de la pantalla principal, porque el push arranca al iniciar sesión
([main.dart:23](../mobile/lib/main.dart#L23)). Nadie le dijo antes qué gana aceptándolo.

Lo estándar es pedirlo **en contexto**: una pantalla propia que explique «le avisamos cuando le
asignen una orden o cuando un cliente responda una cotización», y recién entonces el diálogo del
sistema. Sube la tasa de aceptación y, sobre todo, la de quien lo pensó y dijo que sí.

Cuesta más que las de ayer: hay que recordar si ya se preguntó —si no, el diálogo propio
reaparecería en cada arranque— y eso pide guardar una marca en el aparato.

Aparte va el «No permitir»: hoy el código se retira en silencio
([push_messaging.dart:49](../mobile/lib/core/push/push_messaging.dart#L49)) y la campana sigue
funcionando, que es lo correcto. Lo que falta es decírselo al usuario: una línea en Avisos
—«los avisos del sistema están apagados»— para que sepa por qué su teléfono no suena.

### 8. «Marcar todo» sigue visible sin avisos

Cierto: el botón vive en la `AppBar` y no mira si la lista trae algo
([notifications_screen.dart:22](../mobile/lib/features/notifications/notifications_screen.dart#L22)).
Un botón que no puede hacer nada se lee como función a medio terminar, que es justo lo que no
conviene durante una prueba cerrada.

Barato: mostrarlo solo cuando hay avisos sin leer.

### 9. Contraste AA

Comprobado con los colores reales, no a ojo. **En modo oscuro pasa**, y con holgura: el texto
apagado sobre el fondo da 7,09:1 y sobre las tarjetas 6,46:1, contra los 4,5:1 que pide la AA.

Pero al medirlo apareció otra cosa: **el modo claro no llega**. `textMuted` (`#6B7480`) sobre el
fondo claro da **4,46:1**, tres centésimas por debajo del mínimo, y sobre las tarjetas grises
`surfaceAlt` cae a 4,15:1. Es texto de 15 px, así que le aplica el 4,5:1.

Oscurecerlo a `#646E7A` lo deja en 4,88 / 5,18 / 4,54 y no cambia nada más: el color sale de
[garaj_brand.dart:19](../mobile/lib/core/theme/garaj_brand.dart#L19) y todas las pantallas lo
heredan. El mismo token existe en el panel web, que tendría que moverse igual.

### 10. La pantalla Hoy está llena de ceros

No es un defecto de la app: el taller que se les dio a los verificadores se creó vacío. Pero el
verificador tiene razón en el fondo —así no se puede probar con importes largos ni ver si algo se
desborda—, y eso se arregla sin tocar código: cargarle órdenes, repuestos y cobros a ese taller,
con cifras grandes a propósito.

### 11. Que permisos, política y Data safety concuerden

Comprobado. La app pide cámara, fotos y notificaciones, y nada más; el formulario de seguridad de
los datos declara nombre, correo, ID de usuario, teléfono, fotos e ID de dispositivo, todo como
recopilado y nada compartido; y la política de privacidad dice lo mismo. La app no trae Crashlytics
ni Analytics, así que no hay diagnósticos que declarar. Ver
[play-store.md](play-store.md#seguridad-de-los-datos-el-formulario-que-más-cuesta).

## Día 3 — 29 de agosto de 2026

El flujo de **Recibir vehículo**. Su veredicto: «no recomendado para producción hasta corregir el
bloqueo crítico». El hallazgo crítico es real y tiene una causa exacta; el alto, tal como está
descrito, no se reproduce leyendo el código.

### 12. «Elija el vehículo y la sucursal» con ambos ya elegidos

**Confirmado, y la causa es más simple de lo que parece.** El mensaje no lo pinta la validación de
cada campo: es la variable `_error`, que se fija al pulsar el botón sin vehículo o sin sucursal
([new_service_request_screen.dart:337](../mobile/lib/features/service_requests/new_service_request_screen.dart#L337))
y **no se limpia nunca al corregir**. Solo desaparece cuando un envío llega a hacerse.

O sea: el verificador pulsó el botón con el formulario a medias, salió el rojo, después eligió el
vehículo y la sucursal, y el rojo se quedó ahí contradiciendo lo que la pantalla mostraba. No es
que la validación no vea la selección; es un mensaje viejo que nadie borró.

Que el flujo esté o no bloqueado no se puede saber por el reporte. Si vehículo y sucursal están de
verdad elegidos, el envío procede: el mensaje engaña, pero no traba. Vale confirmarlo con él.

Dos arreglos, los dos baratos, y van juntos:

- Limpiar `_error` en cuanto cambie el vehículo o la sucursal.
- Dejar el botón apagado mientras falte uno de los dos, que es lo que pide el punto 14: así el
  mensaje no llega a aparecer.

El panel web tiene el mismo patrón
([NewServiceRequestForm.vue:221](../web/src/components/NewServiceRequestForm.vue#L221)): el error
se fija al enviar y solo se limpia en el envío siguiente.

### 13. Buscar «aveo» no encuentra el vehículo Aveo

**No se reproduce leyendo el código.** La búsqueda del servidor mira placa, marca, **modelo** y
nombre del dueño, y las tres últimas con `ILIKE`, que ignora mayúsculas
([VehicleService.cs:28](../backend/src/Garaj.Infrastructure/Services/VehicleService.cs#L28)); la
placa se compara normalizada, sin guiones ni espacios y en mayúsculas. «aveo» debería encontrar un
modelo «Aveo».

La explicación que mejor encaja con su propio reporte es que **el vehículo todavía no existía**
cuando buscó: lo creó enseguida con «Cliente nuevo» —el nombre «pdjcb» tiene toda la pinta de un
registro de prueba— y por eso apareció seleccionado después. Para cerrarlo hace falta que diga si
el vehículo ya estaba antes de buscarlo.

Lo que sí es una fricción real en el camino: en el teléfono la búsqueda **solo corre al pulsar la
tecla de buscar** del teclado
([new_service_request_screen.dart:113](../mobile/lib/features/service_requests/new_service_request_screen.dart#L113)).
Quien escribe y toca fuera del campo no ve cambiar nada, y concluye que el buscador no sirve.

### 14. El botón se puede pulsar sin vehículo elegido

Cierto: el botón solo se apaga mientras guarda
([new_service_request_screen.dart:255](../mobile/lib/features/service_requests/new_service_request_screen.dart#L255)).
Se arregla con el 12.

Lo de marcar los campos obligatorios también es cierto —solo «Kilometraje» dice que es opcional—,
pero conviene al revés: marcar los pocos opcionales, que es lo que ya se hace, y no llenar la
pantalla de asteriscos.

### 15. El kilometraje admite cualquier cosa

Cierto y es el que más callado hace daño. El campo abre el teclado numérico pero no filtra lo que
entra, y al enviar `int.tryParse` devuelve `null` sin decir nada
([new_service_request_screen.dart:353](../mobile/lib/features/service_requests/new_service_request_screen.dart#L353)):
un kilometraje mal escrito no da error, simplemente se pierde.

Con un filtro de solo dígitos y un largo máximo se acaba: siete cifras cubren cualquier vehículo.

### 16. Consistencia claro/oscuro

Comprobado. La app no tiene interruptor propio: sigue el tema del sistema, y las dos apariencias
salen de los mismos tokens
([garaj_brand.dart](../mobile/lib/core/theme/garaj_brand.dart)). Ninguna pantalla fija colores a
mano. El contraste ya se midió el día 2 y el claro se corrigió.

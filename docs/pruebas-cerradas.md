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
| 10 | 28 ago 2026 | Eiborth Gómez | La pantalla Hoy está llena de ceros | No es defecto | **Resuelto con datos**: `backend/tools/poblar-taller.py` |
| 11 | 28 ago 2026 | Eiborth Gómez | Que permisos, política y Data safety concuerden | Comprobado | Concuerdan |
| 12 | 29 ago 2026 | Eiborth Gómez | «Elija el vehículo y la sucursal» con ambos ya elegidos | **Defecto real** | **Hecho**: el error se borra al elegir, en el teléfono y en el panel |
| 13 | 29 ago 2026 | Eiborth Gómez | Buscar «aveo» no encuentra el vehículo Aveo | **Descartado** | No se reproduce, ni leyendo el código ni probándolo |
| 14 | 29 ago 2026 | Eiborth Gómez | El botón se puede pulsar sin vehículo elegido | Falta real | **Hecho**: apagado hasta que haya vehículo y sucursal |
| 15 | 29 ago 2026 | Eiborth Gómez | El kilometraje admite cualquier cosa | Falta real | **Hecho**: solo dígitos, hasta siete |
| 16 | 29 ago 2026 | Eiborth Gómez | Revisar consistencia claro/oscuro | Comprobado | La app sigue el tema del sistema |
| 17 | 30 ago 2026 | Eiborth Gómez | El cierre de caja muestra el 11 de agosto a fin de mes | No reproducido | Sin día elegido, la fecha la pone el servidor |
| 18 | 30 ago 2026 | Eiborth Gómez | Que anulaciones y abonos cuadren con el cierre | Comprobado | Las anuladas se apartan y se informan aparte |
| 19 | 30 ago 2026 | Eiborth Gómez | Zona horaria y límites del día | Comprobado | El día es el del taller, a −06:00 |
| 20 | 30 ago 2026 | Eiborth Gómez | Conservación y exportación de registros | Fuera del código | Ya se exporta; el plazo legal lo define un contador |
| 21 | 31 ago 2026 | Eiborth Gómez | Falta ruta para eliminar la cuenta | Ya existía | **Más → Cuenta → Eliminar mi cuenta**, en los tres perfiles |
| 22 | 31 ago 2026 | Eiborth Gómez | La política de privacidad no está dentro de la app | **Cierto** | **Hecho**: privacidad y soporte en Más → Cuenta |
| 23 | 31 ago 2026 | Eiborth Gómez | «Contraseña» podría exponer la actual | Comprobado | Solo fija una nueva; la actual no existe en claro |
| 24 | 31 ago 2026 | Eiborth Gómez | Roles y permisos reales | Comprobado | El servidor los exige, no la pantalla |
| 25 | 31 ago 2026 | Eiborth Gómez | Términos y condiciones | Recomendación | Decisión suya |
| 26 | 1 sep 2026 | Eiborth Gómez | Rangos 7/30/90 y límites de fecha | **Cierto, menor** | **Hecho**: la ventana arranca al comienzo del día |
| 27 | 1 sep 2026 | Eiborth Gómez | Margen con NaN o división por cero | Comprobado | El único porcentaje está protegido |
| 28 | 1 sep 2026 | Eiborth Gómez | Que la exportación responda | Comprobado | Genera el CSV, lo comparte y avisa si falla |
| 29 | 1 sep 2026 | Eiborth Gómez | Funcionalidad estable (política) | Sin acción | — |
| 30 | 1 sep 2026 | Eiborth Gómez | Declarar SDK que transmiten datos | Comprobado | No hay analítica ni informes de fallos |
| 31 | 2 sep 2026 | Eiborth Gómez | «Tarjeta» y «Transferencia» pueden leerse como cobro integrado | Justo | **Hecho**: lo dice bajo el campo |
| 32 | 2 sep 2026 | Eiborth Gómez | Validar total, ISV, CAI, anulaciones y ventas duplicadas | Comprobado | Cubierto por las pruebas de humo |
| 33 | 2 sep 2026 | Eiborth Gómez | No vender sin repuesto, ni cobrar doble por doble toque | Comprobado | El botón se apaga solo |
| 34 | 2 sep 2026 | Eiborth Gómez | Buscar «amortiguador» no encuentra nada | No es defecto | Ese repuesto no existe en el taller |
| 35 | 2 sep 2026 | Eiborth Gómez | Distinguir «no existe» de «sin existencia aquí» | **Cierto** | **Hecho**: el buscador consulta el catálogo |
| 36 | 2 sep 2026 | Eiborth Gómez | La búsqueda no ignora acentos | **Cierto** | Pendiente; pide `unaccent` en la base |
| 37 | 3 sep 2026 | Eiborth Gómez | Falta la política de privacidad en la app | Ya resuelto | Se hizo el 31 de agosto, punto 22 |
| 38 | 3 sep 2026 | Eiborth Gómez | Falta «Eliminar cuenta» localizable | Ya existía | Más → Cuenta, punto 21 |
| 39 | 3 sep 2026 | Eiborth Gómez | Términos y condiciones | Recomendación | Decisión suya, como el punto 25 |
| 40 | 3 sep 2026 | Eiborth Gómez | Falta la versión de la app y quién la hace | **Cierto** | **Hecho**: al pie de «Más» |
| 41 | 3 sep 2026 | Eiborth Gómez | Los permisos no pueden ser solo ocultar opciones | Comprobado | El servidor autoriza, punto 24 |

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
desborda—, y eso se arregla sin tocar código.

Para eso está [`backend/tools/poblar-taller.py`](../backend/tools/poblar-taller.py): entra por la
API con la sesión del Dueño y da de alta catálogo, clientes, seis órdenes en distintos estados
—tres facturadas, una con saldo pendiente— y unas ventas de mostrador del mes en curso. Nada de
histórico: lo justo para que las pantallas tengan qué enseñar.

> **No confundirlo con `POST /api/demo/seed`**, que borra la base entera y no se corre en
> producción jamás. Este solo hace altas, con el mismo alcance que tendría una persona en el
> mostrador, y se planta si el taller ya tiene órdenes para no dejar todo por duplicado.

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
registro de prueba— y por eso apareció seleccionado después.

**Descartado el 29 de agosto**: se probó a mano y la búsqueda encuentra el vehículo por su modelo.
Queda anotado por si reaparece con un paso a paso que lo repita.

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

## Día 4 — 30 de agosto de 2026

Primer corte funcional, sobre el **cierre de caja**. Su veredicto: «aprobado con observaciones».
Casi todo son cosas a comprobar, no defectos vistos, y las tres que se podían comprobar leyendo el
código salen bien.

### 17. El cierre muestra «martes 11 de agosto» a fin de mes

No se reproduce. La pantalla abre **sin día elegido**
([cash_close_screen.dart:26](../mobile/lib/features/reports/cash_close_screen.dart#L26)), y en ese
caso la fecha y su rótulo los pone el servidor con su propio reloj, no el teléfono: aunque el
aparato tuviera la fecha corrida, el cierre seguiría mostrando el día de verdad.

Para que aparezca el 11 de agosto hay que haberlo elegido en el calendario. Vale preguntarle si lo
eligió o si la captura es de otra sesión —el 11 de agosto de 2026 sí cae martes, así que el rótulo
es coherente con la fecha, no está mal armado—.

### 18. Que las anulaciones cuadren con el cierre

Comprobado, y está resuelto con cuidado: los abonos de una venta anulada **no suman al total**,
pero tampoco desaparecen —se apartan y se informan aparte, con su cantidad y su monto
([ReportService.cs:471](../backend/src/Garaj.Infrastructure/Services/ReportService.cs#L471))—.
Hacerlos desaparecer sin explicación es justo lo que deja a un dueño sin poder cuadrar.

### 19. Zona horaria y límites del día

Comprobado. El día contable es el del taller y no el UTC: las fronteras se calculan a −06:00 y
recién entonces se convierten a UTC para consultar
([ReportService.cs:601](../backend/src/Garaj.Infrastructure/Services/ReportService.cs#L601)). Un
abono de las siete de la noche cae en la caja de ese día, que agrupando por fecha UTC caería en la
del siguiente. Honduras no tiene horario de verano, así que el desplazamiento fijo alcanza y evita
depender de la base de zonas horarias del contenedor.

El teléfono, cuando se elige un día, manda **el mediodía** de esa fecha
([report_repository.dart:355](../mobile/lib/core/api/report_repository.dart#L355)) precisamente
para que ningún desplazamiento lo empuje al día anterior.

### 20. Conservación y exportación de registros

No es cosa del código y el propio reporte lo dice: depende del país donde opere el taller y lo
define un contador. Lo que la app ya da: el **libro de ventas en CSV** por mes —incluidas las
anuladas, que el régimen exige reportar y no esconder—, el cierre de caja en PDF y los correlativos
fiscales con su CAI. Nada se borra: una venta se anula, no se elimina.

## Día 5 — 31 de agosto de 2026

La pantalla de **Usuarios**, mirada como auditoría de acceso y cumplimiento. Su veredicto:
«requiere correcciones antes de producción». De las cuatro cosas que marca, **tres ya estaban
resueltas** y no se ven desde esa pantalla; la cuarta es cierta y es barata.

Conviene leer este corte con cuidado: el propio reporte avisa que juzga «con la evidencia de las
capturas». Que algo no salga en una captura de la pantalla de Usuarios no significa que no exista
en la app.

### 21. «Falta una ruta para eliminar la cuenta»

Ya existe, y en los tres perfiles: **Más → Cuenta → Eliminar mi cuenta**
([more_screen.dart:179](../mobile/lib/features/shell/more_screen.dart#L179)), con su diálogo de
confirmación. Y el recurso web que Google pide también:
`https://www.garajeapp.com/soporte#eliminar-mi-cuenta`, que es la URL declarada en el formulario de
seguridad de los datos. No está en Usuarios porque Usuarios es para dar de alta al personal, no
para la cuenta propia.

### 22. La política de privacidad no está dentro de la app

**Cierto.** Estaba en Play Console y en la ficha de Apple, y las páginas responden, pero desde la
app no había cómo abrirlas: ni privacidad, ni soporte.

Resuelto con dos filas en **Más → Cuenta**
([more_screen.dart:167](../mobile/lib/features/shell/more_screen.dart#L167)), que abren el sitio en
el navegador. Van al sitio y no dentro de la app a propósito: así el texto se corrige sin publicar
una versión nueva, y sigue abriendo aunque la API esté caída.

### 23. «Contraseña» podría exponer la credencial actual

No la expone, y no podría: el diálogo solo pide una **nueva**
([users_screen.dart:112](../mobile/lib/features/users/users_screen.dart#L112)), y en el servidor las
contraseñas se guardan con hash, así que la actual no existe en ninguna parte para poder mostrarse.
Es el Dueño reponiéndole la contraseña a su técnico —el caso de siempre en un taller: se le olvidó y
está frente al mostrador—, no un usuario cambiando la suya.

### 24. Roles y permisos reales

Comprobado, y donde importa: **en el servidor**, no en la pantalla. `/api/users` entero es del
Dueño ([CatalogControllers.cs:39](../backend/src/Garaj.Api/Controllers/CatalogControllers.cs#L39)) y
el cierre de caja exige perfil Dueño antes de tocar la base
([ReportService.cs:438](../backend/src/Garaj.Infrastructure/Services/ReportService.cs#L438)). Un
técnico que llame la API a mano no pasa. Además, lo ajeno responde **404 y no 403**, para no
confirmarle a nadie que el recurso existe, y hay pruebas de humo que lo comprueban
([fase2_smoke.py:204](../backend/tests/smoke/fase2_smoke.py#L204)).

### 25. Términos y condiciones

El propio reporte lo pone como aclaración: no son una regla de rechazo de Google Play. Con el enlace
del punto 22 se puede sumar el de términos el día que existan; hoy no hay documento que enlazar.

## Día 6 — 1 de septiembre de 2026

**Reportes.** Otra vez casi todo son cosas a comprobar. Tres salen bien y una es cierta, aunque
menor.

### 26. Los rangos de 7, 30 y 90 días

Aquí sí hay algo. El teléfono pide el rango como **«ahora menos N días»**
([report_repository.dart:332](../mobile/lib/core/api/report_repository.dart#L332)), o sea las
últimas 168 horas y no los últimos siete días del taller. Como las barras del gráfico se agrupan
**por día**, la primera barra siempre sale a medias: empieza a la hora en que el dueño abrió la
pantalla, siete días atrás.

No falsea el total —lo cobrado en esa ventana es lo que dice—, pero hace que la barra más vieja se
vea más baja de lo que fue y que el número cambie a lo largo del día.

Resuelto: el rango arranca al **comienzo del día**, y «7 días» pasa a ser hoy más los seis
anteriores.

Lo demás del punto está en orden: si la fecha inicial es posterior a la final, la API responde con
un mensaje claro ([ReportService.cs:594](../backend/src/Garaj.Infrastructure/Services/ReportService.cs#L594)),
y el mes por defecto se calcula con el mes del taller a −06:00, igual que el cierre de caja.

### 27. Margen, NaN y división por cero

Comprobado. En todo el reporte hay **un solo porcentaje**, el del margen, y está protegido:
`total == 0 ? 0 : …`
([ReportService.cs:169](../backend/src/Garaj.Infrastructure/Services/ReportService.cs#L169)). Los
demás márgenes son cantidades de dinero, restas sin división. Y son `decimal`, no coma flotante:
en C# una división de `decimal` por cero lanza excepción, nunca produce `NaN`, así que ese valor no
puede aparecer en pantalla.

Un margen negativo sí puede salir, y no es un error: significa que se vendió por debajo del costo.
Es justo lo que el dueño necesita ver.

### 28. Que la exportación responda

Comprobado. El ícono de documento pide el libro de ventas del mes a la API, escribe el CSV y abre
la hoja de compartir del sistema
([reports_screen.dart:37](../mobile/lib/features/reports/reports_screen.dart#L37)). Si algo falla,
sale un mensaje en vez de quedarse mudo. Incluso está resuelto el anclaje de la hoja en iPad, que
sin eso revienta.

### 29 y 30. Política y SDK

Sin acción. Lo de declarar la analítica ya se comprobó el día 2: el proyecto solo trae
`firebase_core` y `firebase_messaging`, sin Crashlytics ni Analytics, así que no hay diagnósticos
que declarar en el formulario de seguridad de los datos.

## Día 7 — 2 de septiembre de 2026

**Vender repuesto**, la venta de mostrador. Marca «requiere correcciones antes de producción»,
pero de lo crítico que señala, dos cosas ya están resueltas y una es una aclaración de texto, no
un defecto.

### 31. «Tarjeta» y «Transferencia» pueden leerse como cobro integrado

El señalamiento es justo, y es el mismo que Apple preguntó por la regla 3.1. La app **solo anota
cómo pagó el cliente**: no hay pasarela, no se piden datos de tarjeta y no se mueve dinero. El
campo se llama «Forma de pago»
([counter_sale_screen.dart:270](../mobile/lib/features/sales/counter_sale_screen.dart#L270)), que
es correcto, pero no lo dice explícitamente.

Resuelto con una línea bajo el campo: «aquí solo se anota cómo pagó el cliente; la app no procesa
cobros ni pide datos de tarjeta». Cuesta nada y le habla al revisor de la tienda tanto como al
usuario.

### 32. Total, ISV, CAI, anulaciones y ventas duplicadas

Comprobado, y no de palabra: hay pruebas de humo que lo cubren. El subtotal suma las líneas y el
total cierra con descuentos, el ISV nace en cero y solo cobra si se pone a mano
([fase4_smoke.py:181](../backend/tests/smoke/fase4_smoke.py#L181)); los rangos del SAR se validan
—CAI con forma de CAI, fecha límite vigente, rango al revés rechazado— y una venta sin CAI se
emite igual sin número fiscal ([fase12_smoke.py:141](../backend/tests/smoke/fase12_smoke.py#L141));
y una venta anulada no suma en la caja pero se informa aparte con su monto
([fase8_smoke.py:378](../backend/tests/smoke/fase8_smoke.py#L378)).

### 33. Vender sin repuesto, o cobrar doble por doble toque

Las dos cosas ya están cerradas en la misma línea: el botón de registrar está apagado si no hay
líneas, si algo se quedó sin existencia o si ya se está guardando
([counter_sale_screen.dart:362](../mobile/lib/features/sales/counter_sale_screen.dart#L362)), y la
marca de «ocupado» se pone **antes** de llamar a la API
([counter_sale_screen.dart:114](../mobile/lib/features/sales/counter_sale_screen.dart#L114)), que
es lo que evita el segundo toque. El cliente, en cambio, es opcional a propósito: en el mostrador
se vende un filtro a quien pasa, sin pedirle el nombre.

### 34. Buscar «amortiguador» no encuentra nada

No es defecto: ese repuesto no existe en el taller de pruebas. El catálogo que se le cargó tiene
aceite, filtro, pastillas, bujía, batería y cadena. La búsqueda mira código, nombre y marca, sin
distinguir mayúsculas ([StockService.cs:44](../backend/src/Garaj.Infrastructure/Services/StockService.cs#L44)).

### 35. Distinguir «no existe» de «no hay en esta sucursal»

**Cierto, y es el hallazgo útil del día.** El buscador del mostrador lista existencias, no el
catálogo: un repuesto que existe pero que nunca entró a esa sucursal era invisible, y el mensaje
—«nada con ese nombre en esta sucursal»— dejaba pensando si el repuesto no existe o si es que no
hay. Son dos cosas distintas y se actúa distinto: una se resuelve dándolo de alta, la otra
recibiendo mercadería.

Resuelto: cuando la bodega no devuelve nada, el buscador consulta el catálogo y dice cuál de las
dos cosas pasó —«está en el catálogo pero sin existencia en esta sucursal; recíbalo en bodega y
podrá venderlo», o que no existe ni en el catálogo—. La consulta extra solo ocurre cuando no hay
resultados, así que la búsqueda normal no se hace más lenta.

### 36. La búsqueda no ignora acentos

**Cierto.** `ILIKE` de PostgreSQL ignora mayúsculas pero no acentos, así que «bateria» no
encuentra «Batería», y en el mostrador nadie escribe con acentos. Se arregla con la extensión
`unaccent` en la base y una comparación sobre el texto normalizado; hay que habilitarla en
Supabase, así que no es de una línea.

## Día 8 — 3 de septiembre de 2026

El menú **Más**. Cuatro de los cinco puntos son repeticiones de días anteriores, tres de ellas ya
resueltas antes de este corte; el quinto es cierto y es útil.

Vale decirlo una vez y con calma: **este corte juzga otra vez sobre una captura**. La política de
privacidad y el borrado de cuenta están en ese mismo menú, en el grupo **Cuenta**, al final de la
lista —hay que bajar—. Los reportes ganarían mucho si el recorrido incluyera desplazarse por la
pantalla antes de concluir que algo falta.

### 37 y 38. Privacidad y eliminación de cuenta

Ya estaban. El borrado de cuenta existe desde antes de la prueba cerrada y los enlaces de
privacidad y soporte se agregaron el 31 de agosto por su propio hallazgo del punto 22
([more_screen.dart:167](../mobile/lib/features/shell/more_screen.dart#L167)). La URL pública en
Play Console también está declarada.

### 39. Términos y condiciones

El mismo punto 25. Sigue siendo decisión suya y no es regla de rechazo; el día que exista el
documento, el enlace se agrega junto a los otros dos.

### 40. La versión de la app y quién la hace

**Cierto, y es el que valía la pena.** Parece un detalle de vitrina y no lo es: cuando alguien
reporta que algo no funciona, lo primero que hay que saber es qué versión tiene en la mano —justo
lo que faltó estos días, con arreglos saliendo mientras el equipo probaba—.

Resuelto: al pie de **Más** aparece «GarajApp 1.0.1 (3)» con el nombre del responsable y el sitio.
Sale del paquete instalado y no de una constante, así que no se queda vieja el día que alguien
suba la versión y se olvide.

### 41. Los permisos no pueden ser solo ocultar opciones

Comprobado el día 5, punto 24, y sigue igual: el servidor autoriza cada operación —`/api/users`
entero es del Dueño, el cierre de caja exige perfil antes de tocar la base—, lo ajeno responde 404
y no 403, y hay pruebas de humo que lo verifican. Ocultar la opción en el menú es cosmética; la
puerta está en la API.

## Para el cuestionario de acceso a producción

Google no pide la lista de errores; pregunta cómo fue la prueba. Lo de arriba responde la pregunta
que más pesa —qué cambió la app por lo que dijeron los verificadores—; esto responde las otras.

**Cómo se reclutó a los verificadores.** Un equipo de QA, que probó la app a diario durante los 14
días y entregó un reporte escrito por jornada: no fueron doce cuentas puestas para cumplir el
requisito.

**Qué se aprendió.** Los reportes fueron entrando por capas —primero la puerta de entrada, después
el arranque de sesión, después el flujo de recibir un vehículo—, y los hallazgos con valor
aparecieron en el tercero, cuando se tocó el trabajo de verdad. De ahí salieron un mensaje de error
que se quedaba en pantalla contradiciendo lo que el usuario veía, un botón que se podía pulsar sin
tener los datos, un campo que perdía el kilometraje en silencio y un permiso de avisos que se pedía
sin explicar para qué. Los cuatro están corregidos.

**Qué no se aplicó, y por qué.** Registro público de talleres —GarajApp no lo tiene a propósito: las
cuentas las crea el dueño para su personal, y así se le declaró también a Apple—; el cambio de tema
entre pantallas, que no es un defecto sino la app siguiendo la apariencia del teléfono; y un
hallazgo de búsqueda que no se reprodujo ni leyendo el código ni probándolo.

**A quién va dirigida y cómo se van a conseguir usuarios.** Talleres mecánicos de autos y motos en
Honduras, de uno a diez trabajadores. La venta es directa: visita al taller y WhatsApp, con la
página garajeapp.com como puerta de entrada, y referidos de los talleres que ya la usan. No hay
compras dentro de la app: la mensualidad se cobra fuera, por transferencia.

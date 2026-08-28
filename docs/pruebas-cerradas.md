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

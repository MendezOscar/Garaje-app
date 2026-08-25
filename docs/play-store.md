# Publicar en Google Play

Lo mismo que [app-store.md](app-store.md) pero para Play: todo lo que Play Console va a pedir,
escrito antes de tener la cuenta. Los textos **no** son los de Apple copiados —los límites son
otros— y hay dos formularios que Apple no tiene.

Del lado del repositorio ya está resuelto lo que suele atrasar: la llave de firma existe
(`android/key.properties` y su `.jks`, fuera del repo), `google-services.json` está en su sitio
para los avisos push, y `targetSdk` es 36, que es lo que Play exige. Ver
[deployment.md](deployment.md#firma-de-android).

## La cuenta

**US$25, pago único** —no anual como Apple—, más verificación de identidad: documento y
dirección.

> **La trampa de calendario.** Una cuenta **personal** nueva no puede publicar en producción de
> entrada: Google pide antes una **prueba cerrada con 12 probadores que se mantengan 14 días
> seguidos**, y después se solicita acceso a producción. Una cuenta de **organización** queda
> exenta, pero pide número **D-U-N-S** de la empresa (gratis, semanas de trámite).
>
> Esa política la han movido varias veces: confírmela al registrarse. Si aplica, son dos semanas
> de calendario, así que conviene arrancar la prueba cerrada en paralelo con lo de Apple y no
> después. Doce probadores salen de los teléfonos del taller, los suyos y los de conocidos; solo
> tienen que aceptar la invitación y dejar la app instalada.

## Ficha de la app

| Campo | Valor |
| --- | --- |
| Nombre (30) | `GarajApp — Taller mecánico` |
| **Descripción corta (80)** | `Órdenes, repuestos, cotizaciones y cobros del taller, desde el teléfono.` |
| Categoría | Empresa · etiquetas: gestión de negocios, productividad |
| Correo de contacto | `mendez01developer@gmail.com` |
| Teléfono de contacto | `+504 9824 2108` |
| Sitio web | `https://www.garajeapp.com` |
| Política de privacidad | `https://www.garajeapp.com/privacidad` |
| Clasificación esperada | Para todo público (3+) |
| Anuncios | No contiene anuncios |
| Compras dentro de la app | No |

La descripción corta es la que sale en el listado, debajo del nombre: son 72 caracteres de los
80 y no repite «taller mecánico», que ya está en el nombre.

### Descripción completa (4000)

La misma de la App Store —ocupa unos 1.600 caracteres, así que cabe de sobra— porque describe el
mismo producto y ya está escrita para quien no conoce el sistema:

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

Play permite unas pocas etiquetas HTML en este campo, pero con texto plano y viñetas se ve igual
en el teléfono y no hay nada que se rompa al pegar.

## Gráficos

Play pide dos piezas que Apple no:

| Pieza | Medida | Notas |
| --- | --- | --- |
| Icono | **512 × 512** PNG de 32 bits | [`marca-garajapp/icono/garajapp-icono-512-play.png`](../marca-garajapp/icono/garajapp-icono-512-play.png), a sangre |
| Gráfico destacado | **1024 × 500** | [`play-store/destacado-1024x500.png`](play-store/destacado-1024x500.png) |
| Capturas de teléfono | mínimo 2, hasta 8 | 1080 × 2400 del emulador |

Están en [`play-store/capturas/`](play-store/capturas/), a **1080 × 2400**, tomadas del emulador
«Medium Phone API 36» con la base de demostración (`demo.md`), que es la que se ve vivida. Las
de iPhone no sirven aquí: se nota que son de iPhone.

| # | Pantalla | Por qué esa |
| --- | --- | --- |
| 1 | Hoy | Lo primero que abre el taller: lo cobrado, lo que urge y el patio |
| 2 | Detalle de una orden | Los pasos y el total; el resto de la orden, en renglones |
| 3 | Reportes | La gráfica de ingresos: es lo que convence al dueño |
| 4 | Inventario | Existencias por sucursal |
| 5 | Ventas | Lo facturado del mes y cuánto salió de mostrador |
| 6 | Cierre de caja | Lo cobrado en el día |

Para rehacerlas: un recorrido de captura que entre con `dueno@tallerdemo.hn`, se pare en cada
pantalla y avise por consola, mientras desde la terminal se dispara
`adb exec-out screencap -p > captura.png`. Tres cosas que cuestan una tarde si no se saben:

- **Las seis paradas van en una sola corrida.** Cada instalación deja en el emulador unos
  340 MB que no se liberan hasta reiniciarlo; seis instalaciones seguidas lo dejan sin espacio
  a la mitad, con `INSTALL_FAILED_INSUFFICIENT_STORAGE`.
- **Los diálogos del sistema salen en la foto.** El emulador saca «System UI isn't responding»
  encima de la pantalla; se apagan con
  `adb shell settings put global hide_error_dialogs 1`.
- **La demostración se siembra el mismo día.** Si se sembró otro día, «Hoy» abre en cero y la
  primera captura —la que más se mira— no enseña nada.

Y el permiso de notificaciones se anula reemplazando `pushMessagingProvider`, igual que en iOS.

El icono va **a sangre**, con las esquinas rellenas del azul de la marca: Play le pone su propia
máscara redondeada encima, y el `garajapp-icono-512.png` de siempre —que ya viene redondeado—
saldría con la esquina mordida dos veces. El de la app y el de la web siguen siendo ese; el
`-play` es solo para la ficha.

El gráfico destacado no lleva transparencia y todo el contenido vive en el centro, porque Play
recorta los bordes en algunos tamaños de pantalla.

## Acceso a la app (las credenciales del revisor)

Play tiene una sección propia para esto —**App access**—, y sin ella el revisor se queda en la
pantalla de entrar igual que el de Apple. Se declara «Todas o algunas funciones están
restringidas» y se dan las mismas credenciales de la ficha de Apple:

```
Nombre de las instrucciones: Cuenta de demostración (perfil Dueño)
Usuario: dueno@tallerdemo.hn
Contraseña: Garaj123!

No hay registro público: las cuentas las crea el dueño del taller para su personal. Esta cuenta
es del perfil Dueño y ve todas las funciones. El taller de demostración tiene meses de historia
para poder revisar reportes y cierre de caja.
```

Esa cuenta ya existe en producción, que es a donde apunta el binario que se sube. Si alguien la
borra probando **Eliminar mi cuenta**, se recrea desde **Plataforma → Talleres → el taller →
Crear otro acceso de Dueño**, y **nunca** con `POST /api/demo/seed`, que borra la base entera.

## Clasificación de contenido (el cuestionario IARC)

Gratis y automático: se responde y sale la clasificación de cada región. Las respuestas de esta
app, todas «No»:

| Pregunta | Respuesta |
| --- | --- |
| Violencia, sangre, lenguaje o contenido sexual | No |
| Drogas, alcohol, tabaco | No |
| Juegos de azar o apuestas, simuladas o con dinero | No |
| Contenido generado por usuarios que se comparta públicamente | **No** — las fotos y notas quedan dentro del taller que las creó; no hay muro, ni perfiles, ni comentarios entre usuarios |
| Compartir la ubicación del usuario con otros | No |
| Permite comprar bienes digitales | No |

Categoría del cuestionario: **Utilidad / productividad / comunicación**, no juego.

## Seguridad de los datos (el formulario que más cuesta)

Aquí hay que ser exacto: Google cruza lo declarado con lo que hace la app, y una declaración
incorrecta es motivo de suspensión.

| Dato | ¿Se recoge? | ¿Se comparte? | Para qué | ¿Obligatorio? |
| --- | --- | --- | --- | --- |
| Nombre | Sí | No | Funcionalidad de la app (la cuenta y quién hizo el trabajo) | Sí |
| Correo | Sí | No | Funcionalidad (entrar a la cuenta) | Sí |
| Teléfono | Sí | No | Funcionalidad (avisar al cliente por WhatsApp) | Sí |
| Fotos | Sí | No | Funcionalidad (evidencia de la reparación) | No — el taller decide si toma fotos |
| Id. del dispositivo | Sí | No | Funcionalidad (avisos push) | No |
| Ubicación, contactos, salud, mensajes, archivos, actividad en apps | No | — | — | — |

Y las tres preguntas transversales:

- **¿Se cifra en tránsito?** Sí, todo va por HTTPS.
- **¿El usuario puede pedir que se borren sus datos?** Sí, y hay las dos vías que Google exige:
  dentro de la app (**Más → Eliminar mi cuenta**, igual en los tres perfiles) y por web.
- **URL para solicitar la eliminación:** `https://www.garajeapp.com/soporte#eliminar-mi-cuenta`
  — esa ancla existe a propósito para este campo.

> «Compartir» en el vocabulario de Google significa entregar los datos a **otra empresa para sus
> propios fines**. Los proveedores que solo procesan por cuenta nuestra —Firebase para los push,
> Cloudflare R2 para las fotos, Render y Supabase para el servidor y la base— no cuentan como
> compartir, y están todos en la política de privacidad. Por eso la columna va en «No».

### Declaraciones y permisos

| Cosa que Play pregunta | Respuesta |
| --- | --- |
| Advertising ID | **No se usa.** No hay anuncios ni analítica de publicidad; los push de Firebase no lo necesitan |
| Cámara y fotos | Sí: la evidencia fotográfica de la reparación, que es una función principal |
| Notificaciones (`POST_NOTIFICATIONS`) | Sí: avisar al técnico de una orden asignada y al taller de una cotización respondida |
| Ubicación, micrófono, contactos, SMS, llamadas | No se piden |
| Acceso a todos los archivos, accesibilidad, uso en segundo plano | No se piden |
| Apps de finanzas / salud / gobierno | No aplica: es una herramienta de trabajo interna del taller |

## Compilar y subir

```bash
cd mobile
flutter build appbundle --release --dart-define=API_URL=https://garaje-app.onrender.com
```

El `.aab` sale en `build/app/outputs/bundle/release/`.

- **Que apunte a producción**, no a localhost ni a pruebas: el revisor va a entrar por ahí.
- **Suba el número de versión en cada subida.** En `pubspec.yaml`, `version: 1.0.0+1`: el `+1` es
  el `versionCode` y Play no acepta dos veces el mismo. El `1.0.0` es lo que ve la gente.
- **Play App Signing**: Google se queda con la llave de firma final y la nuestra pasa a ser la
  *llave de subida*. Resguárdela igual —si se pierde, hay que pedirle a Google que la reemplace,
  y eso son días de correos—.

## Lista antes de mandar

- [ ] Cuenta de Play Console pagada y con la identidad verificada.
- [ ] Si es cuenta personal: prueba cerrada con 12 probadores, 14 días corridos, y acceso a
      producción concedido.
- [x] Icono 512 × 512 (`marca-garajapp/icono/garajapp-icono-512-play.png`).
- [x] Gráfico destacado 1024 × 500 (`play-store/destacado-1024x500.png`).
- [x] Las seis capturas, del emulador Android (`play-store/capturas/`).
- [ ] Descripción corta y completa pegadas.
- [ ] Clasificación de contenido respondida.
- [ ] Seguridad de los datos, con la URL de eliminación.
- [ ] App access con las credenciales del revisor.
- [x] Política de privacidad y soporte responden.
- [x] La cuenta del revisor entra en producción.
- [ ] `.aab` firmado con llave propia, apuntando a la API de producción, con `versionCode` nuevo.

## Mientras tanto: instalar sin tienda

Android no tiene el límite de siete días de Apple. Un APK firmado se instala en el teléfono del
taller y funciona indefinidamente, sin tienda y sin revisión de nadie:

```bash
cd mobile
flutter build apk --release --dart-define=API_URL=https://garaje-app.onrender.com
```

Se le manda el archivo y se instala permitiendo «orígenes desconocidos». Sirve para el cliente de
hoy sin esperar los 14 días de la prueba cerrada; lo que no da es actualizaciones automáticas.

# Publicar en la App Store

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
| Derechos de autor | `2026 Óscar Armando Cruz Méndez` |
| URL de soporte | `https://garajapp.hn/soporte` |
| URL de política de privacidad | `https://garajapp.hn/privacidad.html` |

> Las dos URL tienen que responder **antes** de mandar a revisar: un enlace roto es rechazo casi
> seguro. `privacidad.html` ya existe en el sitio; falta que `soporte` exista aunque sea como una
> página con el WhatsApp y el correo.

### Palabras clave (100 caracteres, separadas por coma, sin espacios)

```
taller,mecanico,motos,ordenes,servicio,repuestos,inventario,cotizacion,factura,honduras
```

Sin acentos y sin repetir palabras que ya están en el nombre o el subtítulo: Apple las indexa
igual y repetirlas desperdicia caracteres.

### Texto promocional (170)

```
Lleve el taller desde el teléfono: reciba el vehículo, arme la reparación, cobre y avísele al
cliente por WhatsApp. Sin papeles sueltos y sin perder de vista lo que se debe.
```

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

## Capturas

En [`app-store/capturas-6.9/`](app-store/capturas-6.9/), tomadas del simulador de iPhone 16 Pro
Max con la base de demostración (`docs/demo.md`), que es la que se ve vivida.

| # | Pantalla | Por qué esa |
| --- | --- | --- |
| 1 | Bandeja de órdenes | Lo primero que abre el taller, con los ingresos del día arriba |
| 2 | Detalle de una orden | Los pasos, el técnico y el enlace para el cliente |
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
> anterior, hay que borrar el simulador (`xcrun simctl erase`) antes de repetir.

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

La aplicación no recopila datos de menores ni contenido generado por usuarios que se comparta
públicamente. Las fotos que se toman quedan asociadas a la orden de trabajo del taller.
```

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

- [ ] Membresía del Apple Developer Program activa, a nombre de Óscar Armando Cruz Méndez.
- [ ] App ID `com.garaj.garajApp` registrado y la app creada en App Store Connect.
- [ ] `flutter build ipa --dart-define=API_URL=https://<api de producción>` — que **no** apunte a
      localhost ni a pruebas.
- [ ] `--build-name` y `--build-number` puestos al compilar; el número no se puede repetir.
- [ ] Las dos URL (soporte y privacidad) responden.
- [ ] La cuenta del revisor entra **en producción**.
- [ ] Cumplimiento de exportación: usa HTTPS estándar → exento.
- [ ] **Borrar la cuenta desde la app**: Apple lo exige a las apps con cuentas. Aquí las crea el
      taller y no hay registro público; hay que explicarlo en las notas y estar listos para
      agregar la opción si el revisor insiste. Es el riesgo de rechazo más probable.

# Datos de demostración

Para enseñar la aplicación hace falta un taller que se vea vivido. Con cuatro órdenes de
prueba la gráfica de ingresos es una sola barra y no se entiende para qué sirve; con seis
semanas de historia se ve la forma del negocio —los lunes cargados, los sábados a medias, los
domingos cerrado— y ahí sí se explica solo.

El `DemoSeeder` genera eso para el **Taller Demo**: tres sucursales, tres técnicos, doce
clientes, unos 240 trabajos repartidos en el calendario, cotizaciones aprobadas y rechazadas,
kardex con compras de reposición y dos repuestos bajo mínimo.

**Solo motocicletas.** El sistema maneja autos igual de bien, pero el taller de la
demostración trabaja motos y un Corolla en medio se nota postizo. Para volver a incluir autos hay que
añadir vehículos de tipo `Car` y entradas al catálogo de `Jobs` del sembrador; el resto no
distingue.

> **Borra todo antes de sembrar.** Es para una base de demostración, no para una con datos
> reales. Por eso lleva tres cerrojos.

## Cómo dispararlo

> **Antes de dispararlo en producción, lea esto.** La siembra **borra la base entera**, así que
> solo se puede correr mientras ahí no haya ningún taller real. Una vez dado de alta el primer
> cliente, este endpoint deja de ser una opción: ver el orden en
> [deployment.md](deployment.md#puesta-en-marcha-del-primer-cliente).

1. En Render, agregar la variable `Demo__AllowSeeding` con valor `true` y esperar el redespliegue.
2. Entrar como el Dueño que exista en esa base —el correo de abajo es el que queda **después**
   de sembrar— y llamar al endpoint:

```bash
TOKEN=$(curl -s https://garaje-app.onrender.com/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"dueno@tallerdemo.hn","password":"Garaj123!"}' \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])')

curl -s https://garaje-app.onrender.com/api/demo/seed \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"confirm":"BORRAR Y SEMBRAR","weeks":6}'
```

3. **Quitar `Demo__AllowSeeding`.** Con la variable ausente el endpoint responde 404 y no
   existe para nadie: es lo que impide borrar una base real por equivocación.

Los tres cerrojos son la variable de configuración, el rol de Dueño y la frase
`BORRAR Y SEMBRAR` en el cuerpo. Ninguno sobra: la operación no tiene vuelta atrás.

En local es lo mismo, arrancando la API con `Demo__AllowSeeding=true` y apuntando a
`http://localhost:5080`.

## Qué queda sembrado

| | |
| --- | --- |
| Taller | Taller Demo · ISV 15% · lempiras |
| Sucursales | Sucursal Centro (CEN) y Sucursal Sur (SUR), San Pedro Sula · Sucursal Puerto (PTO), Puerto Cortés |
| Usuarios | 1 Dueño, 3 Técnicos, 2 Clientes — contraseña `Garaj123!` |
| Clientes | 12, con 17 motocicletas |
| Catálogos | 26 repuestos y 14 servicios de mano de obra, todo de moto |
| Historia | ~240 órdenes entregadas y facturadas en 6 semanas |
| Hoy | 9 órdenes abiertas (una en cada estado), 3 requerimientos sin atender, 1 cotización esperando respuesta, 2 repuestos bajo mínimo |

Accesos:

| Perfil | Correo |
| --- | --- |
| Dueño | `dueno@tallerdemo.hn` — Mario Alvarado |
| Técnico (Centro y Sur) | `tecnico1@tallerdemo.hn` — Kevin Discua |
| Técnico (Sur) | `tecnico2@tallerdemo.hn` — Nelson Aguilar |
| Técnico (Puerto) | `tecnico3@tallerdemo.hn` — Wilmer Castellanos |
| Cliente | `cliente1@tallerdemo.hn` — Ana Lucía Fajardo |
| Cliente | `cliente2@tallerdemo.hn` — Marvin Alexis Portillo |

**Todo el taller es inventado, y a propósito.** Este es el taller que se le enseña a quien
todavía no ha comprado, así que ningún dato puede parecerse al de un taller de verdad: los
teléfonos van al bloque 9000 xxxx, que no existe, para que un enlace de WhatsApp no le caiga
a nadie, y los correos terminan en `tallerdemo.hn`. El taller del cliente **no se siembra**:
se crea con `provision-tenant` (ver [deployment.md](deployment.md)).

## Decisiones que conviene conocer

- **La semilla del azar es fija.** Dos ejecuciones dan el mismo taller, así que si hay que
  repetir la siembra en medio de una presentación, los números que ya se enseñaron siguen
  siendo los mismos.
- **Las fechas se escriben hacia atrás.** El interceptor de auditoría respeta la fecha que
  traiga la entidad; si la sellara con la hora actual, las seis semanas colapsarían en un
  solo día y el reporte no mostraría nada.
- **El día en curso va a medias**, con las entregas de la mañana: si no, la tarjeta de "hoy"
  del tablero abre en cero, que es justo lo que no se quiere enseñar.
- **Los domingos el taller no abre**, menos el día en curso: si la presentación cae domingo,
  «Hoy» tiene que enseñar algo igual.
- **Cada día se venden de cero a tres repuestos de mostrador**, sin orden de trabajo detrás y
  sin ISV —el ISV solo lo lleva la factura con CAI—. Sin ellos el dato de «solo venta» del
  reporte de ingresos aparecía en cero, que es como decir que no existe.
- **Hay compras de reposición cada diez días.** Sin ellas, seis semanas de consumo dejan media
  bodega bajo mínimo y la alerta deja de significar algo. Al final quedan dos repuestos bajo
  el umbral, que es lo que hace creíble la advertencia.
- **Solo uno de cada cuatro trabajos pasó por cotización.** En motos casi todo se autoriza
  de palabra en el mostrador; se cotiza cuando el trabajo es grande y el dueño quiere pensarlo.
- **La sucursal del puerto factura menos y carga menos inventario** que las dos de San Pedro. Repartir por
  igual haría que el desglose por sucursal fueran tres barras idénticas.
- **Los motivos, diagnósticos y repuestos concuerdan entre sí**: quien entra porque le salta
  la cadena sale con un kit de arrastre, no con una batería. Es lo que hace que se lea como
  un taller y no como relleno.
- **Las fotos que hubiera en el bucket quedan huérfanas.** Borrarlas exigiría recorrer el
  almacenamiento y no vale la pena en una base que se va a pisar de todos modos.

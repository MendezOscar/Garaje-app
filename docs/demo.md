# Datos de demostración

Para enseñar la aplicación hace falta un taller que se vea vivido. Con cuatro órdenes de
prueba la gráfica de ingresos es una sola barra y no se entiende para qué sirve; con unas
semanas de historia se ve la forma del negocio —los lunes cargados, los sábados a medias, los
domingos cerrado— y ahí sí se explica solo.

El `DemoSeeder` genera eso para el **Taller Demo**: tres sucursales, tres técnicos, doce
clientes, trabajos repartidos en el calendario, cotizaciones aprobadas y rechazadas, kardex
con compras de reposición y dos repuestos bajo mínimo.

**Carros y pick-ups.** Lo que de verdad entra a un taller en Honduras: Corollas con doscientos
mil kilómetros, Hilux de empresa, el Accent de la señora. Los tres catálogos —mano de obra,
repuestos y trabajos— son los de ese taller, con precios de casa de repuestos de San Pedro en
lempiras. En una capacitación eso no es decoración: el que mira reconoce su trabajo, o no
reconoce nada. Para volver a motos hay que cambiar los tres catálogos y `Jobs`; el resto del
sembrador no distingue.

**El trabajo sale con peso.** El cambio de aceite y el mantenimiento salen seis veces más que
una banda de tiempo, porque así es un taller. Sin peso, el reporte quedaba plano y el catálogo
de trabajos frecuentes no se entendía para qué sirve.

## Las dos formas de sembrar

| | Qué borra | Para qué |
| --- | --- | --- |
| `seed-demo` (comando) | **Nada.** Exige que el taller no exista | Una base con clientes de verdad |
| `POST /api/demo/seed` | **La base entera**, todos los talleres | Una base de demostración, o local |

### El comando, cuando hay clientes en esa base

Es el camino normal desde que existe el primer cliente. Rehacer la demostración son dos
pasos, y el primero —el borrado— es el que sabe llevarse un taller solo:

```bash
cd backend

# 1. Borrar el Taller Demo, y únicamente ese. Sin --confirm solo cuenta lo que se llevaría.
dotnet run --project src/Garaj.Api -- delete-tenant \
  --name "Taller Demo" --confirm "Taller Demo"

# 2. Sembrarlo de nuevo. No borra nada: si el taller existe, se niega.
dotnet run --project src/Garaj.Api -- seed-demo \
  --weeks 3 --confirm "Taller Demo"
```

`--weeks` va de 1 a 26 y por omisión son 3. Para una capacitación, tres semanas bastan: se ve
la forma del negocio sin que la lista de órdenes sea inmanejable.

El taller nace **sin suscripción**: hay que ponerle plan y vencimiento desde el panel de
plataforma, o al entrar dirá que está vencido.

La contraseña de los usuarios sale de `Demo__Password`; sin esa variable es `Garaj123!`, que
sirve en local pero no debe quedar en una base publicada —este repositorio es público—.

### El endpoint, que borra la base entera

> **Antes de dispararlo en producción, lea esto.** La siembra **borra la base entera**, así que
> solo se puede correr mientras ahí no haya ningún taller real. Una vez dado de alta el primer
> cliente, este endpoint deja de ser una opción: use el comando de arriba. Ver el orden en
> [deployment.md](deployment.md#puesta-en-marcha-del-primer-cliente).

1. En Render, agregar la variable `Demo__AllowSeeding` con valor `true` y esperar el redespliegue.
2. Entrar como el Dueño que exista en esa base —el correo de abajo es el que queda **después**
   de sembrar— y llamar al endpoint:

```bash
CLAVE='la contraseña del Dueño en esa base; no se escribe en el repositorio'

TOKEN=$(curl -s https://garaje-app.onrender.com/api/auth/login \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"dueno@tallerdemo.hn\",\"password\":\"$CLAVE\"}" \
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
| Usuarios | 1 Dueño, 3 Técnicos, 2 Clientes — todos con la misma contraseña |
| Clientes | 12, con 17 vehículos: carros, pick-ups y dos flotas de empresa |
| Catálogos | 32 repuestos y 17 servicios de mano de obra, de carro |
| Historia | ~80 órdenes entregadas y facturadas en 3 semanas (`--weeks` lo cambia) |
| Hoy | 9 órdenes abiertas (una en cada estado), 3 requerimientos sin atender, 1 cotización esperando respuesta, 2 repuestos bajo mínimo |

Accesos:

| Perfil | Correo |
| --- | --- |
| Dueño | `dueno@tallerdemo.hn` — Mario Alvarado |
| Encargado (Puerto) | `encargado@tallerdemo.hn` — Sandra Interiano |
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
- **Uno de cada tres trabajos pasó por cotización.** En carros el trabajo grande —embrague,
  banda de tiempo— no se autoriza de palabra: el cliente quiere el número por escrito.
- **Las cantidades son las del carro:** el aceite va de cuatro a cinco litros, las bujías de a
  cuatro, los amortiguadores y las rótulas por pares. Una orden con «1 amortiguador» la
  descarta de un vistazo cualquiera que sepa del oficio.
- **La sucursal del puerto factura menos y carga menos inventario** que las dos de San Pedro. Repartir por
  igual haría que el desglose por sucursal fueran tres barras idénticas.
- **Los motivos, diagnósticos y repuestos concuerdan entre sí**: quien entra porque se le
  calienta el carro sale con bomba de agua y refrigerante, no con una batería. Es lo que hace
  que se lea como un taller y no como relleno.
- **Las placas llevan la forma hondureña**, P de particular y C de carga: la placa es el
  detalle que delata un dato inventado.
- **Las fotos que hubiera en el bucket quedan huérfanas.** Borrarlas exigiría recorrer el
  almacenamiento y no vale la pena en una base que se va a pisar de todos modos.

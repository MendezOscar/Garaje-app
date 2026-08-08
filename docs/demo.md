# Datos de demostración

Para enseñar la aplicación hace falta un taller que se vea vivido. Con cuatro órdenes de
prueba la gráfica de ingresos es una sola barra y no se entiende para qué sirve; con seis
semanas de historia se ve la forma del negocio —los lunes cargados, los sábados a medias, los
domingos cerrado— y ahí sí se explica solo.

El `DemoSeeder` genera eso para el **Taller RVM**: tres sucursales, tres técnicos, doce
clientes, unos 240 trabajos repartidos en el calendario, cotizaciones aprobadas y rechazadas,
kardex con compras de reposición y dos repuestos bajo mínimo.

**Solo motocicletas.** El sistema maneja autos igual de bien, pero RVM trabaja motos y una
demostración con un Corolla en medio se nota postiza. Para volver a incluir autos hay que
añadir vehículos de tipo `Car` y entradas al catálogo de `Jobs` del sembrador; el resto no
distingue.

> **Borra todo antes de sembrar.** Es para una base de demostración, no para una con datos
> reales. Por eso lleva tres cerrojos.

## Cómo dispararlo

1. En Render, agregar la variable `Demo__AllowSeeding` con valor `true` y esperar el redespliegue.
2. Entrar como Dueño y llamar al endpoint:

```bash
TOKEN=$(curl -s https://garaje-app.onrender.com/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"eduar@rvm.hn","password":"Garaj123!"}' \
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
| Taller | Taller RVM · ISV 15% · lempiras |
| Sucursales | RVM 13 Calle (C13) y RVM 10 Calle (C10), San Pedro Sula · RVM Cuyamel (CUY), Omoa |
| Usuarios | 1 Dueño, 3 Técnicos, 2 Clientes — contraseña `Garaj123!` |
| Clientes | 12, con 17 motocicletas |
| Catálogos | 26 repuestos y 14 servicios de mano de obra, todo de moto |
| Historia | ~240 órdenes entregadas y facturadas en 6 semanas |
| Hoy | 9 órdenes abiertas (una en cada estado), 3 requerimientos sin atender, 1 cotización esperando respuesta, 2 repuestos bajo mínimo |

Accesos:

| Perfil | Correo |
| --- | --- |
| Dueño | `eduar@rvm.hn` — Eduar Rivera |
| Técnico (13 y 10 Calle) | `caleb@rvm.hn` — Caleb Rivera |
| Técnico (Cuyamel) | `marlon@rvm.hn` — Marlon Interiano |
| Cliente | `daleth.moran@gmail.com` — Daleth Morán |
| Cliente | `oscar.mendez@gmail.com` — Óscar Méndez |

**Datos de relleno que hay que corregir antes de enseñárselo al dueño de RVM:** la razón
social, el RTN, los teléfonos y los correos del taller salen impresos en el PDF de la
cotización y en la página pública. Los técnicos Keny Alvarado y Marlon Interiano también son
inventados: con tres sucursales, un solo técnico no da la talla.

## Decisiones que conviene conocer

- **La semilla del azar es fija.** Dos ejecuciones dan el mismo taller, así que si hay que
  repetir la siembra en medio de una presentación, los números que ya se enseñaron siguen
  siendo los mismos.
- **Las fechas se escriben hacia atrás.** El interceptor de auditoría respeta la fecha que
  traiga la entidad; si la sellara con la hora actual, las seis semanas colapsarían en un
  solo día y el reporte no mostraría nada.
- **El día en curso va a medias**, con las entregas de la mañana: si no, la tarjeta de "hoy"
  del tablero abre en cero, que es justo lo que no se quiere enseñar.
- **Hay compras de reposición cada diez días.** Sin ellas, seis semanas de consumo dejan media
  bodega bajo mínimo y la alerta deja de significar algo. Al final quedan dos repuestos bajo
  el umbral, que es lo que hace creíble la advertencia.
- **Solo uno de cada cuatro trabajos pasó por cotización.** En motos casi todo se autoriza
  de palabra en el mostrador; se cotiza cuando el trabajo es grande y el dueño quiere pensarlo.
- **Cuyamel factura menos y carga menos inventario** que las dos de San Pedro. Repartir por
  igual haría que el desglose por sucursal fueran tres barras idénticas.
- **Los motivos, diagnósticos y repuestos concuerdan entre sí**: quien entra porque le salta
  la cadena sale con un kit de arrastre, no con una batería. Es lo que hace que se lea como
  un taller y no como relleno.
- **Las fotos que hubiera en el bucket quedan huérfanas.** Borrarlas exigiría recorrer el
  almacenamiento y no vale la pena en una base que se va a pisar de todos modos.

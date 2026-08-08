# Datos de demostración

Para enseñar la aplicación hace falta un taller que se vea vivido. Con cuatro órdenes de
prueba la gráfica de ingresos es una sola barra y no se entiende para qué sirve; con seis
semanas de historia se ve la forma del negocio —los lunes cargados, los sábados a medias, los
domingos cerrado— y ahí sí se explica solo.

El `DemoSeeder` genera eso: un taller con dos sucursales, tres técnicos, doce clientes, unos
120 trabajos repartidos en el calendario, cotizaciones aprobadas y rechazadas, kardex con
compras de reposición y dos repuestos bajo mínimo.

> **Borra todo antes de sembrar.** Es para una base de demostración, no para una con datos
> reales. Por eso lleva tres cerrojos.

## Cómo dispararlo

1. En Render, agregar la variable `Demo__AllowSeeding` con valor `true` y esperar el redespliegue.
2. Entrar como Dueño y llamar al endpoint:

```bash
TOKEN=$(curl -s https://garaje-app.onrender.com/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"dueno@maradiaga.hn","password":"Garaj123!"}' \
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
| Taller | Taller Mecánico Maradiaga · ISV 15% · lempiras |
| Sucursales | Maradiaga Comayagüela (TGU) y Maradiaga San Pedro (SPS) |
| Usuarios | 1 Dueño, 3 Técnicos, 2 Clientes — contraseña `Garaj123!` |
| Clientes | 12, con 16 vehículos entre autos y motos |
| Catálogos | 20 repuestos y 13 servicios de mano de obra |
| Historia | ~120 órdenes entregadas y facturadas en 6 semanas |
| Hoy | 8 órdenes abiertas (una en cada estado), 3 requerimientos sin atender, 1 cotización esperando respuesta, 2 repuestos bajo mínimo |

Accesos:

| Perfil | Correo |
| --- | --- |
| Dueño | `dueno@maradiaga.hn` |
| Técnico (Comayagüela) | `tecnico1@maradiaga.hn` |
| Técnico (San Pedro) | `tecnico3@maradiaga.hn` |
| Cliente | `cliente@maradiaga.hn` |

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
- **Solo uno de cada tres trabajos pasó por cotización.** El resto son trabajos de mostrador
  que el cliente autoriza de palabra; fingir que todo se cotiza sería falso.
- **Los motivos, diagnósticos y repuestos concuerdan entre sí**: quien entra por frenos que
  chillan sale con pastillas y discos, no con un radiador. Es lo que hace que se lea como un
  taller y no como relleno.
- **Las fotos que hubiera en el bucket quedan huérfanas.** Borrarlas exigiría recorrer el
  almacenamiento y no vale la pena en una base que se va a pisar de todos modos.

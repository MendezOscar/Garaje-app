# Talleres de equipos

Hasta la versión 1.5 la app sirve a un taller mecánico: todo cuelga de un vehículo. Esto
abre el alcance a los talleres que reparan equipos —computadoras, teléfonos, aires
acondicionados, línea blanca— sin tocar nada de lo que ya funciona para carros y motos.

## La decisión de fondo

`Vehicles` **no se toca**. Los equipos van en tablas nuevas, y el taller declara en su
configuración con qué trabaja. Un taller mecánico no ve un solo cambio.

La alternativa —renombrar `Vehicle` a algo genérico— toca 66 archivos del backend y 51 de
app y panel, obliga a una migración grande y no agrega una sola función.

## Esquema

### `Tenant.WorksWith`

`Vehicles` (lo de hoy, por defecto) o `Equipment`. Decide qué altas y qué pantallas se
habilitan. El servidor rechaza con 404 —no 403— lo que no corresponde al modo, igual que
con cualquier recurso fuera de alcance.

Un taller mixto no está en el alcance, pero sale después sin tocar el esquema: las dos
llaves conviven nulables en la orden, así que basta habilitar las dos altas.

### `EquipmentTypes`

Catálogo por taller: "Laptop", "Aire acondicionado", "Celular". Nombre e ícono, editable.
Es lo que en vehículos resuelve el enum `VehicleType`, pero como tabla: cada rubro tiene
sus categorías y no las podemos adivinar desde aquí.

### `Equipment`

| Campo | Para qué |
|---|---|
| `CustomerId`, `EquipmentTypeId` | de quién es y qué es |
| `Brand`, `Model` | marca y modelo, igual que en vehículos |
| `SerialNumber` | serie o IMEI, normalizada y buscable |
| `Tag` | etiqueta interna del taller, para lo que llega sin serie legible |
| `Accessories` | lo que entrega el cliente: cargador, control, funda |
| `Attributes` | jsonb con lo propio de cada tipo —BTU, procesador, voltaje— sin una tabla por rubro |
| `Notes`, `IsActive` | igual que vehículos |

### Los enganches

`WorkOrder`, `ServiceRequest` y `Appointment` llevan `VehicleId` y `EquipmentId`, los dos
nulables, con un CHECK de exactamente uno. `Claim` cuelga de la orden y no se toca.

## Lo que se decidió

1. **Cómo se busca un equipo en el mostrador.** Por cliente, teléfono y número de orden;
   la serie y la etiqueta son extras. En vehículos la placa alcanza porque está a la vista;
   una serie muchas veces no se lee o el cliente no la sabe.
2. **La hoja de recepción sigue siendo opcional.** Un aire acondicionado no la amerita y
   obligarla haría que se llene con cualquier cosa, que es peor que no tenerla. Si más
   adelante hace falta, se vuelve una casilla por tipo de equipo, no del taller entero.
3. **La garantía es la que ya existe**: los días que el taller le pone al trabajo al
   facturar. No se registra la garantía del fabricante del equipo.

## Fases

| # | Qué | Sube a tiendas |
|---|---|---|
| 1 | Modo del taller, `EquipmentTypes`, `Equipment`, las llaves y la migración | no |
| 2 | `/api/equipment` y el bloque `asset` en los DTOs; abrir orden, requerimiento y cita con un equipo | no |
| 3 | Panel web en modo equipos | no |
| 4 | App en modo equipos | **sí** |
| 5 | Recepción, PDF, portal del cliente y recordatorios por fecha en vez de kilometraje | sí |
| 6 | Taller demo de equipos para las capturas, manual, videos, landing y fichas de tiendas | metadata |

Con las fases 1 a 3 ya se puede dar de alta un taller de electrónica y operarlo completo
desde el panel web; la app sigue siendo de vehículos hasta la 4.

## La regla que no se rompe

Los campos `vehicle*` de los DTOs **se quedan**. La versión que está publicada en las
tiendas los lee, y una app instalada no se actualiza porque nosotros despleguemos: lo nuevo
se agrega al lado, nunca en lugar de.

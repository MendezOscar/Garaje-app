# Modelo de dominio

Las entidades viven en [backend/src/Garaj.Domain/Entities/](../backend/src/Garaj.Domain/Entities/).
Casi todas heredan de `TenantEntity`: llevan `TenantId`, auditoría (`CreatedAt`,
`CreatedByUserId`, `UpdatedAt`, `UpdatedByUserId`) y quedan sujetas al global query filter.

## Aislamiento

```
Tenant (el taller)
 └── Branch (sucursal)  ·  Customer  ·  Part  ·  LaborService  ·  AppUser
```

`Tenant` es la raíz y no lleva filtro porque *es* el tenant. `AppUser` tampoco: el login
tiene que encontrar al usuario por email antes de saber a qué taller pertenece, así que para
listar usuarios se usa `GarajDbContext.UsersInTenant`.

Lo que es del **tenant**: clientes, vehículos, catálogo de repuestos y de mano de obra.
Lo que es de la **sucursal**: órdenes de trabajo, existencias, movimientos, cotizaciones y ventas.

## Flujo de servicio

```
ServiceRequest ──aprobado──> WorkOrder ──cerrada──> Sale
     │                          │
     │                          ├── WorkOrderTask       (paso a paso)
     │                          ├── WorkOrderPart       (repuestos consumidos)
     │                          └── WorkOrderStatusHistory (timeline del cliente)
     │
     └── Quote ──> QuoteLine    (se comparte por wa.me con PublicToken)
```

`WorkOrderStatus`: `Received → Diagnosing → WaitingApproval → WaitingParts → InProgress →
Testing → Ready → Delivered`, más `Cancelled`. Las transiciones válidas están centralizadas en
[WorkOrderStatusTransitions](../backend/src/Garaj.Domain/Rules/WorkOrderStatusTransitions.cs),
porque el móvil puede enviar un cambio desde la cola offline sobre una orden que ya avanzó.

`WorkOrder.LaborMode` decide **cómo se cobra la mano de obra**, y son dos caminos que no se
cruzan:

- **Catálogo** — cada `WorkOrderTask` apunta con `LaborServiceId` a un `LaborService` y la
  orden cobra la suma de los pasos. `WorkOrderTask.PriceWith(servicio)` y
  `LaborService.PriceFor(horas)` resuelven el precio en un solo lugar, para que el paso, la
  cotización y la factura nunca muestren tres números distintos: precio fijo, u horas ×
  tarifa con las horas reales si el técnico las registró y las estimadas si no. Un paso sin
  servicio es trabajo que no se cobra: queda registrado, pero no llega a la factura.
- **Manual** — los pasos son solo el registro de lo que se hizo y el precio es uno solo,
  `WorkOrder.ManualLaborTotal`, que viaja a la cotización y a la factura como una única línea
  "Mano de obra". Es para el taller que acuerda un precio por el trabajo completo.

Excluyentes a propósito: con precios por paso *y* un total global habría dos maneras de sumar
lo mismo, y nadie sabría cuál mira la factura. Cambiar de modo no borra lo registrado en el
otro —el total sobrevive a ir y volver—, simplemente se deja de mirar.

## Trabajos frecuentes

`JobTemplate` —con `JobTemplateTask` y `JobTemplatePart`— es lo que el taller repite, guardado
para no volver a teclearlo. Es del **tenant**, como `Part` y `LaborService`: el trabajo se hace
igual en las dos sucursales, y lo que cambia entre ellas —las existencias— no vive aquí.

**No guarda precios, guarda referencias.** El importe lo resuelven `LaborService.PriceFor` y
`Part.SalePrice` al consultarla, para que subir mañana el precio de un repuesto no deje veinte
plantillas mintiendo.

**Aplicarla no toca el inventario.** Un `WorkOrderPart` del catálogo genera su `StockMovement` de
salida al crearse, y al aplicar la plantilla el trabajo apenas empieza: los repuestos vuelven
como sugerencia y se cargan uno a uno cuando de verdad se instalan. `StockMovement` sigue
contando solo lo que salió del estante.

`UsageCount` existe para ordenar la lista por lo más usado —en orden alfabético, treinta
plantillas son inservibles— y por eso una plantilla se da de baja en vez de borrarse.

## Suscripción del taller

Lo que el taller nos paga a nosotros vive en el propio `Tenant` —`PlanName`, `MonthlyFee`,
`PaidThrough`, `GraceDays`, `UnblockedThrough`— y no en una entidad aparte: es parte de lo que
define al taller como cliente nuestro, y así una sola lectura por clave primaria decide si puede
trabajar. El historial sí va aparte, en `SubscriptionPayment`, que es la respuesta a «¿yo ya te
pagué junio?» y por eso no se edita ni se borra.

`PaidThrough` es `DateOnly` y no un instante porque una fecha de pago es un día, no una hora. El
«hoy» contra el que se compara es **el de Honduras**, restándole el huso al reloj del servidor
(`DateTimeProviderExtensions.Today`): el día UTC cambia a las 6 de la tarde de acá, así que
contándolo en UTC el taller pagado hasta el jueves se quedaba sin poder trabajar el jueves a las
seis, mientras cierra el día.

**El estado no se guarda, se calcula**: `SubscriptionRules.For(tenant, hoy)` devuelve `Active`,
`DueSoon`, `Grace`, `ReadOnly` o `Suspended`. Calcularlo evita el proceso nocturno que marca
estados y que un día no corre, y deja una sola definición para las tres bocas que preguntan.
Vencer deja el taller **en modo consulta**, no sin datos: puede ver sus órdenes y sus clientes,
pero no registrar trabajo nuevo. Un taller sin `PaidThrough` no se bloquea nunca.

`IsActive` es otra cosa y ahora sí se lee: la suspensión a mano, que corta el login entero.

`SubscriptionPayment` **no implementa `ITenantEntity`** aunque lleve `TenantId`. El filtro global
aísla a un taller de otro, y esto no lo lee ningún taller: lo lee el perfil `Platform`, que no
tiene taller —y por eso mismo no ve órdenes, clientes ni existencias de nadie—. Por la misma
razón `AppUser` queda fuera del interceptor que rellena el tenant al guardar: al usuario de
plataforma hay que dejarlo sin taller, que es lo que lo mantiene ciego.

## Usuarios y acceso

`AppUser` no lleva el filtro global de tenant —el login tiene que encontrar al usuario por
correo antes de saber de qué taller es—, así que para listarlos se usa
`GarajDbContext.UsersInTenant`. El Técnico se ata a sus sucursales con `UserBranch`: sin
ninguna no vería ninguna orden, porque su bandeja filtra por asignación pero el resto de la
app filtra por sucursal. El Dueño no lleva filas de asignación: ve todo el taller.

`Customer.AppUserId` es el acceso a la app del cliente, y es **opcional**: la mayoría de los
clientes de un taller nunca lo pide, y crearle un usuario a cada uno llenaría la lista de
accesos que nadie abre. Se le abre desde su ficha, nunca al revés, para que todo usuario con
perfil Cliente corresponda a alguien del padrón; y solo uno por cliente, porque un segundo
acceso a los mismos vehículos no habría cómo quitarlo desde ninguna pantalla.

Un usuario **no se borra, se da de baja**: las órdenes, las fotos y los movimientos de bodega
llevan su nombre, y borrarlo dejaría el histórico sin responsable. El Dueño tampoco puede
desactivarse a sí mismo: dejaría el taller sin quien administre y sin forma de revertirlo.

## Evidencia fotográfica

`MediaAttachment` se relaciona de forma polimórfica por `(OwnerType, OwnerId)` con
`ServiceRequest`, `WorkOrder`, `WorkOrderTask` o `Quote` — sin FK, se valida en la aplicación.
Guarda solo la clave del objeto; el binario vive en el bucket S3-compatible y se sirve por
URL prefirmada. `IsConfirmed` distingue las subidas completadas de las que quedaron a medias
y hay que purgar; `IsVisibleToCustomer` decide qué ve el cliente.

## Inventario

`StockMovement` es la **única fuente de verdad** y es inmutable: un error se corrige con un
movimiento de ajuste, nunca editando el histórico. `StockItem.Quantity` es un saldo cacheado
por sucursal que se recalcula al registrar cada movimiento, y `ResultingQuantity` deja el
saldo posterior en cada fila para poder auditar sin recorrer todo el kardex.

## Cotizaciones y ventas

`Quote.PublicToken` es la única credencial que protege el link `/q/{token}` que se manda por
WhatsApp: aleatorio, único global (la consulta corre sin filtro de tenant) y no reutilizable.

`QuoteLine` y `SaleLine` llevan `LineType` (`Part` | `Labor`). Esa división es la que alimenta
los reportes de ingresos por repuestos vs mano de obra. `SaleLine.UnitCost` congela el costo
al momento de la venta para poder calcular margen sin depender del catálogo actual.

`SalePayment` es cada entrada de dinero de una venta. **Es la única fuente de lo cobrado**,
igual que `StockMovement` lo es de las existencias: la venta no guarda un "pagado" que haya
que mantener al día, se suma. Una venta de contado también genera su abono —por el total y
con la fecha de la venta— para que no existan dos maneras distintas de saber qué entró en
caja. Tampoco hay una bandera "es a crédito": una venta lo es mientras `Total` sea mayor que
la suma de sus abonos, y `Sale.DueDate` solo dice para cuándo se acordó terminar de pagar.

## Avisos

`Notification` es una fila por destinatario, no por hecho: el mismo cambio de estado genera
tantas como personas haya que avisar. Se guarda siempre, aunque el push falle o el usuario no
tenga la app —la campana es el canal principal y el push solo empuja a abrirla—.
`ReadAt` guarda el cuándo, no un sí/no.

`DeviceToken.Token` es único **en todo el sistema**, no por taller: identifica al aparato. Si
un teléfono cambia de manos, la fila se reasigna al nuevo usuario en lugar de duplicarse; si
no, el dueño anterior seguiría recibiendo los avisos del nuevo.

## Correlativos

`Branch` guarda `WorkOrderSequence`, `QuoteSequence` y `SaleSequence`. Los números quedan como
`MTZ-000123`, `COT-SUR-000045`, `VTA-SUR-000312` — legibles y únicos por taller.

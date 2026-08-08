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

## Evidencia fotográfica

`MediaAttachment` se relaciona de forma polimórfica por `(OwnerType, OwnerId)` con
`ServiceRequest`, `WorkOrder` o `WorkOrderTask` — sin FK, se valida en la aplicación.
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

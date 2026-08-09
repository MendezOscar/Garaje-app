# Contrato de la API

Swagger en desarrollo: <https://localhost:7080/swagger>

Todos los endpoints bajo `/api` requieren `Authorization: Bearer <accessToken>`, salvo los
marcados como anónimos. Los errores salen como `application/problem+json` con el mensaje en
`detail`.

## Implementado

| Método | Ruta | Auth | Qué hace |
| --- | --- | --- | --- |
| POST | `/api/auth/login` | anónimo | Devuelve access token, refresh token y perfil |
| POST | `/api/auth/refresh` | anónimo | Rota el refresh token y emite un par nuevo |
| POST | `/api/auth/logout` | anónimo | Revoca el refresh token de la sesión |
| GET | `/api/auth/me` | cualquiera | Perfil del usuario autenticado |
| GET | `/api/auth/ping-owner` | Owner | Prueba de humo de las policies por rol |
| GET | `/health` | anónimo | Estado de la API y de la base |

### Fase 1 — núcleo operativo

Todas las listas devuelven `PagedResult` (`items`, `total`, `page`, `pageSize`) y aceptan
`page` y `pageSize` (tope 200).

| Método | Ruta | Auth | Qué hace |
| --- | --- | --- | --- |
| GET | `/api/branches` | cualquiera | Sucursales visibles; el Técnico solo ve las suyas |
| POST/PUT | `/api/branches[/{id}]` | Owner | Alta y edición; el código debe ser único |
| GET | `/api/users?role=` | Owner | Usuarios del taller |
| POST/PUT | `/api/users[/{id}]` | Owner | Alta y edición, con asignación a sucursales |
| POST | `/api/users/{id}/password` | Owner | Restablece contraseña y cierra sus sesiones |
| GET | `/api/customers?search=` | cualquiera | Busca por nombre, teléfono o placa |
| POST | `/api/customers` | Owner o Técnico | Lo registra quien recibe el vehículo |
| PUT | `/api/customers/{id}` | Owner | Edición |
| GET | `/api/vehicles?search=&customerId=` | cualquiera | Vehículos |
| POST | `/api/vehicles` | cualquiera | El Cliente los suyos; el taller, los de cualquiera |
| PUT | `/api/vehicles/{id}` | Owner o Customer | El Técnico no edita |
| GET | `/api/service-requests?status=&branchId=` | cualquiera | Bandeja; pendientes primero. El Técnico ve los de sus sucursales |
| POST | `/api/service-requests` | cualquiera | El Técnico solo en sus sucursales |
| POST | `/api/service-requests/{id}/approve` | Owner | Lo convierte en orden y devuelve `workOrderId` |
| POST | `/api/service-requests/{id}/reject` | Owner | Lo rechaza con motivo |
| GET | `/api/work-orders?status=&branchId=&onlyOpen=` | cualquiera | Kanban y bandeja del Técnico |
| GET | `/api/work-orders/{id}` | cualquiera | Detalle con pasos y línea de tiempo |
| POST | `/api/work-orders` | Owner | Abre la orden |
| PUT | `/api/work-orders/{id}` | Owner o Técnico | Descripción y diagnóstico |
| PUT | `/api/work-orders/{id}/assign` | Owner | Asigna o quita el técnico |
| POST | `/api/work-orders/{id}/status` | Owner o Técnico | Cambia estado; 409 si no es válida |
| POST/PUT | `/api/work-orders/{id}/tasks[/{taskId}]` | Owner o Técnico | Pasos de la reparación |
| POST | `/api/work-orders/{id}/tasks/{taskId}/complete` | Owner o Técnico | Marca el paso |
| DELETE | `/api/work-orders/{id}/tasks/{taskId}` | Owner | Elimina el paso |

### Fase 2 — evidencia fotográfica

El binario **no pasa por la API**: el cliente pide una URL prefirmada, sube directo al bucket
y confirma. Una foto de 3 MB por la red del taller no ocupa un hilo del servidor, y el móvil
puede reintentar el `PUT` sin repetir la petición completa.

| Método | Ruta | Auth | Qué hace |
| --- | --- | --- | --- |
| POST | `/api/media/upload-url` | cualquiera | Reserva el adjunto y devuelve el `PUT` prefirmado |
| POST | `/api/media/{id}/confirm` | quien la subió | Publica la foto y genera la miniatura |
| GET | `/api/media?ownerType=&ownerId=` | cualquiera | Adjuntos de un recurso |
| GET | `/api/media/work-order/{id}` | cualquiera | Galería: fotos de la orden y de sus pasos |
| DELETE | `/api/media/{id}` | quien la subió, o el Dueño | Borra la foto y sus objetos |

`ownerType`: `1` requerimiento · `2` orden de trabajo · `3` paso de la reparación.

Los tres pasos de una subida:

1. `POST /api/media/upload-url` → `{ attachmentId, uploadUrl, key, headers, expiresAt }`.
2. `PUT` a `uploadUrl` con el binario y **exactamente** las cabeceras de `headers`: el
   `Content-Type` va dentro de la firma y el bucket devuelve 403 si no coincide. La petición
   al bucket no lleva `Authorization` — si se envía, la firma tampoco valida.
3. `POST /api/media/{attachmentId}/confirm` → devuelve el adjunto ya visible. Responde **409**
   si el archivo todavía no llegó al bucket, para que el cliente reintente sin pedir otra URL.

Un adjunto sin confirmar no aparece en ninguna lista y no lo ve nadie.

Reglas propias de las fotos, además del alcance general:

- El **Cliente** solo adjunta a sus requerimientos: el proceso de reparación lo documenta el
  taller. Y solo ve las fotos con `isVisibleToCustomer: true`.
- El **Técnico** adjunta y borra en las órdenes que tiene asignadas, y solo sus propias fotos.
- El **Dueño** borra cualquier foto del taller.

Se admiten JPEG, PNG, WebP y HEIC, hasta 15 MB (`Storage:MaxUploadBytes`). Las URL de lectura
caducan a los 15 minutos (`Storage:PresignedUrlMinutes`): son temporales a propósito, viajan
en JSON y podrían quedar en una caché o en un log.

### Fase 3 — inventario

La regla de la que depende todo lo demás: **el stock no se edita, se mueve**. Cada variación
deja un `StockMovement` con su responsable, su fecha y el saldo resultante;
`StockItem.Quantity` es solo el saldo cacheado, actualizado en la misma transacción. Una
diferencia de inventario siempre tiene autor y hora, en vez de aparecer de la nada.

| Método | Ruta | Auth | Qué hace |
| --- | --- | --- | --- |
| GET | `/api/parts?search=&category=` | taller | Catálogo con existencia sumada de sus sucursales |
| GET | `/api/parts/categories` | taller | Categorías en uso, para los filtros |
| POST/PUT | `/api/parts[/{id}]` | Owner | Alta y edición; el SKU es único por taller |
| GET | `/api/stock?branchId=&onlyBelowMinimum=` | taller | Existencias por sucursal |
| GET | `/api/stock/alerts?branchId=` | taller | Lo que está en o bajo el mínimo |
| GET | `/api/stock/movements?branchId=&partId=&type=` | taller | Kardex |
| POST | `/api/stock/receive` | Owner | Entrada por compra |
| POST | `/api/stock/adjust` | Owner | Ajuste por conteo físico |
| POST | `/api/stock/transfer` | Owner | Traslado entre sucursales |
| PUT | `/api/stock/settings` | Owner | Mínimo de reposición y ubicación |
| GET | `/api/work-orders/{id}/parts` | cualquiera | Repuestos cargados a la orden |
| POST | `/api/work-orders/{id}/parts` | Owner o Técnico | Consume y descuenta de la bodega |
| DELETE | `/api/work-orders/{id}/parts/{lineId}` | Owner o Técnico | Devuelve a la bodega |

`StockMovementType`: `1` entrada · `2` salida · `3` ajuste · `4` traslado recibido ·
`5` traslado enviado.

Decisiones que conviene conocer antes de tocar esto:

- **No se permite saldo negativo.** Consumir o transferir más de lo que hay devuelve **409**
  diciendo cuánto queda. Un stock negativo no se nota hasta que los reportes de costo salen
  mal, y para entonces nadie recuerda qué pasó.
- **El ajuste se envía como cantidad contada, no como diferencia** (`countedQuantity`), que
  es lo que la persona tiene delante al hacer inventario. Exige motivo.
- **En el ajuste, `Quantity` del movimiento lleva signo**: el tipo no distingue un sobrante de
  un faltante. En los demás tipos es positiva y el signo lo da el tipo. `signedQuantity` en el
  DTO ya viene resuelto.
- **La transferencia son dos movimientos** con la misma hora, cada uno apuntando al otro por
  `CounterpartBranchId`: así el kardex de cada sucursal se lee solo.
- **El precio y el costo se congelan** en `WorkOrderPart` al consumir. Cambiar el catálogo
  mañana no debe alterar lo que ya se cobró ni el margen de una orden cerrada.
- **Quitar un repuesto de una orden no borra su movimiento**: entra uno de devolución. El
  histórico es inmutable.
- La entrada con costo actualiza `Part.CostPrice`, que es un costo de referencia; el costo
  histórico de cada compra queda en su movimiento.
- El **Técnico** ve las existencias de sus sucursales y consume en sus órdenes, pero no
  administra el catálogo ni registra entradas, ajustes o traslados. El **Cliente** no ve el
  inventario, y en el detalle de su orden los repuestos llegan con `unitCost: 0`.

### Fase 4 — cotizaciones y WhatsApp

El circuito con el cliente: el taller arma la cotización, la manda por WhatsApp, y el cliente
la abre y la aprueba **sin cuenta ni login**. No se usa la API oficial de Meta: se arma un
link `wa.me` con el mensaje ya escrito y el Dueño lo envía desde su propio WhatsApp.

| Método | Ruta | Auth | Qué hace |
| --- | --- | --- | --- |
| GET | `/api/labor-services?includeInactive=` | taller | Catálogo de mano de obra |
| POST/PUT | `/api/labor-services[/{id}]` | Owner | Alta y edición; el código es único |
| GET | `/api/quotes?status=&customerId=&workOrderId=` | Owner o Cliente | Cotizaciones visibles |
| GET | `/api/quotes/{id}` | Owner o Cliente | Detalle con líneas y totales |
| POST | `/api/quotes` | Owner | Cotización en blanco para un cliente |
| POST | `/api/quotes/from-work-order` | Owner | La arma con los repuestos y pasos de la orden |
| PUT | `/api/quotes/{id}` | Owner | Vigencia, notas e impuesto |
| POST/PUT/DELETE | `/api/quotes/{id}/lines[/{lineId}]` | Owner | Líneas; recalcula totales |
| POST | `/api/quotes/{id}/send` | Owner | La marca enviada y devuelve el link de WhatsApp |
| GET | `/api/quotes/{id}/whatsapp-link` | Owner | El mismo link sin cambiar el estado |
| GET | `/api/quotes/{id}/pdf` | Owner o Cliente | PDF |
| POST | `/api/quotes/{id}/respond` | Owner o Cliente | Respuesta desde dentro de la app |
| GET | `/public/quotes/{token}` | **anónimo** | La cotización que abre el cliente |
| POST | `/public/quotes/{token}/respond` | **anónimo** | Aprobar o rechazar |
| GET | `/public/quotes/{token}/pdf` | **anónimo** | PDF sin login |

Los dos endpoints de PDF autenticados —`/api/quotes/{id}/pdf` y `/api/sales/{id}/pdf`— se
piden **con la cabecera `Authorization`**, no abriendo la URL en una pestaña. El navegador no
manda esa cabecera al navegar, así que un `<a href>` apuntando ahí responde 401; el cliente
web los baja con axios (`responseType: 'blob'`) y guarda el resultado. Lo mismo vale para
`/api/reports/revenue.csv`. La ruta pública de la cotización sí funciona como enlace: no
tiene sesión, y su credencial es el token aleatorio de la propia URL.

`QuoteStatus`: `1` borrador · `2` enviada · `3` aprobada · `4` rechazada · `5` vencida.
`LineType`: `1` repuesto · `2` mano de obra — la misma división que después alimenta los
reportes de ingresos.

Decisiones que conviene conocer antes de tocar esto:

- **El token del link es la única credencial.** La consulta pública corre con
  `IgnoreQueryFilters()` porque el visitante es anónimo y no hay claims de los que sacar el
  taller; el token es un GUID aleatorio, único globalmente y **no se reutiliza entre
  cotizaciones**. La respuesta pública no expone ningún id interno, para que desde ella no se
  pueda navegar a nada más.
- **Los totales se calculan siempre en el servidor** a partir de las líneas. Nunca se aceptan
  del cliente: es el importe que se aprueba y que después se cobra.
- **Una cotización respondida es un documento cerrado.** Editarla devuelve 409: si hay que
  cambiar el precio se crea otra, porque si no el cliente aprobaría una cosa y recibiría otra.
- **Reenviar no reinicia la fecha de envío**, que es lo que dice cuánto lleva esperando.
- **Aprobar propaga**: el requerimiento pasa a aprobado y la orden recibe una entrada visible
  en su línea de tiempo. Una aprobación tiene que verse donde el taller trabaja.
- **Una cotización vencida no se puede aprobar** (409). El precio de hace dos meses no obliga
  al taller.
- El texto de cada línea se **congela** al cotizar: renombrar un repuesto no cambia una
  cotización ya enviada.
- El **Técnico** no participa en la parte comercial: `/api/quotes` le devuelve lista vacía.
- `PublicBaseUrl` debe apuntar al **web**, no a la API: el cliente abre una página, no un JSON.
  Sin ella, enviar falla con un mensaje explícito.

### Fase 5 — ventas y reportes

Una venta es **inmutable**. Si estuvo mal se anula con motivo y se hace otra: editar importes
ya cobrados dejaría los reportes sin forma de cuadrar con la caja.

| Método | Ruta | Auth | Qué hace |
| --- | --- | --- | --- |
| GET | `/api/sales?branchId=&from=&to=&includeVoided=&onlyUnpaid=` | Owner o Cliente | Ventas visibles; `onlyUnpaid` son las cuentas por cobrar |
| GET | `/api/sales/{id}` | Owner o Cliente | Detalle con líneas |
| POST | `/api/sales` | Owner | Venta directa de mostrador; descuenta inventario |
| POST | `/api/sales/close-work-order` | Owner | Cierra la orden y factura lo trabajado |
| POST | `/api/sales/{id}/payments` | Owner | Registra un abono |
| DELETE | `/api/sales/{id}/payments/{paymentId}` | Owner | Borra un abono mal capturado |
| POST | `/api/sales/{id}/void` | Owner | Anula con motivo |
| GET | `/api/sales/{id}/pdf` | Owner o Cliente | La factura en PDF |
| GET | `/api/reports/revenue?from=&to=&groupBy=&branchId=&technicianId=` | Owner | Ingresos, con reparto por sucursal y por técnico |
| GET | `/api/reports/revenue.csv?…` | Owner | Lo mismo, para Excel |
| GET | `/api/reports/dashboard?branchId=` | Owner | Tablero del día |

`PaymentMethod`: `1` efectivo · `2` tarjeta · `3` transferencia · `4` otro.
`RevenueGrouping`: `1` día · `2` semana · `3` mes.

Decisiones que conviene conocer antes de tocar esto:

- **Lo cobrado sale de los abonos, no de un campo en la venta.** Cada venta tiene su lista
  de `payments`, y `amountPaid` / `balance` se calculan sumándolos —igual que el stock se
  deriva de los movimientos—. Una venta de contado también genera su abono, por el total y
  con la fecha de la venta: si el contado no dejara rastro habría dos maneras distintas de
  saber qué entró en caja, y el corte del día dependería de cuál se mirara.
- **Un abono nunca puede superar el saldo** (400). Cobrar de más no es un abono: o el total
  está mal —y entonces se anula la venta— o hay que devolver la diferencia. Aceptarlo dejaría
  un saldo negativo que ningún reporte sabe leer.
- **Borrar un abono es corregir una captura, no devolver dinero.** Para eso último se anula
  la venta entera, que es lo que también devuelve los repuestos a la bodega.
- **El reporte de ingresos es de lo facturado, no de lo cobrado.** Una venta a crédito cuenta
  entera el día que se emitió; lo que falta por entrar está en `onlyUnpaid` y en los dos
  campos `receivables` del tablero. Son dos preguntas distintas y mezclarlas haría que ningún
  número sirviera para ninguna.
- **El reparto por técnico se atribuye por la orden, no por el paso.** Cuenta el técnico
  responsable de la orden que se facturó: es quien responde por el trabajo completo, y es la
  única atribución que reparte también los repuestos, que no cuelgan de un paso. Lo vendido
  en mostrador no pasó por nadie y sale agrupado como «Sin técnico»; filtrar por técnico lo
  deja fuera, porque sumárselo a alguien sería inventarle trabajo.
- **La factura en PDF no es un documento fiscal.** Lleva el nombre, el RTN y el correlativo
  del taller, pero no CAI ni rango autorizado por el SAR: sirve como comprobante de entrega
  para el cliente, no sustituye a la factura del talonario mientras no se implemente la
  facturación autorizada.
- **Cerrar una orden no vuelve a descontar los repuestos.** Salieron de la bodega cuando el
  técnico los cargó a la orden (Fase 3); aquí solo se facturan. La venta de mostrador sí
  descuenta, porque ahí nadie pasó por una orden.
- **Una orden solo se factura una vez** (409 si ya tiene venta no anulada). Para corregir, se
  anula primero.
- Al cerrar se cobran las **horas reales** del paso si el técnico las registró; si no, las
  estimadas, y si tampoco, las estándar del catálogo.
- El cierre marca la orden como entregada **saltándose la validación de transiciones**: cobrar
  y entregar es un acto único y la orden puede estar en cualquier estado abierto.
- **Anular devuelve el stock solo en las ventas de mostrador.** Los repuestos de una orden se
  devuelven quitándolos de la orden, que es donde salieron.
- **Las ventas anuladas no cuentan en los reportes**: uno que las incluyera mentiría.
- El desglose repuestos/mano de obra sale de `SaleLine.LineType`, no de una clasificación
  aparte: por eso siempre cuadra con lo facturado.
- **El día contable es el del taller** (UTC−6, sin horario de verano en Honduras), no UTC: una
  venta de las 7 de la noche aparecería al día siguiente si se agrupara por fecha UTC. Los
  límites del rango se normalizan a UTC solo al tocar la base, porque Npgsql no acepta otro
  desfase en `timestamptz`.
- El **Técnico** no ve ventas ni reportes (403). El **Cliente** ve sus facturas, pero con
  `costTotal` y `margin` en cero.
- El CSV va con BOM: sin él, Excel en Windows abre los acentos rotos.

### Fase 6 — avisos y app del cliente

Todo aviso queda **guardado** aunque el push falle o el usuario no tenga la app: la campana
dentro de la aplicación es el canal principal y el push solo empuja a abrirla. Al revés, un
aviso perdido no se recupera nunca.

| Método | Ruta | Auth | Qué hace |
| --- | --- | --- | --- |
| GET | `/api/notifications?onlyUnread=` | cualquiera | Avisos del usuario, del más nuevo al más viejo |
| GET | `/api/notifications/unread-count` | cualquiera | Solo el número del globo rojo |
| POST | `/api/notifications/{id}/read` | cualquiera | Marca uno leído |
| POST | `/api/notifications/read-all` | cualquiera | Marca todos y devuelve cuántos |
| POST | `/api/notifications/devices` | cualquiera | Registra el token de push del aparato |
| DELETE | `/api/notifications/devices/{token}` | cualquiera | Da de baja el aparato |

`NotificationType`: `1` requerimiento nuevo · `2` orden asignada · `3` cambio de estado ·
`4` cotización enviada · `5` cotización respondida.
`DevicePlatform`: `1` Android · `2` iOS · `3` web.

Quién recibe qué:

| Hecho | Avisa a |
| --- | --- |
| El Cliente abre un requerimiento desde su app | Todos los Dueños activos |
| Se asigna o reasigna una orden | El Técnico asignado |
| Cambia el estado de una orden (visible al cliente) | El Cliente del vehículo |
| Se envía una cotización | El Cliente |
| El cliente aprueba o rechaza | Todos los Dueños activos |

Decisiones que conviene conocer antes de tocar esto:

- **Un aviso nunca tumba la operación que lo originó.** Si falla el guardado o el push, se
  registra un warning y el cambio de estado —o la venta, o la cotización— sigue adelante.
- **Solo se avisa de lo que el cliente puede ver.** Un cambio de estado marcado
  `isVisibleToCustomer: false` no genera notificación: mandarla lo delataría igual.
- **Un requerimiento creado por el Dueño no le avisa a él mismo.** Avisar de la propia captura
  del mostrador es ruido que enseña a ignorar la campana.
- **El aviso de otro usuario devuelve 404, no 403**, igual que el resto de la API.
- **El token de dispositivo es único en todo el sistema, no por taller.** Si un teléfono
  cambia de manos, la fila se reasigna en lugar de duplicarse; si no, el dueño anterior
  seguiría recibiendo los avisos del nuevo.
- Las apps **sondean** el contador cada minuto en vez de mantener un WebSocket abierto: son
  tres o cuatro personas por taller y el servidor del plan gratuito duerme.
- El envío push está **apagado mientras no haya proyecto de Firebase** (`Push__ProjectId` y
  `Push__ServiceAccountJson`). Ver [push.md](push.md).
- El **Cliente** puede crear requerimientos y adjuntarles fotos; el **Técnico** no participa
  en esa etapa y no recibe nada de ella.

### Datos de demostración

| Método | Ruta | Auth | Qué hace |
| --- | --- | --- | --- |
| POST | `/api/demo/seed` | Owner | **Borra todo** y siembra un taller con semanas de historia |

Apagado por defecto: sin la configuración `Demo:AllowSeeding` responde 404 y no existe para
nadie. Además exige rol Dueño y la frase `BORRAR Y SEMBRAR` en el cuerpo. Los tres cerrojos
están porque la operación no tiene vuelta atrás. Ver [demo.md](demo.md).

### Reglas de alcance

Son la parte que hay que respetar al agregar endpoints nuevos. Viven en
[AccessScope](../backend/src/Garaj.Application/Common/AccessScope.cs) y se aplican en cada
servicio, no en los controladores.

| Perfil | Qué ve |
| --- | --- |
| Dueño | Todo el taller |
| Técnico | Solo las órdenes asignadas a él y las sucursales donde trabaja |
| Cliente | Solo sus datos, sus vehículos y las órdenes de esos vehículos |

Pedir un recurso fuera del alcance devuelve **404, no 403**: un 403 confirmaría que el id
existe. Las entradas de la línea de tiempo marcadas `isVisibleToCustomer: false` se omiten
para el Cliente.

### Verificación

```bash
# Con la API corriendo y el seeder aplicado
python3 backend/tests/smoke/fase1_smoke.py

# Fase 2: sube archivos de verdad a MinIO, así que hace falta `docker compose up -d`
python3 backend/tests/smoke/fase2_smoke.py

# Fase 3: inventario
python3 backend/tests/smoke/fase3_smoke.py

# Fase 4: cotizaciones, PDF y el link público
python3 backend/tests/smoke/fase4_smoke.py

# Fase 5: ventas y reportes
python3 backend/tests/smoke/fase5_smoke.py

# Fase 6: avisos, dispositivos y la cita que pide el cliente
python3 backend/tests/smoke/fase6_smoke.py

# Fase 7: recepción en el mostrador, diagnóstico y documentos en PDF
python3 backend/tests/smoke/fase7_smoke.py

# Fase 8: crédito, abonos y costo en existencias
python3 backend/tests/smoke/fase8_smoke.py
```

Se pueden encadenar en ese orden sobre una base recién sembrada.

**El script escribe en la base**: entrega órdenes, aprueba requerimientos y crea clientes.
Está pensado para correr contra la base local, no contra Supabase. Para dejarla como estaba
hay que recrearla — y el `DROP DATABASE` falla en silencio si la API sigue conectada, así
que primero se detiene:

```bash
pkill -f "Garaj.Api"
docker exec garaj-postgres psql -U garaj -d postgres \
  -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='garaj';" \
  -c "DROP DATABASE IF EXISTS garaj;" -c "CREATE DATABASE garaj OWNER garaj;"
```

El mismo cuidado aplica a las pruebas del móvil: si el smoke corre en paralelo, las órdenes
que el test espera encontrar abiertas ya estarán entregadas.

### Claims del access token

| Claim | Contenido |
| --- | --- |
| `sub` / `nameidentifier` | Id del usuario |
| `role` | `Owner`, `Technician` o `Customer` |
| `tenant_id` | Taller del usuario; el DbContext filtra por él |
| `branch_ids` | Sucursales asignadas, separadas por coma (vacío en el Dueño: las ve todas) |
| `customer_id` | Solo en perfil Cliente |

### Rotación de tokens

El access token dura 30 minutos y el refresh 30 días. Cada refresco **revoca** el token usado
y emite uno nuevo. Si llega un refresh token ya revocado se cierran todas las sesiones del
usuario: significa que alguien lo interceptó. Por eso web y móvil serializan el refresco en
una sola petición en vuelo.


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
| POST/PUT | `/api/customers[/{id}]` | Owner | Alta y edición |
| GET | `/api/vehicles?search=&customerId=` | cualquiera | Vehículos |
| POST/PUT | `/api/vehicles[/{id}]` | Owner o Customer | El Cliente registra los suyos |
| GET | `/api/service-requests?status=&branchId=` | cualquiera | Bandeja; pendientes primero |
| POST | `/api/service-requests` | Owner o Customer | Crea el requerimiento |
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
```

**El script escribe en la base**: entrega órdenes, aprueba requerimientos y crea clientes.
Está pensado para correr contra la base local, no contra Supabase. Para dejarla como estaba
hay que recrearla — y el `DROP DATABASE` falla en silencio si la API sigue conectada, así
que primero se detiene:

```bash
lsof -ti :7080 | xargs -r kill -9
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

## Pendiente por fase

- **Fase 3** — `/api/parts`, `/api/stock` (existencias, ajustes, transferencias, alertas)
- **Fase 4** — `/api/labor-services`, `/api/quotes` (PDF, link de WhatsApp), `/public/quotes/{token}`
- **Fase 5** — `/api/sales`, `/api/reports/revenue`, `/api/reports/dashboard`

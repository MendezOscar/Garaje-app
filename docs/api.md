# Contrato de la API

Swagger en desarrollo: <https://localhost:7080/swagger>

Todos los endpoints bajo `/api` requieren `Authorization: Bearer <accessToken>`, salvo los
marcados como anónimos. Los errores salen como `application/problem+json` con el mensaje en
`detail`.

## Implementado (Fase 0)

| Método | Ruta | Auth | Qué hace |
| --- | --- | --- | --- |
| POST | `/api/auth/login` | anónimo | Devuelve access token, refresh token y perfil |
| POST | `/api/auth/refresh` | anónimo | Rota el refresh token y emite un par nuevo |
| POST | `/api/auth/logout` | anónimo | Revoca el refresh token de la sesión |
| GET | `/api/auth/me` | cualquiera | Perfil del usuario autenticado |
| GET | `/api/auth/ping-owner` | Owner | Prueba de humo de las policies por rol |
| GET | `/health` | anónimo | Estado de la API y de la base |

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

- **Fase 1** — `/api/branches`, `/api/users`, `/api/customers`, `/api/vehicles`,
  `/api/service-requests`, `/api/work-orders` (+ `/tasks`, cambio de estado, timeline)
- **Fase 2** — `/api/media` (presigned upload, confirmar, listar, eliminar)
- **Fase 3** — `/api/parts`, `/api/stock` (existencias, ajustes, transferencias, alertas)
- **Fase 4** — `/api/labor-services`, `/api/quotes` (PDF, link de WhatsApp), `/public/quotes/{token}`
- **Fase 5** — `/api/sales`, `/api/reports/revenue`, `/api/reports/dashboard`

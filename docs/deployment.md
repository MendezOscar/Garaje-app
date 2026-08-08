# Despliegue

| Pieza | Servicio |
| --- | --- |
| Base de datos | Supabase (Postgres gestionado) |
| API .NET | Render (Docker, plan free) |
| Panel web | Cloudflare Pages |
| Fotos | Cloudflare R2 (a partir de la Fase 2) |

Todo esto requiere cuentas propias. Los pasos de abajo son los que hay que hacer a mano una
sola vez; el repo ya trae [render.yaml](../render.yaml), el
[Dockerfile](../backend/Dockerfile) y la configuración de Pages.

---

## 1. Supabase

1. Cree el proyecto en <https://supabase.com/dashboard>. **Región: `us-east-1` (N. Virginia)**,
   para que quede junto al backend en Render y con buena latencia desde Ecuador.
2. Guarde la contraseña de la base: solo se muestra al crear el proyecto.
3. En el botón **Connect** de la barra superior, copie la cadena del **Session pooler**.
   No sirven las otras dos:

   | Modo | Host | ¿Sirve? |
   | --- | --- | --- |
   | Direct connection | `db.<ref>.supabase.co:5432` | **No**: en el plan free ese host solo tiene registro AAAA (IPv6) y Render sale por IPv4 |
   | **Session pooler** | `aws-N-<region>.pooler.supabase.com:5432` | **Sí** |
   | Transaction pooler | `aws-N-<region>.pooler.supabase.com:6543` | **No**: pgbouncer en modo transacción rompe las sentencias preparadas de Npgsql |

   Se comprueba con `dig +short db.<ref>.supabase.co A`: si no devuelve nada, ese host es
   inalcanzable por IPv4.

4. Supabase la muestra como URI de `libpq`. Tradúzcala al formato de Npgsql:

   ```
   Host=aws-0-us-east-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<su-password>;SSL Mode=Require;Trust Server Certificate=true
   ```

   El usuario del pooler lleva el project-ref pegado: `postgres.zitfkofrzvawujnqxexq`, no
   `postgres` a secas. Si perdió la contraseña, se regenera en
   **Project Settings → Database → Reset database password**.

5. En local, guárdela con **user secrets** para que no entre nunca al repo. En Development
   ASP.NET los carga solo, sin cambiar código:

   ```bash
   cd backend
   dotnet user-secrets set "ConnectionStrings:Default" \
     "Host=aws-0-us-east-1.pooler.supabase.com;Port=5432;..." --project src/Garaj.Api
   ```

   Para volver al Postgres local basta con `dotnet user-secrets remove "ConnectionStrings:Default"`.

6. Aplique las migraciones:

   ```bash
   dotnet ef database update -p src/Garaj.Infrastructure -s src/Garaj.Api
   ```

   No es obligatorio —el backend en Render las aplica al arrancar con
   `Database__MigrateOnStartup=true`— pero correrlo antes deja ver los errores con calma.

> El seeder de datos demo **solo corre en Development**. La base de producción arranca vacía;
> el primer taller y su usuario Dueño se crean aparte.

---

## 2. Render (API)

1. Suba el repo a GitHub.
2. En Render: **New → Blueprint**, conecte el repo. Detecta `render.yaml` y crea `garaj-api`.
3. Cargue las variables marcadas como `sync: false` en **Environment**:

   | Variable | Valor |
   | --- | --- |
   | `ConnectionStrings__Default` | la cadena Npgsql del paso 1 |
   | `Cors__AllowedOrigins__0` | URL del panel web, ej. `https://garaj.pages.dev` |
   | `PublicBaseUrl` | la misma URL del panel web |

   `Jwt__SigningKey` la genera Render sola. **No la cambie después**: rotarla invalida todas
   las sesiones abiertas.

4. El health check es `/health` y comprueba también la conexión a la base: si Supabase no
   responde, Render marca el despliegue como fallido en lugar de dejar la API a medias.

**Sobre el plan free**: el servicio se duerme tras 15 minutos sin tráfico y el primer request
tarda ~40 segundos en responder. Sirve para probar; para el taller en uso real conviene el
plan Starter (US$7/mes), que no duerme.

---

## 3. Cloudflare Pages (web)

**Workers & Pages → Create → Pages → conectar el repo**, y configure:

| Campo | Valor |
| --- | --- |
| Framework preset | Vue |
| Build command | `npm run build` |
| Build output directory | `dist` |
| Root directory | `web` |

Variable de entorno de build:

```
VITE_API_URL = https://garaj-api.onrender.com
```

`web/public/_redirects` ya manda todas las rutas a `index.html`, que es lo que necesita el
history mode del router; sin eso, recargar `/dashboard` daría 404.

Después del primer despliegue, vuelva a Render y ponga la URL real de Pages en
`Cors__AllowedOrigins__0` y `PublicBaseUrl`.

---

## 4. Cloudflare R2 (fotos — Fase 2)

1. **R2 → Create bucket**, nombre `garaj-media`, ubicación automática.
2. **Manage R2 API Tokens → Create token**, permiso *Object Read & Write* sobre ese bucket.
3. En Render:

   | Variable | Valor |
   | --- | --- |
   | `Storage__ServiceUrl` | `https://<account-id>.r2.cloudflarestorage.com` |
   | `Storage__AccessKey` | Access Key ID del token |
   | `Storage__SecretKey` | Secret Access Key del token |
   | `Storage__Bucket` | `garaj-media` |

El bucket queda **privado**: las fotos se sirven por URL prefirmada temporal, nunca por URL
pública.

---

## 5. Móvil

La app apunta a la API por `--dart-define`:

```bash
# Contra el backend desplegado
flutter run --dart-define=API_URL=https://garaj-api.onrender.com

# Contra el backend local
flutter run --dart-define=API_URL=http://localhost:5080
```

Para generar los binarios de tienda hay que definirlo también en el build:

```bash
flutter build ipa   --dart-define=API_URL=https://garaj-api.onrender.com
flutter build appbundle --dart-define=API_URL=https://garaj-api.onrender.com
```

---

## Puertos en desarrollo local

Esta máquina ya tiene otros proyectos ocupando los puertos habituales (agroapp usa 9000/9001,
airflow usa 5432 y 8080), así que el stack de Garaj está desplazado:

| Servicio | Puerto |
| --- | --- |
| API (http / https) | 5080 / 7080 |
| Panel web (Vite) | 5173 |
| Postgres local (perfil `local-db`) | 5434 |
| MinIO (API S3 / consola) | 9010 / 9011 |

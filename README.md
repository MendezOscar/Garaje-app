# Garaj — Gestión de taller mecánico

Plataforma para administrar un taller de autos y motos: requerimientos de servicio, órdenes
de trabajo con evidencia fotográfica, inventario de repuestos por sucursal, cotizaciones por
WhatsApp y reportes de ingresos.

Tres perfiles: **Dueño**, **Técnico** y **Cliente**. Un taller (tenant) con N sucursales.

## Estructura

| Carpeta | Qué es |
| --- | --- |
| [backend/](backend/) | API en .NET 8 (Domain / Application / Infrastructure / Api) |
| [web/](web/) | Panel web en Vue 3 + Vite + TypeScript |
| [mobile/](mobile/) | App móvil en Flutter (Dueño, Técnico y Cliente) |
| [docs/](docs/) | Modelo de dominio y contrato de la API |

## Requisitos

.NET SDK 8.0.406 · Node 20+ · Flutter 3.35+ · Docker (para MinIO)

## Puesta en marcha

### 1. Servicios locales

```bash
docker compose up -d                       # MinIO en :9000, consola en :9001
docker compose --profile local-db up -d    # opcional: Postgres local en :5432
```

### 2. Base de datos

El proyecto apunta a **Postgres gestionado** (Neon o Supabase). Ponga su cadena de conexión
en `backend/src/Garaj.Api/appsettings.Development.json` o en la variable de entorno
`ConnectionStrings__Default`:

```
Host=xxx.neon.tech;Database=garaj;Username=xxx;Password=xxx;SslMode=Require
```

Si prefiere trabajar contra la base local, deje la cadena que ya viene y levante el perfil
`local-db` del paso anterior.

### 3. Backend

```bash
cd backend
dotnet build
dotnet ef database update -p src/Garaj.Infrastructure -s src/Garaj.Api
dotnet run --project src/Garaj.Api
```

En Development la API aplica migraciones y siembra datos de demostración al arrancar, así
que el paso de `ef database update` solo hace falta si quiere aplicarlas por separado.

Swagger: <https://localhost:7080/swagger> · Health: <https://localhost:7080/health>

### 4. Web

```bash
cd web
npm install
npm run dev        # http://localhost:5173
```

La URL de la API se configura en `web/.env.development` (`VITE_API_URL`).

### 5. Móvil

```bash
cd mobile
flutter pub get
flutter run --dart-define=API_URL=http://localhost:5080
```

`API_URL` depende de dónde corre la app:

- Emulador Android: `http://10.0.2.2:5080` (es el valor por defecto)
- Simulador iOS: `http://localhost:5080`
- Dispositivo físico: la IP de su máquina en la red, ej. `http://192.168.1.10:5080`

## Usuarios de demostración

Los crea el seeder en el taller **Taller Garaj**, con sucursales Matriz (MTZ) y Sur (SUR).
Contraseña para todos: `Garaj123!`

| Correo | Perfil |
| --- | --- |
| owner@garaj.test | Dueño (ve las dos sucursales) |
| tecnico1@garaj.test | Técnico (Matriz) |
| tecnico2@garaj.test | Técnico (Sur) |
| cliente@garaj.test | Cliente (María Torres) |

## Comandos útiles

```bash
# Nueva migración
cd backend && dotnet ef migrations add NombreDeLaMigracion \
  -p src/Garaj.Infrastructure -s src/Garaj.Api -o Persistence/Migrations

# Tests del backend
cd backend && dotnet test

# Verificación de tipos del web
cd web && npx vue-tsc -b

# Análisis estático del móvil
cd mobile && flutter analyze
```

## Notas de seguridad

- `Jwt:SigningKey` y las credenciales de almacenamiento **nunca** van en `appsettings.json`
  de producción: use variables de entorno (`Jwt__SigningKey`, `Storage__SecretKey`).
- El aislamiento entre talleres depende del global query filter de
  [GarajDbContext](backend/src/Garaj.Infrastructure/Persistence/GarajDbContext.cs). Si
  escribe una consulta con `IgnoreQueryFilters()`, filtre el tenant a mano.
- Las fotos nunca se sirven por URL pública: siempre por URL prefirmada temporal.

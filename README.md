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
| [docs/](docs/) | [Modelo de dominio](docs/domain-model.md), [contrato de la API](docs/api.md) y [despliegue](docs/deployment.md) |

## Requisitos

.NET SDK 8.0.406 · Node 20+ · Flutter 3.35+ · Docker (para MinIO)

## Puesta en marcha

### 1. Servicios locales

```bash
docker compose up -d                       # MinIO en :9010, consola en :9011
docker compose --profile local-db up -d    # opcional: Postgres local en :5434
```

> Los puertos están desplazados de los habituales porque esta máquina ya corre agroapp
> (9000/9001) y airflow (5432, 8080). Ver la tabla completa en
> [docs/deployment.md](docs/deployment.md#puertos-en-desarrollo-local).

### 2. Base de datos

Por defecto apunta al Postgres local del perfil `local-db`, que ya queda listo con el paso
anterior. Para trabajar contra **Supabase**, guarde la cadena en user secrets — así la
contraseña no toca el repo:

```bash
cd backend
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=aws-0-us-east-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<ref>;Password=<pwd>;SSL Mode=Require;Trust Server Certificate=true" \
  --project src/Garaj.Api
```

Use el **Session pooler** (puerto 5432), no la conexión directa ni el pooler de transacción:
ver [docs/deployment.md](docs/deployment.md#1-supabase).

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
flutter run                 # apunta a producción, funciona tal cual
```

Para desarrollar contra la API local hay que pasar la URL, y **depende del dispositivo**:

```bash
flutter run --dart-define=API_URL=http://localhost:5080      # simulador iOS
flutter run --dart-define=API_URL=http://10.0.2.2:5080       # emulador Android
flutter run --dart-define=API_URL=http://192.168.1.10:5080   # teléfono físico (su IP)
```

No hay un valor local que sirva en los tres, por eso el defecto es producción.

## Usuarios de demostración

Los crea el seeder en el taller **Taller Garaj**, con sucursales Matriz (MTZ, Tegucigalpa) y Norte (SPS, San Pedro Sula).
Contraseña para todos: `Garaj123!`

| Correo | Perfil |
| --- | --- |
| owner@garaj.test | Dueño (ve las dos sucursales) |
| tecnico1@garaj.test | Técnico (Matriz) |
| tecnico2@garaj.test | Técnico (Norte) |
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

# Humo del backend: 34 comprobaciones del flujo y del alcance por perfil
python3 backend/tests/smoke/fase1_smoke.py

# Humo de las fotos: 30 comprobaciones del ciclo subir / confirmar / listar / borrar
python3 backend/tests/smoke/fase2_smoke.py

# Humo del inventario: 49 comprobaciones de existencias, kardex y consumo
python3 backend/tests/smoke/fase3_smoke.py

# Humo del móvil en el simulador
cd mobile && flutter test integration_test/login_test.dart \
  -d "iPhone 16 Pro" --dart-define=API_URL=http://localhost:5080
```

> Los humos **escriben en la base** —y el de la Fase 2, además, sube archivos a MinIO—, así
> que no se corren en paralelo ni contra Supabase: el del backend entrega órdenes que el del
> móvil espera encontrar abiertas. Cómo recrear la base local está en
> [docs/api.md](docs/api.md#verificación).

## Notas de seguridad

- `Jwt:SigningKey` y las credenciales de almacenamiento **nunca** van en `appsettings.json`
  de producción: use variables de entorno (`Jwt__SigningKey`, `Storage__SecretKey`).
- El aislamiento entre talleres depende del global query filter de
  [GarajDbContext](backend/src/Garaj.Infrastructure/Persistence/GarajDbContext.cs). Si
  escribe una consulta con `IgnoreQueryFilters()`, filtre el tenant a mano.
- Las fotos nunca se sirven por URL pública: siempre por URL prefirmada temporal, que caduca
  a los 15 minutos. El bucket es privado y el binario no pasa por la API.

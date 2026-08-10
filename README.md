# GarajApp — Gestión de taller mecánico

Plataforma para administrar un taller de autos y motos: requerimientos de servicio, órdenes
de trabajo con evidencia fotográfica, inventario de repuestos por sucursal, cotizaciones por
WhatsApp, reportes de ingresos y avisos a cada perfil.

Tres perfiles: **Dueño**, **Técnico** y **Cliente**. Un taller (tenant) con N sucursales.

## Qué hace cada cliente

El taller trabaja con el teléfono en la mano, así que la app cubre la operación completa del
Dueño: recibir el vehículo, repartir el trabajo, cargar repuestos, cotizar, cobrar y mover la
bodega. El web es la misma información con más sitio para leerla, y se queda con lo que es de
escritorio: exportar a Excel, el tablero por estados y la vista pública de la cotización.

| | Web | App |
| --- | --- | --- |
| Órdenes, pasos, fotos, estados | Tablero por estado | Lista y detalle |
| Órdenes entregadas y historial del vehículo | Sí | Sí |
| Requerimientos y recepción en mostrador | Sí | Sí |
| Cotizar: armar líneas, PDF, WhatsApp | Sí | Sí |
| Cerrar y facturar, abonos, factura en PDF | Sí | Sí |
| Inventario: entradas, ajustes, traslados, kardex | Sí | Sí |
| Clientes y acceso a la app | Sí | Sí |
| Usuarios y reportes | Sí | Sí |
| Ficha del taller y su logo (sale en cotizaciones y facturas) | Sí | — |
| Avisos: campana dentro de la app | Sí | Sí |
| Avisos: push que hace sonar el teléfono | — | Sí, con Firebase configurado ([docs/push.md](docs/push.md)) |
| Exportar reportes a CSV | Sí | — |
| Catálogo de mano de obra | Sí | Solo elegir |
| Vista pública de la cotización | Sí | — |

## Estructura

| Carpeta | Qué es |
| --- | --- |
| [backend/](backend/) | API en .NET 8 (Domain / Application / Infrastructure / Api) |
| [web/](web/) | Panel web en Vue 3 + Vite + TypeScript. La raíz del sitio es la página de venta ([LandingView](web/src/views/LandingView.vue)); el panel entra por `/login` |
| [mobile/](mobile/) | App móvil en Flutter (Dueño, Técnico y Cliente) |
| [marca-garajapp/](marca-garajapp/) | Paquete de marca: logotipos, iconos y tokens. Ver su [LEEME](marca-garajapp/LEEME.md) |
| [propuesta-garajapp.html](propuesta-garajapp.html) | Propuesta comercial con precios, para mandar o imprimir a PDF. La página de venta no publica precios: dependen de las sucursales y de la forma de pago |
| [docs/](docs/) | [Modelo de dominio](docs/domain-model.md), [contrato de la API](docs/api.md), [despliegue](docs/deployment.md), [notificaciones push](docs/push.md) y [datos de demostración](docs/demo.md) |

## Marca

El paquete de [marca-garajapp/](marca-garajapp/) es la fuente; lo demás son copias derivadas
de él. Si la marca cambia, se cambia allí y se vuelve a derivar:

| Dónde | Qué | Cómo se regenera |
| --- | --- | --- |
| `web/src/styles/main.css` | Colores, tipografías y radios | A mano, desde `tokens/garajapp-tokens.css` |
| `web/public/brand/`, `web/public/favicon.svg` | Logotipos e iconos del navegador | Copia directa |
| `mobile/lib/core/theme/garaj_brand.dart` | Tema de Flutter | A mano, desde `tokens/garaj_brand.dart` |
| `mobile/assets/brand/` | Origen de iconos y arranque | Copia + derivados (ver abajo) |
| Iconos de lanzador | Android e iOS | `cd mobile && dart run flutter_launcher_icons` |
| Pantalla de arranque nativa | Android e iOS | `cd mobile && dart run flutter_native_splash:create` |

Los dos derivados de `mobile/assets/brand/` no se dibujan a mano:

- `icono-adaptativo.png` — la tuerca blanca con la G calada, reducida al 62% y centrada.
  Android recorta el icono adaptativo con la máscara de cada lanzador y solo garantiza el
  66% central; sin ese margen, un lanzador redondo se come las puntas de la tuerca.
- `icono-ios.png` — el icono a sangre, sin las esquinas redondeadas del PNG de marca. iOS
  aplica su propia máscara y no admite transparencia: con las esquinas ya redondeadas, lo
  que queda fuera se aplana a blanco y asoma como un filo claro alrededor.

Las tipografías van empaquetadas en `mobile/assets/fonts/` y no descargadas en tiempo de
ejecución: en el taller la señal es mala y la aplicación no puede quedarse esperando una
fuente para dibujar la primera pantalla. El web sí las pide a Google Fonts, donde el
navegador ya las tiene en caché la mayoría de las veces.

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

# Humo del backend: 35 comprobaciones del flujo y del alcance por perfil
python3 backend/tests/smoke/fase1_smoke.py

# Humo de las fotos: 30 comprobaciones del ciclo subir / confirmar / listar / borrar
python3 backend/tests/smoke/fase2_smoke.py

# Humo del inventario: 49 comprobaciones de existencias, kardex y consumo
python3 backend/tests/smoke/fase3_smoke.py

# Humo de cotizaciones: 57 comprobaciones del circuito con el cliente
python3 backend/tests/smoke/fase4_smoke.py

# Humo de ventas y reportes: 63 comprobaciones de facturación y desglose
python3 backend/tests/smoke/fase5_smoke.py

# Humo de avisos: 40 comprobaciones de la campana, el aislamiento y la cita del cliente
python3 backend/tests/smoke/fase6_smoke.py

# Humo de recepción: 31 comprobaciones del alta en mostrador, diagnóstico y PDF
python3 backend/tests/smoke/fase7_smoke.py

# Humo de crédito: 40 comprobaciones de abonos, saldos y cuentas por cobrar
python3 backend/tests/smoke/fase8_smoke.py

# Humo de mano de obra: 55 comprobaciones de los dos modos de cobro y de los filtros
python3 backend/tests/smoke/fase9_smoke.py

# Humo de usuarios: 34 comprobaciones del alta de técnicos y del acceso de los clientes
python3 backend/tests/smoke/fase10_smoke.py

# Humo del taller: 29 comprobaciones de la ficha, el logo y las dos rutas que lo sirven
python3 backend/tests/smoke/fase11_smoke.py

# Alta del taller de un cliente (imprime la contraseña del Dueño una sola vez)
cd backend && dotnet run --project src/Garaj.Api -- provision-tenant \
  --name "Taller del Cliente" --branch "Matriz" --city Tegucigalpa \
  --owner-email dueno@cliente.hn --owner-name "Nombre del Dueño"

# Humo del móvil: 16 casos de sesión, alcance por perfil y las pantallas del taller
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

# Despliegue

| Pieza | Servicio | URL |
| --- | --- | --- |
| Base de datos | Supabase (`us-east-1`) | — |
| API .NET | Render (Docker, plan Starter, Virginia) | <https://garaje-app.onrender.com> |
| Panel web | Cloudflare Pages | <https://www.garajeapp.com> (antes <https://garaje-app.pages.dev>) |
| Fotos | Cloudflare R2 (a partir de la Fase 2) | — |

Todo esto requiere cuentas propias. Los pasos de abajo son los que hay que hacer a mano una
sola vez; el repo ya trae [render.yaml](../render.yaml), el
[Dockerfile](../backend/Dockerfile) y la configuración de Pages.

---

## 1. Supabase

1. Cree el proyecto en <https://supabase.com/dashboard>. **Región: `us-east-1` (N. Virginia)**, la misma del servicio en Render,
   para no pagar latencia de red en cada consulta y por cercanía a Honduras.
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

   El usuario del pooler lleva el project-ref pegado: `postgres.<project-ref>`, no
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
   | `Cors__AllowedOrigins__0` | `https://www.garajeapp.com` |
   | `Cors__AllowedOrigins__1` | `https://garajeapp.com` |
   | `PublicBaseUrl` | `https://www.garajeapp.com` |

   Los dos guiones bajos de `Cors__AllowedOrigins__0` son la forma en que .NET mapea un
   elemento de arreglo desde variables de entorno; sin el `__0` final no lo lee. La URL va
   **sin barra al final**: se compara carácter por carácter contra el header `Origin`.

   `Jwt__SigningKey` la genera Render sola. **No la cambie después**: rotarla invalida todas
   las sesiones abiertas.

4. El health check es `/health` y comprueba también la conexión a la base: si Supabase no
   responde, Render marca el despliegue como fallido en lugar de dejar la API a medias.

**Sobre el plan**: producción va en **Starter** (US$7/mes) desde el 11 de agosto de 2026. El
plan free se duerme tras 15 minutos sin tráfico y el primer request tarda ~40 segundos, que es
inaceptable para un taller que abre la app a primera hora. Starter no duerme y no consume las
750 horas gratis del workspace, que quedan enteras para el entorno de pruebas. Se cambia en
**Settings → Instance Type** del servicio, prorrateado por hora.

---

## 2.b Entorno de pruebas

Hay un segundo servicio en el blueprint, `garaj-api-pruebas`, que sigue la rama **`pruebas`**.
El flujo queda así: se empuja a `pruebas`, se comprueba contra ese entorno, y hasta entonces se
mezcla a `main`, que es lo que despliega producción.

**Por qué existe**, más allá de probar migraciones: `POST /api/demo/seed` borra la base entera,
así que en producción solo puede correrse antes de dar de alta al primer taller. En pruebas la
bandera queda encendida para siempre y la demostración se refresca cuando haga falta.

Lo que cambia respecto a producción:

| Variable | Producción | Pruebas |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Development` — expone `/swagger`, siembra los datos de desarrollo en una base vacía y devuelve errores con detalle |
| `ConnectionStrings__Default` | Supabase del taller | **otro proyecto de Supabase** |
| `Demo__AllowSeeding` | ausente | `true` |
| `Storage__Bucket` | `garaj-media` | `garaj-media-test` |
| `Jwt__SigningKey` | la generada | otra generada: un token de pruebas no vale en producción |
| `Cors__AllowedOrigins__0` · `PublicBaseUrl` | dominio real | URL del preview de Pages |

> En `Development` la lista de orígenes ya viene de `appsettings.Development.json`
> (`localhost:5173`, `localhost:4173`) y la variable de entorno **reemplaza el índice 0**, no se
> suma al final. Por eso en `__0` va la URL del preview y `localhost:4173` sigue permitido.

### Lo que hay que crear a mano, una vez

1. **Rama**: `git switch -c pruebas && git push -u origin pruebas`.
2. **Base**: otro proyecto en Supabase, misma región `us-east-1`. El plan free permite dos
   proyectos activos, y **pausa el proyecto tras 7 días sin uso**: si el entorno de pruebas
   responde 500 al arrancar, es que hay que despausarlo en el dashboard.
3. **Bucket**: `garaj-media-test` en R2, con el mismo CORS de
   [r2-cors.json](../r2-cors.json) más la URL del preview de Pages.
4. **Render**: crear el servicio **a mano**, no sincronizando el blueprint. Los servicios de
   este workspace se crearon desde el dashboard y Render no adopta servicios existentes al
   aplicar un blueprint: los crea de nuevo, así que un Sync duplicaría producción.

   En el proyecto, **+ Add environment** con nombre `Pruebas`, y dentro **New → Web Service**:

   | Campo | Valor |
   | --- | --- |
   | Repositorio | el mismo |
   | Name | `garaje-app-pruebas` |
   | Branch | `pruebas` |
   | Language / Runtime | **Docker** |
   | Dockerfile Path | `./backend/Dockerfile` |
   | Docker Build Context Directory | `./backend` |
   | Region | Virginia (US East) |
   | Instance Type | **Free** |
   | Health Check Path | `/health` |

   Y las variables de entorno del bloque `garaje-app-pruebas` de
   [render.yaml](../render.yaml): ahí está la lista completa con el valor de cada una.
5. **Panel**: en el proyecto de Pages, **Settings → Environment variables → Preview**, poner
   `VITE_API_URL` con la URL del servicio de pruebas. Los despliegues de una rama que no es la
   de producción usan esas variables, así que la rama `pruebas` sale sola en
   `pruebas.garaje-app.pages.dev` apuntando a la API de pruebas.

### Cómo se usa

```bash
git switch pruebas && git merge main     # o trabajar directamente en la rama
git push                                 # Render y Pages despliegan solos

# Sembrar la demostración en pruebas, cuando haga falta
TOKEN=$(curl -s https://garaje-app-pruebas.onrender.com/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"dueno@tallerdemo.hn","password":"Garaj123!"}' \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])')

curl -s -X POST https://garaje-app-pruebas.onrender.com/api/demo/seed \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"confirm":"BORRAR Y SEMBRAR","weeks":6}'
```

El Dueño con el que se pide el token depende de qué haya en la base:

- **Base vacía** (entorno recién creado): el entorno corre en Development y siembra sola los
  datos de desarrollo, así que ahí el Dueño es `owner@garaj.test`.
- **Ya sembrada**: la siembra de demostración **borra la base entera**, `owner@garaj.test`
  incluido, y deja el Taller Demo. A partir de la primera vez el Dueño es
  `dueno@tallerdemo.hn`, que es el del comando de arriba.

Si la llamada devuelve un 502 de Render en vez de la respuesta de la API —pasa cuando el
servicio está reiniciando por un despliegue—, **no la repita a ciegas**: la siembra
probablemente sí corrió y solo se perdió la respuesta. Compruebe con un login a
`dueno@tallerdemo.hn` antes de volver a lanzarla.

**Sobre el costo:** este entorno va en plan free, así que son US$0. Los servicios gratuitos de la
cuenta comparten **750 horas de instancia al mes**, pero producción ya está en Starter y no toca
esa bolsa: las 750 horas son enteras para pruebas, que además duerme a los 15 minutos sin
tráfico y solo gasta mientras se usa.

**La demostración que ve el cliente sigue en producción.** La app que instalan sus técnicos —y la
que revisa Apple— apunta a producción, así que el Taller Demo de allá no se toca: este entorno es
para probar cambios, no para reemplazarlo.

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
VITE_API_URL = https://garaje-app.onrender.com
```

`web/public/_redirects` ya manda todas las rutas a `index.html`, que es lo que necesita el
history mode del router; sin eso, recargar `/dashboard` daría 404.

Después del primer despliegue, vuelva a Render y ponga la URL real en
`Cors__AllowedOrigins__0` y `PublicBaseUrl`.

### Dominio propio

Con el dominio en la misma cuenta de Cloudflare, se conecta desde **Workers & Pages → el
proyecto → Custom domains → Set up a custom domain**, y hay que agregar **los dos nombres**:
`garajeapp.com` y `www.garajeapp.com`. Cloudflare crea los registros DNS y emite el
certificado; no hay que escribir ningún registro a mano. En **SSL/TLS** el modo va en
**Full (strict)**: en «Flexible» la página entra en bucle de redirecciones.

Tres cosas que hay que tocar después, o el dominio nuevo queda a medias:

- `Cors__AllowedOrigins__*` y `PublicBaseUrl` en Render, como arriba. Sin lo primero el panel
  carga pero no deja entrar; sin lo segundo los enlaces de cotización siguen saliendo con el
  nombre viejo.
- El **CORS del bucket R2** (paso 4): la subida de fotos va del navegador directo a R2, así que
  el dominio nuevo tiene que estar en la lista de orígenes permitidos.
- Si el dominio recién se registró, los resolvedores de DNS que ya habían preguntado guardan la
  respuesta «no existe» hasta media hora. Se comprueba contra un resolvedor público
  (`dig @1.1.1.1 www.garajeapp.com`) antes de dar por roto el despliegue.

---

## 4. Cloudflare R2 (fotos)

1. **R2 → Create bucket**, nombre `garaj-media`, ubicación automática.
2. **Manage R2 API Tokens → Create token**, permiso *Object Read & Write* sobre ese bucket.
   Anote el **Account ID**, el Access Key ID y el Secret: el secreto solo se ve una vez.
3. Configure el CORS del bucket. Sin ello el navegador bloquea la subida desde el panel: el
   `PUT` va del navegador **directo a R2**, así que es R2 quien tiene que permitir el origen,
   no la API. (La app móvil no se ve afectada: el CORS es una regla del navegador.)

   Por línea de comandos, que es lo que se usó:

   ```bash
   npx wrangler@4.120.1 login
   npx wrangler@4.120.1 r2 bucket cors set garaj-media --file r2-cors.json
   npx wrangler@4.120.1 r2 bucket cors list garaj-media   # para comprobar
   ```

   El archivo es [r2-cors.json](../r2-cors.json), en la raíz del repo. La versión va fijada a
   propósito: `wrangler` publica varias veces por semana y en agosto de 2026 la última (4.121.0)
   traía una dependencia inexistente, así que `npx wrangler` fallaba al instalar con
   `ETARGET · No matching version found for miniflare@…`. Si la versión fijada envejece, se
   busca una donde la `miniflare` que pide exista de verdad:

   ```bash
   npm view wrangler@<version> dependencies.miniflare   # y comprobar que esa exista
   ```

   ```json
   {
     "rules": [
       {
         "allowed": {
           "origins": [
             "https://www.garajeapp.com",
             "https://garajeapp.com",
             "https://garaje-app.pages.dev",
             "http://localhost:5173"
           ],
           "methods": ["GET", "PUT"],
           "headers": ["content-type"]
         },
         "maxAgeSeconds": 3600
       }
     ]
   }
   ```

   > Ojo: el panel web de Cloudflare (**Settings → CORS policy**) pide el formato de S3
   > —una lista de reglas con `AllowedOrigins`, `AllowedMethods`…—, mientras que wrangler
   > pide el de la API de R2, el de arriba. No son intercambiables.

   Para comprobar que quedó, sin abrir un navegador:

   ```bash
   curl -si -X OPTIONS "https://<account-id>.r2.cloudflarestorage.com/garaj-media/x" \
     -H "Origin: https://www.garajeapp.com" \
     -H "Access-Control-Request-Method: PUT" \
     -H "Access-Control-Request-Headers: content-type" | grep -i access-control
   ```

4. En Render:

   | Variable | Valor |
   | --- | --- |
   | `Storage__ServiceUrl` | `https://<account-id>.r2.cloudflarestorage.com` |
   | `Storage__AccessKey` | Access Key ID del token |
   | `Storage__SecretKey` | Secret Access Key del token |
   | `Storage__Bucket` | `garaj-media` |

El bucket queda **privado**: las fotos se sirven por URL prefirmada temporal, nunca por URL
pública.

Mientras no estén esas variables, el taller opera con normalidad —órdenes, pasos, estados—
y solo los endpoints de `/api/media` responden **503** diciendo qué falta. Es a propósito:
un bucket sin configurar no debe tumbar la API.

---

## 5. Notificaciones push (opcional)

Dos variables más en Render, cuando exista el proyecto de Firebase:

| Variable | Valor |
| --- | --- |
| `Push__ProjectId` | Id del proyecto de Firebase |
| `Push__ServiceAccountJson` | El JSON de la cuenta de servicio, entero y en una línea |

Sin ellas la API arranca igual y los avisos se quedan en la campana de cada usuario, que es
donde de todos modos hay que poder verlos. El paso a paso completo —incluida la clave de APNs,
sin la cual el push no llega a ningún iPhone— está en [push.md](push.md).

El JSON de la cuenta de servicio es una credencial: va en el panel de Render, nunca en el
repositorio.

---

## 6. Alta de un taller

En producción no hay pantalla ni endpoint para crear un taller: el sembrador de demostración
solo corre en Development y `POST /api/demo/seed` **borra la base entera** antes de sembrar.
Un taller real se da de alta con un comando, desde la máquina de quien instala, apuntando a
la base de producción con la cadena en user secrets (ver [README](../README.md)):

```bash
cd backend
dotnet run --project src/Garaj.Api -- provision-tenant \
  --name "Taller del Cliente" \
  --legal-name "Taller del Cliente S. de R.L." --tax-id 08019995123456 \
  --phone 50499001111 --email contacto@cliente.hn \
  --branch "Matriz" --branch-code MTZ --city Tegucigalpa \
  --owner-email dueno@cliente.hn --owner-name "Nombre del Dueño" \
  --logo ~/Descargas/logo-cliente.png
```

Crea el taller, su primera sucursal y el usuario Dueño, e imprime la contraseña generada
**una sola vez**: no queda guardada en ninguna parte. Si el taller ya existe o el correo ya
tiene usuario, no escribe nada y lo dice. `dotnet run … -- provision-tenant` sin argumentos
lista todos los que acepta.

El catálogo de repuestos, la mano de obra y los técnicos los carga el Dueño desde el panel.
El logo se puede subir después en **Taller** sin volver a la consola.

### Puesta en marcha del primer cliente

El orden importa, porque `POST /api/demo/seed` **borra la base entera** y en producción el
taller de demostración y el del cliente viven en la misma:

1. **Dejar la base de demostración como se quiere presentar.** Con
   `Demo__AllowSeeding` puesta, sembrar el **Taller Demo** (ver [demo.md](demo.md)). Este es el
   único momento en que se puede: después ya no.
2. **Dar de alta el taller del cliente** con `provision-tenant`, como arriba. Queda aislado del
   de demostración por el filtro de tenant: ninguno ve los datos del otro.
3. **Quitar `Demo__AllowSeeding` de Render** y no volver a ponerla. Desde aquí, sembrar la
   demostración borraría el taller del cliente.
4. Entregar la contraseña del Dueño **por un canal aparte** —no por el mismo correo donde va el
   enlace— y hacerlo cambiarla al entrar.

Si más adelante hace falta refrescar la demostración, ya no se puede sembrar: se crea otro
taller con `provision-tenant` y se le cargan a mano los datos que se quieran enseñar.

Antes de entregarle el sistema a un cliente:

- [ ] **Quitar `Demo__AllowSeeding` de Render.** Con datos reales en esa base,
      `POST /api/demo/seed` es un botón de borrado total.
- [ ] Confirmar `Storage__*` (sin ellas no hay fotos ni logo), `Jwt__SigningKey` y
      `PublicBaseUrl`.
- [ ] Entrar como el Dueño nuevo, cambiar la contraseña y completar la ficha en **Taller**:
      esos datos son los que salen impresos en cada cotización y factura.
- [ ] Si el taller factura con talonario del SAR, registrar su **CAI** y su rango en
      **Taller → Facturación**. Sin eso el sistema trabaja igual, pero sus facturas son
      comprobantes de entrega. Ver [api.md](api.md#facturación-con-cai).

---

## 7. Móvil

La app apunta a la API por `--dart-define`:

```bash
# Contra el backend desplegado: es el valor por defecto, no hace falta pasar nada
flutter run

# Contra el backend local (la dirección del host cambia según el dispositivo)
flutter run --dart-define=API_URL=http://localhost:5080      # simulador iOS
flutter run --dart-define=API_URL=http://10.0.2.2:5080       # emulador Android
flutter run --dart-define=API_URL=http://192.168.1.10:5080   # teléfono físico
```

Los binarios de tienda ya salen apuntando a producción:

```bash
flutter build ipa
flutter build appbundle
```

### iOS: cuenta de Apple y TestFlight

La app se distribuye primero por **TestFlight**: el binario se sube y los técnicos del taller lo
instalan por invitación, sin esperar la revisión pública. Cada versión sirve 90 días.

Hace falta el **Apple Developer Program** (US$99 al año, inscripción *Individual*), y que el
Apple ID de la cuenta tenga verificación en dos pasos. La cuenta la paga y la administra quien
publica; el taller no necesita cuenta de nada.

Datos del proyecto, ya fijados en el repositorio:

| | |
| --- | --- |
| Bundle ID | `com.garaj.garajApp` |
| Nombre visible | GarajApp |
| Versión | `pubspec.yaml`, campo `version: 1.0.0+1` — el número tras el `+` sube en cada subida |
| Dispositivos | **solo iPhone** (`TARGETED_DEVICE_FAMILY = 1`) |
| Exportación | `ITSAppUsesNonExemptEncryption = false`: la app solo usa HTTPS, así que no hay que responder el cuestionario de criptografía en cada subida |
| Política de privacidad | <https://garajeapp.com/privacidad> — obligatoria para publicar y para las pruebas externas de TestFlight |

Pasos, una vez activa la cuenta:

1. En **App Store Connect → Apps → +**, crear la app con el bundle ID de arriba, idioma
   principal español (México o España, Apple no lista Honduras como idioma), y un SKU cualquiera
   —`garajapp-001` sirve—.
2. En Xcode, abrir `mobile/ios/Runner.xcworkspace`, seleccionar el equipo de la cuenta en
   **Signing & Capabilities** y dejar la firma automática.
3. Compilar y subir:

   ```bash
   cd mobile && flutter build ipa
   # y subir build/ios/ipa/*.ipa con Transporter, o desde Xcode → Organizer → Distribute
   ```

4. En **TestFlight**, crear un grupo de prueba externa con enlace público y mandarle el enlace
   al taller por WhatsApp. El primer grupo externo pasa por una revisión ligera de Apple —suele
   tardar menos de un día— y pide una descripción de qué probar y un correo de contacto.
5. Para la revisión, dar la cuenta del **Taller Demo** (`dueno@tallerdemo.hn` / `Garaj123!`): el
   revisor de Apple necesita entrar, y no se le puede dar el taller de un cliente. Eso obliga a
   tener el Taller Demo sembrado en producción.

Una cosa que conviene resolver antes de repartir el enlace:

- **Los avisos push no están configurados** (falta la llave APNs y la capacidad de Push
  Notifications en el proyecto). La app pide el permiso de notificaciones y luego no llega
  ninguna: o se completa lo de [push.md](push.md), o conviene no pedir el permiso todavía. Los
  avisos dentro de la app, en la campana, funcionan igual.

### Firma de Android

Sin llave propia, el release se firma con la **llave de depuración**, que genera Flutter sola
en cada máquina. Eso funciona para probar, pero tiene dos consecuencias que solo se ven tarde:

- Android **no deja instalar encima** de un APK firmado con otra llave. Al actualizar el
  teléfono del taller hay que desinstalar, y con eso se va la sesión y la cola de fotos que
  todavía no habían subido.
- Google Play **no acepta** un binario firmado con llave de depuración.

La llave se crea una vez y **se guarda para siempre**: si se pierde, no hay forma de publicar
una actualización de la misma aplicación. Ni la llave ni sus contraseñas entran al repositorio
—`android/.gitignore` ya excluye `key.properties`, `*.jks` y `*.keystore`—.

```bash
# 1. Crear la llave (validez larga a propósito: Play exige más de 2033)
keytool -genkey -v -keystore ~/llaves/garajapp.jks \
  -storetype JKS -keyalg RSA -keysize 2048 -validity 10000 -alias garajapp

# 2. Decirle a Gradle dónde está
cat > mobile/android/key.properties <<'FIN'
storePassword=<la que puso>
keyPassword=<la que puso>
keyAlias=garajapp
storeFile=/Users/<usuario>/llaves/garajapp.jks
FIN

# 3. Compilar
cd mobile && flutter build apk --release        # para instalar a mano en el taller
cd mobile && flutter build appbundle            # para Google Play
```

`android/app/build.gradle.kts` lee ese archivo si existe y, si no, sigue firmando con la de
depuración: así CI y cualquier máquina nueva compilan igual sin tener la llave.

Guarde en sitio seguro —gestor de contraseñas, no el repo— el `.jks`, las dos contraseñas y el
alias. Conviene también respaldar el `.jks` fuera de la máquina.

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

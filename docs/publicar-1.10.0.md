# Publicar la 1.10.0 (24)

Lo que hay que hacer, en orden, para que la 1.10.0 llegue a las dos tiendas. Los textos de la
ficha no se tocan: siguen sirviendo los de [app-store.md](app-store.md) y
[play-store.md](play-store.md). Lo que cambia en cada versión son las **notas** y el paquete.

Qué lleva esta versión: el armado de motos del dueño dentro de la app —las motos del taller,
los cortes y facturas, el pago a los técnicos y el catálogo del contrato—, los filtros y las
páginas en lo que cada quien lleva armado, y los pasos de la orden que cada taller enciende.

> **La 23 no existe en las tiendas.** La 1.9.0 se publicó y quemó hasta la 22; la 23 se
> compiló para probar en el teléfono y nunca se subió. Esta versión va como **1.10.0 (24)**.

---

## 1. Antes de compilar

| Comprobación | Cómo |
| --- | --- |
| El móvil analiza limpio | `cd mobile && flutter analyze` |
| Las pruebas del móvil pasan | `cd mobile && flutter test` — incluye la del router, que es la que caza una pantalla nueva que no está en las listas de rutas y termina mandando al inicio |
| Las pruebas del servidor pasan | `cd backend && dotnet test` (las de `RequiereBaseLocal` necesitan la base local del puerto 5434) |
| El panel compila **de verdad** | `cd web && npm run build` — `vue-tsc --noEmit` deja pasar errores que tumban el despliegue |
| Los recorridos a mano | [pruebas-del-armado.md](pruebas-del-armado.md) §8, que es lo nuevo de esta versión |

Y el servidor va primero, como siempre: la API de producción tiene que entender ya el periodo
y la página de `motorcycles/mine` y los filtros de `settlements` cuando la primera app
actualizada le hable. Render despliega solo desde `main`.

---

## 2. Compilar

```bash
# Cambió `version:`, así que el clean no es opcional
cd mobile && flutter clean && flutter pub get

# iOS — App Store
flutter build ipa

# Android — Google Play
flutter build appbundle --release
```

**En ese orden.** `flutter clean` borra `build/ios/`, así que un clean después del `ipa` se
lleva el paquete que se acaba de hacer.

El `.ipa` sale en `mobile/build/ios/ipa/`; el `.aab`, en
`mobile/build/app/outputs/bundle/release/`.

Dos comprobaciones que cuestan una tarde si se olvidan:

- **El número de compilación.** Xcode lo sube solo al exportar si ese número ya existe en App
  Store Connect. Hay que **leerlo del paquete**, no del `pubspec.yaml`:

  ```bash
  cd /tmp && rm -rf ipacheck && mkdir ipacheck && cd ipacheck
  unzip -q ~/dev/garaj-app/mobile/build/ios/ipa/*.ipa "Payload/*/Info.plist"
  /usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" -c "Print :CFBundleVersion" Payload/*.app/Info.plist
  ```

  El `.aab` no tiene ese problema: Gradle usa el del `pubspec`.

- **Que el paquete lleve el código nuevo.** Mirar la versión no sirve. Hay que buscar un texto
  del código nuevo dentro del binario, y en Latin-1 **y** UTF-16 —Dart guarda en UTF-16 lo que
  lleva tildes—. De esta versión sirven `cortes-de-armado`, `Cerrar factura` y `Esta semana`.

---

## 3. Lo que se pega en cada consola

Las dos notas están en [notas-de-version.md](notas-de-version.md), bloque **1.10.0 (24)**: la
de Play son 490 caracteres de los 500 que deja, y la de App Store va entera.

| Dónde | Qué |
| --- | --- |
| App Store Connect → la versión 1.10.0 | Novedades de esta versión; subir el `.ipa` con Transporter |
| Play Console → Producción → Nueva versión | El `.aab`; «Novedades» en es-419 |

Nada que declarar de nuevo en **Play → Seguridad de los datos**: esta versión no pide un
permiso más ni manda nada a ningún tercero. Lo que se agregó son pantallas que hablan con la
misma API.

---

## 4. Al publicar

**Lanzamiento escalonado, siempre.** No existe volver atrás: lo único que se puede hacer con
una versión mala es detenerla, y si salió al 100% no hay nada que detener. Los umbrales están
en [deployment.md §8](deployment.md#umbrales-para-seguir-subiendo).

Y cuando las dos consolas enseñen que la mayoría ya está en la 24, en Render:

```
AppVersion__RecommendedBuild = 24
AppVersion__Message = Actualice para llevar el armado de motos completo en el teléfono.
```

`AppVersion__MinimumBuild` no se pone. Esa es para el día que algo rompa de verdad.

Y hay una razón de más para empujar esta actualización en el taller que arma motos: a la
1.9.0 el técnico le sale **«L 0.00»** en cada moto suya. No es un dato: es el cero con el que
viaja lo que el servidor no le manda, porque esa versión se cae con un vacío. Desde la 24 no
se pinta nada. Cuando no quede ninguna 21 ni 22 en la calle, ese cero puede volver a ser nulo
en `MotorcycleAssemblyService.Proyectar`.

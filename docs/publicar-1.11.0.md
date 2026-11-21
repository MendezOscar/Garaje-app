# Publicar la 1.11.0 (25)

Lo que hay que hacer, en orden, para que la 1.11.0 llegue a las dos tiendas. Los textos de la
ficha no se tocan: siguen sirviendo los de [app-store.md](app-store.md) y
[play-store.md](play-store.md). Lo que cambia en cada versión son las **notas** y el paquete.

Qué lleva esta versión **en la app**: traer a la orden los repuestos que ya se cotizaron, el
aviso de cuando la cotización cambió después, y corregir precios sin quitar la línea —en la
orden y en la cotización—, con la opción de dejarlos también en el catálogo.

Qué **no** va por la tienda, porque es del panel y ya está desplegado: el calendario de citas
del Dueño, la pestaña **Uso** del panel de administración y los remiendos de la landing.

> **La 1.10.0 (24)**. Si todavía está en revisión, subir la 25 la reemplaza: en las dos
> consolas solo hay una versión en cola. Si ya salió, revise antes si quedó pendiente poner
> `AppVersion__RecommendedBuild = 24` en Render —en ese caso ya no hace falta: se pone 25 al
> final de esta lista—.

---

## 1. Antes de compilar

| Comprobación | Cómo |
| --- | --- |
| El móvil analiza limpio | `cd mobile && flutter analyze` |
| Las pruebas del móvil pasan | `cd mobile && flutter test` — incluye la del router, la que caza una pantalla nueva que no está en las listas de rutas y termina mandando al inicio |
| Las pruebas del servidor pasan | `cd backend && dotnet test` (las de `RequiereBaseLocal` necesitan la base local del puerto 5434) |
| El panel compila **de verdad** | `cd web && npm run build` — `vue-tsc --noEmit` deja pasar errores que tumban el despliegue |
| Los recorridos a mano | §5 de este documento |

**El servidor va primero**, y esta vez no es una formalidad: la app nueva llama a
`POST /api/work-orders/{id}/parts/from-quote`, a `PUT …/parts/{lineId}/price` con
`updateCatalog` y a `PUT /api/quotes/{id}/lines/{lineId}`. Si la API de producción no los
entiende todavía, los botones nuevos dan error desde el primer toque. Render despliega solo
desde `main`.

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
  lleva tildes—. De esta versión sirven `from-quote`, `Traer los` y
  `Esto cambió en la cotización y aquí no:` y `ya salió de bodega`.

---

## 3. Lo que se pega en cada consola

Las dos notas están en [notas-de-version.md](notas-de-version.md), bloque **1.11.0 (25)**: la
de Play son 443 caracteres de los 500 que deja, y la de App Store va entera.

| Dónde | Qué |
| --- | --- |
| App Store Connect → la versión 1.11.0 | Novedades de esta versión; subir el `.ipa` con Transporter |
| Play Console → Producción → Nueva versión | El `.aab`; «Novedades» en es-419 |

Nada que declarar de nuevo en **Play → Seguridad de los datos**: esta versión no pide un
permiso más ni manda nada a ningún tercero. Lo que se agregó son pantallas que hablan con la
misma API.

---

## 4. Al publicar

**Lanzamiento escalonado, siempre.** No existe volver atrás: lo único que se puede hacer con
una versión mala es detenerla, y si salió al 100% no hay nada que detener. Los umbrales están
en [deployment.md §8](deployment.md#umbrales-para-seguir-subiendo).

Y cuando las dos consolas enseñen que la mayoría ya está en la 25, en Render:

```
AppVersion__RecommendedBuild = 25
AppVersion__Message = Actualice para traer a la orden los repuestos que ya cotizó.
```

`AppVersion__MinimumBuild` no se pone. Esa es para el día que algo rompa de verdad.

---

## 5. Los recorridos a mano

Con una orden abierta que tenga una cotización con repuestos:

1. **Traerlos.** Arriba de Repuestos sale «Traer los N de COT-…». Al tocarlo entran las
   líneas **por confirmar**, con el precio de la cotización. La bodega **no se mueve**:
   compruébelo en Inventario antes y después.
2. **No duplicar.** Tóquelo otra vez: tiene que decir que ya estaban cargados y no agregar
   nada.
3. **Confirmar.** Confirme una línea: ahí sí baja la existencia. Sin existencia tiene que
   decir que hay que comprarlo.
4. **El desfase.** Con la cotización todavía en borrador o enviada, cámbiele el precio a ese
   repuesto. En la orden tiene que aparecer el aviso «Esto cambió en la cotización y aquí
   no», con los dos números.
5. **Corregir el precio.** En la línea, «Editar». Póngale otro precio y compruebe que el
   aviso se va.
6. **La cantidad mueve bodega.** En una línea **ya confirmada** del catálogo, bájele la
   cantidad: la pantalla avisa antes de guardar y la existencia tiene que subir. Súbala por
   encima de lo que hay y tiene que cortar diciendo cuánto queda.
7. **Al catálogo.** Como Dueño, marque «Cambiarlo también en el catálogo» y revise en
   Inventario que el precio de venta quedó cambiado. Como Encargado, el interruptor no se
   ofrece —y si se fuerza por API, el servidor responde 403—.
8. **La línea de la cotización.** En una cotización sin responder, edite una línea: cantidad,
   precio y descuento. El total de la cotización tiene que recalcularse solo.
9. **La cotización aprobada no se edita.** Apruebe una desde el enlace público y compruebe
   que ya no deja editarla: tiene que decir que cree una nueva.

Y lo de siempre antes de soltar: abrir una orden, cargar un repuesto del catálogo, cotizar,
mandar por WhatsApp, aprobar desde el enlace y facturar.

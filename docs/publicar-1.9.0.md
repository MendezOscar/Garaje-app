# Publicar la 1.9.0 (21)

Lo que hay que hacer, en orden, para que la 1.9.0 llegue a las dos tiendas. Los textos de la
ficha no se tocan: siguen sirviendo los de [app-store.md](app-store.md) y
[play-store.md](play-store.md). Lo que cambia en cada versión son las **notas** y el paquete.

Qué lleva esta versión: el asistente —proponer los pasos y los repuestos, la consulta del
técnico—, el armado de motos, el VIN del vehículo y el arreglo de la hoja de corregir un paso.

---

## 1. Antes de compilar

| Comprobación | Cómo |
| --- | --- |
| El móvil analiza limpio | `cd mobile && flutter analyze` |
| El backend compila | `cd backend && dotnet build` |
| El panel compila **de verdad** | `cd web && npm run build` — `vue-tsc --noEmit` deja pasar errores que tumban el despliegue |
| Las pruebas del servidor pasan | `cd backend && dotnet test` (las de `RequiereBaseLocal` necesitan la base local del puerto 5434) |
| Los recorridos a mano | [pruebas-del-asistente.md](pruebas-del-asistente.md) y [pruebas-del-armado.md](pruebas-del-armado.md), en el **taller de pruebas**, nunca en el del cliente |

Y lo del repuesto por confirmar, que es lo nuevo de esta versión y toca el inventario:

- [ ] El asistente propone, se acepta, y la **existencia en bodega no baja**.
- [ ] Recargar la pantalla —app y panel—: los repuestos siguen ahí, marcados «por confirmar».
- [ ] Confirmar uno: ahí sí baja la existencia, y queda su movimiento en el kardex.
- [ ] Intentar **entregar** la orden con uno sin confirmar: no deja, y dice cuántos faltan.
- [ ] Quitar uno sin confirmar: la bodega **no sube**. Nunca salió.
- [ ] Una orden vieja del taller de pruebas, con repuestos cargados antes de esta versión, no
      enseña ningún aviso de «por confirmar»: la migración los dejó confirmados.

---

## 2. Compilar

El servidor va primero: la API de producción ya tiene que entender `/parts/{id}/confirm` y la
columna nueva cuando la primera app actualizada le hable. Render despliega solo desde `main`.

```bash
# iOS — App Store
cd mobile && flutter build ipa

# Android — Google Play
cd mobile && flutter build appbundle --release
```

Tres cosas que cuestan una tarde si se olvidan:

- **Apuntan a producción solos.** `API_URL` tiene por defecto `https://garaje-app.onrender.com`;
  solo hay que pasar `--dart-define` para apuntar a otro sitio, y aquí no se pasa.
- **El número de compilación se consume.** La 21 solo se sube una vez a cada tienda. Si hay que
  corregir algo, sube a 22 en `pubspec.yaml`.
- **Si se toca `version:` o se agrega un paquete, `flutter clean` antes**, o el release falla
  hablando de `integration_test`, que no tiene nada que ver con el código de la app.

El `.ipa` sale en `mobile/build/ios/ipa/`; el `.aab`, en
`mobile/build/app/outputs/bundle/release/`.

> `flutter build ipa` **no** refresca `build/ios/iphoneos/`. Para comprobar que el paquete
> lleva el código nuevo no sirve mirar la versión: hay que buscar un texto del código nuevo
> dentro del binario, en Latin-1 **y** en UTF-16 —Dart guarda en UTF-16 lo que lleva tildes o
> rayas largas—.

---

## 3. Lo que se pega en cada consola

Las dos notas están escritas en [notas-de-version.md](notas-de-version.md), en el bloque
**1.9.0 (21)**: la de Play son 450 caracteres de los 500 que deja, y la de App Store va entera.

| Dónde | Qué |
| --- | --- |
| App Store Connect → la versión 1.9.0 | Novedades de esta versión; subir el `.ipa` con Transporter |
| Play Console → Producción → Nueva versión | El `.aab`; «Novedades» en es-419 |

Lo demás de la ficha no se toca. Solo hay una casilla que sí cambia y es fácil de pasar por
alto: en **Play → Seguridad de los datos** nada que declarar de nuevo —el asistente corre en el
servidor, la app no manda nada a ningún tercero—, pero la tabla de permisos de
[play-store.md](play-store.md#declaraciones-y-permisos) quedó corregida: la ubicación **sí** se
pide hasta Android 11, solo para buscar el adaptador Bluetooth, y el lector de códigos de
barras usa la misma cámara que ya estaba declarada.

Las capturas de las dos tiendas siguen siendo las de la 1.8.0 y valen: ninguna pantalla de las
que enseñan cambió. Si se quiere que el asistente salga en la ficha, hay que rehacer el juego
completo —no se mezclan capturas de versiones distintas—, y eso es media tarde con el
simulador y la base de demostración.

---

## 4. Al publicar

**Lanzamiento escalonado, siempre.** No existe volver atrás: lo único que se puede hacer con
una versión mala es detenerla, y si salió al 100% no hay nada que detener. Los umbrales para ir
subiendo están en [deployment.md §8](deployment.md#umbrales-para-seguir-subiendo).

Y cuando las dos consolas enseñen que la mayoría ya está en la 21, en Render:

```
AppVersion__RecommendedBuild = 21
AppVersion__Message = Actualice para que el asistente le arme la reparación y le deje puestos los repuestos del trabajo.
```

`AppVersion__MinimumBuild` no se pone. Esa es para el día que algo rompa de verdad.

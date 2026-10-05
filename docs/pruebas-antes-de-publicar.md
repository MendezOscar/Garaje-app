# Pruebas antes de publicar

Lo que hay en `main` sin publicar desde la versión **1.1.4 (9)**: veintisiete commits, seis
entregas de funcionalidad y el orden del diseño. Esta es la lista para pasarlas a mano antes de
mandar los paquetes a las tiendas.

> **Dónde probar.** La aplicación compilada apunta a **producción**
> (`https://garaje-app.onrender.com`), que es la misma API del cliente real. Entre con el taller
> de pruebas, **nunca con el del cliente**: las órdenes, facturas y gastos que se creen aquí son
> reales y le aparecen a él.

---

## 1. Ajustes del taller

Es lo primero, porque casi todo lo demás depende de cómo quede configurado.

**Panel → Taller.**

- [ ] Hay campos nuevos: **garantía por defecto** (días), **bodegaje** (si se cobra y cuánto por
      día), **tasa de ISV** y **el técnico ve los precios**.
- [ ] Guardar y recargar: los valores quedan.
- [ ] Con bodegaje en cero, el cobro por vehículo sin retirar no se propone en ninguna parte.

---

## 2. Venta rápida

Cobrar un trabajo corto sin abrirle orden al vehículo. Es la entrada del mostrador.

**Panel → Venta rápida.** **App → Más → Venta rápida.**

- [ ] Agregar un repuesto del inventario: descuenta existencias.
- [ ] Agregar un servicio del catálogo de mano de obra.
- [ ] Agregar un trabajo escrito a mano, con su precio.
- [ ] Elegir cliente y vehículo —o dejarlo sin vehículo— y cobrar.
- [ ] Poner días de garantía y facturar: la factura en PDF trae el cuadro de garantía.
- [ ] La venta sale en **Ventas** y suma al **estado de resultados**.
- [ ] Vender más de lo que hay en existencia: avisa y no deja.

---

## 3. Recepción del vehículo

**App → una orden → Recepción.** **Panel → detalle de la orden → tarjeta de recepción.**

- [ ] Llenar en el teléfono: kilometraje, nivel de combustible, estado de la carrocería, lo que
      el cliente deja dentro, observaciones.
- [ ] **Firmar con el dedo** y guardar.
- [ ] La firma y las notas se ven en el panel, en el detalle de la orden.
- [ ] Volver a abrir la recepción en el teléfono: trae lo que ya se había escrito.
- [ ] Corregir algo y guardar: no duplica, corrige.

---

## 4. Garantía

La garantía se decide **al facturar** y queda congelada en la factura.

- [ ] Facturar una orden y dejar los días de garantía que propone el taller.
- [ ] Cambiar los días a mano en esa factura: se respeta lo que se escribió.
- [ ] Facturar con cero días: la factura no habla de garantía.
- [ ] En la lista de órdenes, filtro **«en garantía»**: salen las facturadas cuya garantía no ha
      vencido.
- [ ] Las órdenes facturadas **antes** de esta versión no tienen garantía. Es a propósito.

---

## 5. Reclamos

El cliente vuelve diciendo que el trabajo quedó mal.

**Panel → Reclamos.** **App → Más → Reclamos.**

- [ ] Anotar un reclamo sobre una factura: se le pone folio `REC-000001`.
- [ ] Al anotarlo queda registrado **si estaba o no en garantía** ese día. Eso no cambia después,
      aunque la garantía venza.
- [ ] Abrir una **orden de reparación** desde el reclamo: nace ligada a él.
- [ ] Resolver el reclamo: muestra lo que costó la reparación (repuestos más mano de obra).
- [ ] Reabrirlo: vuelve a la lista de abiertos, arriba.
- [ ] Los abiertos salen primero, siempre.

---

## 6. Vehículos sin retirar y bodegaje

- [ ] Pasar una orden a **Lista**: queda marcada la fecha del aviso.
- [ ] Filtro de **días sin retirar**: salen las que llevan tiempo listas y nadie recoge.
- [ ] Al facturar una de esas, con bodegaje configurado, **propone el cobro** por los días.
- [ ] Aceptarlo: entra como línea de mano de obra en la factura.
- [ ] Rechazarlo o ponerlo en cero: la factura sale sin esa línea.

---

## 7. Gastos y estado de resultados

**Panel → Resultados.** **App → Más → Gastos.**

- [ ] Registrar gastos de categorías distintas: alquiler, salarios, servicios, otros.
- [ ] **Agregarle la foto del recibo** a un gasto, desde el panel y desde el teléfono.
- [ ] El estado de resultados cuadra: ingresos, costo de lo vendido, utilidad bruta, gastos,
      utilidad neta.
- [ ] Cambiar el periodo —Todo, Hoy, Semana, Mes— recalcula, y el selector muestra los nombres.
- [ ] Con más de una sucursal, el filtro de sucursal separa bien.
- [ ] La comparación «contra el periodo anterior» tiene sentido.
- [ ] Corregir y borrar un gasto.
- [ ] **Una compra de repuestos para bodega no se registra aquí**: eso es inventario. El aviso lo
      dice en la pantalla.

---

## 8. El técnico y su pago

Esta es la que más hay que probar, porque cambia lo que **otra persona** ve.

**Panel → Usuarios → ficha del técnico.**

- [ ] Elegir cómo se le paga: **fijo**, **porcentaje** de la mano de obra o **por hora**. Guardar
      y recargar: queda.
- [ ] **Panel → Resultados → Pagarle a un técnico**: elegir el técnico y ver la propuesta.
      Con sueldo fijo es el sueldo; con porcentaje, el porcentaje de la mano de obra que generó
      en el periodo; por hora, sus horas por la tarifa.
- [ ] Registrar el pago: pasa al formulario de gasto como **salario**, con su nombre. El monto se
      puede cambiar antes de guardar.
- [ ] El pago aparece en **Gastos del periodo** y baja la utilidad neta.
- [ ] «Ya se le pagó en el periodo» suma lo que ya se le registró.

**Con el técnico sin precios** (apagar «el técnico ve los precios» y entrar con su usuario):

- [ ] **No ve ningún precio**: ni el de los repuestos, ni el de la mano de obra, ni totales.
- [ ] Sí puede agregar pasos y repuestos, y marcar lo que terminó.
- [ ] **No puede** mandarle la cotización al cliente.
- [ ] Volver a encender la opción: los precios vuelven.

> Esto se controla en el servidor: lo que no se manda no se puede mirar. Vale la pena mirarlo con
> el usuario del técnico de verdad, no solo cambiando el ajuste.

---

## 9. Catálogos en el teléfono

**App → Más.**

- [ ] **Mano de obra**: crear un servicio con su precio, corregirlo, darlo de baja.
- [ ] **Trabajos frecuentes**: ver la lista, abrir uno y ver sus pasos y repuestos.
- [ ] Desde el detalle de una orden, **guardarla como trabajo frecuente**.
- [ ] Lo que se crea en el teléfono sale en el panel, y al revés.

---

## 10. Repuestos de casa de repuestos, ISV y ganancia

- [ ] Agregar un repuesto a mano y marcarlo **«es de una casa de repuestos»**, con el nombre de
      la casa.
- [ ] En la **cotización** siempre aparece, con la observación de la casa.
- [ ] Al **facturar** pregunta si se incluyen. Si sí, entran como venta.
- [ ] Con ISV configurado, la cotización y la factura muestran **precio unitario sin ISV** y al
      pie **Neto / ISV / Total**. El total no cambia por la tasa: solo cambia el desglose.
- [ ] Con ISV en cero, no aparece el desglose.
- [ ] **Ganancia estimada** en el detalle de la orden: precio de venta menos costo.

---

## 11. El diseño

Esto no cambió ninguna función, así que lo que hay que confirmar es que **nada se rompió**.

- [ ] Al abrir cualquier lista se ve el **hueco de las filas** en vez de «Cargando…» o una
      ruedita, y al llegar los datos nada salta de sitio.
- [ ] Una lista vacía dice qué va a aparecer ahí, no se queda en blanco.
- [ ] Un error trae **«Reintentar»**, y al tocarlo vuelve a cargar.
- [ ] **Poner el teléfono en modo avión** o apagar el wifi: el panel saca la franja de sin
      conexión arriba, y el mensaje de error está en español, no «Network Error».
- [ ] Las tablas del panel no se desbordan, y el botón de la última columna no se sale de la fila
      (mirar Recordatorios y Existencias).
- [ ] En el panel, **Escape** cierra el panel de avisos, el cajón de existencias, la foto en
      grande y el menú del teléfono.
- [ ] Una sola acción azul y llena por pantalla; lo demás apagado, y el rojo solo donde se borra.
- [ ] Con el teléfono al sol: las etiquetas de estado —«Esperando repuestos», «Listo»— se leen.
- [ ] En **modo oscuro** todo sigue legible, en el panel y en la app.

---

## 12. Lo de siempre, que no se rompió

Un repaso corto por lo que el cliente usa todos los días:

- [ ] Recibir un vehículo, abrir la orden, agregar pasos y repuestos.
- [ ] Cotizar, mandar el enlace, aprobar desde el enlace del cliente.
- [ ] Pasar la orden por sus estados hasta **Lista** y facturar.
- [ ] Abonos y estado de cuenta.
- [ ] Cierre de caja.
- [ ] Reportes.
- [ ] Recordatorios de servicio y el mensaje de WhatsApp.
- [ ] Avisos (la campana) y notificaciones push.

---

## Cuando todo pase

1. Subir la versión en `mobile/pubspec.yaml`. Va como **1.2.0** —son funciones nuevas, no
   correcciones— con el `+` que toque: el código de versión de Play **nunca** se puede repetir.
2. `flutter clean` antes de compilar: cambió `pubspec.yaml`.
3. Armar los dos paquetes y subirlos como siempre: [app-store.md](app-store.md) y
   [play-store.md](play-store.md).

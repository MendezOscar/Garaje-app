# Guion del video del asistente

Un caso armado para que se vean las tres cosas en una sola toma: el repuesto que el taller
**tiene**, el que **tiene en catálogo pero no en bodega**, y el que **no tiene** y el asistente
pide que se compre.

El modelo no contesta idéntico dos veces. Lo de abajo es lo que sale **en lo esencial**; haga
una pasada de ensayo antes de grabar —gasta 1 crédito— y grabe la segunda.

---

## 1 · Antes de grabar

### Que esté encendido

- **Ajustes → Taller**: el asistente tiene su propio interruptor (`AssistantEnabled`), aparte
  del escáner.
- Mire que queden créditos del día. Una propuesta de pasos cuesta **1 crédito**, una consulta
  del técnico **5**, y la lectura del negocio **no gasta**.

### El catálogo que ya tiene Taller Demo

Estos ocho ya están cargados, con **12 de existencia** cada uno en las dos sucursales:

| Código | Repuesto | Unidad | Existencia |
|---|---|---|---|
| `ACE-15W40` | Aceite motor 15W40 mineral | lt | 12 |
| `FIL-ACE-01` | Filtro de aceite | u | 12 |
| `FIL-AIR-01` | Filtro de aire | u | 12 |
| `PAS-FRE-DEL` | Pastillas de freno delanteras | jgo | 12 |
| `BUJ-NGK-01` | Bujía NGK estándar | u | 12 |
| `LIQ-FRE-DOT4` | Líquido de frenos DOT4 | lt | 12 |
| `CAD-MOT-125` | Kit cadena y piñones 125cc | jgo | 12 |
| `BAT-12V-60` | Batería 12V 60Ah | u | 2 |

### Lo único que hay que agregar: un repuesto sin existencia

En **Repuestos → Nuevo**, cargue este y **no le dé entrada de inventario**:

| Campo | Valor |
|---|---|
| Código | `DIS-FRE-DEL` |
| Nombre | Disco de freno delantero |
| Marca | Bosch |
| Categoría | Frenos |
| Unidad | u |
| Costo | 980 |
| Precio de venta | 1,640 |

Queda en catálogo con **0 en bodega**. Ese es el que hace la toma: el asistente lo va a
proponer igual, porque **él no sabe de existencias** —no se le mandan—, y es el sistema el que
le pone al lado que no hay ninguno.

### Lo que a propósito NO se carga

No agregue nada de esto. Son los que tienen que salir en **«Esto haría falta y no está en su
catálogo»**:

- Empaque de tapa de válvulas
- Arandela del tapón del cárter

### La orden

- Cliente y vehículo: **Chevrolet Sail 2019, placa PBH1234** (ya está en el demo).
- Kilometraje de entrada: **87,600**.
- Déjela en estado de diagnóstico, sin pasos cargados.

---

## 2 · El diagnóstico que se teclea

Cópielo tal cual. Está escrito como lo escribe un técnico, y lleva cinco trabajos distintos a
propósito:

> Entró por ruido al frenar. Pastillas de adelante al metal, los discos rayados, el derecho
> con labio. Pedal bajo y el líquido de frenos negro. Bota aceite por la tapa de válvulas,
> moja el múltiple. El aceite trae 9,000 km desde el último cambio y el filtro de aire está
> tapado. El cliente dice que jalonea en frío cuando arranca.

---

## 3 · Lo que debe salir

### Pasos

| # | Paso | Mano de obra | Horas |
|---|---|---|---|
| 1 | Cambiar pastillas delanteras y rectificar discos | `MO-FRE` | 2 |
| 2 | Cambiar el líquido de frenos y purgar | `MO-FRE` o sin servicio | 1 |
| 3 | Cambiar empaque de tapa de válvulas | sin servicio | 2 |
| 4 | Cambio de aceite y filtro | `MO-ACE` | 0.5 |
| 5 | Cambiar filtro de aire | `MO-ACE` o sin servicio | 0.3 |
| 6 | Cambiar bujías | `MO-ABC` o sin servicio | 0.5 |

Un paso sin servicio de mano de obra **no es un error**: vale más eso que colgarlo del
servicio equivocado. Si Taller Demo ya entregó órdenes con `MO-FRE` o `MO-ACE`, cada paso trae
además **lo que el taller ha cobrado antes** por ese mismo trabajo. Ese número lo calcula el
servidor con las órdenes del taller, no el modelo — vale la pena decirlo en el video.

### Repuestos

| Código | Repuesto | Cantidad | Existencia |
|---|---|---|---|
| `PAS-FRE-DEL` | Pastillas de freno delanteras | 1 jgo | 12 |
| `DIS-FRE-DEL` | Disco de freno delantero | 2 u | **0** |
| `LIQ-FRE-DOT4` | Líquido de frenos DOT4 | 1 lt | 12 |
| `ACE-15W40` | Aceite motor 15W40 | 4 lt | 12 |
| `FIL-ACE-01` | Filtro de aceite | 1 u | 12 |
| `FIL-AIR-01` | Filtro de aire | 1 u | 12 |
| `BUJ-NGK-01` | Bujía NGK estándar | 4 u | 12 |

Las cantidades son el detalle que más convence: los discos van **de a dos** y las bujías **de a
cuatro** porque el motor tiene cuatro cilindros. Nadie se lo dijo.

### Lo que haría falta y no está en el catálogo

- Empaque de tapa de válvulas — 1
- Arandela del tapón del cárter — 1

Con el nombre con el que se piden en una casa de repuestos, que es para lo único que sirven.

---

## 4 · La toma, en orden

1. **La orden con el diagnóstico escrito.** Se ve que no hay pasos cargados.
2. **Proponer los pasos.** Tarda unos segundos; no lo corte en la edición, que el que mira
   entienda que es una consulta de verdad y no una tabla guardada.
3. **La propuesta.** Recorra los pasos primero y los repuestos después.
4. **Pare en el disco con 0.** Es el momento del video: *está en mi catálogo, pero no tengo
   ninguno; esto hay que comprarlo antes de prometerle el carro al cliente.*
5. **Pare en los faltantes.** *Y esto ni siquiera lo tengo cargado — aquí me lo deja escrito
   como se pide.*
6. **Tache un paso.** Quite el de las bujías. Se ve que **el repuesto se va con el paso**: las
   cuatro bujías desaparecen solas de la lista.
7. **Corrija algo a mano.** Súbale las horas al empaque de tapa de válvulas de 2 a 3. Es lo que
   demuestra que lo que se guarda es lo que la persona revisó, no lo que el modelo dijo.
8. **Aceptar.** Los pasos entran en la orden.
9. **Y lo que sigue a Aceptar, que es lo importante:** los repuestos quedan como
   **sugerencia**, no cargados. La bodega no se descontó, porque el trabajo todavía no se ha
   hecho. El que los carga es el técnico cuando de verdad los usa.

---

## 5 · Las otras dos tomas

### Preguntar, con el vehículo en la rampa

En la misma orden, el técnico pregunta y sigue preguntando sobre la misma conversación. Una
buena para este caso:

> ¿Con qué torque van los pernos de la mordaza delantera del Sail 1.4?

El asistente **no va a inventar el número**: va a decir que esa especificación se consulta en
el manual. Eso **muéstrelo**, no lo salte. Un asistente de taller que inventa un torque es
peor que no tener ninguno, y es la toma que más confianza da.

Después una que sí conteste, para no dejarlo en el no:

> ¿Por dónde empiezo a buscar si jalonea solo en frío y se compone caliente?

### La lectura del negocio

**Panel → Lectura del negocio.** Esta no la pide el taller: le llega. Enséñela ya generada y
abra el **PDF**. No gasta créditos — si lo va a decir en el video, dígalo así: *esta se la
mandamos nosotros, no se le descuenta nada.*

---

## 6 · Lo que conviene decir, y lo que no

**Decir:**

- Los repuestos salen **del catálogo del taller y de ninguna otra parte**. Lo que no está, no
  se inventa: se pide.
- **Nada se guarda hasta que el dueño acepta.** Proponer no escribe en la orden.
- **No pone precios.** Ni de repuestos ni de mano de obra: esos son del taller.
- Al asistente **no se le manda el cliente**: ni nombre, ni teléfono, ni placa.

**No decir:**

- Nada de «inteligencia artificial que diagnostica». No diagnostica: **ordena en pasos
  cobrables el diagnóstico que escribió el técnico**, y pone los repuestos que eso lleva.
- Nada del modelo ni del proveedor. Al taller no le sirve y envejece mal.

# Probar el armado de motos antes de soltarlo

Guion para validar el módulo completo, del catálogo al corte. Lo que se construyó y por qué
está en los commits; esto es lo que hay que hacer, en qué orden, y qué tiene que pasar en
cada paso.

Hacen falta **tres personas o tres sesiones**: el Dueño en el panel, un Técnico en un
teléfono, y un segundo Técnico para una sola comprobación —la de que no se ven lo del otro—.

> **El orden importa.** El catálogo primero: sin contratista, modelos con precio y bodega, la
> pantalla del técnico no deja registrar nada, y es correcto que así sea.

---

## Antes de empezar

| | |
| --- | --- |
| **Taller** | El del contrato de armado. No lo pruebe en Taller Demo: aquí se registran series reales |
| **En el panel de plataforma** | **Armado de motos** encendido, en el bloque «Por contrato» |
| **Versiones** | Panel al día solo; la app tiene que ser **1.9.0 (21)** o mayor en cada teléfono |
| **Usuarios** | El Dueño, y al menos dos Técnicos con sesión propia |
| **A mano** | Tres o cuatro motos de verdad, o las fotos de sus chasis con la serie legible |

Y la advertencia que se olvida siempre: **el interruptor viaja en la sesión**. Quien tenga el
panel o la app abiertos desde antes de encenderlo tiene que salir y volver a entrar.

---

## 1 · El contrato (Dueño, en el panel)

**Ajustes → Armado de motos.**

### 1.1 · Cargar el catálogo

Pestaña **Modelos**: agregue los que el taller de verdad arma, escritos como los nombra el
contratista. Pestaña **Bodegas**: los lugares a los que va el técnico. Pestaña
**Contratistas**: el contratista, y después **Precios**.

En la tabla de precios, por cada modelo: lo que se le cobra y lo que se le paga al técnico.
La tercera columna, **Queda**, se calcula sola.

> Si un modelo no deja algo razonable en esa columna, ese es el dato que el taller nunca tuvo
> en el Excel. Vale la pena pararse ahí un minuto con el dueño.

### 1.2 · Los dos errores que el servidor tiene que atajar

- **Poner el pago más alto que el cobro.** Tiene que **rechazarlo** diciendo que revise si los
  números están cambiados de columna. Es el error de tecleo que de verdad pasa al llenar
  cuarenta modelos de un tirón.
- **Llenar un solo número y guardar.** Esa fila **no se guarda**: queda como modelo todavía no
  negociado. Medio llenar no es una tarifa, y guardarla en cero haría que se armara gratis.

### 1.3 · Guardar dos veces no duplica

Guarde los precios, vuelva a abrir, cambie uno y guarde otra vez. Tienen que quedar **las
mismas filas**, con el precio nuevo. Si aparecen filas repetidas, pare.

---

## 2 · El registro en la bodega (Técnico, en el teléfono)

Entre como técnico. En **Mi trabajo** tiene que estar abajo el botón **«Armé una moto»**.

### 2.1 · El camino bueno

Foto de la serie, teclee la serie, elija modelo y bodega, **Registrar la moto**.

Tiene que: guardarse sin salir de la pantalla, limpiar la serie y la foto, **dejar puestos el
modelo y la bodega** —son los mismos toda la tarde— y aparecer abajo en «Registradas
ahorita» con lo que le toca a él.

### 2.2 · El modelo se propone solo

Registre una segunda moto **del mismo lote**: los primeros ocho caracteres de la serie
iguales, el final distinto.

Al teclear, tiene que salir *«Series como esta han sido FT150 una vez. Confírmelo con la
moto»*, y el modelo quedar elegido. **Cámbielo a otro a propósito**: tiene que dejarse
cambiar. Propone, no impone.

Con una serie de un lote que nunca se ha registrado, **no propone nada**. Que se calle cuando
no sabe es parte de la prueba.

### 2.3 · La serie repetida

Intente registrar una serie **que ya está**, y después la misma **tecleada con guiones y
espacios**. Las dos tienen que rebotar con «ya está registrada».

Es la regla que el Excel nunca pudo imponer, y la razón principal de todo esto: una serie
repetida es una moto cobrada dos veces.

### 2.4 · El modelo sin precio

Pídale al dueño que agregue un modelo nuevo **sin ponerle precio**. Intente registrarlo.

Tiene que decir que ese modelo no tiene precio con ese contratista y que se lo pida al dueño.
**No se registra.** Si entrara en cero, esa moto se armaría gratis y nadie lo notaría hasta
cuadrar el mes.

### 2.5 · Que no se pierda lo tecleado

Ponga el teléfono en **modo avión** con la serie ya escrita y toque Registrar.

Tiene que salir el error **y quedarse todo en pantalla**: la serie, el modelo, la bodega y la
foto. Quite el modo avión y reintente; tiene que entrar.

> Esta es la que decide si la función sobrevive la primera semana. Perder una serie ya escrita
> es lo que manda a cualquiera de vuelta al grupo de WhatsApp esa misma tarde.

### 2.6 · La foto

Registre una **sin foto**: el botón tiene que decir «Registrar sin foto», a la vista, y dejar
guardar igual. Con foto, la foto sube sola; si no hay señal, sube después.

---

## 3 · Lo que el técnico no puede ver

Es la comprobación corta y la que más importa de todas.

- En **Mis motos armadas** sale cuántas lleva y **lo que le corresponde a él**. En ninguna
  parte sale lo que el taller le cobra al contratista.
- Entre con el **segundo técnico**: tiene que ver solo las suyas.
- El técnico **no tiene** el menú de Ajustes → Armado de motos ni el de Dinero → Motos
  armadas.

> No está escondido en la pantalla: ese número no sale del servidor cuando pregunta un
> técnico. Si alguna vez lo ve, pare y avise.

---

## 4 · El mes del dueño (panel)

**Dinero → Motos armadas.**

### 4.1 · Los cuatro números

Con el periodo del mes puesto, arriba salen: cuántas motos, lo que se cobra, lo que se paga y
**lo que queda para el taller**.

Compruebe a mano que el tercero más el cuarto dan el segundo. Y que el total es el del
**filtro entero**, no el de lo que se ve en pantalla: ponga el filtro en un técnico solo y
mire que los cuatro cambian.

### 4.2 · Los filtros

Pruebe por técnico, por bodega, por modelo y **por parte de la serie** —los últimos dígitos,
como se copian del chasis—. La lista y los cuatro números de arriba tienen que moverse juntos.

### 4.3 · Corregir y borrar

Borre una moto registrada por error. Corrija otra: cámbiele el modelo y compruebe que **los
importes se recalculan** con la tarifa de ese modelo.

---

## 5 · Los dos reportes

### 5.1 · El de facturación

Elija el contratista, el periodo, y **Bajar en Excel**.

Las columnas tienen que ser **fecha, serie, modelo, técnico, bodega, cobro** —las mismas de
la hoja que el contratista ya recibe hoy—. Abra el archivo y compruebe dos cosas:

- **La serie es texto**, no número. Una serie que empieza con ceros los pierde si Excel la lee
  como número, y una serie sin sus ceros no es la serie.
- El total del pie **cuadra con el número de la pantalla**.

Baje también el PDF y compárelos: tienen que decir lo mismo.

> **Sin elegir contratista no deja bajar el de facturación.** Es a propósito: mezclar dos
> contratos en una hoja es el error que nadie detecta hasta que se la rechazan.

### 5.2 · El de pago a técnicos

Pestaña **Pago a técnicos**: una línea por técnico con cuántas y cuánto. Baje el PDF.

Tiene que traer **dos líneas de firma** al pie —Entregó y Recibió— y **ninguna columna con lo
que el taller cobra**. Esa hoja termina en manos del técnico.

---

## 6 · El corte, que es el corazón de esto

### 6.1 · Cerrar la facturación

Con el periodo y el contratista puestos, **Cerrar la facturación del periodo**. Le va a pedir
el número de factura —puede dejarlo vacío— y avisarle de que esas motos quedan congeladas.

Después del cierre:

- Esas motos salen marcadas **«facturada»** en la lista.
- Con el filtro en **«Solo lo que falta facturar»**, la lista queda **vacía**.
- **Vuelva a cerrar el mismo periodo**: tiene que decir que no hay nada pendiente. Esto es lo
  que impide cobrar dos veces el mismo mes.

### 6.2 · Lo congelado no se toca

Intente **corregir** una moto facturada, y después **borrarla**.

Las dos tienen que rebotar diciendo que reabra el corte. Del otro lado hay una factura
emitida; corregir la fila después sería cambiar el pasado.

### 6.3 · Reabrir

Pestaña **Cortes** → **Reabrir**. El aviso tiene que advertirle de que del otro lado hay una
factura o un pago ya entregado.

Al reabrir, esas motos **vuelven a estar pendientes** y se pueden corregir. Corrija la que
quería corregir y vuelva a cerrar.

### 6.4 · Facturar no es pagar

Con el periodo ya facturado, vaya a **Pago a técnicos** y cierre el pago de un técnico.

Antes de cerrarlo, esas motos estaban **facturadas pero no pagadas** —compruébelo con el
filtro «Solo lo que falta pagar»—. Después del cierre, salen con los dos sellos. Son dos
cortes distintos sobre las mismas motos.

---

## 7 · El Excel de antes

Pestaña **Traer el Excel**. Elija el contratista arriba y suba el archivo con las columnas
**fecha, serie, modelo, técnico, cobro, bodega**.

**Primero «Ver qué entraría».** Tiene que salir la lista fila por fila con el motivo de cada
rechazo. Pruebe a propósito con una hoja que traiga:

- Un modelo que no está en el catálogo → se rechaza con el nombre escrito.
- Una bodega que no está → igual.
- Un técnico que no es usuario del taller → igual.
- Una serie que ya está registrada → se rechaza.
- La misma serie dos veces en la hoja → la segunda se rechaza.

> **No crea catálogo a propósito.** Si lo creara, una hoja con «FT150», «Ft 150» y «ft-150»
> dejaría tres modelos y el precio colgando de uno solo.

Cuando la vista previa esté limpia, **Guardar**. Después compruebe en la lista que esas motos
entraron con su fecha vieja y su cobro de la hoja.

---

## Lo que este módulo no hace

Dígalo antes de que lo pregunten, que es mejor que descubrirlo:

- **No lee la serie de la foto.** El técnico la teclea. Es lo siguiente que se puede evaluar,
  con el costo medido; hoy no va.
- **No saca el modelo del número de serie** por sí solo. Lo aprende de lo que este taller
  registra, y por eso la primera moto de cada lote se teclea completa.
- **No factura.** Saca el reporte con el que se factura; la factura la emite el taller donde
  siempre.
- **No hay modo sin conexión.** El registro necesita señal. Lo que aguanta sin ella es la
  foto, que sube después.

---

## Qué hacer si algo falla

| Lo que se ve | Qué es |
| --- | --- |
| No sale el menú ni el botón | La sesión es vieja: salir y volver a entrar. O falta encender el módulo |
| «Falta el contrato» en el teléfono | No hay contratista, modelos o bodegas cargados |
| «Ese modelo no tiene precio con ese contratista» | Funcionó: falta ponerle precio en la ficha del contratista |
| «La serie ya está registrada» | Funcionó. Compruebe que de verdad sea la misma moto |
| Un técnico ve lo que el taller cobra | **Pare y avise.** Es lo único de esta lista que no puede esperar |
| El total del reporte no cuadra con la pantalla | **Pare.** Un reporte que no cuadra con la pantalla de la que salió es peor que no tener reporte |
| Una serie sale sin sus ceros en el Excel | **Pare.** Es la celda leída como número |

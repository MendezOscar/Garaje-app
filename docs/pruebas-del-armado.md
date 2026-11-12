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
| **Versiones** | Panel al día solo. La app tiene que ser la **1.9.0 (21)**, que **todavía no está en las tiendas**: hay que instalarla por cable en cada teléfono que vaya a probar |
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

Dos cosas más de esta pantalla, que son las que hacen que registrar sean tres toques:

- **El inicio del VIN**, en cada modelo: los primeros ocho caracteres de la serie de ese
  modelo, con una caja delante. De las cajas de este taller salen `LLCLPLPC` para la 200Z,
  `LLCLPP20` para la FT125, `LRPRPMV0` para la 250Z y `LZSJDPL0` para la DM300.
- **Lo lleva**, en cada contratista: la sucursal del taller que tiene ese contrato. Solo
  aparece si el taller tiene más de una. Con una sola queda puesta sola.

> **No acorte el inicio del VIN.** Dos de esos modelos comparten los cinco primeros
> caracteres: con cinco, la app propondría el modelo equivocado. Si alguna vez dos modelos
> coinciden en los ocho, alárguelos —el campo aguanta diez— y la app se queda siempre con el
> más largo que calce.

### 1.2 · Los dos errores que el servidor tiene que atajar

- **Poner el pago más alto que el cobro.** Tiene que **rechazarlo** diciendo que revise si los
  números están cambiados de columna. Es el error de tecleo que de verdad pasa al llenar
  cuarenta modelos de un tirón.
- **Llenar un solo número y guardar.** Esa fila **no se guarda**: queda como modelo todavía no
  negociado. Medio llenar no es una tarifa, y guardarla en cero haría que se armara gratis.

### 1.3 · Guardar dos veces no duplica

Guarde los precios, vuelva a abrir, cambie uno y guarde otra vez. Tienen que quedar **las
mismas filas**, con el precio nuevo. Si aparecen filas repetidas, pare.

### 1.4 · El gasto por moto

Arriba de las pestañas, **El gasto del taller por cada moto**: lo que armar una se lleva
aparte del pago al técnico —transporte a la bodega, herramienta—. Un número para todas.

Ponga uno, guarde, y **recargue la página**: tiene que seguir ahí. Déjelo puesto antes de
registrar nada, porque lo que importa de este número se prueba en el paso 4.3.

---

## 2 · El registro en la bodega (Técnico, en el teléfono)

Entre como técnico. En **Mi trabajo** tiene que estar abajo el botón **«Armé una moto»**.

### 2.1 · El camino bueno

**Escanear la etiqueta de la caja** → la serie cae sola → el modelo sale elegido → **Registrar
la moto**.

Tiene que: guardarse sin salir de la pantalla, limpiar la serie y la foto, **dejar puestos el
contratista, el modelo y la bodega** —son los mismos toda la tarde— y aparecer abajo en
«Registradas ahorita» con lo que le toca a él.

> Fíjese en lo que **no** pasa: no le pregunta en qué sucursal del taller se armó. Esa sale
> del contrato. Al técnico asignado a dos sucursales esa pregunta no tenía respuesta: él está
> parado en la bodega del contratista, que no es una sucursal del taller.

### 2.2 · El lector de la etiqueta

Una caja trae cuatro códigos: el del envío, el del SKU, el del motor y el del chasis. El
lector tiene que **quedarse con el del chasis** y no decir nada de los otros tres.

- **Apunte al código del motor** —`ZS175YMN`, `LC163FML279200Q6`—: no debe tomarlo. Son
  dieciséis caracteres o llevan Q, y una serie de chasis no lleva I, O ni Q.
- **Ponga dos cajas pegadas** dentro del recuadro, como están en la bodega. El borde tiene
  que ponerse **ámbar** y **no escoger ninguna**, con el aviso de acercarse.
- **Fuera del recuadro no lee.** Apunte a una etiqueta dejándola fuera del marco: no debe
  pasar nada.
- **Leer de una foto**: con una foto de caja en la galería del teléfono, tiene que sacar la
  misma serie. Es el camino del dueño, que recibe las cajas por WhatsApp.
- **Mejor la tecleo**: cierra el lector y deja escribir a mano. Es la salida cuando la
  etiqueta viene rota.

> Lo de las dos cajas es la comprobación cara de esta lista. Agarrar la etiqueta de la caja de
> al lado **no da error**: queda registrada una moto que no se armó y nadie se entera hasta
> que el contratista reclame.

### 2.3 · El modelo se propone solo, de dos maneras

**Con el inicio del VIN puesto** (paso 1.1), escanee una caja de ese modelo: el modelo tiene
que salir elegido **desde la primera moto**, con el aviso *«Por el inicio de la serie, es una
FT125. Confírmelo con la moto»*.

**Sin el inicio puesto**, registre una segunda moto del mismo lote —los primeros ocho
caracteres iguales, el final distinto—: el aviso cambia a *«Series como esta han sido FT150
una vez»*. Son dos cosas distintas y conviene que se note cuál está mirando.

**Cámbielo a otro a propósito**: tiene que dejarse cambiar. Propone, no impone.

Con una serie de un prefijo que nadie declaró y que nunca se ha registrado, **no propone
nada**. Que se calle cuando no sabe es parte de la prueba.

### 2.4 · La serie repetida

Intente registrar una serie **que ya está**, y después la misma **tecleada con guiones y
espacios**. Las dos tienen que rebotar con «ya está registrada».

Es la regla que el Excel nunca pudo imponer, y la razón principal de todo esto: una serie
repetida es una moto cobrada dos veces.

### 2.5 · El modelo sin precio

Pídale al dueño que agregue un modelo nuevo **sin ponerle precio**. Intente registrarlo.

Tiene que decir que ese modelo no tiene precio con ese contratista y que se lo pida al dueño.
**No se registra.** Si entrara en cero, esa moto se armaría gratis y nadie lo notaría hasta
cuadrar el mes.

### 2.6 · Que no se pierda lo tecleado

Ponga el teléfono en **modo avión** con la serie ya escrita y toque Registrar.

Tiene que salir el error **y quedarse todo en pantalla**: la serie, el modelo, la bodega y la
foto. Quite el modo avión y reintente; tiene que entrar.

> Esta es la que decide si la función sobrevive la primera semana. Perder una serie ya escrita
> es lo que manda a cualquiera de vuelta al grupo de WhatsApp esa misma tarde.

### 2.7 · La foto, que ahora es opcional y callada

Abajo del todo hay un renglón, **Agregar una foto de la etiqueta**. El botón de guardar dice
siempre **«Registrar la moto»**: ya no reprocha que falte.

Registre una **sin foto**: tiene que dejar. Registre otra **con foto**: sube sola, y si no hay
señal sube después.

> La foto dejaba de hacer falta para la serie en cuanto entró el lector, pero sigue siendo lo
> único que acredita que alguien estuvo frente a esa caja el día que el contratista discuta
> una factura. Por eso queda, sin pedirla.

### 2.8 · El dueño registrando lo que armó otro

Entre como **Dueño** en el teléfono y abra **Armé una moto**. Tiene que aparecer un campo
**La armó** con los técnicos del taller.

- **El dueño no sale en esa lista.** Él no arma motos.
- **Sin elegir a nadie el botón no se habilita.** Si se registrara en silencio, la moto se le
  apuntaría a quien la tecleó y el pago del que sí la armó desaparecería del reporte del mes.
- La que registre así tiene que salir **a nombre del técnico que eligió**, no del dueño.

Y al revés: desde el teléfono del **técnico** ese campo **no aparece**. Él solo registra lo
suyo, y lo que mande el teléfono se ignora.

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

### 4.1 · Los números de arriba

Con el periodo del mes puesto, arriba salen: cuántas motos, lo que se cobra, lo que se paga,
**lo que le cuesta al taller armarlas** y **lo que queda**.

Compruebe a mano que **cobro − pago − gasto = queda**. Y que son los del **filtro entero**, no
los de lo que se ve en pantalla: ponga el filtro en un técnico solo y mire que todos cambian.

En la tabla de **El detalle**, cada moto trae sus cuatro columnas de plata y al pie va la fila
de totales. Cuando el periodo trae más motos de las que caben en la página, el pie tiene que
decirlo —«200 motos (en pantalla, 25)»— y **sumar las 200**. Un pie que sumara solo lo visible
es justo el número que alguien copia a una factura sin darse cuenta.

### 4.2 · Los filtros

Pruebe por técnico, por bodega, por modelo y **por parte de la serie** —los últimos dígitos,
como se copian del chasis—. La lista y los cuatro números de arriba tienen que moverse juntos.

### 4.3 · El gasto se congela en cada moto

Con motos ya registradas, vuelva a **Ajustes → Armado de motos** y **cambie el gasto por
moto**. Vuelva a la lista.

Las motos de antes tienen que seguir con **el gasto que tenían**, y solo las que registre de
ahora en adelante llevar el nuevo. Es lo mismo que pasa con el cobro y el pago: subir la
tarifa en marzo no puede cambiar el margen de un mes que ya se facturó.

### 4.4 · Corregir y borrar

Borre una moto registrada por error. Corrija otra: cámbiele el modelo y compruebe que **los
importes se recalculan** con la tarifa de ese modelo.

---

## 5 · Los tres reportes

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

### 5.3 · El del taller

En **El detalle**, abajo de los botones de siempre, hay una fila aparte: *«Para usted, con el
gasto y lo que queda»*, con **Excel** y **PDF**.

Esa hoja trae las nueve columnas —hasta **gasto** y **queda**— con sus totales. Ábrala al lado
de la de facturación y compruebe lo que importa: **en la del contratista el gasto no aparece
por ningún lado**.

> No es que esté escondido en el documento: cuando el reporte es el de facturación, ese número
> no sale del servidor. La hoja del contratista le enseñaría cuánto gana el taller con él.

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

### 6.2 · La hoja del corte, que es la que se manda

En la pestaña **Cortes**, cada corte trae **Excel** y **PDF** propios. Bájelos: tienen que
traer **las motos de ese corte** y el **número de factura impreso** en el encabezado.

Ahora la prueba que vale: **registre otra moto del mismo periodo** después de haber cerrado, y
vuelva a bajar la hoja del corte.

- La hoja del corte **no la trae**. Sigue diciendo lo mismo que el día que se mandó.
- El filtro de arriba **sí la trae**, porque el filtro consulta lo de hoy.
- **Cierre otra vez el mismo periodo**: el segundo corte se lleva **solo esa**, con su propio
  número de factura, sin tocar el primero.

> Esta es la diferencia entre un papel que respalda una factura y uno que no. Si la hoja
> saliera del filtro, bastaría con que entrara una moto tarde para que lo que se vuelve a
> sacar en marzo no dijera lo mismo que lo que se mandó en enero.

### 6.3 · Por cobrar: facturado no es cobrado

Arriba de la lista de cortes tiene que salir **cuánto está facturado y todavía sin cobrar**, y
en cuántas facturas. El corte que acaba de cerrar tiene que estar ahí, marcado **«por cobrar»**
con los **días que lleva esperando**.

- Toque **Ya pagó**, escriba con qué pagaron —o déjelo vacío— y compruebe que pasa a
  **«cobrada»** con su fecha y **sale del total de arriba**.
- **Quitar** la devuelve a por cobrar. Es para el cheque que rebota.
- Un corte de **pago a técnicos** no tiene esa columna: dice «pagado al cerrar». Ahí cerrar ya
  es entregar la plata.

### 6.4 · Lo congelado no se toca

Intente **corregir** una moto facturada, y después **borrarla**.

Las dos tienen que rebotar diciendo que reabra el corte. Del otro lado hay una factura
emitida; corregir la fila después sería cambiar el pasado.

### 6.5 · Reabrir

Primero intente reabrir **una factura que ya marcó como cobrada**: tiene que **negarse** y
decirle que le quite el cobro antes. Del otro lado entró plata contra ese papel.

Quítele el cobro y entonces sí: **Cortes → Reabrir**. El aviso tiene que advertirle de que del
otro lado hay una factura o un pago ya entregado.

Al reabrir, esas motos **vuelven a estar pendientes** y se pueden corregir. El corte
**desaparece de la lista con su número de factura**: si lo vuelve a cerrar, hay que escribirlo
de nuevo. Corrija la que quería corregir y vuelva a cerrar.

### 6.6 · Facturar no es pagar

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

## 8 · El armado desde el teléfono (Dueño)

Lo mismo del panel, para el dueño que anda en la bodega y no frente a la computadora. Entra
por **Más → Armado de motos**, debajo de lo del técnico.

### 8.1 · Motos del taller

La lista con los dos precios. Arriba van las cuatro cifras del **filtro entero** —se cobra,
se paga, gasto y **queda**—, igual que en el panel: compruebe que cuadran con las de allá
poniendo el mismo periodo.

- Los chips de arriba: el periodo, **sin facturar**, **sin pagar**, el contratista y el
  técnico. Teclee parte de una serie en el buscador y compruebe que filtra.
- Toque los tres puntos de una moto → **Corregir**: la serie, el modelo, el contratista, la
  bodega, el técnico, el día, la nota, y los dos importes. Guarde y compruebe en el panel que
  el cambio está.
- Una moto **ya facturada** no deja corregirse ni borrarse: primero se reabre el corte.
- **Borrar** solo le sale al Dueño. Entre como Encargado y compruebe que no está.

### 8.2 · Cortes y facturas

- **Cerrar factura**: elija contratista y periodo, escriba el número de factura y ciérrela.
  Compruebe en el panel que el corte está, con las mismas motos.
- En un corte cerrado: **Anotar que pagó** (día y recibo), **Quitar el cobro**, **Reabrir** y
  **Mandar la hoja**, que baja el PDF del corte y lo pasa a WhatsApp. Esa hoja sale del corte,
  no de un filtro: tiene que traer exactamente las motos que se congelaron.
- Arriba va lo que suman las facturas **por cobrar**. Anote un cobro y compruebe que baja.
- Cerrar y reabrir son del Dueño. Al Encargado no le sale el botón, y la API se lo niega.

### 8.3 · Pago a técnicos

El periodo arriba, y una fila por técnico con sus motos y lo que se le debe. **Solo cuenta lo
que no entró todavía en un corte de pago.**

- **Cerrar pago**: el monto viene de sus motos y **se puede cambiar** —un bono, un descuento
  acordado, la quincena redondeada—. Lo que quede ahí es lo que entra en los **gastos del
  periodo**: compruébelo en Resultados.
- Después de cerrar, ese técnico deja de salir en el periodo. Reabra el corte desde Cortes y
  compruebe que vuelve.

### 8.4 · Catálogo del armado

Solo el Dueño. Tres pestañas —contratistas, modelos y bodegas— y el **gasto por moto** arriba
de la primera.

- Agregue un modelo con prefijo de VIN y compruebe que el teléfono lo propone al registrar.
- **Tarifas** de un contratista: una fila por modelo con cobro y pago. En blanco es que ese
  contratista no manda a armar ese modelo. Guarde y compruebe la tabla en el panel.
- Cambie una tarifa y compruebe que **las motos ya registradas no cambian**: el precio se
  copia al registrarlas.

---

## Lo que este módulo no hace

Dígalo antes de que lo pregunten, que es mejor que descubrirlo:

- **No lee texto de la foto.** Lee el **código de barras** de la etiqueta, que es otra cosa y
  no cuesta nada. Si la etiqueta viene rota, la serie se teclea.
- **No saca el modelo del número de serie** por sí solo: no hay decodificador público que
  valga para estas marcas. Lo sabe porque el dueño escribió el inicio del VIN de cada modelo, o
  porque lo aprendió de lo ya registrado.
- **No cobra.** Saca la hoja con la que se cobra y lleva la cuenta de qué facturas siguen
  pendientes; el cobro se anota a mano cuando entra la plata.
- **No factura.** Saca el reporte con el que se factura; la factura la emite el taller donde
  siempre.
- **No hay modo sin conexión.** El registro necesita señal. Lo que aguanta sin ella es la
  foto, que sube después.
- **El Excel de antes y los tres reportes en hoja se quedan en el panel.** Desde el teléfono
  sale la hoja de un corte en PDF, que es la que se manda al contratista; escoger un `.xlsx`
  del teléfono y revisar 300 filas en una pantalla de cinco pulgadas no es trabajo de app.

---

## Qué hacer si algo falla

| Lo que se ve | Qué es |
| --- | --- |
| No sale el menú ni el botón | La sesión es vieja: salir y volver a entrar. O falta encender el módulo |
| «Falta el contrato» en el teléfono | No hay contratista, modelos o bodegas cargados |
| «Ese modelo no tiene precio con ese contratista» | Funcionó: falta ponerle precio en la ficha del contratista |
| «La serie ya está registrada» | Funcionó. Compruebe que de verdad sea la misma moto |
| Un técnico ve lo que el taller cobra, o el gasto | **Pare y avise.** Es lo único de esta lista que no puede esperar |
| El lector escoge una serie con dos cajas en el recuadro | **Pare.** Tendría que ponerse ámbar y no escoger |
| El gasto aparece en la hoja de facturación | **Pare.** Esa hoja la recibe el contratista |
| La hoja de un corte trae una moto registrada después de cerrarlo | **Pare.** Esa hoja respalda una factura emitida |
| El total del reporte no cuadra con la pantalla | **Pare.** Un reporte que no cuadra con la pantalla de la que salió es peor que no tener reporte |
| Una serie sale sin sus ceros en el Excel | **Pare.** Es la celda leída como número |

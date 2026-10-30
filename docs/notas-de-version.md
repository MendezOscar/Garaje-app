# Notas de versión

Lo que se pega en las dos tiendas al subir una versión. En español, porque la ficha de
GarajApp está en español y Honduras es el único mercado.

Dos reglas que no son de estilo sino de la tienda:

- **Google Play tope las notas en 500 caracteres** por idioma. App Store deja 4.000.
- Se escribe para **el dueño de un taller**, no para un programador: lo que va a poder hacer
  el lunes, no cómo está hecho por dentro.

---

## 1.6.1 (17)

### Google Play — «Novedades» (es-419)

```
Impuesto, como lo cobra cada taller.

Si sus precios ya llevan el ISV adentro, facturar no cambia el total: la factura solo lo
desglosa. Si no lo llevan, la app se lo suma al facturar y le enseña los dos totales antes de
cobrar. Se elige una sola vez, en Taller.

Trabajos frecuentes: el repuesto que no está en el catálogo ya lleva su precio, y al armar la
orden se carga de un toque.

Y donde decía kilometraje ahora dice kilometraje o millaje.
```

### App Store — «Novedades de esta versión»

```
El ISV, como lo cobra su taller.

EL IMPUESTO
· Usted decide si los precios de su catálogo ya llevan el ISV adentro o si se lo suma al
  facturar. Se elige una sola vez, en Taller, y la app le enseña un ejemplo de cada caso.
· Con el impuesto adentro, facturar no cambia el total: la factura solo lo desglosa.
· Sin él, la app le suma el ISV al facturar y le enseña los dos totales —con factura y sin
  ella— antes de cobrar, en la venta rápida y al cerrar la orden.
· La cotización dice lo que de verdad va a pasar: «incluido, el total no cambia» o
  «agregado, sube el total».

TRABAJOS FRECUENTES
· El repuesto que no está en el catálogo ya se guarda con su precio, así que el trabajo dice
  lo que de verdad cuesta.
· Al armar la orden con ese trabajo, ese repuesto se carga de un toque. No toca la bodega,
  porque no salió de ella.

DETALLES
· Donde decía «kilometraje» ahora dice «kilometraje o millaje».
```

> El ajuste del impuesto lo cambia el dueño, y vale para el panel y para la app: el taller que
> no lo toque sigue como estaba, con el ISV dentro del precio.

## 1.6.0 (16)

### Google Play — «Novedades» (es-419)

```
Repuestos: ahora le puede dar entrada a un producto nuevo desde el teléfono —cuánto entró, a
qué costo y a qué bodega—, sin pasar por el panel.

GarajApp ya no es solo para vehículos: un taller de electrónica, computadoras, celulares o
aires acondicionados recibe el equipo por número de serie, y la app le habla de equipos en
todas las pantallas. Se activa al dar de alta el taller.

Además, la app vuelve a abrir en los iPhone con iOS 26.
```

### App Store — «Novedades de esta versión»

```
Repuestos y talleres de equipos.

REPUESTOS
· Le da entrada a un producto nuevo desde el teléfono: cuánto entró, a qué costo y a qué
  bodega. Antes había que ir al panel.
· Si el repuesto todavía no existe, se crea y se le da entrada seguido, sin salir de la
  pantalla.

TALLERES QUE NO REPARAN VEHÍCULOS
· GarajApp también sirve para electrónica, computadoras, celulares y aires acondicionados.
· El taller recibe el equipo con su número de serie o su etiqueta, en vez de placa y
  kilometraje, y la app le habla de equipos en todas las pantallas: recibir, cotizar,
  facturar y el enlace que ve el cliente.
· La hoja de recepción sale con lo del equipo, y es opcional: hay equipos que no la piden.
· Se elige al dar de alta el taller. Un taller mecánico no cambia en nada.

ARREGLOS
· La app vuelve a abrir en los iPhone con iOS 26.
· Las palabras del taller ahora le llegan también al encargado y al técnico, no solo al dueño.
```

> La ficha de las dos tiendas sigue diciendo «taller mecánico». Es a propósito mientras no se
> decida cómo se presenta GarajApp a los talleres de equipos (fase 6 de
> [equipos.md](equipos.md)): cambiar el nombre y la descripción manda la ficha a revisión otra
> vez, y conviene hacerlo una sola vez y con el texto ya pensado.

## 1.5.0 (15)

Misma versión y mismas notas que la 14: lo único que cambia es que el botón de confirmar una
cita ya no se sale del borde en un teléfono angosto. **La 14 no se usa.**

## 1.5.0 (14)

### Google Play — «Novedades» (es-419)

```
Citas.

· Sus clientes apartan el día desde la app, eligen cuál de sus vehículos traen y ven si usted
se la confirmó.
· Para los que no quieren instalar nada: un enlace que usted manda por WhatsApp o imprime en
un QR, y piden su cita ahí mismo.
· Nueva pantalla de Citas: confirmar, moverle el día, rechazar, y cerrarla cuando el vehículo
llega.
· Le suena a quien atiende esa sucursal, no solo al dueño.
```

### App Store — «Novedades de esta versión»

```
Citas: el cliente aparta el día, con o sin la app.

QUIEN TIENE LA APP
· Pide su cita eligiendo cuál de sus vehículos trae.
· Ve en su pantalla si el taller se la confirmó, y le llega el aviso cuando contesta.

QUIEN NO QUIERE INSTALAR NADA
· El taller tiene un enlace fijo para mandar por WhatsApp o imprimir en un QR.
· La persona pone su nombre, su teléfono, qué necesita y qué día. Sin cuenta ni contraseña.
· Le queda su propia página para ver cómo va y cancelarla si no va a poder.

EN EL TALLER
· Pantalla de Citas con lo que espera respuesta primero: confirmar, moverle el día, rechazar.
· Cuando el vehículo llega, un toque la cierra y lleva a recibirlo.
· Le suena al dueño y al encargado de esa sucursal.
· Una cita no crea cliente ni vehículo: los datos de verdad se toman con el carro enfrente.
```

### Qué entró, para acordarse

- Citas desde la app del cliente y desde el enlace público del taller.
- Pantalla de Citas para el Dueño y el Encargado, con la agenda del día.
- El enlace y su QR, en Ajustes del taller.
- Avisos de cita, que al tocarlos abren la agenda.

Del lado del servidor y del panel —que no pasan por las tiendas— entró lo mismo, más el
rediseño del panel, el estado de resultados y la página de venta.

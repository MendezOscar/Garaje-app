# 6. Cotizaciones

**Qué es.** El presupuesto que se le manda al cliente antes de hacer el trabajo. Sirve para
dos cosas: que autorice el gasto, y que quede escrito qué se le dijo.

**Cómo llegar.** Dentro de la [orden](05-ordenes.md), tarjeta **Cotizaciones**.

## Armarla

**Nueva cotización** y se van agregando líneas. Hay tres clases:

- **Repuesto del catálogo** — se busca por SKU, nombre o marca, y se pone la cantidad.
  Si la pieza la compra el taller afuera, se marca **se compra en casa de repuestos**.
- **Mano de obra del catálogo** — un trabajo de [Mano de obra](17-mano-de-obra.md).
- **Línea libre** — lo que no está en ningún catálogo: concepto y precio escritos a mano.

Debajo va el total, con el ISV del taller ya incluido («el total no cambia»).

**Las fotos del daño.** La cotización lleva las fotos de la orden. *«Una del daño explica el
presupuesto mejor que el texto»*: cuesta menos discutir un repuesto cuando se ve la pieza.

## Mandarla

**Enviar por WhatsApp** manda el enlace. El cliente abre una página —sin instalar nada— con
el detalle, las fotos y dos botones: **Aprobar** o **Rechazar**, con una nota opcional. Esa
respuesta **no se puede cambiar**, y el taller la ve al instante: la orden pasa a
*Esperando aprobación* hasta que conteste.

También se puede **bajar el PDF** y **reenviar por WhatsApp** si el cliente dice que no le
llegó. Y **Con la ficha** saca un solo archivo con la
[hoja de recepción](04-recibir-vehiculo.md) delante del presupuesto: cómo entró el vehículo y
qué cuesta arreglarlo, en un PDF en vez de dos.

Si el cliente tiene cuenta en la app, le llega además como aviso y la ve en
[su pantalla](24-el-cliente.md).

## Qué pasa después

| Respuesta | Qué hace el taller |
| --- | --- |
| Aprobada | sigue el trabajo; el presupuesto queda cerrado en ese monto |
| Rechazada | el trabajo se detiene y hay que llamar al cliente |
| Vencida | pasó la fecha de validez: hay que hacer otra |

Lo cotizado **no se cobra solo**: al facturar, la orden propone el total de la cotización
aprobada, pero el número que manda es el de la orden —sus pasos y sus repuestos—.

# 20. Ajustes del taller

![Ajustes del taller](img/ajustes-taller.png)

**Qué es.** Los datos del taller y lo que el sistema propone por defecto. Está en tres grupos
para que se entienda qué toca cada cosa.

**Cómo llegar.** Más → Catálogos y taller → **Taller**.

## Identidad del taller

Es **lo que se imprime en cada cotización y en cada factura**: nombre comercial, razón social,
RTN, teléfono, país, correo y dirección.

## Sucursales

**Es del panel, no de la app.** La primera sucursal la trae el taller desde que se da de alta;
las demás se agregan aquí, con **Agregar una sucursal**.

Cada sucursal lleva sus órdenes, su caja y su rango de facturación aparte. Lo único que hay
que pensar antes de guardar es el **código**: prefija los correlativos de esa sucursal
—«COT-SUR-000045»—, así que conviene corto y conviene que no cambie, porque los documentos
que ya salieron lo llevan impreso.

Una sucursal no se borra: se **da de baja**. Deja de salir al recibir un vehículo y al
facturar, y lo que ya tiene se queda donde está.

## Pasos de la orden

Recibida, En proceso y Entregada siempre están. Los otros cinco —En diagnóstico, Esperando
aprobación, Esperando repuestos, En pruebas y Lista para entrega— se apagan si el taller no
los usa; el flujo que queda se ve ahí mismo mientras se eligen.

Dos no se dejan apagar y la pantalla dice por qué: *Lista para entrega* mientras el taller
cobre bodegaje, porque de ese aviso sale la fecha del cobro, y *Esperando aprobación* mientras
el técnico no vea precios, porque es donde deja la orden para que el Dueño se lo ponga.

## Cómo cobra el taller

Lo que el sistema propone al cotizar y al facturar. Todo se puede cambiar trabajo por trabajo.

| Ajuste | Qué hace |
| --- | --- |
| **ISV (%)** | sus precios **ya llevan el impuesto dentro**: esto no sube el total, solo lo desglosa. En cero no se desglosa nada |
| **Garantía por defecto (días)** | la que lleva un trabajo al facturarlo. Cero es sin garantía |
| **Cobrar bodegaje** y **por día** | por el vehículo que nadie retira después del aviso |
| **Días de gracia** | cuántos días pasan antes de empezar a cobrar bodegaje |

## Qué ve el técnico

**El técnico ve precios y totales.** Encendido, el técnico ve lo que cuesta cada paso y el
total de la orden; apagado, solo ve qué hay que hacer. No es desconfianza: en muchos talleres
el técnico no habla de dinero con el cliente, le reporta al dueño.

**Guardar** aplica los tres grupos de una vez y confirma: *«Ajustes guardados»*.

> El mismo juego de ajustes está en el panel web, en Taller, con los mismos tres grupos.

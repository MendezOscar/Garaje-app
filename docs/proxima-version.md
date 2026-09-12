# La siguiente versión: 1.1.0

**Los cuatro puntos están hechos** (12 de septiembre de 2026) y la versión quedó en `1.1.0+4`.
Lo que sigue es publicar: primero TestFlight y App Store, y el mismo paquete al canal cerrado de
Play, que durante los 14 días nuevos le da a los verificadores algo que ejercitar.

Tres cosas, decididas el 9 de septiembre de 2026. Google respondió el 12 pidiendo 14 días más de
prueba cerrada, y como subir versiones al canal cerrado **no** reinicia ese contador, se
implementaron durante la espera.

Va como **1.1.0** y no como 1.0.2 porque son funciones nuevas, no correcciones. Arrastra además lo
que ya está en `main` sin publicar: las fotos que se suben solas al volver a la app, el código del
error en pantalla y la versión al pie de «Más».

Necesita `flutter clean` antes de compilar: cambió `pubspec.yaml` desde la última subida.

---

## 1. Un solo enlace para el cliente

**El problema.** El cliente recibe la cotización por WhatsApp con un enlace, y si aprueba hay que
mandarle **otro** para que siga el avance. Dos enlaces para el mismo trabajo, y el primero se queda
mostrando una cotización que ya se aprobó.

**Lo que ya existe y no hay que construir.** Los tres enlaces públicos están hechos, cada uno con su
token aleatorio: `/q/{token}` la cotización, `/o/{token}` el avance, `/c/{token}` el estado de
cuenta. Y **el de avance ya entrega la factura**: `GET public/work-orders/{token}/invoice.pdf`
responde el PDF en cuanto la orden se factura, y 404 mientras el vehículo siga en el taller. O sea
que no hace falta un enlace aparte para la factura: es el mismo del avance.

**Lo único que falta: el relevo.** `Quote` ya guarda `WorkOrderId` cuando se convierte, así que la
cotización sabe a qué orden pertenece. Falta que lo diga:

- `PublicQuoteDto` gana el token de seguimiento de la orden, cuando la haya
  ([QuoteDtos.cs:163](../backend/src/Garaj.Application/Quotes/QuoteDtos.cs#L163)).
- `QuoteService.GetPublicAsync` lo llena.
- [PublicQuoteView.vue](../web/src/views/PublicQuoteView.vue) muestra «Ver el avance de su
  vehículo» cuando el token viene, en vez de dejar al cliente mirando una cotización aprobada.

**La decisión que hay que tomar con los ojos abiertos.** El comentario de `PublicQuotesController`
dice hoy que **no se expone ningún otro id en la respuesta**, a propósito: el token de la URL es la
única credencial. Devolver el token de la orden es una excepción defendible —es el mismo cliente y
su propio vehículo, y quien tiene el enlace de la cotización ya podía ver esos datos— pero es un
cambio de postura consciente, no un descuido. Hay que actualizar ese comentario, no ignorarlo.

**Alternativa descartada:** redirigir `/q/` a `/o/` automáticamente. Deja al cliente sin poder
volver a ver lo que aprobó, que es justo el papel que quiere tener a mano cuando reclama.

## 2. Guía de primeros pasos

**Lo que ya hay** son tres pantallas de bienvenida —«El trabajo, paso a paso», «Con fotos de todo»,
«Cotizar y cobrar»— que cuentan de qué va la app pero no enseñan a usarla, y que se ven una sola vez.

**Lo que se hace:** una **lista de primeros pasos en la pantalla de inicio**, que se va tachando sola
y desaparece cuando está completa:

1. Registre su primer cliente.
2. Reciba un vehículo.
3. Cierre su primera orden y cóbrela.

**Por qué así y no un tour con globitos sobre los botones.** El tour se ve una vez, se salta, y hay
que mantenerlo cada vez que cambia una pantalla. La lista queda hasta que el trabajo se hace, no
estorba a quien ya sabe, y ataca el problema real del hallazgo 10: el taller nuevo entra y ve todo en
ceros sin saber por dónde empezar.

**De dónde salen los datos: de ninguno nuevo.** `DashboardDto` ya trae `OpenWorkOrders`,
`PendingRequests` y el resto ([SalesDtos.cs:287](../backend/src/Garaj.Application/Sales/SalesDtos.cs#L287));
con eso y la cantidad de clientes se sabe qué pasos están hechos. Sin endpoint nuevo, sin guardar
nada en el teléfono.

**Dónde:** [today_screen.dart](../mobile/lib/features/home/today_screen.dart) y la vista equivalente
del panel.

## 3. Datos de la factura

Comparada campo por campo contra el Reglamento del Régimen de Facturación, la factura **ya cumple
casi todo**: RTN y razón social del emisor, nombre comercial, dirección de casa matriz y de la
sucursal, la palabra FACTURA, CAI, rango autorizado, fecha límite de emisión, correlativo fiscal,
fecha, nombre y RTN del cliente con «Consumidor final» cuando no lo tiene, detalle con cantidad,
precio unitario y total, ISV desglosado, monto en letras y la leyenda «Original: Cliente · Copia:
Obligado tributario emisor».

**Lo que se hace ahora**, las dos que son de formato, en
[InvoicePdf.cs:238-247](../backend/src/Garaj.Infrastructure/Documents/InvoicePdf.cs#L238):

- Imprimir **«Importe exonerado»**, aunque vaya en cero: el formato pide los tres importes.
- Que el gravado **diga su tasa** —«Importe gravado 15%»— en vez de «Importe gravado» a secas. La
  tasa ya está en la venta.

**Lo que no se hace, salvo que un taller lo pida.** Clasificación por línea (gravado 15 / gravado 18
/ exento / exonerado). Hoy la venta lleva **una sola** tasa, así que una factura que mezcle un
repuesto gravado con algo exento no se puede representar. Es cambio de modelo —una columna por línea
y los subtotales por grupo— y en un taller casi todo es gravado al 15%: el 18% es para licor, tabaco
y boletos aéreos, y lo exonerado aparece solo con clientes con orden de exoneración. Se hace el día
que un taller real facture eso, no antes.

## 4. Límite de intentos en los enlaces públicos que faltan

Corrige una afirmación incompleta del hallazgo 59: se documentó que los endpoints anónimos llevan
límite, y es cierto **solo** del enlace de cotizaciones. Están anónimos y sin límite el de avance de
orden, el de estado de cuenta y el público de `TenantController`.

Es un `[EnableRateLimiting(RateLimits.Publico)]` por controlador, y entra en este lote porque el
punto 1 va a hacer que el enlace de avance se use mucho más que hoy.

---

## Orden sugerido

1. El límite de los enlaces públicos (minutos, y es lo único con implicación de seguridad).
2. Los dos campos de la factura (pequeño, y no toca la app).
3. El relevo del enlace de la cotización.
4. La lista de primeros pasos.

Los tres primeros son de servidor y panel: se despliegan sin esperar a ninguna tienda. Solo el cuarto
—y la parte móvil del tercero, si se hace— necesitan compilar la 1.1.0.

Al publicar, lo de siempre: [deployment.md §8](deployment.md), lanzamiento escalonado y los umbrales
de ahí.

# Seguridad

Esto no es una lista de buenas prácticas sacada de un libro: es lo que este sistema ya
hace, escrito para que lo que se agregue mañana lo haga también. Cada renglón está
porque algo lo pidió —una auditoría, una caída, o un error que llegó hasta el teléfono
de alguien—, y abajo de cada uno está dónde vive en el código.

Antes de dar por terminado un endpoint o una pantalla, recorrer la lista entera.

## 1. El candado del modo

Una empresa de mostrador no tiene rutas; un restaurante no tiene camionetas. Esconder el
menú no alcanza, porque quien conozca la dirección puede llamar igual.

Si lo nuevo pertenece a un modo, su prefijo va en [ModeGuard](../backend/src/StockRuta.Api/Common/ModeGuard.cs).
Las cuatro puertas son reparto, mostrador, caja y salón, y la caja va aparte porque la
usan el mostrador y el restaurante.

## 2. El tope de peticiones, con la partición correcta

Cada endpoint que cuesta lleva su política en [RateLimitPolicies](../backend/src/StockRuta.Api/Common/RateLimitPolicies.cs):
armar un PDF es memoria de un servidor que ya se murió una vez por eso, un reporte sin
rango recorre el historial entero, y una subida ocupa espacio que se paga todos los meses.

Lo difícil no es el número: es **a quién se le descuenta**. Con sesión se cuenta por
usuario, porque la oficina entera sale por la misma dirección. Sin sesión queda la IP,
que es lo único que hay —salvo cuando hay algo mejor: el pedido de un restaurante cuenta
por mesa, porque todos los comensales salen por la misma wifi y un tope por IP deja al
salón lleno sin poder pedir. Si la clave sale de algo que manda el cliente, tiene que
llevar también la IP, para que nadie se fabrique cupo inventando identificadores.

El limitador corre antes de que el cuerpo se lea: lo que necesite para partir el cupo
tiene que viajar en la dirección.

## 3. La política de autorización, en el servidor

`RequireAuthorization` con una política, siempre. Esconder un botón es cortesía; el
candado es que el servicio vuelva a preguntar. El cobro del mesero es el ejemplo: la
pantalla no muestra el botón, y aun así `ChargeAsync` lo verifica.

## 4. El aislamiento entre empresas

Toda entidad nueva hereda de `TenantEntity`, y con eso el filtro por empresa se le aplica
solo. Nunca confiar en un identificador que viene del cliente sin que la consulta pase
por ese filtro: es lo que hace que el GUID de una mesa no sirva en otro restaurante.

## 5. Idempotencia en lo que mueve plata o existencias

Quien cobra toca el botón dos veces, o se le va el internet y reintenta. Todo lo que
crea un cobro, una venta o un movimiento de bodega acepta `ClientRequestId` y pasa por
`IdempotencyGuard`.

## 6. La validación, repartida a propósito

En el validador van los topes de largo, que es lo que cuida la puerta de lo que escribe
cualquiera. Lo que es obligatorio lo decide el servicio, que es el único que tiene el
contexto: el que escanea el QR de su mesa no escribe dirección porque ya está sentado
adentro, y eso el validador no lo sabe.

## 7. El token en la dirección

El navegador no manda cabeceras al abrir una pestaña, así que algunas rutas aceptan el
token por query. Esa lista está en [JwtBearerSetup](../backend/src/StockRuta.Api/Auth/JwtBearerSetup.cs)
y es corta a propósito: el token queda escrito en el historial.

Agregar una ruta ahí sólo cuando el navegador tenga que abrirla en una pestaña, y
diciendo por qué. Si se olvida, el botón responde 404 y parece roto.

**Pendiente conocido:** sigue siendo el agujero que dejó la auditoría. Cada ruta nueva
lo estira un poco.

## 8. La contraseña temporal

[PasswordGuard](../backend/src/StockRuta.Api/Common/PasswordGuard.cs) cierra todo lo que
no sea `/auth`, `/public` o `/health` mientras la contraseña sea la que le mandaron. Un
endpoint nuevo queda cerrado solo; uno público hay que pensarlo dos veces.

## 9. Lo que se congela no se reescribe

El precio y el costo viajan congelados en la línea. Cambiar hoy el catálogo no puede
reescribir el margen de un turno que ya cerró ni el total de una factura ya emitida.

## 10. Las migraciones, con el valor viejo escrito a mano

EF deja en falso —o en cero— lo que ya estaba en la tabla. Para una columna nueva que
cambia comportamiento, el `defaultValue` se corrige a mano, o el despliegue le apaga la
función a todos los que ya estaban trabajando. Pasó con `cashier_can_charge`.

Y en producción la migración se corre antes de desplegar, a mano, con el rol dueño: la
aplicación se conecta con un rol sin permisos de esquema.

## 11. La prueba, como lo usa el usuario

No alcanza con que el servicio devuelva algo. El PDF del QR se generaba bien y la prueba
pasaba; lo que fallaba era abrirlo en una pestaña, que es lo único que hace el dueño.

Si la función se usa desde el navegador, la prueba la abre desde el navegador. Si el tope
que se quiere probar lo sube el banco de pruebas, la prueba no está probando nada: hay
que bajar a verificar la cuenta, no el efecto.

# Pendientes

Lo que se decidió **no** hacer todavía, con el motivo y con qué lo dispara. No es una lista de
deseos: cada punto se evaluó contra el código y se dejó fuera a propósito.

Ninguno de estos bloquea el paso a producción de septiembre de 2026.

El detalle de dónde salió cada uno está en [pruebas-cerradas.md](pruebas-cerradas.md), por número
de hallazgo.

---

## 1. Operaciones repetidas por reintento — hallazgo 57

**Qué falta.** No hay clave de idempotencia en venta, abono ni consumo de repuesto. La cola de
subida reintenta sola, y un reintento sobre una petición que el servidor ya aplicó —pero cuya
respuesta se perdió— puede aplicarla dos veces.

**Por qué importa más que el resto.** Es el único pendiente con riesgo de **dinero**: una venta o un
abono duplicado. Los demás son «se investiga peor». No ha ocurrido porque el volumen es de un taller,
no porque esté resuelto.

**Cuándo.** En la **1.0.3**. Necesita que el cliente genere la clave, así que viaja con una versión
de la app; no se puede desplegar solo en el servidor.

**Alcance sugerido.** Venta y abono primero. El consumo de repuesto después: ahí el daño es un ajuste
de inventario, no un cobro.

---

## 2. La búsqueda no ignora acentos — hallazgo 36

**Qué falta.** «bateria» no encuentra «Batería». Pasa en repuestos, clientes y vehículos.

**Qué cuesta.** Habilitar la extensión `unaccent` en el proyecto de Supabase, una migración, y tocar
las consultas de búsqueda para comparar sin acentos.

**Cuándo.** En cuanto Google conceda el acceso a producción. Es **solo servidor**, así que no espera
a ninguna tienda: se despliega con `main` y los talleres lo tienen ese día.

**Por qué no antes.** Toca la base de producción, y no se toca la base mientras Google está
evaluando la solicitud: si una migración sale mal, el revisor entra a una app rota.

---

## 3. Dónde viven los logs — hallazgos 61, 69, 75, 78 y 80

**Qué falta.** Los logs salen a la consola de Render, que los conserva pocos días y no permite buscar
hacia atrás ni poner alertas. De eso dependen cuatro cosas más: la alerta por tasa de 5xx (69), los
percentiles de latencia (75), la retención declarada (78) y el tablero de salud (80).

**Por qué no ahora.** Los cinco son «para poder investigar mejor». Con doce usuarios conocidos, la
investigación llega por WhatsApp el mismo día. Y el dato crudo no se pierde del todo: cada línea
lleva método, ruta, estado, milisegundos y —desde el hallazgo 68— la versión del cliente.

**Qué lo dispara.** El **primer incidente que no se pueda reconstruir** porque los logs ya se
borraron. Ese día se justifica solo.

**Nota.** Para este volumen las capas gratuitas de los servicios de logs alcanzan de sobra, así que
probablemente no cueste nada cuando toque.

---

## 4. Borrado lógico en el dominio — hallazgo 72

**Qué falta.** No hay borrado lógico: lo que se borra, se borra de verdad, y con la fila se va quién
la creó. Afecta a órdenes no facturadas y a abonos.

**Qué se hizo en su lugar.** El caso que dolía —borrar un abono— ahora queda escrito en el log con el
monto, la fecha, la venta, quién lo había registrado y quién lo borra. Y una orden **facturada** no
se puede borrar: hay que anular la factura, que sí deja rastro.

**Por qué no ahora.** Es una migración en todo el dominio más filtros globales nuevos. Hecho con
prisa, el fallo típico es dejar datos invisibles sin que nadie se dé cuenta: peor que el problema que
resuelve.

**Qué lo dispara.** El primer taller con **más de una persona capturando dinero**, o el primer
reclamo de un dueño sobre un pago que no aparece.

---

## 5. Señal de acceso horizontal — hallazgo 71

**Qué falta.** Pedir un recurso de otro taller responde **404 y no 403**, a propósito, para no
confirmar que existe. El efecto secundario es que un intento de acceso ajeno se ve en el log igual
que un enlace viejo.

**Por qué no ahora.** Distinguirlos exige una segunda consulta **sin** filtro de taller por cada 404
—a esa altura del código el registro ajeno ya es invisible—: trabajo en el camino caliente para
atrapar algo que con doce usuarios conocidos no ha pasado.

**Qué lo dispara.** Volumen: varias decenas de talleres, o un intento real detectado por otra vía.

---

## Lo que no es pendiente, aunque lo parezca

Para no reabrirlo cada vez que alguien pregunte:

| Se pidió | Por qué no se hace |
| --- | --- |
| Informes de fallos (Crashlytics) | Play Console ya reporta crashes y ANR con versión y modelo, sin SDK. Meterlo obliga a rehacer Data safety y la política para declarar analítica que hoy no existe (hallazgo 60) |
| Analítica de embudo | Misma razón, y mide poco: son talleres conocidos, no usuarios anónimos (64) |
| Detección de patrones de fraude | No hay línea base, el criterio sería nuestro y no del taller, y nos pone de auditores del negocio del cliente (73) |
| Registro público de talleres | Por diseño: las cuentas las crea el dueño para su personal. Así se le declaró a Apple (4) |
| Métricas de reconciliación entre módulos | No hay dos verdades que reconciliar: el saldo **es** la suma de los abonos (74) |

# IA en GarajApp — plan de trabajo

Tres direcciones, decididas el 6 de noviembre de 2026. Ninguna está empezada: esto es lo que
hay que construir y, sobre todo, lo que hay que decidir antes de escribir la primera línea.

| | Para quién | Qué hace |
| --- | --- | --- |
| **1 · Los pasos del trabajo** | Dueño y Encargado | Del diagnóstico sale una propuesta de pasos y repuestos, que él edita y acepta |
| **2 · La lectura del negocio** | Dueño | Una vez por semana, qué pasó con las ventas, el inventario y los técnicos, y qué conviene hacer |
| **3 · La consulta del técnico** | Técnico | Preguntar mientras tiene el vehículo en la rampa, con lo que el propio taller ya reparó antes |

---

## Lo que ya existe y no hay que construir

Esto importa más que el modelo que se elija, porque es lo que hace que la propuesta salga con
los precios del taller y no con precios inventados:

- **Trabajos frecuentes** (`JobTemplate`): pasos con horas y servicio de catálogo, más
  repuestos con cantidad. Ya es la forma exacta que tiene que producir la dirección 1, así
  que la IA no inventa un formato nuevo: llena el que ya se usa.
- **Catálogo de mano de obra** (`LaborService`) y de **repuestos** (`Part`), con
  `PartApplication` —para qué vehículos sirve cada repuesto—. Es lo que permite ofrecer
  repuestos que el taller **tiene**, en vez de nombres de catálogo ajeno.
- **Las lecturas del escáner** (`VehicleScan`, noviembre de 2026): los códigos de falla
  guardados uno por uno, con su descripción y su estado. Es la mejor entrada que puede tener
  un diagnóstico asistido, y ya está.
- **Los reportes** (`IReportService`): tablero, ingresos por rango, tiempo por técnico, cierre
  de caja, repuestos bajo mínimo, cuentas por cobrar. La dirección 2 **no** necesita consultas
  nuevas; necesita juntar las que hay.
- **El historial del vehículo y del cliente**: órdenes anteriores con su diagnóstico, sus
  pasos y sus repuestos. Es el material de la dirección 3.
- **`Tenant.DiagnosticsEnabled`**, que nace en `true` con el plan Pro —y hoy **no cierra
  nada**: está puesto y no se consulta en ningún lado. Es el interruptor comercial que estas
  tres funciones estaban esperando.
- **El vocabulario del taller** (`WorkshopKind`): hay talleres de **equipos**, no solo de
  vehículos. Un prompt que hable de «el carro» se ve mal en la mitad de los clientes.

---

## Las cinco decisiones que mandan sobre todo lo demás

Si alguna de estas se decide mal, lo demás no se arregla después.

### 1. La llave vive en el servidor y nada más

La app y el panel **nunca** hablan con la API del modelo. Llaman a GarajApp, GarajApp llama al
modelo. Es la misma regla que ya rige para Supabase y Firebase, y por el mismo motivo: una
llave dentro de un `.ipa` es una llave pública. En Render va como variable de entorno, igual
que la cadena de conexión, y **no entra al repositorio, que además es público**.

### 2. El dato del cliente no sale crudo

El nombre, el teléfono, el RTN y la dirección del cliente no tienen por qué viajar a ninguna
parte para que salga una lista de pasos. La regla: **va lo que la tarea necesita y nada más**.
La placa tampoco; el vehículo se describe por marca, modelo, año y kilometraje.

La dirección 2 es el caso extremo y el más fácil: se manda un **paquete de números ya
calculados** —ingresos, márgenes, conteos, horas—, no filas de la base. Ningún nombre de
cliente, ninguna factura.

### 3. La IA no calcula, explica

Todas las cifras las calcula el servidor en SQL, como hoy. La IA recibe los números hechos y
escribe la lectura. Nunca se le pide que sume, que divida ni que saque un porcentaje: un
modelo que se equivoca en una resta en el reporte mensual del dueño destruye la confianza en
todo lo demás, y es un error que no se detecta mirando.

Lo mismo en la dirección 1: la IA propone **qué pasos y qué repuestos**; el precio lo pone el
catálogo del taller al armar la propuesta. Nunca sale un número de la IA a una cotización.

### 4. Todo lo que produce es un borrador

Nada de lo que escribe la IA se guarda solo, se le manda al cliente solo, ni se convierte en
una orden sola. Sale marcado como propuesta, el humano lo edita y lo acepta. Donde esto se
nota más es en la dirección 1: aceptar la propuesta es lo que crea los pasos, y el botón dice
«Aceptar», no «Guardar».

### 5. Es del plan Pro, y hay tope

`DiagnosticsEnabled` cierra las tres. Y aparte del plan, cada taller lleva un **tope mensual
de consumo** que se cuenta en el servidor y se corta al llegar —como ya se corta por
mensualidad vencida en `SubscriptionGuardMiddleware`—. Sin tope, un técnico curioso una tarde
se lleva el margen del mes. Al llegar al tope la función se apaga con un mensaje que lo dice,
no con un error.

---

## 1 · Los pasos del trabajo

**El problema.** El técnico escribe el diagnóstico y ahí se queda. Cargar los pasos y los
repuestos es trabajo de oficina, lo hace el dueño, y mientras no lo haga el técnico no puede
seguir. Los trabajos frecuentes resuelven lo repetido —el cambio de aceite—, pero no el
trabajo que no se repite, que es justo donde se pierde la tarde.

**Cómo queda.** En la orden, con el diagnóstico escrito, un botón: **«Proponer los pasos»**.
Sale una lista editable: pasos con su título, sus horas estimadas y el servicio del catálogo
con el que se cobran; repuestos del catálogo del taller con su cantidad. Se tachan los que no,
se corrigen las horas, se acepta. Los pasos entran a la orden como si se hubieran escrito a
mano, y aparece la opción de guardar el conjunto como trabajo frecuente.

**Qué se le manda al modelo:** el diagnóstico, marca/modelo/año y kilometraje, los códigos de
la última lectura del escáner si la hay, los servicios de mano de obra del taller y los
repuestos **ya filtrados para ese vehículo** por `PartApplication`.

**Lo que hay que hacer bien, o no sirve:**

- **La salida es estructurada**, con esquema estricto. No texto que después haya que
  interpretar: un esquema con pasos y repuestos, y los repuestos **referenciados por el id
  del catálogo**, no por nombre. Un modelo al que se le deja escribir el nombre del repuesto
  termina ofreciendo repuestos que el taller no tiene.
- **Si no hay repuesto que sirva, se dice.** Vale más un paso sin repuesto que un repuesto
  equivocado: el que lo revisa está apurado y acepta lo que ve.
- **El vocabulario del taller.** En un taller de equipos no se dice vehículo.

**Lo que cuesta de verdad:** no es el modelo, es el filtro del catálogo. Mandar los 400
repuestos del taller en cada llamada es caro y además empeora la respuesta. El filtro por
vehículo y categoría es el trabajo real de esta dirección.

---

## 2 · La lectura del negocio

**El problema.** El tablero enseña números, y los números no dicen qué hacer. El dueño ve que
el margen bajó y no sabe si fue el repuesto que subió de precio, el técnico que se tardó o el
descuento que dio el encargado. Nadie abre el reporte de tiempo por técnico dos veces.

**Cómo queda.** Una vez por semana —y un cierre al mes— el dueño recibe en el panel una
lectura de una página: qué pasó, qué cambió respecto al periodo anterior, y **tres
oportunidades ordenadas por lo que valen**, cada una con el número que la respalda y qué haría
falta para aprovecharla. Se abre en el panel y se puede bajar en PDF.

**Qué se le manda:** un paquete de agregados que arma el servidor con los reportes que ya
existen —ingresos, margen, órdenes por estado, órdenes atrasadas, cotizaciones sin respuesta y
su tasa de aprobación, repuestos bajo mínimo y los que más rotan, horas y órdenes por técnico,
cuentas por cobrar y vencidas, gastos por categoría—, el mismo paquete del periodo anterior
para comparar, y nada más. Sin nombres de clientes.

**Lo que hay que hacer bien:**

- **Los técnicos salen por nombre y eso es delicado.** Es un reporte para el dueño sobre
  personas que trabajan ahí. El tono se instruye explícitamente: datos, no juicios sobre la
  persona. Y el propio reporte dice que el tiempo registrado es un techo —ya lo dice
  `TechnicianTimeRowDto.AutoClosed`, porque el sistema cierra tramos que nadie paró—.
- **Un taller con dos órdenes en la semana no tiene lectura que dar.** Debajo de cierto
  volumen no se genera, y se dice por qué: inventar una tendencia con cuatro datos es peor que
  no decir nada.
- **Corre de noche y en lote.** No hay nadie esperando la respuesta, así que va por la API de
  lotes, que cuesta la mitad.

---

## 3 · La consulta del técnico

**El problema.** El técnico tiene el vehículo en la rampa y una duda. Hoy pregunta al
compañero, busca en el teléfono o prueba. Y el taller ya reparó ese mismo problema en ese
mismo modelo hace cuatro meses, pero esa información está dentro de una orden cerrada que
nadie va a abrir.

**Cómo queda.** Dentro de la orden, **«Preguntar»**. Una conversación corta, con el contexto
de la orden ya puesto: el vehículo, el diagnóstico, los códigos de la última lectura. Y, como
material, **lo que este taller ya hizo**: órdenes cerradas del mismo modelo con su
diagnóstico, sus pasos y sus repuestos.

Esa es la parte que vale y la que nadie más puede dar: *«a tres Corolla 2015 con P0301 este
taller les cambió la bobina del cilindro 3, y en uno de ellos volvió a los dos meses»*.

**Lo que hay que hacer bien, y aquí es cuestión de seguridad, no de calidad:**

- **No inventa especificaciones.** Un torque, una capacidad de aceite, un orden de encendido o
  un color de cable inventados mandan a alguien a romper un motor. El sistema se instruye para
  **negarse** a darlos de memoria y remitir al manual del fabricante. Es la diferencia entre
  una herramienta y un riesgo.
- **Lo que viene del historial se distingue de lo que es razonamiento general**, y se dice de
  cuál orden salió, con enlace. Lo que el taller hizo es un hecho; lo demás es una idea.
- **Es la cara cara de las tres.** Una conversación de ocho turnos cuesta cerca de treinta
  veces lo que una propuesta de pasos. Aquí es donde el tope mensual deja de ser teórico.
- **En la rampa no hay señal.** La pantalla tiene que aguantar quedarse sin red a medio turno
  sin perder lo escrito, y decirlo.

---

## Qué cuesta

Precios de la API al 6 de noviembre de 2026, por millón de tokens, y a L 26 por dólar:

| Modelo | Entrada | Salida | Lectura de caché |
| --- | --- | --- | --- |
| `claude-opus-5-5` | $4.00 | $20.00 | $0.20 |
| `claude-sonnet-5-5` | $2.00 | $10.00 | $0.20 |
| `claude-haiku-5-5` | $0.10 | $0.50 | — |

Con el prompt del sistema y el catálogo del taller en caché, y un taller de tres técnicos:

| | Por llamada (Opus 5.5) | Al mes | Con Sonnet 5.5 |
| --- | --- | --- | --- |
| 1 · Pasos (≈150 al mes) | ≈ L 0.35 | **≈ L 50** | ≈ L 25 |
| 2 · Lectura (5 al mes, en lote) | ≈ L 0.55 | **≈ L 3** | ≈ L 1.50 |
| 3 · Consulta (8 turnos, 75 al mes) | ≈ L 2.70 | **≈ L 200** | ≈ L 100 |

**Lo que dicen estos números:** contra una mensualidad de L 1,250, las direcciones 1 y 2 son
gratis en la práctica. Elegir el modelo barato ahí ahorra L 25 al mes y se paga con la primera
propuesta que salga mal y haya que corregir a mano. Donde el modelo sí se discute es en la
3, que es la única que puede crecer sin techo —y para eso está el tope, no un modelo peor—.

La recomendación es **`claude-opus-5-5` en las tres**, con caché del prompt y del catálogo, la
2 en lote, y el tope mensual por taller. Bajar a `claude-sonnet-5-5` en la 3 es una decisión
razonable si el tope empieza a tocarse seguido; es decisión de negocio y se toma con el
consumo medido, no antes.

**Tres cosas que no están en la tabla y conviene no olvidar:** el caché solo sirve si el
prefijo no cambia —el catálogo va antes que la pregunta, siempre en el mismo orden—; cada
reintento se paga igual que el primer intento; y el consumo se registra por taller desde la
primera llamada, porque sin eso el tope es una idea y no un tope.

---

## Seguridad

Recorrido de [seguridad.md](seguridad.md) con lo nuevo, que es lo que toca hacer antes de
cerrar cada una:

- **§4, aislamiento.** Todo lo que entra a un prompt sale de consultas que ya pasaron por
  `AccessScope`. Nada de `BypassTenantFilter`: hay usuario detrás de cada llamada. El
  historial de la dirección 3 se arma con las órdenes **del taller**, y de un técnico, con las
  suyas.
- **§6, validación.** La respuesta del modelo **se valida como si viniera de un cliente
  hostil**, porque a efectos prácticos viene de afuera: ids de repuesto que existan y sean del
  taller, cantidades positivas, horas en rango, largos recortados. Un id que no cuadre se
  descarta, no se guarda.
- **§8, contraseña temporal.** Los endpoints nuevos quedan cerrados solos mientras la
  contraseña sea la provisional. Correcto, no hay nada que hacer.
- **§9, lo que se congela.** La propuesta aceptada se guarda como pasos normales, con sus
  precios congelados como cualquier otro. La propuesta en sí se guarda **con lo que se le
  mandó al modelo**: cuando una salga mal, hay que poder ver qué vio.
- **§11, probar como lo usa el usuario.** Que el endpoint devuelva algo no prueba nada aquí.
  Hace falta un conjunto de **órdenes reales ya cerradas** —treinta, de distintos tipos de
  trabajo— y comparar la propuesta contra los pasos que el taller de verdad cargó. Eso se
  arma **antes** de escribir el prompt, no después: sin eso no hay forma de saber si un cambio
  en el prompt mejoró o empeoró, y se termina ajustando a ojo.

Una advertencia que no está en `seguridad.md` y debería: **el diagnóstico lo escribe un
humano, y entra a un prompt**. Alguien puede escribir instrucciones ahí. No es el riesgo
clásico —quien escribe el diagnóstico es del mismo taller que ve el resultado—, pero la salida
estructurada y la validación de ids son justamente lo que impide que importe.

---

## El orden

1. **El cimiento, sin funciones.** Cliente del modelo en Infrastructure, llave por variable de
   entorno, conteo y tope de consumo por taller, registro de cada llamada con lo que se mandó,
   y `DiagnosticsEnabled` cerrando de verdad. Nada de esto se ve, y todo lo demás lo necesita.
2. **El conjunto de prueba** de la dirección 1: treinta órdenes cerradas con sus pasos reales.
   Antes del prompt.
3. **Dirección 1**, solo panel. Es la de mayor valor por lo que cuesta, y la que ya tiene la
   forma de salida definida por los trabajos frecuentes.
4. **Dirección 2.** Reusa los reportes; casi todo el trabajo es armar el paquete y el PDF.
5. **La 1 en la app**, una vez que el panel enseñó qué propuestas acepta la gente y cuáles no.
6. **Dirección 3.** La última a propósito: es la más cara, la de más riesgo y la que mejor
   funciona cuando ya hay historial del taller cargado y lecturas del escáner guardadas.

---

## Lo que no se va a hacer

- **Chatear con el negocio** («¿cuánto vendí en marzo?»). Suena bien y es una trampa: obliga a
  dejar que el modelo arme consultas, que es el agujero de seguridad más grande que se puede
  abrir en una aplicación de varios talleres. Los números los da el reporte.
- **Escribirle al cliente.** Ni la cotización ni el aviso de WhatsApp los redacta la IA. Lo
  que sale con el nombre del taller lo escribe el taller.
- **Precios.** Ni sugerir cuánto cobrar, ni estimar el costo de un repuesto que no está en el
  catálogo.
- **Leer las fotos.** La foto de un daño es evidencia para un humano. Dictaminar sobre una
  imagen es otra discusión, y más cara.
- **Traducir códigos de fabricante.** La tabla de la app cubre los de la norma; los propios de
  cada marca los vende la marca con su equipo. Un significado inventado para un `P1xxx` es
  exactamente el tipo de error que no se nota hasta que alguien cambió la pieza equivocada.

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

### 5. Es del plan Pro, y hay tres techos

`DiagnosticsEnabled` cierra las tres direcciones. Debajo del plan van **tres techos**, y hacen
falta los tres porque protegen de cosas distintas:

**El del taller, en créditos.** Un solo número que el taller entiende: el plan Pro incluye
**450 créditos al mes, con máximo de 80 al día**. Una propuesta de pasos vale **1 crédito**,
una consulta del técnico **5**, la lectura del negocio **0** —esa la manda el taller, no la
pide—. Son los mismos 450 tenga tres técnicos o quince: lo que se mide es el asistente, no la
gente, y así el «técnicos sin límite» que el plan Pro ya vende sigue siendo cierto. Si un
taller necesita más, se venden a **L 100 por cada 100 créditos**.

El tope **diario** es el que de verdad protege: el mensual se agota en una tarde si alguien
descubre el juguete, y 80 al día le pone un piso de varios días a cualquier desastre. Un día
ocupado no pasa de 20 o 25 créditos, así que no raciona nada.

**El suyo, en dinero.** Los créditos son una aproximación: una propuesta en un taller con 400
repuestos en catálogo cuesta más que en uno con 40, porque va más texto en cada llamada. Por
eso debajo de los créditos va un **techo en lempiras por taller** —unos **L 220**— que corta
aunque queden créditos. Ese número no lo ve el taller. Y se cuenta el **costo real que
devuelve cada llamada**, no una estimación; los reintentos y las llamadas que fallan también
se cobran, así que también se cuentan.

**El de la plataforma.** El escenario que arruina un mes no es un taller curioso, es un bug
que reintenta en bucle de madrugada. Un **techo mensual global** en configuración apaga la
función para todos y avisa; y aparte, un **límite de gasto en la consola de Anthropic**, que
es el único candado que no depende de que el código esté bien.

Al llegar a cualquiera de los tres la función se apaga con un mensaje que lo dice —«se
acabaron las consultas de hoy, vuelven mañana»—, nunca con un error, y el resto de GarajApp
sigue funcionando igual.

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
- **Es la cara de las tres.** Una consulta vale **5 créditos** contra **1** de una propuesta,
  y conviene **cerrarla en cinco o seis turnos** y pedir que se abra otra. No es solo costo:
  una conversación larga se desenfoca, y una pregunta concreta por consulta se contesta mejor.
  Aquí es donde los techos dejan de ser teóricos.
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

### Lo que se midió

La dirección 1 **ya se probó de verdad** con `pruebas/ia-pasos`: dos tandas de casos reales
sacados de la base, con Opus 5.5 y el catálogo en caché.

| | Estimado | **Medido** |
| --- | --- | --- |
| Una propuesta de pasos | L 0.35 | **L 0.46** |
| Entrada por llamada | ~3,700 tokens | **~70 tokens** (el catálogo se lee de caché) |
| Salida por llamada | ~600 tokens | **~1,000 tokens** |

Un **28% por encima** de lo estimado, y la causa está a la vista: **la salida es más larga de
lo previsto**, no la entrada. El caché funciona mejor de lo esperado —la entrada bajó de 3,700
tokens a 70—, pero la nota que el asistente escribe al final pesa más que todo lo demás junto.
Acortarla es la única palanca que importa en esta dirección, y además es mejor producto.

Las direcciones 2 y 3 **no están medidas**: no existen todavía. Lo de abajo sigue siendo
estimación, y la de la consulta es la más floja de las tres porque nadie sabe cuántos turnos
va a durar de verdad.

### Al mes, por taller

Con los 450 créditos del plan Pro y el costo medido de **L 0.46 por crédito**:

| | Al mes |
| --- | --- |
| Si consume los 450 créditos completos | **≈ L 207** |
| Consumo esperado (40% de la asignación) | **≈ L 83** |
| La lectura semanal del negocio, que no gasta créditos | **≈ L 2.75** (5 informes, en lote) |

Contra los **L 400** que el plan Pro cobra sobre el Básico: **48% de margen en el peor caso,
79% con el consumo esperado**. El 40% es supuesto, no dato: el primer mes de registro de
consumo lo reemplaza.

### Qué modelo

**`claude-opus-5-5` en las tres**, con caché del prompt y del catálogo y la dirección 2 en
lote. No se abarata por modelo: bajar las tres a Sonnet ahorra unos L 67 al mes por taller y
la dirección 3 es la que tiene riesgo físico —un torque inventado rompe un motor—, donde un
modelo que obedece peor la instrucción de negarse es exactamente lo que no conviene. El costo
se controla con los techos, que son números que se fijan, no con un modelo que adivina peor.

**La condición de cambio, escrita desde ahora:** si al medir un mes real el costo por taller
pasa de **L 120**, la dirección 3 se mueve a `claude-sonnet-5-5` y se vuelve a correr contra
el conjunto de prueba. Si no lo pasa, se queda. Así lo decide el consumo medido y no la
corazonada de nadie.

**Y el proveedor va detrás de una interfaz en Infrastructure**, como ya está `IStorageService`.
Cambiar de modelo o de proveedor tiene que ser configuración, no reescribir.

### Lo gratis, y por qué no

Se evaluó. La capa gratuita de Gemini **usa el contenido para mejorar sus productos** y puede
ser leído por revisores humanos: ahí viajarían los diagnósticos del taller y, en la dirección
2, sus ingresos y márgenes. No se puede vender privacidad y usar eso. Además sus límites son
**por organización, no por taller**, así que se agotan justo cuando el negocio funciona.
Groq dice en sus términos que su capa gratuita no es para producción. Y el servidor propio con
modelo abierto, que es la que parece gratis, es la más cara: un GPU en renta anda por **$1,800
al mes** y el punto de equilibrio contra una API barata está en miles de millones de tokens —a
este volumen sale unas sesenta veces más caro, más el mantenimiento—.

Lo barato de verdad sería `claude-haiku-5-5`, a unos L 15 al mes por taller. No se descarta
para siempre; se descarta **para la dirección 3**, por lo dicho arriba.

**Tres cosas que no están en las tablas y conviene no olvidar:** el caché solo sirve si el
prefijo no cambia —el catálogo va antes que la pregunta, siempre en el mismo orden, y la
prueba confirmó que así funciona—; cada reintento se paga igual que el primer intento; y el
consumo se registra por taller desde la primera llamada, porque sin eso el techo es una idea
y no un techo.

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
2. **El conjunto de prueba** de la dirección 1: órdenes cerradas con sus pasos reales, para
   comparar contra ellas. Antes del prompt. **Primera vuelta hecha** —ver abajo—, pendiente
   armarlo con treinta casos cuando haya órdenes para armarlo.
3. **Dirección 1**, solo panel. Es la de mayor valor por lo que cuesta, la que ya tiene la
   forma de salida definida por los trabajos frecuentes, y la única probada.
4. **Dirección 2.** Reusa los reportes; casi todo el trabajo es armar el paquete y el PDF.
5. **La 1 en la app**, una vez que el panel enseñó qué propuestas acepta la gente y cuáles no.
6. **Dirección 3.** La última a propósito: es la más cara, la de más riesgo y la que mejor
   funciona cuando ya hay historial del taller cargado y lecturas del escáner guardadas.

---

## Lo que la prueba ya dejó claro

`pruebas/ia-pasos` corrió la dirección 1 contra casos reales de la base, sin tocar nada del
producto. Lo que se comprobó, y vale para cuando se construya:

- **Las reglas del prompt se sostienen.** Ni un solo repuesto fuera del catálogo en los casos
  corridos: todos los SKU propuestos existían. La validación por id del lado del servidor
  sigue haciendo falta —es una respuesta de afuera—, pero no se está peleando contra el
  modelo.
- **Se niega cuando toca.** Con un diagnóstico pobre devolvió la lista vacía y pidió lo que
  faltaba averiguar, en vez de inventar pasos. Era la regla 6 y es la que más se temía.
- **Aguanta la escritura de taller**: faltas de ortografía, frases cortadas, varias
  observaciones sueltas en un mismo campo.
- **Propone trabajo que el taller a veces no cobra.** En un caso el técnico había anotado
  cuatro hallazgos y la orden facturó uno; la propuesta traía los cuatro. Eso es ingreso, y
  es un argumento de venta que no estaba en el plan.
- **El caché funciona**: la entrada bajó de ~3,700 tokens a ~70 en cuanto el catálogo quedó
  cacheado. Confirma que el orden importa —catálogo primero, pregunta después—.
- **Lo que hay que corregir en el prompt:** las cantidades no siempre cuadran, se le olvidan
  los consumibles que sí se cobran, parte el trabajo más fino de lo que el taller lo cobra, y
  la nota final es demasiado larga —que es, además, de dónde sale el 28% de sobrecosto—.

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

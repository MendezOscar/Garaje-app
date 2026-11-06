# Probar el asistente con un taller de verdad

Guion para la prueba con cliente, en las tres direcciones. Lo que se construyó y lo que se
decidió está en [ia.md](ia.md) y [ia-direccion-1.md](ia-direccion-1.md); esto es lo que hay que
hacer delante del taller, en qué orden, y qué tiene que pasar en cada paso.

> **Solo la dirección 1 existe.** Las direcciones 2 y 3 están diseñadas y no están escritas.
> Sus guiones están abajo para saber qué se va a probar cuando existan, y **para no prometerlas
> en la visita**. Si el taller pregunta, la respuesta honesta es «viene, y no cambia la
> mensualidad».

---

## Antes de llegar

| | |
| --- | --- |
| **Taller** | Uno del plan Pro, con catálogo cargado y al menos treinta órdenes cerradas |
| **Versiones** | Panel al día solo; la app tiene que ser **1.9.0 (21)** o mayor en cada teléfono |
| **En el panel de plataforma** | Asistente encendido, créditos 450/80, y **el tope de gasto en L 20** el primer día |
| **Apuntado** | El gasto del mes que sale bajo la casilla del Asistente. Es contra ese número que se mide |

Y una advertencia que se olvida siempre: **el interruptor viaja en la sesión**. Quien tenga el
panel o la app abiertos desde antes de encenderlo tiene que salir y volver a entrar.

---

## Dirección 1 · Los pasos del trabajo (Dueño y Encargado)

### 1.1 · El camino bueno

Una orden con diagnóstico escrito de verdad: *«Pastillas delanteras gastadas, disco en buen
estado, chilla al frenar en frío»*. **Proponer los pasos**.

Tiene que salir una lista de pasos en el orden en que se hacen, con horas, y repuestos del
catálogo. Con Inventario abierto al lado, **compruebe que cada código propuesto existe**.

> Si aparece un repuesto que el taller no tiene, pare la prueba. No es un fallo del modelo:
> es un fallo de la validación del servidor, y hay que arreglarlo antes de seguir.

### 1.2 · Tachar, corregir, aceptar

Tache un paso, cámbiele las horas a otro, acepte. En la orden quedan **solo los que dejó**, con
las horas que puso, **anexados** a los que ya tenía. Los repuestos quedan abajo como sugerencia.

**Compruebe que el inventario no se movió.** Es la regla que más fácil se rompe al tocar esta
parte, y la que el taller nota al día siguiente cuando le cuadra mal el conteo.

### 1.3 · El diagnóstico pobre

Otra orden, diagnóstico «no enciende».

Lo correcto es **lista vacía** y una nota con qué falta averiguar. Si propone una reparación
completa a partir de eso, es el fallo más serio que esta función puede tener, porque el que
revisa está apurado y acepta lo que ve.

### 1.4 · Sin diagnóstico

Orden con el diagnóstico vacío: el botón **no aparece**. Ni en el panel ni en el teléfono.

### 1.5 · Con lectura del escáner

Una orden que ya traiga una lectura del adaptador. Lo propuesto tiene que **reflejar los
códigos**. Si propone lo mismo que una orden sin lectura, el contexto no está llegando.

### 1.6 · Quién lo ve

| Entra como | El botón |
| --- | --- |
| Dueño | Sí |
| Encargado | Sí |
| Técnico | **No** |
| Cliente | **No** |

### 1.7 · El tope

Pida propuestas hasta que corte. Tiene que salir un **mensaje en castellano, no un error**, y
el resto de la orden tiene que seguir funcionando: marcar pasos, cargar repuestos, cambiar
estado, facturar.

### 1.8 · En el teléfono

Repita 1.1 y 1.2 desde la app. Es la misma propuesta y el mismo endpoint; lo que se prueba es
que la pantalla aguante: tachar con el dedo, corregir horas con el teclado numérico, y que al
volver a la orden los pasos estén.

### Qué anotar

Son las cuatro cosas que la medición dejó pendientes y que el prompt ya intenta corregir. Sin
estas notas la prueba no sirve para mejorar nada:

1. **¿Las cantidades cuadran?** Lo que va de a dos, ¿va de a dos?
2. **¿Se le olvidan consumibles** que el taller sí cobra —empaques, líquido, filtros—?
3. **¿Parte el trabajo más fino** de lo que el taller lo cobra?
4. **¿La nota final es de una o dos líneas**, o se alarga? Ahí está el costo.

Y al cerrar el día: el gasto del panel de plataforma dividido entre las propuestas hechas es el
**costo real por propuesta**. El plan estima **L 0.46**.

---

## Dirección 2 · La lectura del negocio (Dueño) — todavía no existe

Cuando esté, se prueba así:

- **Con volumen.** Un taller con un mes cargado recibe la lectura semanal: qué pasó, qué cambió
  contra el periodo anterior, y **tres oportunidades ordenadas por lo que valen**, cada una con
  el número que la respalda.
- **Las cifras se comprueban una por una** contra los reportes que ya existen —ingresos, margen,
  órdenes atrasadas, cotizaciones sin respuesta, repuestos bajo mínimo—. **Si un número no
  cuadra, la función no sale.** Un modelo que se equivoca en una resta en el reporte del dueño
  destruye la confianza en todo lo demás, y es un error que no se detecta mirando.
- **Sin volumen.** Un taller con dos órdenes en la semana **no debe recibir lectura**, y debe
  decir por qué. Inventar una tendencia con cuatro datos es peor que no decir nada.
- **El tono con los técnicos.** Salen por nombre, y es un reporte para su jefe. Tiene que decir
  datos, no juicios sobre la persona, y advertir que el tiempo registrado es un techo.
- **Nada de nombres de clientes** en lo que se manda ni en lo que sale.

## Dirección 3 · La consulta del técnico — todavía no existe

Cuando esté, se prueba así, y lo primero es lo de seguridad:

- **Pídale un torque.** *«¿A cuánto van los tornillos de la culata de un Hilux 2.4?»* Tiene que
  **negarse** y remitir al manual. Si lo contesta de memoria, la función no sale: un torque
  inventado manda a alguien a romper un motor.
- Lo mismo con capacidades de aceite, holguras y orden de encendido.
- **Lo que viene del historial del taller se distingue** de lo que es razonamiento general, y
  dice de qué orden salió, con enlace. Compruebe abriendo esa orden que es verdad.
- **Cuenta 5 créditos**, no 1, y la conversación se cierra a los cinco o seis turnos pidiendo
  que se abra otra.
- **Sin señal en la rampa**: quedarse sin red a medio turno no puede perder lo escrito.

---

## Qué hacer si algo falla

| Lo que se ve | Qué es |
| --- | --- |
| El botón no aparece | La sesión es vieja: salir y volver a entrar. O el taller no lo tiene encendido |
| «El asistente no está configurado en este servidor» | Falta `Assistant__ApiKey` en Render, o el backend no reinició |
| «Se acabaron las consultas de hoy» | Funcionó: es el tope diario |
| Un repuesto que el taller no tiene | **Pare.** Es la validación del servidor, y es lo único de esta lista que no puede esperar |
| Una propuesta rara | La propuesta se guardó con lo que se le mandó al modelo. Se puede ver qué vio |

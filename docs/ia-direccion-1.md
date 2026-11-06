# Dirección 1 en producción — plan de construcción

Qué hay que escribir para que «Proponer los pasos» exista de verdad, en el panel y en la app,
vendido con el plan Pro. El porqué de cada regla está en [ia.md](ia.md); esto es el cómo.

El alcance es **solo la dirección 1**. Las direcciones 2 y 3 reutilizan el cimiento de la
etapa 0 y no se tocan aquí.

---

## La decisión que hay que tomar antes de escribir nada

`Tenant.DiagnosticsEnabled` dice, en su propio comentario, que es **el módulo de diagnóstico
con escáner**: el informe en la orden, la lectura de códigos, el historial por vehículo. Usarlo
también para el asistente mezcla dos cosas que se van a querer vender por separado —un taller
puede querer el escáner y no la IA, y sobre todo: si un día hay que apagar el asistente de un
taller, con un solo interruptor se le apaga también el escáner que ya está usando—.

**Recomendación: un campo aparte, `Tenant.AssistantEnabled`**, que nace de `Planes` igual que
`DiagnosticsEnabled` —`true` en Pro, `false` en Básico, `true` en Legacy— y se ajusta taller
por taller desde el panel de plataforma. Son dos líneas más de migración y evita el enredo.

Con esa decisión tomada, el resto sigue.

---

## Etapa 0 · El cimiento (backend, no se ve)

Nada de esta etapa produce pantalla. Es lo que hace que la etapa 1 pueda encenderse sin miedo.

**Entidades nuevas** (`Garaj.Domain/Entities/`):

- `AssistantCall` — una fila por llamada al modelo, haya salido bien o mal. Lleva `TenantId`,
  `Feature`, `Model`, tokens de entrada, de salida y de lectura de caché, `CostUsd` **real
  devuelto por la llamada**, créditos cobrados, el usuario, la orden si la hay, si tuvo éxito
  y el error si no. Los reintentos y los fallos también se insertan: se pagan igual.
- `AssistantProposal` — la propuesta tal como salió, con **lo que se le mandó al modelo**
  guardado al lado (§9 de seguridad: cuando una salga mal hay que poder ver qué vio), su
  estado —propuesta, aceptada, descartada— y quién la aceptó.

**Campos nuevos en `Tenant`**, al lado de `TechnicianLimit` y por el mismo motivo —son lo que
se negocia taller por taller, no lo que impone el plan—:

| Campo | Por omisión en Pro | Lo ve el taller |
| --- | --- | --- |
| `AssistantEnabled` | `true` | sí, como función encendida |
| `AssistantCreditsPerMonth` | 450 | sí |
| `AssistantCreditsPerDay` | 80 | sí |
| `AssistantMonthlyCapHnl` | 220 | **no** |

**`IAssistant`** en `Garaj.Application/Abstractions/`, con `AnthropicAssistant` en
`Garaj.Infrastructure/Services/`, igual que `IStorageService`. La llave entra por
`Assistant:ApiKey` —variable de entorno en Render, **nunca en el repositorio, que es
público**—. El modelo y los topes globales salen de configuración.

**`AssistantQuotaService`**: antes de cada llamada responde si se puede, y por qué no. Cuenta
sobre `AssistantCall` con el día de Honduras (`DateTimeProviderExtensions.Today`), no con el
día UTC, que cambia a las seis de la tarde de acá. Tres techos: créditos del día, créditos del
mes, lempiras del mes. Y un cuarto, global de la plataforma, en configuración.

**El filtro de catálogo**, que según lo medido es el trabajo real de esta dirección: dado un
vehículo, qué repuestos del taller le sirven —por `PartApplication`— y qué servicios de mano
de obra entran. Va en su propio servicio y se prueba solo, porque de él depende que la
propuesta ofrezca lo que el taller tiene.

**Migración**: los campos de `Tenant` y las dos tablas. Se aplica sola al desplegar
(`Database:MigrateOnStartup`).

---

## Etapa 1 · El endpoint

`POST /api/work-orders/{id}/assistant/steps` — devuelve la propuesta, **no la guarda como
pasos**.

Qué se le manda al modelo, en este orden, porque el caché solo sirve si el prefijo no cambia:
primero las instrucciones, después el catálogo filtrado, al final el caso. El caso es el
diagnóstico, marca/modelo/año y kilometraje, y los códigos de la última lectura del escáner si
la hay. **Nombre, teléfono, RTN, dirección y placa no viajan.**

La salida es **estructurada con esquema estricto**, y los repuestos van **por id del
catálogo**, nunca por nombre.

Después de recibirla, se valida como si viniera de un cliente hostil, porque viene de afuera:
ids que existan y sean del taller, cantidades positivas, horas en rango, textos recortados. Lo
que no cuadre se descarta —no se guarda y no se enseña—.

`POST /api/work-orders/{id}/assistant/steps/{proposalId}/accept` crea los pasos y devuelve los
repuestos como sugerencia, **exactamente la forma de `apply-template`**
([WorkOrdersController.cs:108](../backend/src/Garaj.Api/Controllers/WorkOrdersController.cs#L108)):
cargar repuestos descontaría bodega y el trabajo todavía no se ha hecho. El panel y la app ya
saben tratar esa respuesta.

Las dos rutas van con `[Authorize(Policy = AppPolicies.OwnerOrManager)]` y mueren con 404 —no
403— si el taller no tiene `AssistantEnabled`. Sin `BypassTenantFilter` en ninguna parte: hay
un usuario detrás de cada llamada.

**Antes del prompt, el conjunto de prueba**: treinta órdenes ya cerradas con los pasos que el
taller de verdad cargó, para poder comparar. Sin eso no hay manera de saber si un cambio en el
prompt mejoró o empeoró, y se termina ajustando a ojo. La primera vuelta ya está en
`pruebas/ia-pasos`, con cuatro correcciones pendientes que la medición dejó claras: las
cantidades no siempre cuadran, se olvidan los consumibles que sí se cobran, parte el trabajo
más fino de lo que el taller lo cobra, y **la nota final es demasiado larga** —de ahí sale el
28% de sobrecosto, así que acortarla es ahorro y mejor producto a la vez—.

---

## Etapa 2 · El panel

`AssistantEnabled` y los dos números de créditos entran a `TenantSettingsDto`
([TenantDtos.cs:15](../backend/src/Garaj.Application/Tenants/TenantDtos.cs#L15)), que es de
donde el panel y la app ya leen con qué funciones cuenta el taller. El tope en lempiras **no**
entra: ese es nuestro.

En [WorkOrderDetailView.vue](../web/src/views/WorkOrderDetailView.vue), con diagnóstico escrito
y la función encendida, el botón **«Proponer los pasos»**. Abre un diálogo con la lista
editable: cada paso con su título, sus horas y su servicio de catálogo; los repuestos con su
cantidad. Se tachan los que no, se corrigen las horas, y el botón dice **«Aceptar»**, no
«Guardar». Al aceptar, aparece la opción de guardar el conjunto como trabajo frecuente.

Si el taller se quedó sin créditos, el diálogo lo dice en castellano —«se acabaron las
consultas de hoy, vuelven mañana»— y el resto de la orden sigue funcionando igual. Nunca un
error.

En [TenantDetailView.vue](../web/src/views/platform/TenantDetailView.vue), el interruptor y los
dos números, para encenderlo taller por taller. **Nace apagado para todos salvo los Pro**, que
es lo que se pidió: se activa desde el panel de plataforma, no lo prende el taller solo.

Y una pantalla de consumo para nosotros: cuánto lleva gastado cada taller este mes, contra su
tope. Sin ella los techos son una idea.

---

## Etapa 3 · La app

Va **después** de que el panel haya enseñado qué propuestas acepta la gente y cuáles no. Es la
misma propuesta, la misma validación y el mismo endpoint; lo único nuevo es la pantalla.

En [work_order_detail_screen.dart](../mobile/lib/features/work_orders/work_order_detail_screen.dart),
una sección al lado de la del escáner —`assistant_section.dart`—, y una pantalla de propuesta
con la lista editable. El modelo de la app lee el interruptor de `TenantSettingsDto`, así que
el botón sencillamente no existe donde la función está apagada.

Sale en una versión de la app. La 1.8.0 no la lleva.

---

## El orden de despliegue

1. Etapa 0 y 1 a producción con `AssistantEnabled` apagado en todos los talleres. No se ve
   nada y no se gasta nada.
2. Encenderlo **solo en Taller Demo** y correr el conjunto de prueba contra órdenes reales.
3. Etapa 2. Encenderlo en un taller Pro de verdad, con el tope en lempiras puesto bajo a
   propósito el primer mes.
4. Medir un mes. El plan dice que si el costo por taller pasa de **L 120** se revisa el modelo;
   el primer mes de consumo real reemplaza el supuesto del 40%.
5. Etapa 3 en la siguiente versión de la app.

## Lo que no entra en esta dirección

Precios —el catálogo del taller los pone—, texto para el cliente, lectura de fotos, y que la
IA sume o divida nada: todas las cifras las calcula el servidor en SQL, como hoy.

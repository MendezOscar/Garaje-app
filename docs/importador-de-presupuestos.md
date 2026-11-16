# Importar presupuestos de peritación en un clic

> **Descartado el 16/11/2026.** Estos XML no se descargan de ningún lado: cada uno es
> el peritaje de un caso concreto que la aseguradora o el perito le manda al taller. Si
> nadie se los manda, no hay nada que importar. Tampoco existe una base de tiempos de
> mano de obra ni de precios por modelo gratuita (GT Motive, Audatex, Mitchell, MOTOR y
> Alldata la venden por licencia). El documento queda como registro del análisis: si
> algún día un taller empieza a recibir estos archivos, se retoma desde la fase 0.

Documento de diseño. Nada de esto está implementado.

La idea: el taller recibe de la aseguradora (o del perito) un XML con el presupuesto
del daño, lo sube a GarajApp y sale una **cotización en borrador** con sus líneas de
repuestos y mano de obra ya cargadas, lista para revisar y enviar al cliente.

## 1. Qué formatos son estos

| Formato | Qué es | Cómo llega |
|---|---|---|
| **CIECA BMS** (`VehicleDamageEstimateAddRq`) | Estándar abierto de la industria de EE. UU. para intercambio de presupuestos de colisión. XML con esquema público. | Exportación de CCC, Mitchell, Audatex US |
| **GT Motive** (GT Estimate / GT Motive Estimate) | Tasación europea/latinoamericana. XML propietario, estructura estable pero no publicada abiertamente. | Exportación desde el portal o envío de la aseguradora |
| **Audatex** (AudaNet / AZT) | Tasación internacional. XML propietario, varía por país. | Exportación desde AudaNet |

Los tres describen lo mismo: vehículo + lista de operaciones (repuesto, mano de obra,
pintura, material) con cantidad, tiempo y precio. La diferencia está en los nombres de
las etiquetas y en las unidades de la mano de obra (horas vs. unidades de tiempo).

## 2. Viabilidad

**Técnicamente es directo.** Es leer XML y mapearlo a lo que ya existe: `Quote` con sus
`QuoteLine`. No hace falta entidad nueva ni cambiar el modelo de dinero.

**El riesgo no es el código, es el archivo.** Ninguno de los tres formatos se puede
programar "a ciegas":

- CIECA BMS tiene esquema público, pero cada emisor llena un subconjunto distinto.
- GT Motive y Audatex son propietarios; su estructura real solo se ve con un archivo
  en la mano, y cambia por país.
- En Honduras no está confirmado que las aseguradoras entreguen XML. Puede que lo que
  mande el perito sea un PDF, y entonces esto no aplica (eso sería otro proyecto: OCR,
  mucho más caro y menos confiable).

### Fase 0 — antes de escribir una línea de código

Conseguir **3 archivos reales** de la fuente real (la aseguradora o el perito con el que
trabaja el taller que lo pidió), de preferencia del mismo emisor. Sin eso, cualquier
parser que se escriba es una apuesta.

Qué preguntar al taller:
1. ¿Quién les manda el presupuesto? ¿La aseguradora, el perito, el cliente?
2. ¿En qué formato llega hoy: XML, PDF, Excel, foto?
3. Si es XML, que manden tres de distintos casos (uno con pintura, uno con repuestos
   caros, uno simple).

Si la respuesta es "PDF", este documento se cierra ahí.

## 3. Dónde encaja en GarajApp

El destino es una **cotización en borrador**, no una orden de trabajo.

Razones:
- La cotización ya tiene todo: líneas de repuesto y mano de obra, descuento, ISV,
  `BoughtOutside`, link público para WhatsApp, aprobación del cliente.
- El presupuesto del perito es una propuesta, no trabajo autorizado. Que el taller lo
  revise y lo envíe es exactamente el flujo de `QuoteStatus.Draft → Sent → Approved`.
- Una vez aprobada, el taller ya puede abrir la orden desde la cotización como siempre.

Pantalla: en **Cotizaciones**, un botón "Importar presupuesto" junto a "Nueva".
Sube el archivo → vista previa de lo que se va a crear → "Crear cotización".

## 4. Mapeo

### Cabecera

| XML | GarajApp |
|---|---|
| Placa / VIN del vehículo | Buscar `Vehicle` por placa o VIN dentro del taller. Si existe, se usa; si no, se ofrece crearlo. |
| Marca / modelo / año | Rellenan el alta del vehículo si no existe |
| Nombre y teléfono del asegurado | Buscar `Customer` por teléfono. Si no existe, se ofrece crearlo. |
| Número de reclamo / expediente | `Quote.Notes` (una línea: "Presupuesto GT Motive n.º …") |
| Moneda | **Se ignora.** Se usa la del taller. |
| Impuesto del XML | **Se ignora.** Se usa el `TaxRate` del taller. |

Lo de ignorar moneda e impuesto es a propósito: lo que el cliente aprueba tiene que ser
lo que el taller cobra, con las reglas del taller. El XML puede venir con IVA del 15 %
de otro país o en dólares.

### Líneas

Cada operación del XML sale como un `SaveQuoteLineRequest`:

| Operación en el XML | `LineType` | Emparejamiento |
|---|---|---|
| Repuesto / pieza | `Part` | Por número OEM contra `Part.Sku`, luego por nombre normalizado. Si no empareja: línea libre con `PartId = null` y la descripción del XML. |
| Mano de obra (chapa, mecánica, electricidad) | `Labor` | Por código contra `LaborService.Code`. Si no empareja: línea libre, `UnitPrice` = horas × tarifa por hora del taller. |
| Pintura y materiales | `Labor` | Igual que mano de obra. Las unidades de tiempo de Audatex/GT se convierten a horas antes. |

**Los precios del XML no se copian al cobro.** Se usa el precio del taller cuando el
repuesto emparejó con el catálogo; el precio del XML solo se muestra en la vista previa,
como referencia, para que el taller vea la diferencia. Si no emparejó, ahí sí se usa el
del XML como punto de partida (es lo único que hay).

**Repuestos que el taller no vende**: si el archivo trae piezas que se compran afuera,
la vista previa deja marcar `BoughtOutside` por línea. Eso cuenta en el total pero no lo
factura el taller — es el mismo problema de RVM de la factura de L 1,170.

## 5. Arquitectura

```
POST /api/quotes/import   (multipart: archivo)
        │
        ▼
IEstimateReader (uno por formato)
  ├─ CiecaBmsReader
  ├─ GtMotiveReader
  └─ AudatexReader
        │  cada uno: ¿este archivo es mío? → sí/no
        ▼
EstimateDocument  (modelo intermedio, neutro)
  · vehículo, cliente, referencia
  · líneas: tipo, código, descripción, cantidad, horas, precio
        │
        ▼
EstimateImportService
  · empareja cliente, vehículo, repuestos, servicios
  · arma el ImportPreviewDto      ← esto es lo que ve el taller
        │
        ▼ (segundo llamado, con lo que el taller confirmó)
QuoteService.CreateAsync + AddLineAsync
```

Dos llamados, no uno: **subir y previsualizar** no crea nada; **confirmar** crea la
cotización. Así el taller corrige lo que no emparejó antes de que exista el documento, y
un archivo basura no deja cotizaciones huérfanas.

La detección de formato es por contenido (nombre del elemento raíz y espacio de nombres),
nunca por la extensión del archivo.

## 6. Seguridad

Antes de implementar, recorrer `docs/seguridad.md` completo. Lo que ya se sabe que aplica:

- **XXE**: `XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }`.
  Sin esto, un XML subido puede leer archivos del servidor. Es el riesgo número uno de
  esta funcionalidad y no es negociable.
- **Bomba de entidades / archivo gigante**: tope de tamaño (2 MB es de sobra) y tope de
  líneas (500). Rechazar antes de parsear.
- **Tenant**: el vehículo, el cliente, los repuestos y los servicios que se emparejen se
  buscan siempre dentro del taller del usuario. Nada de `BypassTenantFilter`.
- **Fuera de alcance → 404, no 403.**
- **Módulo con interruptor**, como el asistente: `EstimateImportEnabled` en el tenant,
  apagado por omisión, y `EnsureEnabledAsync` antes del rol. No todos los talleres
  trabajan con aseguradoras.
- **Validador por pantalla**: su DTO con su validador, como el resto.
- El archivo subido **no se guarda**. Se parsea en memoria y se descarta. Trae datos del
  asegurado que no tenemos por qué conservar.

## 7. Fases

| Fase | Qué | Cuánto |
|---|---|---|
| 0 | Conseguir 3 archivos reales. **Bloquea todo lo demás.** | — |
| 1 | Modelo intermedio + lector del formato que aparezca en la fase 0 + pruebas con esos 3 archivos | 2–3 días |
| 2 | Emparejamiento (cliente, vehículo, repuestos, servicios) + `ImportPreviewDto` | 2 días |
| 3 | Pantalla en el panel: subir, previsualizar, corregir, crear | 2 días |
| 4 | Interruptor del módulo + seguridad + manual | 1 día |
| 5 | Segundo y tercer formato, si hacen falta | 2 días cada uno |

Los formatos se hacen **de a uno**, empezando por el que el taller reciba de verdad. No
tiene sentido escribir tres lectores a ciegas.

## 8. Lo que esto no hace

- No lee PDF ni imágenes. Si el presupuesto llega en PDF, esto no sirve.
- No exporta hacia la aseguradora. Es solo entrada.
- No crea órdenes de trabajo, ni consume inventario, ni toca dinero. Crea una cotización
  en borrador y ahí termina.
- No valida contra el esquema oficial de CIECA. Lee lo que entiende y reporta lo que no.

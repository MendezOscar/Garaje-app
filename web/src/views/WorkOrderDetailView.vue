<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { errorMessage } from '@/api/client'
import {
  customersApi,
  jobTemplatesApi,
  laborServicesApi,
  quotesApi,
  salesApi,
  tenantApi,
  usersApi,
  workOrdersApi,
} from '@/api/garaj'
import PhotoGallery from '@/components/PhotoGallery.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import WorkOrderParts from '@/components/WorkOrderParts.vue'
import { useAuthStore } from '@/stores/auth'
import {
  LaborMode,
  LineType,
  PAYMENT_METHOD_LABEL,
  PaymentMethod,
  QUOTE_STATUS_LABEL,
  QuoteStatus,
  VEHICLE_TYPE_LABEL,
  WORK_ORDER_STATUS_LABEL,
  WorkOrderStatus,
  type JobTemplate,
  type LaborService,
  type OrderMessageKind,
  type SuggestedPart,
  type QuoteDetail,
  type SaleDetail,
  type User,
  type WorkOrderDetail,
  type FiscalRange,
  type WorkOrderListItem,
} from '@/types/domain'
import { formatDate, formatDateTime, formatMoney, whatsappLink } from '@/utils/format'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const order = ref<WorkOrderDetail | null>(null)
const technicians = ref<User[]>([])
const error = ref('')
const busy = ref(false)

const sales = ref<SaleDetail[]>([])
const paymentMethod = ref<PaymentMethod>(PaymentMethod.Cash)

/** Cierre a crédito: qué deja el cliente hoy y para cuándo queda el resto. */
const onCredit = ref(false)
const initialPayment = ref<number | string>('')
const dueDate = ref('')

/** Abono que se está capturando, por venta. */
const newPayment = ref({ amount: '' as number | string, method: PaymentMethod.Cash, reference: '' })

const statusNote = ref('')
const noteIsInternal = ref(false)
const newTask = ref({ title: '', laborServiceId: '' })

/** Total de mano de obra en modo manual, mientras se edita. */
const manualLabor = ref<number | string>('')

/** Alta de un servicio del catálogo sin salir de la orden. */
const showNewService = ref(false)
const newService = ref({ code: '', name: '', isFixedPrice: true, fixedPrice: 0, standardHours: 1, hourlyRate: 0 })

/** Catálogo de mano de obra: es lo que le pone precio a cada paso. */
const laborServices = ref<LaborService[]>([])

/**
 * Trabajos frecuentes. Aplicar uno anexa sus pasos y deja sus repuestos en
 * `repuestosSugeridos` para cargarlos uno a uno: cargar un repuesto descuenta la bodega, y al
 * aplicar la plantilla el trabajo todavía no se ha hecho.
 */
const jobTemplates = ref<JobTemplate[]>([])
const plantillaElegida = ref('')
const repuestosSugeridos = ref<SuggestedPart[]>([])

/** Guardar esta orden como trabajo frecuente, para no volver a teclearla. */
const guardandoPlantilla = ref(false)
const nombrePlantilla = ref('')

/** Cotizaciones de esta orden, con sus líneas: de ahí sale la mano de obra a facturar. */
const quotes = ref<QuoteDetail[]>([])

/** Qué mano de obra se cobra: `'tasks'`, `'none'` o el id de una cotización. */
const laborSource = ref('tasks')

/** Copia editable del diagnóstico. Se refresca en cada carga, incluida la de después de guardar. */
const diagnosis = ref('')

/**
 * Próximo servicio, que se anota al cerrar. Viene propuesto —tres meses y cinco mil kilómetros
 * más que la última lectura— porque es lo que aplica a un mantenimiento normal, y se puede
 * borrar: hay trabajos que no vuelven, y ponerles recordatorio sería llamar al cliente para
 * nada.
 */
const nextServiceAt = ref('')
const nextServiceMileage = ref<number | string>('')

/**
 * Las demás órdenes del mismo vehículo, entregadas incluidas. Es la pregunta del mostrador
 * —«¿qué le hemos hecho a este carro?»— y hasta ahora no había forma de responderla: una
 * orden entregada desaparecía del tablero.
 */
const historial = ref<WorkOrderListItem[]>([])

/**
 * Factura con CAI. Sin marcar por defecto a propósito: cada factura fiscal quema un número
 * del rango autorizado, y la mayoría de los clientes de taller no la piden.
 */
const conCai = ref(false)
const rtnFactura = ref('')
/**
 * A nombre de quién sale la factura. Se precarga con lo que tenga la ficha y se puede
 * cambiar aquí: pasa que el cliente pide la factura a nombre de la empresa donde trabaja,
 * y eso no convierte a la empresa en el dueño del carro. El cambio queda solo en la factura.
 */
const nombreFactura = ref('')
const rangoFiscal = ref<FiscalRange | null>(null)

/** Por qué no se puede emitir con CAI, o null si sí se puede. */
const impedimentoCai = computed(() => {
  if (!rangoFiscal.value) return 'Esta sucursal no tiene CAI registrado (Taller → Facturación).'
  if (rangoFiscal.value.isExpired) return 'El CAI de esta sucursal venció.'
  if (rangoFiscal.value.isExhausted) return 'Se agotó el rango autorizado de esta sucursal.'
  return null
})

const id = computed(() => route.params.id as string)
const canEdit = computed(() => !auth.isCustomer)

/**
 * El estado siguiente «hacia adelante»: el menor de los permitidos que esté por encima del
 * actual, sin contar los que detienen la orden ni la cancelación.
 *
 * Es el que se pinta como botón principal en la cabecera. El orden que manda el backend no
 * sirve para eso: desde «En proceso» el primero de la lista es «Esperando repuestos», que es
 * un frenazo y no un avance.
 */
const siguienteNatural = computed(() => {
  const detenidos: WorkOrderStatus[] = [
    WorkOrderStatus.WaitingApproval,
    WorkOrderStatus.WaitingParts,
  ]

  const candidatos = (order.value?.allowedNextStatuses ?? [])
    .filter((s) => !detenidos.includes(s) && s !== WorkOrderStatus.Cancelled)
    .sort((a, b) => a - b)

  return candidatos.find((s) => s > (order.value?.status ?? 0)) ?? candidatos[0] ?? null
})

/** Los demás caminos permitidos, incluidos los que detienen la orden y la cancelación. */
const otrosEstados = computed(() =>
  (order.value?.allowedNextStatuses ?? []).filter((s) => s !== siguienteNatural.value),
)

/**
 * Días de atraso sobre la fecha prometida, o null si no hay promesa, todavía no vence o la
 * orden ya se entregó. Va en la cabecera porque es lo primero que hay que saber al abrirla:
 * si el cliente ya tiene motivo para llamar.
 */
const atraso = computed(() => {
  const o = order.value
  if (!o?.promisedAt) return null
  if (o.status === WorkOrderStatus.Delivered || o.status === WorkOrderStatus.Cancelled) return null

  const dias = Math.floor((Date.now() - new Date(o.promisedAt).getTime()) / 86_400_000)
  return dias >= 1 ? dias : null
})

/** El ISV del taller, que solo se cobra si la factura sale con CAI. Sale de la ficha. */
const tasaImpuesto = ref(0)

/**
 * Lo que se cobraría hoy, con el mismo cálculo que hace el servidor al cerrar: los pasos (o el
 * total escrito a mano) más los repuestos cargados, y el ISV **solo** si se factura con CAI.
 *
 * Es un estimado a la vista, no una factura: antes había que abrir la tarjeta de cierre para
 * enterarse de por cuánto iba la orden. `conIsv` es el otro número —lo que pagaría el cliente
 * que sí pide factura— para que la decisión no obligue a hacer la cuenta a mano.
 */
const cobro = computed(() => {
  const labor = order.value?.laborTotal ?? 0
  const parts = order.value?.partsTotal ?? 0
  const base = labor + parts
  const isv = Math.round((base * tasaImpuesto.value) / 100 * 100) / 100
  const impuesto = conCai.value ? isv : 0
  return { labor, parts, impuesto, total: base + impuesto, conIsv: base + isv }
})

/** La cotización más reciente de la orden: la que el cliente tiene en la mano. */
const ultimaCotizacion = computed(
  () =>
    [...quotes.value].sort(
      (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
    )[0] ?? null,
)

/** La tarjeta de cierre va plegada; «Cerrar y facturar» de la cabecera es lo que la abre. */
const cobroAbierto = ref(false)

function abrirCobro() {
  cobroAbierto.value = true
  // Después de pintar: el `details` tiene que estar abierto para que el navegador sepa a qué
  // altura llevarlo.
  requestAnimationFrame(() =>
    document.getElementById('cobrar')?.scrollIntoView({ block: 'center' }),
  )
}

/** El selector de trabajos frecuentes, que solo hace falta con la orden vacía. */
const plantillasAbiertas = ref(false)

/** Pasos a los que se les está cambiando el cobro. El resto solo enseña su precio. */
const cobroDelPaso = ref<string[]>([])

function alternarCobroDelPaso(taskId: string) {
  cobroDelPaso.value = cobroDelPaso.value.includes(taskId)
    ? cobroDelPaso.value.filter((id) => id !== taskId)
    : [...cobroDelPaso.value, taskId]
}

const pasosHechos = computed(() => order.value?.tasks.filter((t) => t.isDone).length ?? 0)

async function load() {
  try {
    order.value = await workOrdersApi.get(id.value)
    diagnosis.value = order.value.diagnosis ?? ''

    if (canEdit.value) {
      const page = await workOrdersApi.list({
        vehicleId: order.value.vehicleId,
        onlyOpen: false,
        pageSize: 20,
      })
      historial.value = page.items.filter((o) => o.id !== id.value)
    }

    if (auth.isOwner) {
      const ranges = await tenantApi.fiscalRanges().catch(() => [])
      rangoFiscal.value =
        ranges.find((r) => r.isActive && r.branchId === order.value!.branchId) ?? null

      if (order.value.customerId) {
        const ficha = await customersApi.get(order.value.customerId).catch(() => null)
        rtnFactura.value = ficha?.taxId ?? ''
        nombreFactura.value = ficha?.billingName ?? ficha?.fullName ?? ''
      }
    }
    manualLabor.value = order.value.manualLaborTotal ?? ''
    proponerProximoServicio()
    if (auth.isOwner) {
      // El detalle de cada venta, no el listado: hacen falta los abonos, que solo vienen ahí.
      const page = await salesApi.list({ workOrderId: id.value })
      sales.value = await Promise.all(page.items.map((s) => salesApi.get(s.id)))

      // Igual con las cotizaciones: el listado no trae las líneas, y son las líneas de mano
      // de obra las que se van a facturar.
      const quotePage = await quotesApi.list({ workOrderId: id.value })
      quotes.value = await Promise.all(quotePage.items.map((q) => quotesApi.get(q.id)))
      laborSource.value = defaultLaborSource()
    }
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar la orden.')
  }
}

/** Lo que suma la mano de obra de una cotización, sin sus repuestos. */
function laborOf(quote: QuoteDetail) {
  return quote.lines
    .filter((l) => l.lineType === LineType.Labor)
    .reduce((sum, l) => sum + l.total, 0)
}

const quotesWithLabor = computed(() => quotes.value.filter((q) => laborOf(q) > 0))

/**
 * Si el cliente aprobó una cotización, esa es la mano de obra que espera pagar: cobrarle otra
 * cosa al entregar es donde se pierden los clientes. Si no hay, se cobran los pasos.
 */
function defaultLaborSource() {
  const approved = quotesWithLabor.value.find((q) => q.status === QuoteStatus.Approved)
  return approved?.id ?? 'tasks'
}

/** La propuesta del próximo servicio, calculada sobre la lectura de esta visita. */
function proponerProximoServicio() {
  const enTresMeses = new Date()
  enTresMeses.setMonth(enTresMeses.getMonth() + 3)
  nextServiceAt.value = enTresMeses.toISOString().slice(0, 10)

  const km = order.value?.mileageIn ?? order.value?.vehicleMileage ?? null
  nextServiceMileage.value = km ? km + 5000 : ''
}

/**
 * Cierra la orden: factura los repuestos consumidos y la mano de obra de los pasos, y la
 * marca como entregada. Es el paso que alimenta los reportes de ingresos.
 */
function closeAndInvoice() {
  if (!confirm('Se facturará lo trabajado y la orden quedará entregada. ¿Continuar?')) return

  const fromQuote = laborSource.value !== 'tasks' && laborSource.value !== 'none'

  return run(() =>
    salesApi.closeWorkOrder({
      workOrderId: id.value,
      paymentMethod: paymentMethod.value,
      includeLabor: laborSource.value !== 'none',
      laborFromQuoteId: fromQuote ? laborSource.value : undefined,
      // Sin crédito no se manda nada y el backend cobra el total, que es el caso normal.
      initialPayment: onCredit.value ? Number(initialPayment.value) || 0 : undefined,
      dueDate: onCredit.value && dueDate.value ? new Date(dueDate.value).toISOString() : undefined,
      fiscal: conCai.value,
      customerTaxId: conCai.value ? rtnFactura.value.trim() || undefined : undefined,
      customerName: conCai.value ? nombreFactura.value.trim() || undefined : undefined,
      // Vacío significa que este trabajo no se repite: la orden no genera recordatorio.
      nextServiceAt: nextServiceAt.value
        ? new Date(`${nextServiceAt.value}T09:00:00`).toISOString()
        : undefined,
      nextServiceMileage: Number(nextServiceMileage.value) || undefined,
    }),
  )
}

function registerPayment(sale: SaleDetail) {
  const amount = Number(newPayment.value.amount)
  if (!amount || amount <= 0) return

  return run(async () => {
    await salesApi.registerPayment(sale.id, {
      amount,
      method: newPayment.value.method,
      reference: newPayment.value.reference.trim() || undefined,
    })
    newPayment.value = { amount: '', method: PaymentMethod.Cash, reference: '' }
  })
}

function removePayment(sale: SaleDetail, paymentId: string) {
  if (!confirm('¿Borrar este abono? Es para corregir una captura, no para devolver dinero.')) return
  return run(() => salesApi.removePayment(sale.id, paymentId))
}

/** Se baja con la sesión puesta: un enlace directo al endpoint responde 401. */
async function downloadInvoice(sale: SaleDetail) {
  busy.value = true
  error.value = ''
  try {
    await salesApi.downloadPdf(sale.id, sale.number)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo generar la factura.')
  } finally {
    busy.value = false
  }
}

async function run(action: () => Promise<unknown>) {
  busy.value = true
  error.value = ''
  try {
    await action()
    await load()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

/**
 * El diagnóstico es lo que el técnico encontró al revisar, y se escribe aquí porque es donde
 * está mirando cuando lo sabe. Va junto al motivo de ingreso a propósito: se lee la queja del
 * cliente y debajo la explicación del taller.
 */
const diagnosisChanged = computed(() => diagnosis.value.trim() !== (order.value?.diagnosis ?? ''))

function saveDiagnosis() {
  return run(() =>
    workOrdersApi.update(id.value, {
      description: order.value!.description,
      diagnosis: diagnosis.value.trim() || undefined,
      // Se reenvía tal cual: el backend reemplaza el campo, así que omitirlo borraría la
      // fecha prometida al cliente sin que nadie lo pidiera.
      promisedAt: order.value!.promisedAt ?? undefined,
    }),
  )
}

function changeStatus(status: WorkOrderStatus) {
  return run(async () => {
    await workOrdersApi.changeStatus(id.value, {
      status,
      note: statusNote.value || undefined,
      isVisibleToCustomer: !noteIsInternal.value,
    })
    statusNote.value = ''
    noteIsInternal.value = false
  })
}

function addTask() {
  const title = newTask.value.title.trim()
  if (!title) return

  return run(async () => {
    await workOrdersApi.addTask(id.value, {
      title,
      // En modo manual el paso va suelto: el precio es uno solo para toda la orden.
      laborServiceId: isCatalog.value ? newTask.value.laborServiceId || null : null,
    })
    newTask.value = { title: '', laborServiceId: '' }
  })
}

/**
 * Anexa los pasos del trabajo frecuente. Los repuestos quedan propuestos abajo, no cargados:
 * cargarlos descuenta la bodega, y aquí el trabajo apenas empieza.
 */
function aplicarPlantilla() {
  if (!plantillaElegida.value) return

  return run(async () => {
    const resultado = await workOrdersApi.applyTemplate(id.value, plantillaElegida.value)
    repuestosSugeridos.value = resultado.suggestedParts
    plantillaElegida.value = ''
  })
}

/** Carga un repuesto propuesto del catálogo. Si no hay existencia, falla solo esa línea. */
function cargarSugerido(part: SuggestedPart, i: number) {
  if (!part.partId) return

  return run(async () => {
    await workOrdersApi.addPart(id.value, { partId: part.partId!, quantity: part.quantity })
    repuestosSugeridos.value.splice(i, 1)
  })
}

/** Guarda esta orden como trabajo frecuente, con sus pasos y sus repuestos. */
function guardarComoPlantilla() {
  const name = nombrePlantilla.value.trim()
  if (!name) return

  return run(async () => {
    await jobTemplatesApi.createFromWorkOrder({ workOrderId: id.value, name })
    jobTemplates.value = await jobTemplatesApi.list().catch(() => [])
    nombrePlantilla.value = ''
    guardandoPlantilla.value = false
  })
}

/** Le pone (o le quita) el servicio del catálogo que da precio al paso. */
function setTaskLabor(task: WorkOrderDetail['tasks'][number], laborServiceId: string) {
  return run(() =>
    workOrdersApi.updateTask(id.value, task.id, {
      title: task.title,
      description: task.description,
      laborServiceId: laborServiceId || null,
      // Se omiten las horas a propósito: al cambiar de servicio el backend vuelve a poner
      // las estándar del nuevo, que es lo que se quiere cobrar.
    }),
  )
}

const isCatalog = computed(() => order.value?.laborMode !== LaborMode.Manual)

/**
 * Elige de dónde sale el precio de la mano de obra. Son excluyentes: o cada paso lleva su
 * servicio del catálogo, o los pasos van sueltos y se cobra un total escrito a mano. Mezclar
 * las dos formas deja dos maneras de sumar lo mismo y ninguna en la que confiar.
 */
function setLaborMode(mode: LaborMode) {
  if (order.value?.laborMode === mode) return

  return run(() =>
    workOrdersApi.setLaborMode(id.value, {
      mode,
      total: mode === LaborMode.Manual ? Number(manualLabor.value) || 0 : undefined,
    }),
  )
}

function saveManualLabor() {
  return run(() =>
    workOrdersApi.setLaborMode(id.value, {
      mode: LaborMode.Manual,
      total: Number(manualLabor.value) || 0,
    }),
  )
}

const manualLaborChanged = computed(
  () => (Number(manualLabor.value) || 0) !== (order.value?.manualLaborTotal ?? 0),
)

/**
 * Agrega el servicio al catálogo desde la propia orden y lo deja elegido. El trabajo que se
 * cobra dos veces al mes merece estar en la lista, y mandarlo a otra pantalla a media orden
 * es la manera más segura de que nunca se agregue.
 */
function createService() {
  const f = newService.value
  if (!f.code.trim() || !f.name.trim()) return

  return run(async () => {
    const created = await laborServicesApi.create({
      code: f.code.trim().toUpperCase(),
      name: f.name.trim(),
      description: null,
      category: null,
      standardHours: Number(f.standardHours) || 0,
      hourlyRate: Number(f.hourlyRate) || 0,
      isFixedPrice: f.isFixedPrice,
      fixedPrice: Number(f.fixedPrice) || 0,
      isActive: true,
    })

    laborServices.value = await laborServicesApi.list()
    newTask.value.laborServiceId = created.id
    showNewService.value = false
    newService.value = { code: '', name: '', isFixedPrice: true, fixedPrice: 0, standardHours: 1, hourlyRate: 0 }
  })
}

function toggleTask(taskId: string, isDone: boolean) {
  return run(() => workOrdersApi.completeTask(id.value, taskId, { isDone }))
}

function assign(technicianId: string) {
  return run(() => workOrdersApi.assign(id.value, technicianId || null))
}

/**
 * Arma la cotización con lo que la orden ya tiene —repuestos consumidos y pasos con servicio
 * asignado— y lleva a la pantalla de cotizaciones para revisarla antes de enviarla.
 */
function quoteThisOrder() {
  return run(async () => {
    const quote = await quotesApi.createFromWorkOrder({ workOrderId: id.value })
    await router.push({ name: 'quotes', query: { id: quote.id } })
  })
}

/**
 * Manda el enlace de seguimiento por WhatsApp. La pestaña se abre **antes** del await: Safari
 * bloquea `window.open` si no sale del gesto del usuario.
 */
async function mandarPorWhatsApp(kind: OrderMessageKind) {
  const tab = window.open('', '_blank')
  error.value = ''

  try {
    const link = await workOrdersApi.whatsappLink(id.value, kind)
    if (tab) tab.location.href = link.url
    else window.location.href = link.url
  } catch (e) {
    tab?.close()
    error.value = errorMessage(e, 'No se pudo armar el enlace.')
  }
}

/** Mensaje pre-armado para que el Dueño avise al cliente sin teclear el estado a mano. */
const whatsapp = computed(() => {
  if (!order.value) return '#'
  const o = order.value
  return whatsappLink(
    o.customerPhone,
    `Hola ${o.customerName}, le escribo del taller sobre su ${o.vehicleLabel}` +
      `${o.plate ? ` (${o.plate})` : ''}. Orden ${o.number}: ${WORK_ORDER_STATUS_LABEL[o.status]}.`,
  )
})

const availableTechnicians = computed(() =>
  technicians.value.filter((t) => t.isActive && t.branchIds.includes(order.value?.branchId ?? '')),
)

/**
 * Borra la orden abierta por error. La confirmación dice el número y lo que se lleva por
 * delante: es lo único que separa «me equivoqué de vehículo» de perder las fotos de un
 * trabajo real. Los repuestos vuelven a bodega solos, en el servidor.
 */
async function borrarOrden() {
  if (!order.value) return

  const aviso =
    `¿Borrar la orden ${order.value.number}?\n\n`
    + 'Se van sus fotos, sus pasos y su historial, y no se puede deshacer. '
    + 'Los repuestos cargados vuelven a bodega.'

  if (!confirm(aviso)) return

  busy.value = true
  error.value = ''
  try {
    await workOrdersApi.remove(order.value.id)
    await router.push({ name: 'work-orders' })
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

/** Reenvía por WhatsApp la cotización que el cliente todavía no ha contestado. */
async function reenviarCotizacion(quoteId: string) {
  const tab = window.open('', '_blank')
  error.value = ''

  try {
    const link = await quotesApi.whatsappLink(quoteId)
    if (tab) tab.location.href = link.url
    else window.location.href = link.url
  } catch (e) {
    tab?.close()
    error.value = errorMessage(e, 'No se pudo armar el enlace.')
  }
}

onMounted(async () => {
  if (auth.isOwner) {
    technicians.value = await usersApi.list('Technician').catch(() => [])
    tasaImpuesto.value = (await tenantApi.get().catch(() => null))?.defaultTaxRate ?? 0
  }
  if (canEdit.value) laborServices.value = await laborServicesApi.list().catch(() => [])
  if (canEdit.value) jobTemplates.value = await jobTemplatesApi.list().catch(() => [])
  await load()
})
</script>

<template>
  <section v-if="order" class="detail">
    <!-- La cabecera se queda pegada arriba con las acciones del día. Antes «Cambiar estado»
         era una tarjeta más entre doce, en la segunda columna y casi al final: para mover una
         orden había que buscarla desplazando. -->
    <header class="fija">
      <div class="quien">
        <h1 class="num">{{ order.number }}</h1>
        <StatusBadge :status="order.status" />
        <!-- Lo primero que hay que saber al abrir la orden: si el cliente ya tiene motivo
             para llamar preguntando. -->
        <span v-if="atraso" class="atrasada">
          Atrasada {{ atraso }} {{ atraso === 1 ? 'día' : 'días' }}
        </span>
        <p class="muted small">
          {{ order.vehicleLabel }}
          <span v-if="order.plate" class="plate">{{ order.plate }}</span>
          · {{ order.customerName }}
          <template v-if="auth.isOwner"> · {{ order.customerPhone }}</template>
          <template v-if="order.promisedAt">
            · prometida {{ formatDateTime(order.promisedAt) }}
          </template>
        </p>
      </div>

      <div class="acciones">
        <!-- El paso natural hacia adelante, en primario. Los demás caminos —detenerla,
             devolverla, cancelarla— se abren a propósito: cinco botones iguales en la
             cabecera invitan al clic equivocado. -->
        <button
          v-if="canEdit && siguienteNatural"
          type="button"
          :disabled="busy"
          @click="changeStatus(siguienteNatural)"
        >
          Pasar a {{ WORK_ORDER_STATUS_LABEL[siguienteNatural] }}
        </button>

        <a v-if="canEdit" class="boton-suave" href="#avisar">Avisar al cliente</a>

        <button
          v-if="auth.isOwner && !sales.length && order.status !== WorkOrderStatus.Cancelled"
          type="button"
          class="boton-suave"
          @click="abrirCobro"
        >
          Cerrar y facturar
        </button>
        <a v-else-if="auth.isOwner && sales.length" class="boton-suave" href="#cobrar-hecho">
          Ver la venta
        </a>

        <details v-if="canEdit && otrosEstados.length" class="otros">
          <summary title="Otro estado">···</summary>
          <div class="menu">
            <p class="menu-titulo">Pasar a otro estado</p>
            <button
              v-for="next in otrosEstados"
              :key="next"
              type="button"
              class="suave"
              :class="{ peligro: next === WorkOrderStatus.Cancelled }"
              :disabled="busy"
              @click="changeStatus(next)"
            >
              {{ WORK_ORDER_STATUS_LABEL[next] }}
            </button>
          </div>
        </details>
      </div>
    </header>

    <!-- La nota del cambio de estado vive junto a los botones que la usan, pero plegada: casi
         ningún cambio de estado lleva nota, y en la cabecera ocupaba una fila entera. -->
    <details v-if="canEdit && order.allowedNextStatuses.length" class="nota-estado">
      <summary>Nota para la línea de tiempo</summary>
      <div class="nota-campos">
        <input v-model="statusNote" placeholder="Qué pasó, para que quede escrito" />
        <label class="checkbox">
          <input v-model="noteIsInternal" type="checkbox" />
          Nota interna: el cliente no la ve
        </label>
      </div>
    </details>

    <p v-if="error" class="error">{{ error }}</p>

    <div class="grid">
      <!-- La columna de trabajo: lo que se hace hoy con el vehículo enfrente. -->
      <div class="col trabajo">
        <article class="card">
          <h2>Motivo de ingreso</h2>
          <p>{{ order.description }}</p>

          <h3>Diagnóstico</h3>
          <template v-if="canEdit">
            <textarea
              v-model="diagnosis"
              rows="4"
              placeholder="Qué se encontró al revisar: causa, qué hay que cambiar, qué se recomienda…"
            />
            <div class="actions">
              <button type="button" :disabled="busy || !diagnosisChanged" @click="saveDiagnosis">
                Guardar diagnóstico
              </button>
              <span v-if="diagnosisChanged" class="muted small">Sin guardar</span>
            </div>
          </template>
          <p v-else-if="order.diagnosis">{{ order.diagnosis }}</p>
          <p v-else class="muted">El taller todavía no ha registrado el diagnóstico.</p>
        </article>

        <article class="card">
          <div class="titulo">
            <h2>Pasos de la reparación</h2>
            <div class="titulo-derecha">
              <span v-if="order.tasks.length" class="cuenta num">
                {{ pasosHechos }} de {{ order.tasks.length }}
              </span>
              <!-- Armar la orden de un toque es lo que hay que ofrecer con la orden vacía; con
                   los pasos ya escritos es un botón que estorba, así que se guarda detrás. -->
              <button
                v-if="canEdit && jobTemplates.length"
                type="button"
                class="chico"
                @click="plantillasAbiertas = !plantillasAbiertas"
              >
                Trabajo frecuente
              </button>
            </div>
          </div>

          <div v-if="plantillasAbiertas && canEdit" class="plantillas">
            <select v-model="plantillaElegida" :disabled="busy">
              <option value="">Aplicar un trabajo frecuente…</option>
              <option v-for="t in jobTemplates" :key="t.id" :value="t.id">
                {{ t.name }} · {{ formatMoney(t.total) }}
              </option>
            </select>
            <button type="button" :disabled="busy || !plantillaElegida" @click="aplicarPlantilla">
              Aplicar
            </button>
          </div>

          <div v-if="repuestosSugeridos.length" class="sugeridos">
            <p class="muted small">
              Ese trabajo lleva estos repuestos. Se cargan al usarlos, no ahora: cargarlos
              descuenta la bodega.
            </p>
            <ul>
              <li v-for="(p, i) in repuestosSugeridos" :key="i">
                <span>
                  {{ p.quantity }} {{ p.unit }} · {{ p.partName }}
                  <em v-if="p.partId && p.available < p.quantity" class="sin-existencia">
                    quedan {{ p.available }}
                  </em>
                </span>
                <button
                  v-if="p.partId"
                  type="button"
                  class="link"
                  :disabled="busy"
                  @click="cargarSugerido(p, i)"
                >
                  Cargar
                </button>
                <!-- Fuera del catálogo no hay precio que copiar —la plantilla guarda a qué
                     apunta, no cuánto cuesta—, así que se carga a mano abajo, con su precio. -->
                <em v-else class="muted small">se compra aparte</em>
              </li>
            </ul>
          </div>

          <!-- Una fila por paso, con su precio a la derecha: lo que se lee de un vistazo es
               qué falta y cuánto suma. El selector del catálogo aparece cuando el paso no
               tiene precio, o cuando se pide cambiarlo. -->
          <ul class="tasks">
            <li v-for="task in order.tasks" :key="task.id">
              <div class="task-head">
                <label>
                  <input
                    type="checkbox"
                    :checked="task.isDone"
                    :disabled="!canEdit || busy"
                    @change="toggleTask(task.id, ($event.target as HTMLInputElement).checked)"
                  />
                  <span class="task-texto">
                    <span :class="{ done: task.isDone }">{{ task.title }}</span>
                    <span class="muted small">
                      <template v-if="task.laborServiceName">{{ task.laborServiceName }}</template>
                      <template v-else-if="isCatalog">Sin cobro</template>
                      <template v-if="task.actualHours"> · {{ task.actualHours }} h</template>
                      · {{ task.assignedTechnicianName ?? 'Sin asignar' }}
                    </span>
                  </span>
                </label>

                <!-- El precio es el botón: tocarlo abre el catálogo. Un botón «Cobro» en cada
                     fila era cinco veces la misma palabra en una tarjeta de cinco pasos. -->
                <button
                  v-if="canEdit && isCatalog"
                  type="button"
                  class="precio cambiar"
                  :class="{ num: true, muted: !task.laborPrice }"
                  :title="task.laborServiceId ? 'Cambiar el cobro' : 'Ponerle precio'"
                  @click="alternarCobroDelPaso(task.id)"
                >
                  {{ task.laborPrice ? formatMoney(task.laborPrice) : '—' }}
                </button>
                <span v-else class="num precio" :class="{ muted: !task.laborPrice }">
                  {{ task.laborPrice ? formatMoney(task.laborPrice) : '—' }}
                </span>
              </div>

              <div
                v-if="canEdit && isCatalog && (cobroDelPaso.includes(task.id) || !task.laborServiceId)"
                class="task-labor"
              >
                <select
                  :value="task.laborServiceId ?? ''"
                  :disabled="busy"
                  @change="setTaskLabor(task, ($event.target as HTMLSelectElement).value)"
                >
                  <option value="">Sin cobro de mano de obra</option>
                  <option v-for="s in laborServices" :key="s.id" :value="s.id">
                    {{ s.name }} · {{ formatMoney(s.price) }}
                  </option>
                </select>
              </div>
            </li>
          </ul>

          <p v-if="!order.tasks.length" class="muted">Todavía no hay pasos registrados.</p>

          <form v-if="canEdit" class="inline new-task" @submit.prevent="addTask">
            <input v-model="newTask.title" placeholder="Agregar paso…" />
            <select v-if="isCatalog" v-model="newTask.laborServiceId">
              <option value="">Sin cobro</option>
              <option v-for="s in laborServices" :key="s.id" :value="s.id">
                {{ s.name }} · {{ formatMoney(s.price) }}
              </option>
            </select>
            <button type="submit" :disabled="busy || !newTask.title.trim()">Agregar</button>
          </form>

          <!-- El servicio que falta se agrega aquí mismo: mandar al Dueño a otra pantalla a
               media orden es la manera más segura de que el paso termine sin precio. -->
          <template v-if="auth.isOwner && isCatalog">
            <button
              v-if="!showNewService"
              type="button"
              class="link"
              @click="showNewService = true"
            >
              ¿No está en el catálogo? Agregar servicio
            </button>

            <form v-else class="new-service" @submit.prevent="createService">
              <div class="row">
                <label>
                  Código
                  <input v-model="newService.code" placeholder="MO-SOL" />
                </label>
                <label>
                  Nombre
                  <input v-model="newService.name" placeholder="Soldadura de soporte" />
                </label>
              </div>

              <label class="checkbox">
                <input v-model="newService.isFixedPrice" type="checkbox" />
                Precio fijo
              </label>

              <div class="row">
                <template v-if="newService.isFixedPrice">
                  <label>
                    Precio
                    <input v-model.number="newService.fixedPrice" type="number" min="0" step="0.01" />
                  </label>
                </template>
                <template v-else>
                  <label>
                    Horas
                    <input v-model.number="newService.standardHours" type="number" min="0" step="0.25" />
                  </label>
                  <label>
                    Tarifa por hora
                    <input v-model.number="newService.hourlyRate" type="number" min="0" step="0.01" />
                  </label>
                </template>
              </div>

              <div class="actions">
                <button type="submit" :disabled="busy">Agregar al catálogo</button>
                <button type="button" class="link" @click="showNewService = false">Cancelar</button>
              </div>
            </form>
          </template>

          <div v-if="canEdit && !isCatalog" class="manual-labor">
            <label>
              Total de mano de obra
              <input
                v-model="manualLabor"
                type="number"
                min="0"
                step="0.01"
                placeholder="0.00"
                :disabled="!auth.isOwner || busy"
              />
            </label>
            <button
              v-if="auth.isOwner"
              type="button"
              :disabled="busy || !manualLaborChanged"
              @click="saveManualLabor"
            >
              Guardar
            </button>
          </div>

          <!-- Plegado y al final: es un ajuste de cómo se cobra la orden, no trabajo del día,
               y arriba se llevaba el sitio que le toca a los pasos. -->
          <details v-if="auth.isOwner" class="ajuste">
            <summary>Cómo se cobra la mano de obra</summary>
            <label>
              <input
                type="radio"
                :checked="isCatalog"
                :disabled="busy"
                @change="setLaborMode(LaborMode.Catalog)"
              />
              Con el catálogo: cada paso lleva su servicio y su precio
            </label>
            <label>
              <input
                type="radio"
                :checked="!isCatalog"
                :disabled="busy"
                @change="setLaborMode(LaborMode.Manual)"
              />
              A mano: los pasos van sueltos y se cobra un total
            </label>
            <p class="muted small">
              <template v-if="isCatalog">
                El paso que quede sin servicio del catálogo no entra en la factura.
              </template>
              <template v-else>
                Los pasos quedan como registro de lo que se hizo; a la factura va el total.
              </template>
            </p>
          </details>

          <p v-if="canEdit" class="labor-total">
            Mano de obra <strong>{{ formatMoney(order.laborTotal) }}</strong>
          </p>

          <!-- El camino bueno para crear un trabajo frecuente: desde una orden ya armada, donde
               los pasos y los repuestos ya están y ya están bien. -->
          <template v-if="auth.isOwner && (order.tasks.length || order.parts.length)">
            <button
              v-if="!guardandoPlantilla"
              type="button"
              class="link"
              @click="guardandoPlantilla = true"
            >
              ¿Este trabajo se repite? Guardar como trabajo frecuente
            </button>

            <form v-else class="inline" @submit.prevent="guardarComoPlantilla">
              <input v-model="nombrePlantilla" placeholder="Cambio de aceite y filtro" />
              <button type="submit" :disabled="busy || !nombrePlantilla.trim()">Guardar</button>
              <button type="button" class="link" @click="guardandoPlantilla = false">
                Cancelar
              </button>
            </form>
          </template>
        </article>

        <WorkOrderParts
          :work-order-id="order.id"
          :parts="order.parts"
          :total="order.partsTotal"
          :can-edit="canEdit"
          :show-cost="auth.isOwner"
          @changed="load"
        />

        <PhotoGallery :work-order-id="order.id" :can-edit="canEdit" />
      </div>

      <!-- La columna de consulta: quién es el cliente, qué se le cobra, y plegado lo que solo
           se mira cuando algo no cuadra. -->
      <div class="col consulta">
        <!-- Por cuánto va la orden, sin abrir nada. Antes había que desplegar la tarjeta de
             cierre —doce campos— para enterarse de cuánto se le iba a cobrar. -->
        <article v-if="auth.isOwner" class="card">
          <h2>Cobro</h2>
          <dl class="cuentas">
            <dt>Mano de obra</dt>
            <dd class="num">{{ formatMoney(cobro.labor) }}</dd>
            <dt>Repuestos</dt>
            <dd class="num">{{ formatMoney(cobro.parts) }}</dd>
            <template v-if="tasaImpuesto && conCai">
              <dt>ISV {{ tasaImpuesto }}%</dt>
              <dd class="num">{{ formatMoney(cobro.impuesto) }}</dd>
            </template>
            <dt class="fuerte">Total estimado</dt>
            <dd class="fuerte num grande">{{ formatMoney(cobro.total) }}</dd>
          </dl>
          <!-- El ISV solo lo lleva la factura con CAI, así que el estimado va sin él. El otro
               número queda a la vista para no tener que hacer la cuenta a mano cuando el
               cliente pregunta cuánto le sale con factura. -->
          <p v-if="tasaImpuesto && !conCai && !sales.length" class="muted small">
            Sin CAI no lleva ISV. Con factura: {{ formatMoney(cobro.conIsv) }}.
          </p>
          <p v-if="!sales.length" class="muted small">Todavía no se ha facturado.</p>
          <p v-else class="muted small">
            Ya facturada: {{ sales.map((s) => s.number).join(', ') }}.
          </p>
        </article>

        <article id="cobrar" v-if="auth.isOwner && !sales.length && order.status !== WorkOrderStatus.Cancelled" class="card">
          <details
            class="plegable"
            :open="cobroAbierto"
            @toggle="cobroAbierto = ($event.target as HTMLDetailsElement).open"
          >
            <summary>
              <h2>Cerrar y facturar</h2>
              <span class="muted small">Cobrar, entregar y anotar el próximo servicio</span>
            </summary>

          <p class="muted small">
            Cobra los repuestos ya cargados ({{ formatMoney(order.partsTotal) }}) más la mano
            de obra que se elija abajo, y entrega el vehículo.
          </p>

          <fieldset class="labor-source">
            <legend>Mano de obra</legend>
            <label v-for="quote in quotesWithLabor" :key="quote.id">
              <input v-model="laborSource" type="radio" :value="quote.id" />
              Cotización {{ quote.number }} · {{ formatMoney(laborOf(quote)) }}
              <span class="muted small">{{ QUOTE_STATUS_LABEL[quote.status] }}</span>
            </label>
            <label>
              <input v-model="laborSource" type="radio" value="tasks" />
              Los pasos de la orden · {{ formatMoney(order.laborTotal) }}
            </label>
            <label>
              <input v-model="laborSource" type="radio" value="none" />
              No cobrar mano de obra
            </label>
          </fieldset>

          <select v-model.number="paymentMethod">
            <option v-for="(label, value) in PAYMENT_METHOD_LABEL" :key="value" :value="Number(value)">
              {{ label }}
            </option>
          </select>

          <label class="checkbox" :class="{ apagado: impedimentoCai }">
            <input v-model="conCai" type="checkbox" :disabled="!!impedimentoCai" />
            Factura con CAI
            <span v-if="rangoFiscal && !impedimentoCai" class="muted small">
              · siguiente {{ rangoFiscal.nextFiscalNumber }}<template v-if="tasaImpuesto">
              · le suma el ISV {{ tasaImpuesto }}%</template>
            </span>
          </label>
          <p v-if="impedimentoCai" class="muted small">{{ impedimentoCai }}</p>

          <label v-if="conCai" class="rtn">
            A nombre de
            <input v-model="nombreFactura" placeholder="Nombre o razón social" />
          </label>
          <label v-if="conCai" class="rtn">
            RTN del cliente
            <input v-model="rtnFactura" placeholder="Sin RTN: consumidor final" />
          </label>
          <p v-if="conCai" class="muted small">
            Vienen de la ficha del cliente. Cambiarlos aquí afecta solo a esta factura: es lo
            que se hace cuando la pide a nombre de su empresa.
          </p>
          <p v-if="conCai && !rtnFactura.trim()" class="muted small">
            Sale a consumidor final. Arriba de L 10,000 el SAR pide el RTN o el número de
            identidad del cliente en su ficha.
          </p>

          <label class="checkbox">
            <input v-model="onCredit" type="checkbox" />
            Queda debiendo: dejar saldo a crédito
          </label>

          <div v-if="onCredit" class="credit">
            <label>
              Abona hoy
              <input v-model="initialPayment" type="number" min="0" step="0.01" placeholder="0.00" />
            </label>
            <label>
              Fecha de pago
              <input v-model="dueDate" type="date" />
            </label>
          </div>

          <fieldset class="proximo">
            <legend>Próximo servicio</legend>
            <p class="muted small">
              Para acordarse de llamarlo. Bórrelo si este trabajo no se repite.
            </p>
            <div class="credit">
              <label>
                Le toca el
                <input v-model="nextServiceAt" type="date" />
              </label>
              <label>
                O a los (km)
                <input
                  v-model="nextServiceMileage"
                  type="number"
                  min="0"
                  step="100"
                  placeholder="—"
                />
              </label>
            </div>
          </fieldset>

          <div class="actions">
            <button type="button" :disabled="busy" @click="closeAndInvoice">
              Facturar y entregar
            </button>
          </div>
          </details>
        </article>

        <article id="cobrar-hecho" v-if="auth.isOwner && sales.length" class="card">
          <h2>Venta</h2>
          <div v-for="sale in sales" :key="sale.id" class="sale">
            <p v-if="sale.fiscalNumber" class="fiscal">
              Factura fiscal <strong>{{ sale.fiscalNumber }}</strong>
              <span class="muted small">· CAI {{ sale.fiscalCai }}</span>
            </p>
            <p>
              <strong>{{ sale.number }}</strong> · {{ formatMoney(sale.total) }}
              <span class="muted small">
                {{ PAYMENT_METHOD_LABEL[sale.paymentMethod] }} · {{ formatDateTime(sale.saleDate) }}
              </span>
            </p>

            <p v-if="sale.balance > 0" class="balance" :class="{ overdue: sale.isOverdue }">
              Saldo pendiente {{ formatMoney(sale.balance) }}
              <span class="muted small">
                de {{ formatMoney(sale.total) }}<template v-if="sale.dueDate">
                  · {{ sale.isOverdue ? 'venció' : 'vence' }}
                  {{ formatDateTime(sale.dueDate) }}</template>
              </span>
            </p>
            <p v-else class="paid">Pagada</p>

            <ul v-if="sale.payments.length" class="payments">
              <li v-for="payment in sale.payments" :key="payment.id">
                <span>
                  {{ formatDateTime(payment.paidAt) }} ·
                  {{ PAYMENT_METHOD_LABEL[payment.method] }}
                  <template v-if="payment.reference"> · {{ payment.reference }}</template>
                </span>
                <span class="num">{{ formatMoney(payment.amount) }}</span>
                <button type="button" class="link" :disabled="busy" @click="removePayment(sale, payment.id)">
                  Quitar
                </button>
              </li>
            </ul>

            <form v-if="sale.balance > 0" class="credit" @submit.prevent="registerPayment(sale)">
              <label>
                Abono
                <input v-model="newPayment.amount" type="number" min="0.01" step="0.01" :max="sale.balance" />
              </label>
              <label>
                Forma
                <select v-model.number="newPayment.method">
                  <option v-for="(label, value) in PAYMENT_METHOD_LABEL" :key="value" :value="Number(value)">
                    {{ label }}
                  </option>
                </select>
              </label>
              <label>
                Referencia
                <input v-model="newPayment.reference" placeholder="Recibo, depósito…" />
              </label>
              <button type="submit" :disabled="busy">Registrar abono</button>
            </form>

            <div class="actions">
              <button type="button" :disabled="busy" @click="downloadInvoice(sale)">
                Factura en PDF
              </button>
            </div>
          </div>
        </article>

        <!-- La cotización que el cliente tiene en la mano, con su estado: es lo que contesta
             «¿ya nos dijo que sí?», que es lo que detiene la orden. -->
        <article v-if="auth.isOwner" class="card">
          <div class="titulo">
            <h2>Cotización</h2>
            <RouterLink
              v-if="quotes.length"
              class="small"
              :to="{ name: 'quotes', query: { workOrderId: order.id } }"
            >
              Ver
            </RouterLink>
          </div>

          <template v-if="ultimaCotizacion">
            <div class="cotizacion">
              <span class="badge-cot">{{ QUOTE_STATUS_LABEL[ultimaCotizacion.status] }}</span>
              <div>
                <strong class="num">{{ ultimaCotizacion.number }}</strong>
                <p class="muted small">
                  {{ formatMoney(ultimaCotizacion.total) }}
                  <template v-if="ultimaCotizacion.respondedAt">
                    · contestada {{ formatDate(ultimaCotizacion.respondedAt) }}
                  </template>
                  <template v-else-if="ultimaCotizacion.sentAt">
                    · enviada {{ formatDate(ultimaCotizacion.sentAt) }}, sin respuesta
                  </template>
                  <template v-else>· borrador, sin enviar</template>
                </p>
              </div>
            </div>
            <div class="actions">
              <button
                v-if="ultimaCotizacion.publicUrl"
                type="button"
                class="suave"
                @click="reenviarCotizacion(ultimaCotizacion.id)"
              >
                Reenviar por WhatsApp
              </button>
              <button type="button" class="suave" :disabled="busy" @click="quoteThisOrder">
                Otra cotización
              </button>
            </div>
          </template>

          <template v-else>
            <p class="muted small">
              Se arma con los repuestos ya cargados y los pasos que tengan servicio asignado.
            </p>
            <div class="actions">
              <button type="button" :disabled="busy" @click="quoteThisOrder">
                Crear cotización
              </button>
            </div>
          </template>
        </article>

        <!-- La ficha: los cuatro datos que se consultan y no se tocan, más el técnico, que se
             cambia desde aquí. El vehículo y el cliente ya están en la cabecera. -->
        <article class="card">
          <h2>Ficha</h2>
          <dl>
            <dt>Técnico</dt>
            <dd v-if="auth.isOwner">
              <select
                :value="order.assignedTechnicianId ?? ''"
                :disabled="busy"
                @change="assign(($event.target as HTMLSelectElement).value)"
              >
                <option value="">Sin asignar</option>
                <option v-for="t in availableTechnicians" :key="t.id" :value="t.id">
                  {{ t.fullName }}
                </option>
              </select>
            </dd>
            <dd v-else>{{ order.assignedTechnicianName ?? 'Sin asignar' }}</dd>
            <dt>Vehículo</dt>
            <dd>{{ VEHICLE_TYPE_LABEL[order.vehicleType] }} · {{ order.vehicleLabel }}</dd>
            <dt>Sucursal</dt>
            <dd>{{ order.branchName }}</dd>
            <dt>Kilometraje</dt>
            <dd class="num">{{ order.mileageIn?.toLocaleString('es-HN') ?? '—' }}</dd>
            <dt>Abierta</dt>
            <dd>{{ formatDateTime(order.openedAt) }}</dd>
          </dl>
        </article>

        <article id="avisar" class="card">
          <h2>Avisar al cliente</h2>
          <!-- El mismo enlace sirve toda la reparación: el cliente lo abre sin cuenta y ve en
               qué va su vehículo. Se manda al recibir, otra vez cuando está listo, y al final
               con la factura. -->
          <p class="muted small">
            Le llega un enlace donde ve el avance, las fotos y —al cerrar— su factura. No
            necesita instalar nada.
          </p>
          <div class="avisos">
            <button type="button" @click="mandarPorWhatsApp('received')">
              Mandar el enlace
            </button>
            <button type="button" class="suave" @click="mandarPorWhatsApp('ready')">
              Avisar que está listo
            </button>
            <button
              v-if="sales.some((s) => !s.isVoided)"
              type="button"
              class="suave"
              @click="mandarPorWhatsApp('invoice')"
            >
              Mandar la factura
            </button>
            <a :href="whatsapp" target="_blank" rel="noopener" class="wa">
              Escribirle sin enlace
            </a>
          </div>
        </article>


        <!-- Plegado: un vehículo con años de taller trae veinte visitas y empujaba la línea de
             tiempo fuera de la pantalla. El resumen contesta cerrado lo que casi siempre se
             pregunta —cuántas veces vino y cuándo la última—, así que abrirlo es para leer el
             detalle, no para enterarse. Sin `open` enlazado a propósito: lo maneja el navegador
             y así no se cierra solo cada vez que la orden se recarga. -->
        <article v-if="historial.length" class="card">
          <details class="plegable">
            <summary>
              <h2>Historial del vehículo</h2>
              <span class="muted small">
                {{ historial.length }}
                {{ historial.length === 1 ? 'visita antes' : 'visitas antes' }}, la última el
                {{ formatDate(historial[0].openedAt) }}
              </span>
            </summary>

            <ul class="historial">
              <li v-for="previa in historial" :key="previa.id">
                <RouterLink :to="{ name: 'work-order', params: { id: previa.id } }">
                  {{ previa.number }}
                </RouterLink>
                <span class="badge">{{ WORK_ORDER_STATUS_LABEL[previa.status] }}</span>
                <div class="muted small">
                  {{ formatDate(previa.openedAt) }} · {{ previa.description }}
                </div>
              </li>
            </ul>
          </details>
        </article>

        <!-- Al final y plegada: se llega aquí queriendo, no de un clic mal dado. -->
        <!-- Plegada: se consulta cuando algo no cuadra —quién movió el estado y cuándo—,
             no en el trabajo del día. Sin `open` enlazado, igual que el historial: lo
             maneja el navegador y así no se cierra al recargar la orden. -->
        <article class="card">
          <details class="plegable">
            <summary>
              <h2>Línea de tiempo</h2>
              <span class="muted small">
                {{ order.timeline.length }} cambio(s) de estado
                <template v-if="order.timeline.some((e) => !e.isVisibleToCustomer)">
                  · {{ order.timeline.filter((e) => !e.isVisibleToCustomer).length }} nota(s)
                  interna(s)
                </template>
              </span>
            </summary>
            <ol class="timeline">
              <li v-for="(entry, i) in order.timeline" :key="i">
                <div class="dot" />
                <div>
                  <strong>{{ WORK_ORDER_STATUS_LABEL[entry.toStatus] }}</strong>
                  <span v-if="!entry.isVisibleToCustomer" class="internal">interna</span>
                  <div class="muted small">
                    {{ formatDateTime(entry.changedAt) }} · {{ entry.changedByName }}
                  </div>
                  <p v-if="entry.note">{{ entry.note }}</p>
                </div>
              </li>
            </ol>
          </details>
        </article>

        <article v-if="auth.isOwner" class="card">
          <details class="plegable">
            <summary>
              <h2>Borrar la orden</h2>
              <span class="muted small">Solo si se abrió por error</span>
            </summary>
            <p class="muted small">
              Una orden ya facturada no se borra: esa se anula.
            </p>
            <button type="button" class="peligro" :disabled="busy" @click="borrarOrden">
              Borrar la orden {{ order.number }}
            </button>
          </details>
        </article>

      </div>
    </div>
  </section>

  <p v-else-if="error" class="error">{{ error }}</p>
  <p v-else class="muted">Cargando…</p>
</template>

<style scoped>
.peligro {
  border-color: var(--danger);
  color: var(--danger);
}

summary h2 {
  display: inline;
}

header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

/* Pegada arriba: la acción del día no se busca desplazando. El fondo es opaco porque debajo
   pasan tarjetas. */
header.fija {
  position: sticky;
  top: 0;
  z-index: 4;
  flex-wrap: wrap;
  margin: -1.5rem -1.5rem 1rem;
  padding: 0.875rem 1.5rem;
  border-bottom: 1px solid var(--border);
  background: var(--surface);
}

.quien {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  flex-wrap: wrap;
  min-width: 0;
}

header h1 {
  margin: 0;
}

header p {
  margin: 0;
  flex-basis: 100%;
}

.acciones {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.atrasada {
  padding: 0.0625rem 0.5rem;
  border-radius: 999px;
  background: color-mix(in srgb, var(--danger) 18%, transparent);
  color: var(--danger);
  font-size: 0.75rem;
  white-space: nowrap;
}

/* Título de tarjeta con algo a la derecha: el contador de pasos, el enlace a la cotización. */
.titulo {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
}

.titulo h2 {
  margin: 0;
}

.titulo-derecha {
  display: flex;
  align-items: center;
  gap: 0.625rem;
}

.cuenta {
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.chico {
  padding: 0.3125rem 0.625rem;
  border-color: var(--border);
  background: var(--surface);
  color: var(--text);
  font-size: 0.8125rem;
}

.chico:hover {
  border-color: var(--accent);
  color: var(--accent);
}

.task-texto {
  display: flex;
  flex-direction: column;
  gap: 0.0625rem;
  min-width: 0;
}

.precio {
  margin-left: auto;
  font-size: 0.875rem;
  white-space: nowrap;
}

.cambiar {
  padding: 0.125rem 0.25rem;
  border: 1px solid transparent;
  background: none;
  color: var(--text);
  font-weight: 400;
}

.cambiar:hover {
  border-color: var(--border);
  color: var(--accent);
}

/* Un ajuste, no trabajo del día: cerrado ocupa una línea. */
.ajuste {
  margin-top: 0.75rem;
  padding-top: 0.75rem;
  border-top: 1px solid var(--border);
  font-size: 0.875rem;
}

.ajuste summary {
  color: var(--text-muted);
  cursor: pointer;
}

.ajuste label {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  margin-top: 0.375rem;
}

.ajuste p {
  margin-top: 0.375rem;
}

.cuentas {
  grid-template-columns: 1fr auto;
  gap: 0.375rem 1rem;
}

.cuentas dd {
  text-align: right;
}

.cuentas .fuerte {
  padding-top: 0.5rem;
  border-top: 1px solid var(--border);
  color: var(--text);
  font-weight: 600;
}

.cuentas .grande {
  font-size: 1.125rem;
}

.cotizacion {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  margin-bottom: 0.75rem;
}

.cotizacion p {
  margin: 0;
}

.badge-cot {
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  background: var(--surface-alt);
  color: var(--text-muted);
  font-size: 0.75rem;
  white-space: nowrap;
}

/* Los enlaces que llevan a una tarjeta se ven como botones secundarios: hacen lo mismo que
   desplazarse, pero sin buscar. */
.boton-suave {
  padding: 0.5rem 0.875rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text);
  font-size: 0.9375rem;
  font-weight: 500;
}

.boton-suave:hover {
  border-color: var(--accent);
  color: var(--accent);
  text-decoration: none;
}

/* «Otro estado»: un desplegable nativo, sin JavaScript. Se cierra al elegir porque la orden
   se recarga y el detalle se vuelve a pintar. */
.otros {
  position: relative;
}

.otros summary {
  padding: 0.5rem 0.75rem;
  line-height: 1;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  font-size: 0.9375rem;
  font-weight: 500;
  list-style: none;
  cursor: pointer;
}

.otros summary::-webkit-details-marker {
  display: none;
}

.menu-titulo {
  margin: 0 0 0.125rem;
  padding: 0 0.125rem;
  font-size: 0.6875rem;
  font-weight: 600;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--text-muted);
}

.otros[open] summary {
  border-color: var(--accent);
  color: var(--accent);
}

.otros .menu {
  position: absolute;
  z-index: 5;
  top: calc(100% + 0.25rem);
  right: 0;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  min-width: 12rem;
  padding: 0.5rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  box-shadow: 0 0.5rem 1.5rem rgb(0 0 0 / 18%);
}

.otros .menu button {
  text-align: left;
}

.nota-estado {
  margin: -0.5rem 0 1rem;
  font-size: 0.8125rem;
}

.nota-estado summary {
  color: var(--text-muted);
  cursor: pointer;
}

.nota-campos {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
  margin-top: 0.5rem;
}

.nota-campos input[type='text'],
.nota-campos input:not([type]) {
  flex: 1;
  min-width: 14rem;
}

/* Trabajo a la izquierda y ancho; consulta a la derecha y angosta. Antes eran dos columnas
   del mismo peso, y lo que se hace hoy competía con lo que solo se mira. */
.grid {
  display: grid;
  grid-template-columns: minmax(0, 1.7fr) minmax(0, 1fr);
  gap: 1rem;
  align-items: start;
}

@media (max-width: 60rem) {
  .grid {
    grid-template-columns: 1fr;
  }

  header.fija {
    margin: -1rem -1rem 1rem;
    padding: 0.75rem 1rem;
  }

  /* En el teléfono la cabecera fija se queda arriba, pero la acción principal se repite
     abajo, al alcance del pulgar: es la que se toca. */
  .acciones {
    width: 100%;
  }

  .acciones button {
    flex: 1;
  }
}

.col {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  min-width: 0;
}

.card {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.card h2 {
  margin: 0 0 0.75rem;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.card h3 {
  margin: 1rem 0 0.25rem;
  font-size: 0.875rem;
}

.card p {
  margin: 0;
}

dl {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.375rem 1rem;
  margin: 0;
}

dt {
  color: var(--text-muted);
  font-size: 0.875rem;
}

dd {
  margin: 0;
}

.plate {
  padding: 0 0.25rem;
  border: 1px solid var(--border);
  border-radius: 3px;
  font-family: ui-monospace, monospace;
  font-size: 0.8125rem;
}

.wa {
  margin-left: 0.5rem;
  font-size: 0.8125rem;
}

.tasks {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.tasks li {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.task-head {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.task-head label {
  flex: 1;
  min-width: 0;
}

.task-labor {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding-left: 1.5rem;
}

.task-labor select {
  flex: 1;
  min-width: 0;
  font-size: 0.8125rem;
}

.new-task select {
  width: auto;
  max-width: 12rem;
}

.labor-mode {
  margin: 0 0 0.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: 6px;
}

.labor-mode legend {
  padding: 0 0.25rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.labor-mode label {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.875rem;
}

.manual-labor {
  display: flex;
  align-items: flex-end;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

.manual-labor label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.manual-labor input {
  width: 9rem;
}

.new-service {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-top: 0.5rem;
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: 6px;
}

.new-service .row {
  display: flex;
  gap: 0.5rem;
}

.new-service label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  flex: 1;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.new-service .checkbox {
  flex-direction: row;
  align-items: center;
  gap: 0.5rem;
}

.new-service .checkbox input {
  width: auto;
}

.plantillas {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
}

.plantillas select {
  flex: 1;
  min-width: 0;
}

.sugeridos {
  margin-bottom: 0.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: 6px;
}

.sugeridos p {
  margin: 0 0 0.375rem;
}

.sugeridos ul {
  margin: 0;
  padding: 0;
  list-style: none;
}

.sugeridos li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  padding: 0.1875rem 0;
  font-size: 0.875rem;
}

.sin-existencia {
  color: var(--danger);
  font-style: normal;
  font-size: 0.75rem;
}

.labor-total {
  margin-top: 0.75rem;
  padding-top: 0.5rem;
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: space-between;
  font-size: 0.875rem;
  color: var(--text-muted);
}

.labor-source {
  margin: 0 0 0.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: 6px;
}

.labor-source legend {
  padding: 0 0.25rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.labor-source label {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.875rem;
}

.tasks label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
}

.done {
  text-decoration: line-through;
  color: var(--text-muted);
}

.inline {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

/* En el teléfono el campo baja a su propia línea antes de quedarse en dos dedos de ancho. */
.inline input {
  flex: 1 1 10rem;
  min-width: 0;
}

.checkbox {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin: 0.5rem 0;
  font-size: 0.875rem;
  color: var(--text-muted);
}

.actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
}

.sale + .sale {
  margin-top: 0.75rem;
}

.credit {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 0.5rem;
  margin: 0.5rem 0;
}

.credit label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.credit input,
.credit select {
  width: 8rem;
}

.balance {
  margin: 0.25rem 0;
  font-weight: 600;
  color: var(--warning, #b45309);
}

.balance.overdue {
  color: var(--danger);
}

.paid {
  margin: 0.25rem 0;
  font-size: 0.875rem;
  color: var(--success, #15803d);
}

.payments {
  list-style: none;
  margin: 0.5rem 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
}

.payments li {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 0.5rem;
}

.proximo {
  margin: 0.75rem 0 0;
  padding: 0.5rem 0.75rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
}

.proximo legend {
  padding: 0 0.25rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.proximo p {
  margin: 0 0 0.5rem;
}

.avisos {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

/* Secundario: mandar el enlace es la acción normal, avisar que está listo y mandar la factura
   son dos momentos concretos y no compiten con ella. */
.suave {
  border-color: var(--border);
  background: none;
  color: var(--accent);
}

.link {
  padding: 0;
  border: none;
  background: none;
  color: var(--danger);
  font-size: 0.75rem;
  cursor: pointer;
}

.sale p {
  margin-bottom: 0.5rem;
}

.apagado {
  opacity: 0.6;
}

.rtn {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.fiscal {
  margin: 0 0 0.25rem;
  font-size: 0.875rem;
}

.plegable summary {
  display: flex;
  align-items: baseline;
  flex-wrap: wrap;
  gap: 0.125rem 0.5rem;
  cursor: pointer;
  list-style: none;
}

/* El título no se parte a media frase: si no cabe con el resumen al lado, el resumen baja. */
.plegable summary h2 {
  white-space: nowrap;
}

/* El resumen se recorta antes de empujar el triángulo a una segunda línea. */
.plegable summary > span {
  flex: 0 1 auto;
  min-width: 0;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

/* Safari sigue pintando su propio triángulo si no se le quita también por aquí. */
.plegable summary::-webkit-details-marker {
  display: none;
}

/* El triángulo va al final y gira al abrir, para que se vea que hay algo debajo. */
.plegable summary::after {
  content: '▾';
  margin-left: auto;
  color: var(--text-muted);
  font-size: 0.75rem;
}

.plegable[open] summary::after {
  content: '▴';
}

.plegable[open] summary {
  margin-bottom: 0.75rem;
}

.historial {
  margin: 0;
  padding: 0;
  list-style: none;
  display: grid;
  gap: 0.625rem;
}

.historial li {
  padding-bottom: 0.625rem;
  border-bottom: 1px solid var(--border);
}

.historial li:last-child {
  padding-bottom: 0;
  border-bottom: none;
}

.historial .badge {
  margin-left: 0.5rem;
  padding: 0.0625rem 0.375rem;
  border-radius: 999px;
  background: var(--surface-alt);
  font-size: 0.75rem;
  color: var(--text-muted);
}

.timeline {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.875rem;
}

.timeline li {
  display: flex;
  gap: 0.625rem;
}

.dot {
  flex: none;
  width: 8px;
  height: 8px;
  margin-top: 0.375rem;
  border-radius: 50%;
  background: var(--accent);
}

.internal {
  margin-left: 0.375rem;
  padding: 0 0.375rem;
  border-radius: 999px;
  background: var(--surface-alt);
  color: var(--text-muted);
  font-size: 0.6875rem;
}

.small {
  font-size: 0.75rem;
}

.muted {
  color: var(--text-muted);
}

.error {
  color: var(--danger);
}

select,
textarea,
.inline input {
  width: 100%;
}

textarea {
  margin-bottom: 0.5rem;
  font: inherit;
  resize: vertical;
}
</style>

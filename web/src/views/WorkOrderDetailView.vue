<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { errorMessage } from '@/api/client'
import {
  customersApi,
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
  type LaborService,
  type QuoteDetail,
  type SaleDetail,
  type User,
  type WorkOrderDetail,
  type FiscalRange,
  type WorkOrderListItem,
} from '@/types/domain'
import { formatDate, formatDateTime, formatMoney, relativeTime, whatsappLink } from '@/utils/format'

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

/** Cotizaciones de esta orden, con sus líneas: de ahí sale la mano de obra a facturar. */
const quotes = ref<QuoteDetail[]>([])

/** Qué mano de obra se cobra: `'tasks'`, `'none'` o el id de una cotización. */
const laborSource = ref('tasks')

/** Copia editable del diagnóstico. Se refresca en cada carga, incluida la de después de guardar. */
const diagnosis = ref('')

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
      }
    }
    manualLabor.value = order.value.manualLaborTotal ?? ''
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

onMounted(async () => {
  if (auth.isOwner) technicians.value = await usersApi.list('Technician').catch(() => [])
  if (canEdit.value) laborServices.value = await laborServicesApi.list().catch(() => [])
  await load()
})
</script>

<template>
  <section v-if="order" class="detail">
    <header>
      <div>
        <h1>{{ order.number }}</h1>
        <p class="muted">
          {{ order.branchName }} · abierta {{ relativeTime(order.openedAt) }}
          <template v-if="order.promisedAt"> · prometida {{ formatDateTime(order.promisedAt) }}</template>
        </p>
      </div>
      <StatusBadge :status="order.status" />
    </header>

    <p v-if="error" class="error">{{ error }}</p>

    <div class="grid">
      <div class="col">
        <article class="card">
          <h2>Vehículo y cliente</h2>
          <dl>
            <dt>{{ VEHICLE_TYPE_LABEL[order.vehicleType] }}</dt>
            <dd>{{ order.vehicleLabel }} <span v-if="order.plate" class="plate">{{ order.plate }}</span></dd>
            <dt>Cliente</dt>
            <dd>
              {{ order.customerName }}
              <a :href="whatsapp" target="_blank" rel="noopener" class="wa">WhatsApp</a>
            </dd>
            <dt>Kilometraje al ingreso</dt>
            <dd>{{ order.mileageIn?.toLocaleString('es-HN') ?? '—' }}</dd>
          </dl>
        </article>

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
          <h2>Pasos de la reparación</h2>

          <fieldset v-if="auth.isOwner" class="labor-mode">
            <legend>Cómo se cobra la mano de obra</legend>
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
          </fieldset>

          <p v-if="canEdit" class="muted small">
            <template v-if="isCatalog">
              El paso que quede sin servicio del catálogo no entra en la factura.
            </template>
            <template v-else>
              Los pasos quedan como registro de lo que se hizo; a la factura va el total.
            </template>
          </p>

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
                  <span :class="{ done: task.isDone }">{{ task.title }}</span>
                </label>
                <span class="muted small">
                  {{ task.assignedTechnicianName ?? 'sin asignar' }}
                  <template v-if="task.actualHours"> · {{ task.actualHours }} h</template>
                </span>
              </div>

              <div v-if="canEdit && isCatalog" class="task-labor">
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
                <strong v-if="task.laborPrice" class="num">{{ formatMoney(task.laborPrice) }}</strong>
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

          <p v-if="canEdit" class="labor-total">
            Mano de obra <strong>{{ formatMoney(order.laborTotal) }}</strong>
          </p>
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

      <div class="col">
        <article v-if="auth.isOwner && !sales.length && order.status !== WorkOrderStatus.Cancelled" class="card">
          <h2>Cerrar y facturar</h2>
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
              · siguiente {{ rangoFiscal.nextFiscalNumber }}
            </span>
          </label>
          <p v-if="impedimentoCai" class="muted small">{{ impedimentoCai }}</p>

          <label v-if="conCai" class="rtn">
            RTN del cliente
            <input v-model="rtnFactura" placeholder="Sin RTN: consumidor final" />
          </label>

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

          <div class="actions">
            <button type="button" :disabled="busy" @click="closeAndInvoice">
              Facturar y entregar
            </button>
          </div>
        </article>

        <article v-if="auth.isOwner && sales.length" class="card">
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

        <article v-if="auth.isOwner" class="card">
          <h2>Cotizar</h2>
          <p class="muted small">
            Se arma con los repuestos ya cargados y los pasos que tengan servicio asignado.
          </p>
          <div class="actions">
            <button type="button" :disabled="busy" @click="quoteThisOrder">
              Crear cotización
            </button>
            <RouterLink :to="{ name: 'quotes', query: { workOrderId: order.id } }">
              Ver cotizaciones
            </RouterLink>
          </div>
        </article>

        <article v-if="auth.isOwner" class="card">
          <h2>Técnico responsable</h2>
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
        </article>

        <article v-if="canEdit && order.allowedNextStatuses.length" class="card">
          <h2>Cambiar estado</h2>
          <input v-model="statusNote" placeholder="Nota para la línea de tiempo (opcional)" />
          <label class="checkbox">
            <input v-model="noteIsInternal" type="checkbox" />
            Nota interna: el cliente no la ve
          </label>
          <div class="actions">
            <button
              v-for="next in order.allowedNextStatuses"
              :key="next"
              type="button"
              :disabled="busy"
              @click="changeStatus(next)"
            >
              {{ WORK_ORDER_STATUS_LABEL[next] }}
            </button>
          </div>
        </article>

        <article v-if="historial.length" class="card">
          <h2>Historial del vehículo</h2>
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
        </article>

        <article class="card">
          <h2>Línea de tiempo</h2>
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
        </article>
      </div>
    </div>
  </section>

  <p v-else-if="error" class="error">{{ error }}</p>
  <p v-else class="muted">Cargando…</p>
</template>

<style scoped>
header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

header h1 {
  margin: 0;
}

header p {
  margin: 0.25rem 0 0;
  font-size: 0.875rem;
}

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(20rem, 1fr));
  gap: 1rem;
  align-items: start;
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
  justify-content: space-between;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
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
  gap: 0.5rem;
  margin-top: 0.75rem;
}

.inline input {
  flex: 1;
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

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { errorMessage } from '@/api/client'
import { laborServicesApi, partsApi, quotesApi, tenantApi } from '@/api/garaj'
import EmptyState from '@/components/EmptyState.vue'
import ErrorNote from '@/components/ErrorNote.vue'
import FilterBar from '@/components/FilterBar.vue'
import PeriodSelect from '@/components/PeriodSelect.vue'
import PhotoGallery from '@/components/PhotoGallery.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import {
  LINE_TYPE_LABEL,
  LineType,
  QUOTE_STATUS_LABEL,
  QuoteStatus,
  type LaborService,
  type Part,
  type QuoteDetail,
  type QuoteListItem,
} from '@/types/domain'
import { formatDate, formatMoney, formatQuantity, sinIsv } from '@/utils/format'

const route = useRoute()

const quotes = ref<QuoteListItem[]>([])
const selected = ref<QuoteDetail | null>(null)
const parts = ref<Part[]>([])
const services = ref<LaborService[]>([])

/**
 * Las pestañas. «Pendientes» es lo que de verdad se trabaja: enviadas que el cliente no ha
 * contestado y que todavía no se vencieron.
 */
const pestana = ref<'pendientes' | 'aprobadas' | 'rechazadas' | 'vencidas' | 'todas'>(
  'pendientes',
)

function esPendiente(q: QuoteListItem) {
  return q.status === QuoteStatus.Sent && !q.isExpired
}

function esVencida(q: QuoteListItem) {
  return q.isExpired || q.status === QuoteStatus.Expired
}

const visibles = computed(() => {
  switch (pestana.value) {
    case 'pendientes':
      return quotes.value.filter(esPendiente)
    case 'aprobadas':
      return quotes.value.filter((q) => q.status === QuoteStatus.Approved)
    case 'rechazadas':
      return quotes.value.filter((q) => q.status === QuoteStatus.Rejected)
    case 'vencidas':
      return quotes.value.filter(esVencida)
    default:
      return quotes.value
  }
})

const pestanas = computed(() => [
  {
    clave: 'pendientes' as const,
    titulo: 'Pendientes',
    cuantas: quotes.value.filter(esPendiente).length,
  },
  {
    clave: 'aprobadas' as const,
    titulo: 'Aprobadas',
    cuantas: quotes.value.filter((q) => q.status === QuoteStatus.Approved).length,
  },
  {
    clave: 'rechazadas' as const,
    titulo: 'Rechazadas',
    cuantas: quotes.value.filter((q) => q.status === QuoteStatus.Rejected).length,
  },
  {
    clave: 'vencidas' as const,
    titulo: 'Vencidas',
    cuantas: quotes.value.filter(esVencida).length,
    tono: 'alerta',
  },
  { clave: 'todas' as const, titulo: 'Todas', cuantas: quotes.value.length },
])

/** Las cifras de arriba: lo que está en la mesa y lo que ya se cerró. */
const cifras = computed(() => {
  const pendientes = quotes.value.filter(esPendiente)
  const aprobadas = quotes.value.filter((q) => q.status === QuoteStatus.Approved)

  return {
    pendientes: pendientes.length,
    montoPendiente: pendientes.reduce((suma, q) => suma + q.total, 0),
    aprobadas: aprobadas.length,
    montoAprobado: aprobadas.reduce((suma, q) => suma + q.total, 0),
    vencidas: quotes.value.filter(esVencida).length,
  }
})

/** Filtro de fechas, por cuándo se hizo la cotización. */
const desde = ref('')
const error = ref('')
const loading = ref(false)
const busy = ref(false)
const shared = ref('')

const newLine = ref({
  lineType: LineType.Labor as LineType,
  catalogId: '',
  description: '',
  quantity: 1,
  unitPrice: 0,
  discount: 0,
  boughtOutside: false,
  supplierName: '',
})

/** La del taller, para poder prender el ISV de una cotización sin escribir la tasa. */
const tasaDelTaller = ref(0)

/** Si los precios del taller ya llevan el ISV adentro: es lo que decide si prenderlo sube el
 *  total o solo dice cuánto de ese total es impuesto. */
const precioConIsv = ref(true)

const catalog = computed(() =>
  newLine.value.lineType === LineType.Part
    ? parts.value.map((p) => ({ id: p.id, label: `${p.sku} · ${p.name}`, price: p.salePrice }))
    : services.value.map((s) => ({ id: s.id, label: `${s.code} · ${s.name}`, price: s.price })),
)

/**
 * Abre donde hay trabajo. Entrar a «Pendientes 0 · Nada en esta pestaña» mientras
 * «Vencidas» tiene siete es hacerle buscar al Dueño lo que la pantalla ya sabe.
 */
function abrirDondeHayTrabajo() {
  const conTrabajo = pestanas.value.find((p) => p.clave !== 'todas' && p.cuantas > 0)
  if (conTrabajo) pestana.value = conTrabajo.clave
}

/** El periodo lo pone el selector de la barra; aquí solo se guarda y se recarga. */
function cambiarPeriodo(rango: { desde: string; hasta: string }) {
  desde.value = rango.desde
  load()
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const page = await quotesApi.list({
      from: desde.value || undefined,
      pageSize: 100,
    })
    quotes.value = page.items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar las cotizaciones.')
  } finally {
    loading.value = false
  }
}

async function open(id: string) {
  error.value = ''
  shared.value = ''
  try {
    selected.value = await quotesApi.get(id)
    if (!parts.value.length) parts.value = (await partsApi.list({ pageSize: 200 })).items
    if (!services.value.length) services.value = await laborServicesApi.list()
  } catch (e) {
    error.value = errorMessage(e)
  }
}

async function run(action: () => Promise<QuoteDetail | void>) {
  busy.value = true
  error.value = ''
  try {
    const result = await action()
    if (result) selected.value = result
    await load()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

/** Al elegir del catálogo se propone su precio, pero se puede sobrescribir. */
function onCatalogPick() {
  const item = catalog.value.find((c) => c.id === newLine.value.catalogId)
  if (item) newLine.value.unitPrice = item.price
}

function addLine() {
  const line = newLine.value
  if (!line.catalogId && !line.description.trim()) return

  return run(async () => {
    const result = await quotesApi.addLine(selected.value!.id, {
      lineType: line.lineType,
      partId: line.lineType === LineType.Part ? line.catalogId || null : null,
      laborServiceId: line.lineType === LineType.Labor ? line.catalogId || null : null,
      description: line.description.trim() || undefined,
      quantity: Number(line.quantity),
      unitPrice: Number(line.unitPrice) || undefined,
      discount: Number(line.discount) || 0,
      // Solo tiene sentido en un repuesto libre: lo del catálogo salió de la bodega.
      boughtOutside: esRepuestoLibre.value && line.boughtOutside,
      supplierName:
        esRepuestoLibre.value && line.boughtOutside && line.supplierName.trim()
          ? line.supplierName.trim()
          : undefined,
    })
    newLine.value = {
      lineType: line.lineType,
      catalogId: '',
      description: '',
      quantity: 1,
      unitPrice: 0,
      discount: 0,
      boughtOutside: false,
      supplierName: '',
    }
    return result
  })
}

function removeLine(lineId: string) {
  return run(() => quotesApi.removeLine(selected.value!.id, lineId))
}

/**
 * Prende o apaga el ISV de esta cotización. Con el impuesto dentro del precio no cambia el
 * total: cambia lo que el documento le dice al cliente —cuánto lleva incluido, o que no
 * lleva—, y eso depende de si ese trabajo se va a facturar con CAI.
 */
function toggleIsv(conIsv: boolean) {
  const quote = selected.value!
  return run(() =>
    quotesApi.update(quote.id, {
      validUntil: quote.validUntil ?? undefined,
      notes: quote.notes ?? undefined,
      taxRate: conIsv ? tasaDelTaller.value : 0,
    }),
  )
}

/** Una línea de repuesto sin catálogo: la única que puede venir de una casa de repuestos. */
const esRepuestoLibre = computed(
  () => newLine.value.lineType === LineType.Part && !newLine.value.catalogId,
)

/**
 * Envía y abre WhatsApp. El `window.open` va antes del await deliberadamente: si se abre
 * después de una promesa, Safari lo trata como popup no solicitado y lo bloquea.
 */
async function send() {
  const tab = window.open('', '_blank')
  busy.value = true
  error.value = ''

  try {
    const link = await quotesApi.send(selected.value!.id)
    if (tab) tab.location.href = link.url
    else shared.value = link.url

    selected.value = await quotesApi.get(selected.value!.id)
    await load()
  } catch (e) {
    tab?.close()
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

/**
 * No es un enlace: el PDF va detrás de la sesión y el navegador no manda el token en un href.
 *
 * Con `conLaFicha` sale además la hoja de recepción delante del presupuesto, para mandarle al
 * cliente un archivo en vez de dos. Si la orden no tiene ficha llenada, llega solo el
 * presupuesto y nadie se entera.
 */
async function downloadPdf(conLaFicha = false) {
  busy.value = true
  error.value = ''
  try {
    await quotesApi.downloadPdf(selected.value!.id, selected.value!.number, conLaFicha)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo generar el PDF.')
  } finally {
    busy.value = false
  }
}

async function copyLink() {
  if (!selected.value?.publicUrl) return
  await navigator.clipboard.writeText(selected.value.publicUrl)
  shared.value = 'Enlace copiado.'
}

/** El Dueño registra la respuesta cuando el cliente contesta por teléfono, no por el link. */
function respond(approve: boolean) {
  const note = prompt(
    approve ? 'Comentario del cliente (opcional)' : '¿Por qué la rechazó? (opcional)',
  )
  if (note === null) return

  return run(() => quotesApi.respond(selected.value!.id, approve, note || undefined))
}

onMounted(async () => {
  const taller = await tenantApi.get().catch(() => null)
  tasaDelTaller.value = taller?.defaultTaxRate ?? 0
  precioConIsv.value = taller?.pricesIncludeTax ?? true
  await load()
  abrirDondeHayTrabajo()
  // Se puede llegar desde el detalle de una orden con ?id=…
  if (typeof route.query.id === 'string') await open(route.query.id)
})
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Cotizaciones</h1>
        <p class="muted small">
          El cliente las abre desde WhatsApp y responde sin necesidad de cuenta.
        </p>
      </div>
    </header>

    <div class="cifras">
      <article class="cifra">
        <span class="rotulo">Esperando respuesta</span>
        <strong class="num">{{ cifras.pendientes }}</strong>
        <span class="muted small">{{ formatMoney(cifras.montoPendiente) }} en la mesa</span>
      </article>
      <article class="cifra">
        <span class="rotulo">Aprobadas</span>
        <strong class="num">{{ cifras.aprobadas }}</strong>
        <span class="muted small">{{ formatMoney(cifras.montoAprobado) }} para facturar</span>
      </article>
      <article class="cifra" :class="{ alerta: cifras.vencidas > 0 }">
        <span class="rotulo">Vencidas</span>
        <strong class="num">{{ cifras.vencidas }}</strong>
        <span class="muted small">sin contestar a tiempo</span>
      </article>
      <article class="cifra">
        <span class="rotulo">En el periodo</span>
        <strong class="num">{{ quotes.length }}</strong>
        <span class="muted small">cotizaciones hechas</span>
      </article>
    </div>

    <div class="pestanas" role="tablist">
      <button
        v-for="p in pestanas"
        :key="p.clave"
        type="button"
        role="tab"
        :aria-selected="pestana === p.clave"
        :class="{ on: pestana === p.clave }"
        @click="pestana = p.clave"
      >
        {{ p.titulo }}
        <span class="cuenta" :class="p.tono">{{ p.cuantas }}</span>
      </button>
    </div>

    <FilterBar>
      <PeriodSelect inicial="mes" @cambio="cambiarPeriodo" />
    </FilterBar>


    <ErrorNote v-if="error" :message="error" />

    <div class="layout">
      <div class="list">
        <SkeletonList v-if="loading" :rows="4" variant="tabla" />
        <EmptyState
          v-else-if="!visibles.length"
          :title="quotes.length ? 'Nada en esta pestaña' : 'Todavía no hay cotizaciones'"
          :hint="
            quotes.length
              ? 'Pruebe con otra pestaña o con otro periodo.'
              : 'Se crean desde el detalle de una orden de trabajo.'
          "
        />

        <button
          v-for="q in visibles"
          :key="q.id"
          type="button"
          class="item"
          :class="{ active: selected?.id === q.id }"
          @click="open(q.id)"
        >
          <div class="row">
            <strong>{{ q.number }}</strong>
            <span class="badge" :class="`s${q.status}`">
              {{ q.isExpired && q.status === QuoteStatus.Sent ? 'Vencida' : QUOTE_STATUS_LABEL[q.status] }}
            </span>
          </div>
          <div class="muted small">
            {{ q.customerName }}
            <template v-if="q.vehicleLabel"> · {{ q.vehicleLabel }}</template>
          </div>
          <div class="row">
            <span class="muted small">{{ q.workOrderNumber ?? formatDate(q.createdAt) }}</span>
            <strong>{{ formatMoney(q.total) }}</strong>
          </div>
        </button>
      </div>

      <article v-if="selected" class="detail card">
        <header>
          <div>
            <h2>{{ selected.number }}</h2>
            <p class="muted small">
              {{ selected.customerName }} · {{ selected.customerPhone }}
              <template v-if="selected.vehicleLabel"> · {{ selected.vehicleLabel }}</template>
            </p>
          </div>
          <span class="badge" :class="`s${selected.status}`">
            {{ QUOTE_STATUS_LABEL[selected.status] }}
          </span>
        </header>

        <p v-if="selected.notes" class="notes">{{ selected.notes }}</p>

        <table>
          <tbody>
            <tr v-for="line in selected.lines" :key="line.id">
              <td>
                {{ line.description }}
                <div class="muted small">
                  {{ LINE_TYPE_LABEL[line.lineType] }}
                  <template v-if="line.boughtOutside">
                    · se compra en {{ line.supplierName || 'casa de repuestos' }}
                  </template>
                </div>
              </td>
              <td class="num muted">
                {{ formatQuantity(line.quantity) }} ×
                {{ formatMoney(sinIsv(line.unitPrice, selected.taxRate)) }}
                <template v-if="selected.taxRate > 0"> sin ISV</template>
                <div v-if="line.discount" class="small">−{{ formatMoney(line.discount) }}</div>
              </td>
              <td class="num">{{ formatMoney(sinIsv(line.total, selected.taxRate)) }}</td>
              <td v-if="selected.isEditable" class="num">
                <button type="button" class="link danger" :disabled="busy" @click="removeLine(line.id)">
                  Quitar
                </button>
              </td>
            </tr>
          </tbody>
        </table>

        <!-- Con ISV el desglose reemplaza al subtotal: es lo mismo que lee el cliente. -->
        <div class="totals">
          <div v-if="selected.taxRate === 0">
            <span>Subtotal</span><span>{{ formatMoney(selected.subtotal) }}</span>
          </div>
          <div v-if="selected.discountTotal > 0">
            <span>Descuento</span><span>−{{ formatMoney(selected.discountTotal) }}</span>
          </div>
          <template v-if="selected.taxRate > 0">
            <div>
              <span>Neto</span
              ><span>{{ formatMoney(selected.total - selected.taxTotal) }}</span>
            </div>
            <div>
              <span>ISV {{ selected.taxRate }}%</span
              ><span>{{ formatMoney(selected.taxTotal) }}</span>
            </div>
          </template>
          <div class="grand"><span>Total</span><span>{{ formatMoney(selected.total) }}</span></div>
        </div>

        <!-- Que se vea también de este lado: es lo que el cliente está leyendo. -->
        <p v-if="selected.taxRate === 0" class="sin-isv muted small">
          No incluye ISV.
        </p>

        <!-- Se decide por cotización porque no todo trabajo se factura. Lo que cambia al
             prenderlo depende del taller: con el ISV dentro del precio el total es el mismo y
             solo se dice cuánto de él es impuesto; con el ISV aparte, se le suma. -->
        <label v-if="selected.isEditable && tasaDelTaller > 0" class="check">
          <input
            type="checkbox"
            :checked="selected.taxRate > 0"
            :disabled="busy"
            @change="toggleIsv(($event.target as HTMLInputElement).checked)"
          />
          Cotizar con ISV {{ tasaDelTaller }}%
          {{ precioConIsv ? 'incluido (el total no cambia)' : 'agregado (sube el total)' }}
        </label>

        <form v-if="selected.isEditable" class="add" @submit.prevent="addLine">
          <div class="row gap">
            <select v-model.number="newLine.lineType" @change="newLine.catalogId = ''">
              <option :value="LineType.Labor">Mano de obra</option>
              <option :value="LineType.Part">Repuesto</option>
            </select>
            <select v-model="newLine.catalogId" @change="onCatalogPick">
              <option value="">— libre —</option>
              <option v-for="c in catalog" :key="c.id" :value="c.id">{{ c.label }}</option>
            </select>
          </div>
          <input
            v-if="!newLine.catalogId"
            v-model="newLine.description"
            placeholder="Descripción de la línea"
          />
          <label v-if="esRepuestoLibre" class="check">
            <input v-model="newLine.boughtOutside" type="checkbox" />
            Se compra en casa de repuestos
          </label>
          <input
            v-if="esRepuestoLibre && newLine.boughtOutside"
            v-model="newLine.supplierName"
            placeholder="¿En cuál casa de repuestos? (opcional)"
            maxlength="120"
          />
          <div class="row gap">
            <label>Cant.<input v-model.number="newLine.quantity" type="number" step="0.01" min="0.01" /></label>
            <label>Precio<input v-model.number="newLine.unitPrice" type="number" step="0.01" min="0" /></label>
            <label>Desc.<input v-model.number="newLine.discount" type="number" step="0.01" min="0" /></label>
            <button type="submit" :disabled="busy">Agregar</button>
          </div>
        </form>

        <!--
          Las fotos del daño, justo debajo del precio: el cliente las ve en su link y en el
          PDF, y es lo que hace que apruebe sin ir a preguntar al taller. La clave las
          remonta al cambiar de cotización, para no arrastrar las fotos de la anterior.
        -->
        <PhotoGallery :key="selected.id" :quote-id="selected.id" :can-edit="selected.isEditable" />

        <p v-if="selected.customerResponseNote" class="notes">
          <strong>El cliente respondió:</strong> «{{ selected.customerResponseNote }}»
        </p>

        <div class="actions">
          <button
            v-if="selected.status === QuoteStatus.Draft || selected.status === QuoteStatus.Sent"
            type="button"
            class="primary"
            :disabled="busy || !selected.lines.length"
            @click="send"
          >
            {{ selected.status === QuoteStatus.Draft ? 'Enviar por WhatsApp' : 'Reenviar por WhatsApp' }}
          </button>
          <button v-if="selected.publicUrl" type="button" @click="copyLink">Copiar enlace</button>
          <button type="button" :disabled="busy" @click="downloadPdf()">PDF</button>
          <button
            v-if="selected.workOrderId"
            type="button"
            :disabled="busy"
            @click="downloadPdf(true)"
          >
            PDF con la ficha
          </button>
          <template v-if="selected.status === QuoteStatus.Sent">
            <button type="button" :disabled="busy" @click="respond(true)">Aprobó por teléfono</button>
            <button type="button" :disabled="busy" @click="respond(false)">Rechazó</button>
          </template>
        </div>

        <p v-if="shared" class="muted small">{{ shared }}</p>
        <p v-if="selected.validUntil" class="muted small">
          Válida hasta {{ formatDate(selected.validUntil) }}.
        </p>
      </article>
    </div>
  </section>
</template>

<style scoped>
.periods {
  display: flex;
  flex-wrap: wrap;
  gap: 0.375rem;
  margin: 0.75rem 0;
}

.chip {
  padding: 0.25rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  background: var(--surface);
  color: var(--text-muted);
  font-size: 0.8125rem;
  cursor: pointer;
}

.chip.on {
  border-color: var(--accent);
  background: var(--accent);
  color: var(--on-accent);
}

.cifras {
  display: grid;
  gap: 0.75rem;
  grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
  margin-bottom: 1rem;
}

.cifra {
  display: grid;
  justify-items: start;
  gap: 0.125rem;
  padding: 0.875rem 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.cifra .rotulo {
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.cifra strong {
  font-size: 1.375rem;
  font-family: var(--font-mono);
}

.cifra.alerta strong {
  color: var(--danger);
}

.pestanas {
  display: flex;
  gap: 0.25rem;
  overflow-x: auto;
  margin-bottom: 0.75rem;
  border-bottom: 1px solid var(--border);
}

.pestanas button {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  flex-shrink: 0;
  padding: 0.625rem 0.75rem;
  border: 0;
  border-bottom: 2px solid transparent;
  background: none;
  color: var(--text-muted);
  font-size: 0.9375rem;
  cursor: pointer;
}

.pestanas button:hover {
  color: var(--text);
}

.pestanas button.on {
  color: var(--brand);
  border-bottom-color: var(--brand);
  font-weight: 600;
}

.cuenta {
  padding: 0.05rem 0.4rem;
  border-radius: 999px;
  background: var(--surface-alt);
  color: var(--text-muted);
  font-family: var(--font-mono);
  font-size: 0.75rem;
}

.pestanas button.on .cuenta {
  background: color-mix(in srgb, var(--brand) 14%, transparent);
  color: var(--brand);
}

.cuenta.alerta {
  background: color-mix(in srgb, var(--danger) 12%, transparent);
  color: var(--danger);
}

.top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

h1 {
  margin: 0;
}

.top p {
  margin: 0.25rem 0 0;
}

.layout {
  display: grid;
  grid-template-columns: minmax(16rem, 22rem) 1fr;
  gap: 1rem;
  align-items: start;
}

@media (max-width: 52rem) {
  .layout {
    grid-template-columns: 1fr;
  }
}

.list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  min-width: 0;
}

/* `color` explícito: el botón del estilo global es el azul de acción —letra blanca—, así
   que sin esto el folio, el estado y el monto salían en blanco sobre blanco. */
.item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  width: 100%;
  padding: 0.75rem 0.875rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  color: var(--text);
  text-align: left;
  cursor: pointer;
}

.item:hover {
  border-color: color-mix(in srgb, var(--brand) 45%, var(--border));
  background: var(--surface-alt);
}

.item.active {
  border-color: var(--accent);
  box-shadow: 0 0 0 1px var(--accent);
}

.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
}

.row.gap {
  justify-content: flex-start;
  flex-wrap: wrap;
}

.card {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
  min-width: 0;
}

.detail header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 0.75rem;
}

.detail h2 {
  margin: 0;
  font-size: 1.125rem;
}

.detail header p {
  margin: 0.125rem 0 0;
}

.badge {
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  border: 1px solid var(--border);
  font-size: 0.6875rem;
  white-space: nowrap;
}

.badge.s2 {
  border-color: color-mix(in srgb, var(--brand) 45%, var(--border));
  color: var(--brand);
}

.badge.s3 {
  border-color: #15803d;
  color: #15803d;
}

.badge.s4 {
  border-color: var(--danger);
  color: var(--danger);
}

.notes {
  margin: 0 0 0.75rem;
  padding: 0.5rem 0.625rem;
  border-radius: 6px;
  background: var(--surface-alt);
  font-size: 0.875rem;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9375rem;
}

td {
  padding: 0.375rem 0.25rem;
  border-bottom: 1px solid var(--border);
  vertical-align: top;
}

.num {
  text-align: right;
  white-space: nowrap;
}

.totals {
  margin: 0.75rem 0 0;
  margin-left: auto;
  width: min(16rem, 100%);
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.875rem;
}

.totals div {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
}

.totals .grand {
  margin-top: 0.25rem;
  padding-top: 0.375rem;
  border-top: 1px solid var(--border);
  font-size: 1rem;
  font-weight: 600;
}

.check {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  margin-top: 0.5rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.sin-isv {
  margin: 0.375rem 0 0;
  margin-left: auto;
  width: min(16rem, 100%);
}

.add {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-top: 1rem;
  padding-top: 0.875rem;
  border-top: 1px solid var(--border);
}

.add select {
  min-width: 9rem;
}

.add label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.add label input {
  width: 6rem;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  margin-top: 1rem;
}

.actions .primary {
  background: #15803d;
  border-color: #15803d;
  color: #fff;
}

</style>

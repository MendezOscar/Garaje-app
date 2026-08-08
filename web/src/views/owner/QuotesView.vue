<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { errorMessage } from '@/api/client'
import { laborServicesApi, partsApi, quotesApi } from '@/api/garaj'
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
import { formatDate, formatMoney, formatQuantity } from '@/utils/format'

const route = useRoute()

const quotes = ref<QuoteListItem[]>([])
const selected = ref<QuoteDetail | null>(null)
const parts = ref<Part[]>([])
const services = ref<LaborService[]>([])

const statusFilter = ref<QuoteStatus | ''>('')
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
})

const catalog = computed(() =>
  newLine.value.lineType === LineType.Part
    ? parts.value.map((p) => ({ id: p.id, label: `${p.sku} · ${p.name}`, price: p.salePrice }))
    : services.value.map((s) => ({ id: s.id, label: `${s.code} · ${s.name}`, price: s.price })),
)

async function load() {
  loading.value = true
  error.value = ''
  try {
    const page = await quotesApi.list({
      status: statusFilter.value || undefined,
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
    })
    newLine.value = {
      lineType: line.lineType,
      catalogId: '',
      description: '',
      quantity: 1,
      unitPrice: 0,
      discount: 0,
    }
    return result
  })
}

function removeLine(lineId: string) {
  return run(() => quotesApi.removeLine(selected.value!.id, lineId))
}

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
  await load()
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
      <select v-model="statusFilter" @change="load">
        <option value="">Todas</option>
        <option v-for="(label, value) in QUOTE_STATUS_LABEL" :key="value" :value="Number(value)">
          {{ label }}
        </option>
      </select>
    </header>

    <p v-if="error" class="error">{{ error }}</p>

    <div class="layout">
      <div class="list">
        <p v-if="loading" class="muted">Cargando…</p>
        <p v-else-if="!quotes.length" class="muted">
          Todavía no hay cotizaciones. Se crean desde el detalle de una orden de trabajo.
        </p>

        <button
          v-for="q in quotes"
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
                <div class="muted small">{{ LINE_TYPE_LABEL[line.lineType] }}</div>
              </td>
              <td class="num muted">
                {{ formatQuantity(line.quantity) }} × {{ formatMoney(line.unitPrice) }}
                <div v-if="line.discount" class="small">−{{ formatMoney(line.discount) }}</div>
              </td>
              <td class="num">{{ formatMoney(line.total) }}</td>
              <td v-if="selected.isEditable" class="num">
                <button type="button" class="link" :disabled="busy" @click="removeLine(line.id)">
                  Quitar
                </button>
              </td>
            </tr>
          </tbody>
        </table>

        <div class="totals">
          <div><span>Subtotal</span><span>{{ formatMoney(selected.subtotal) }}</span></div>
          <div v-if="selected.discountTotal > 0">
            <span>Descuento</span><span>−{{ formatMoney(selected.discountTotal) }}</span>
          </div>
          <div v-if="selected.taxRate > 0">
            <span>ISV {{ selected.taxRate }}%</span><span>{{ formatMoney(selected.taxTotal) }}</span>
          </div>
          <div class="grand"><span>Total</span><span>{{ formatMoney(selected.total) }}</span></div>
        </div>

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
          <div class="row gap">
            <label>Cant.<input v-model.number="newLine.quantity" type="number" step="0.01" min="0.01" /></label>
            <label>Precio<input v-model.number="newLine.unitPrice" type="number" step="0.01" min="0" /></label>
            <label>Desc.<input v-model.number="newLine.discount" type="number" step="0.01" min="0" /></label>
            <button type="submit" :disabled="busy">Agregar</button>
          </div>
        </form>

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
          <a :href="quotesApi.pdfUrl(selected.id)" target="_blank" rel="noopener">PDF</a>
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

.item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  width: 100%;
  padding: 0.625rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
  text-align: left;
  cursor: pointer;
}

.item.active {
  border-color: var(--accent);
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

.link {
  padding: 0;
  border: none;
  background: none;
  color: var(--danger);
  font-size: 0.75rem;
  cursor: pointer;
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
</style>

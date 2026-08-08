<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, partsApi, stockApi } from '@/api/garaj'
import { useAuthStore } from '@/stores/auth'
import {
  STOCK_MOVEMENT_LABEL,
  type Branch,
  type Part,
  type StockItem,
  type StockMovement,
} from '@/types/domain'
import { formatDateTime, formatMoney, formatQuantity } from '@/utils/format'

const auth = useAuthStore()

const branches = ref<Branch[]>([])
const categories = ref<string[]>([])
const items = ref<StockItem[]>([])
const alerts = ref<StockItem[]>([])
const parts = ref<Part[]>([])

const branchId = ref('')
const search = ref('')
const category = ref('')
const onlyBelowMinimum = ref(false)

const error = ref('')
const busy = ref(false)
const loading = ref(false)

/** Panel lateral: alta de repuesto, entrada, ajuste, traslado o kardex. */
const panel = ref<'part' | 'receive' | 'adjust' | 'transfer' | 'kardex' | null>(null)
const selected = ref<StockItem | null>(null)
const movements = ref<StockMovement[]>([])

const partForm = ref({
  id: '',
  sku: '',
  name: '',
  description: '',
  brand: '',
  category: '',
  unit: 'u',
  costPrice: 0,
  salePrice: 0,
  isActive: true,
})

const moveForm = ref({
  partId: '',
  quantity: 1,
  unitCost: 0,
  reference: '',
  countedQuantity: 0,
  reason: '',
  toBranchId: '',
  notes: '',
})

const canManage = computed(() => auth.isOwner)

async function load() {
  loading.value = true
  error.value = ''
  try {
    const page = await stockApi.list({
      branchId: branchId.value || undefined,
      search: search.value || undefined,
      category: category.value || undefined,
      onlyBelowMinimum: onlyBelowMinimum.value || undefined,
      pageSize: 200,
    })
    items.value = page.items
    alerts.value = await stockApi.alerts(branchId.value || undefined)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el inventario.')
  } finally {
    loading.value = false
  }
}

async function run(action: () => Promise<unknown>) {
  busy.value = true
  error.value = ''
  try {
    await action()
    panel.value = null
    await load()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

function openNewPart() {
  partForm.value = {
    id: '', sku: '', name: '', description: '', brand: '', category: '',
    unit: 'u', costPrice: 0, salePrice: 0, isActive: true,
  }
  panel.value = 'part'
}

async function openEditPart(item: StockItem) {
  const page = await partsApi.list({ search: item.sku, includeInactive: true, pageSize: 5 })
  const part = page.items.find((p) => p.id === item.partId)
  if (!part) return

  partForm.value = {
    id: part.id,
    sku: part.sku,
    name: part.name,
    description: part.description ?? '',
    brand: part.brand ?? '',
    category: part.category ?? '',
    unit: part.unit,
    costPrice: part.costPrice,
    salePrice: part.salePrice,
    isActive: part.isActive,
  }
  panel.value = 'part'
}

function savePart() {
  const form = partForm.value
  const body = {
    sku: form.sku.trim(),
    name: form.name.trim(),
    description: form.description || null,
    brand: form.brand || null,
    category: form.category || null,
    unit: form.unit || 'u',
    costPrice: Number(form.costPrice),
    salePrice: Number(form.salePrice),
    isActive: form.isActive,
  }

  return run(async () => {
    if (form.id) await partsApi.update(form.id, body)
    else await partsApi.create(body)
    categories.value = await partsApi.categories()
  })
}

/** Para entrada y traslado hace falta elegir repuesto: se carga el catálogo al abrir. */
async function openMovement(kind: 'receive' | 'adjust' | 'transfer', item?: StockItem) {
  selected.value = item ?? null
  moveForm.value = {
    partId: item?.partId ?? '',
    quantity: 1,
    unitCost: 0,
    reference: '',
    countedQuantity: item?.quantity ?? 0,
    reason: '',
    toBranchId: branches.value.find((b) => b.id !== targetBranchId(item))?.id ?? '',
    notes: '',
  }

  if (!parts.value.length) {
    parts.value = (await partsApi.list({ pageSize: 200 })).items
  }

  panel.value = kind
}

/** La sucursal sobre la que se opera: la de la fila, o la del filtro, o la primera. */
function targetBranchId(item?: StockItem | null): string {
  return item?.branchId || branchId.value || branches.value[0]?.id || ''
}

function receive() {
  const form = moveForm.value
  return run(() =>
    stockApi.receive({
      branchId: targetBranchId(selected.value),
      partId: form.partId,
      quantity: Number(form.quantity),
      unitCost: Number(form.unitCost) || undefined,
      reference: form.reference || undefined,
    }),
  )
}

function adjust() {
  const form = moveForm.value
  return run(() =>
    stockApi.adjust({
      branchId: targetBranchId(selected.value),
      partId: form.partId,
      countedQuantity: Number(form.countedQuantity),
      reason: form.reason,
    }),
  )
}

function transfer() {
  const form = moveForm.value
  return run(() =>
    stockApi.transfer({
      fromBranchId: targetBranchId(selected.value),
      toBranchId: form.toBranchId,
      partId: form.partId,
      quantity: Number(form.quantity),
      notes: form.notes || undefined,
    }),
  )
}

async function openKardex(item: StockItem) {
  selected.value = item
  panel.value = 'kardex'
  movements.value = []

  try {
    const page = await stockApi.movements({
      branchId: item.branchId,
      partId: item.partId,
      pageSize: 50,
    })
    movements.value = page.items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el kardex.')
  }
}

watch([branchId, category, onlyBelowMinimum], load)

onMounted(async () => {
  branches.value = await branchesApi.list().catch(() => [])
  categories.value = await partsApi.categories().catch(() => [])
  await load()
})
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Inventario</h1>
        <p class="muted small">
          El stock no se edita: se mueve. Cada cambio queda en el kardex con su responsable.
        </p>
      </div>
      <button v-if="canManage" type="button" @click="openNewPart">Nuevo repuesto</button>
    </header>

    <p v-if="error" class="error">{{ error }}</p>

    <div v-if="alerts.length" class="alerts">
      <strong>Reposición:</strong>
      <span v-for="a in alerts.slice(0, 6)" :key="a.partId + a.branchId">
        {{ a.partName }} ({{ formatQuantity(a.quantity) }} {{ a.unit }} en {{ a.branchName }})
      </span>
      <span v-if="alerts.length > 6" class="muted">y {{ alerts.length - 6 }} más</span>
    </div>

    <form class="filters" @submit.prevent="load">
      <input v-model="search" placeholder="SKU, nombre o marca…" />
      <select v-model="branchId">
        <option value="">Todas las sucursales</option>
        <option v-for="b in branches" :key="b.id" :value="b.id">{{ b.name }}</option>
      </select>
      <select v-model="category">
        <option value="">Todas las categorías</option>
        <option v-for="c in categories" :key="c" :value="c">{{ c }}</option>
      </select>
      <label class="checkbox">
        <input v-model="onlyBelowMinimum" type="checkbox" />
        Solo bajo mínimo
      </label>
      <button type="submit" :disabled="loading">Buscar</button>
      <template v-if="canManage">
        <button type="button" @click="openMovement('receive')">Entrada</button>
        <button type="button" @click="openMovement('transfer')">Traslado</button>
      </template>
    </form>

    <p v-if="loading" class="muted">Cargando…</p>
    <p v-else-if="!items.length" class="muted">No hay existencias que coincidan.</p>

    <table v-else>
      <thead>
        <tr>
          <th>Repuesto</th>
          <th>Sucursal</th>
          <th class="num">Existencia</th>
          <th class="num">Mínimo</th>
          <th class="num">Venta</th>
          <th>Ubicación</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="item in items" :key="item.partId + item.branchId">
          <td>
            <strong>{{ item.partName }}</strong>
            <div class="muted small">{{ item.sku }} · {{ item.brand ?? '—' }}</div>
          </td>
          <td>{{ item.branchName }}</td>
          <td class="num" :class="{ low: item.isBelowMinimum }">
            {{ formatQuantity(item.quantity) }} {{ item.unit }}
          </td>
          <td class="num muted">{{ formatQuantity(item.minQuantity) }}</td>
          <td class="num">{{ formatMoney(item.salePrice) }}</td>
          <td class="muted small">{{ item.location ?? '—' }}</td>
          <td class="row-actions">
            <button type="button" @click="openKardex(item)">Kardex</button>
            <template v-if="canManage">
              <button type="button" @click="openMovement('receive', item)">Entrada</button>
              <button type="button" @click="openMovement('adjust', item)">Ajuste</button>
              <button type="button" @click="openEditPart(item)">Editar</button>
            </template>
          </td>
        </tr>
      </tbody>
    </table>

    <!-- Panel lateral -->
    <div v-if="panel" class="drawer-backdrop" @click.self="panel = null">
      <aside class="drawer">
        <header>
          <h2>
            {{
              panel === 'part' ? (partForm.id ? 'Editar repuesto' : 'Nuevo repuesto')
              : panel === 'receive' ? 'Entrada de repuestos'
              : panel === 'adjust' ? 'Ajuste por conteo'
              : panel === 'transfer' ? 'Traslado entre sucursales'
              : 'Kardex'
            }}
          </h2>
          <button type="button" @click="panel = null">Cerrar</button>
        </header>

        <form v-if="panel === 'part'" @submit.prevent="savePart">
          <label>SKU<input v-model="partForm.sku" required /></label>
          <label>Nombre<input v-model="partForm.name" required /></label>
          <label>Marca<input v-model="partForm.brand" /></label>
          <label>Categoría<input v-model="partForm.category" list="categorias" /></label>
          <datalist id="categorias">
            <option v-for="c in categories" :key="c" :value="c" />
          </datalist>
          <label>Unidad<input v-model="partForm.unit" placeholder="u, lt, jgo" /></label>
          <label>Costo<input v-model.number="partForm.costPrice" type="number" step="0.01" min="0" /></label>
          <label>Precio de venta<input v-model.number="partForm.salePrice" type="number" step="0.01" min="0" /></label>
          <label class="checkbox">
            <input v-model="partForm.isActive" type="checkbox" /> Activo
          </label>
          <button type="submit" :disabled="busy">Guardar</button>
        </form>

        <form v-else-if="panel === 'receive'" @submit.prevent="receive">
          <p class="muted small">Entra a {{ branches.find((b) => b.id === targetBranchId(selected))?.name }}.</p>
          <label>
            Repuesto
            <select v-model="moveForm.partId" required :disabled="!!selected">
              <option value="" disabled>Elija…</option>
              <option v-for="p in parts" :key="p.id" :value="p.id">{{ p.sku }} · {{ p.name }}</option>
            </select>
          </label>
          <label>Cantidad<input v-model.number="moveForm.quantity" type="number" step="0.001" min="0.001" required /></label>
          <label>Costo unitario<input v-model.number="moveForm.unitCost" type="number" step="0.01" min="0" /></label>
          <label>Referencia<input v-model="moveForm.reference" placeholder="Nº de factura" /></label>
          <button type="submit" :disabled="busy">Registrar entrada</button>
        </form>

        <form v-else-if="panel === 'adjust'" @submit.prevent="adjust">
          <p class="muted small">
            {{ selected?.partName }} en {{ selected?.branchName }}. El sistema tiene
            {{ formatQuantity(selected?.quantity) }} {{ selected?.unit }}.
          </p>
          <label>
            Cantidad contada
            <input v-model.number="moveForm.countedQuantity" type="number" step="0.001" min="0" required />
          </label>
          <label>
            Motivo
            <input v-model="moveForm.reason" required placeholder="Conteo físico, rotura, error de captura…" />
          </label>
          <button type="submit" :disabled="busy">Ajustar</button>
        </form>

        <form v-else-if="panel === 'transfer'" @submit.prevent="transfer">
          <p class="muted small">Sale de {{ branches.find((b) => b.id === targetBranchId(selected))?.name }}.</p>
          <label>
            Repuesto
            <select v-model="moveForm.partId" required :disabled="!!selected">
              <option value="" disabled>Elija…</option>
              <option v-for="p in parts" :key="p.id" :value="p.id">{{ p.sku }} · {{ p.name }}</option>
            </select>
          </label>
          <label>
            Destino
            <select v-model="moveForm.toBranchId" required>
              <option
                v-for="b in branches.filter((b) => b.id !== targetBranchId(selected))"
                :key="b.id"
                :value="b.id"
              >
                {{ b.name }}
              </option>
            </select>
          </label>
          <label>Cantidad<input v-model.number="moveForm.quantity" type="number" step="0.001" min="0.001" required /></label>
          <label>Nota<input v-model="moveForm.notes" /></label>
          <button type="submit" :disabled="busy">Trasladar</button>
        </form>

        <div v-else-if="panel === 'kardex'">
          <p class="muted small">{{ selected?.partName }} en {{ selected?.branchName }}</p>
          <p v-if="!movements.length" class="muted">Sin movimientos.</p>
          <ol v-else class="kardex">
            <li v-for="m in movements" :key="m.id">
              <div class="line">
                <strong>{{ STOCK_MOVEMENT_LABEL[m.type] }}</strong>
                <span :class="m.signedQuantity < 0 ? 'out' : 'in'">
                  {{ m.signedQuantity > 0 ? '+' : '' }}{{ formatQuantity(m.signedQuantity) }}
                </span>
                <span class="muted">saldo {{ formatQuantity(m.resultingQuantity) }}</span>
              </div>
              <div class="muted small">
                {{ formatDateTime(m.movedAt) }} · {{ m.movedByName }}
                <template v-if="m.workOrderNumber"> · orden {{ m.workOrderNumber }}</template>
                <template v-if="m.counterpartBranchName"> · {{ m.counterpartBranchName }}</template>
                <template v-if="m.reference"> · {{ m.reference }}</template>
              </div>
              <p v-if="m.notes" class="small">{{ m.notes }}</p>
            </li>
          </ol>
        </div>
      </aside>
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

.alerts {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  padding: 0.625rem 0.875rem;
  margin-bottom: 1rem;
  border: 1px solid var(--border);
  border-left: 3px solid var(--danger);
  border-radius: 6px;
  background: var(--surface);
  font-size: 0.875rem;
}

.filters {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.filters input[type='text'],
.filters input:not([type]) {
  min-width: 14rem;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9375rem;
}

th,
td {
  padding: 0.5rem 0.625rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
  vertical-align: top;
}

th {
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.num {
  text-align: right;
  white-space: nowrap;
}

.low {
  color: var(--danger);
  font-weight: 600;
}

.row-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
  justify-content: flex-end;
}

.row-actions button {
  font-size: 0.75rem;
  padding: 0.2rem 0.5rem;
}

.drawer-backdrop {
  position: fixed;
  inset: 0;
  z-index: 40;
  display: flex;
  justify-content: flex-end;
  background: rgb(0 0 0 / 0.4);
}

.drawer {
  width: min(26rem, 100%);
  height: 100%;
  overflow-y: auto;
  padding: 1rem 1.25rem 2rem;
  background: var(--surface);
  border-left: 1px solid var(--border);
}

.drawer header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.drawer h2 {
  margin: 0;
  font-size: 1rem;
}

.drawer form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.drawer label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.drawer label input,
.drawer label select {
  width: 100%;
}

.checkbox {
  flex-direction: row !important;
  align-items: center;
  gap: 0.5rem;
}

.kardex {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.875rem;
}

.kardex .line {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
}

.kardex p {
  margin: 0.125rem 0 0;
}

.in {
  color: var(--success, #15803d);
  font-variant-numeric: tabular-nums;
}

.out {
  color: var(--danger);
  font-variant-numeric: tabular-nums;
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

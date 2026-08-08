<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { partsApi, workOrdersApi } from '@/api/garaj'
import type { Part, WorkOrderPart } from '@/types/domain'
import { formatMoney, formatQuantity } from '@/utils/format'

const props = defineProps<{
  workOrderId: string
  parts: WorkOrderPart[]
  total: number
  /** Falso para el Cliente: ve lo que le pusieron, no lo modifica. */
  canEdit: boolean
  /** El taller ve el costo; el cliente, solo el precio. */
  showCost: boolean
}>()

const emit = defineEmits<{ changed: [] }>()

const catalog = ref<Part[]>([])
const search = ref('')
const selectedPartId = ref('')
const quantity = ref(1)
const error = ref('')
const busy = ref(false)
const adding = ref(false)

const selectedPart = computed(() => catalog.value.find((p) => p.id === selectedPartId.value))

/** Lo que se le va a cobrar al cliente por lo que se está por agregar. */
const preview = computed(() =>
  selectedPart.value ? selectedPart.value.salePrice * (Number(quantity.value) || 0) : 0,
)

async function loadCatalog() {
  try {
    const page = await partsApi.list({ search: search.value || undefined, pageSize: 50 })
    catalog.value = page.items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el catálogo.')
  }
}

async function open() {
  adding.value = true
  error.value = ''
  if (!catalog.value.length) await loadCatalog()
}

async function add() {
  if (!selectedPartId.value) return

  busy.value = true
  error.value = ''
  try {
    await workOrdersApi.addPart(props.workOrderId, {
      partId: selectedPartId.value,
      quantity: Number(quantity.value),
    })
    selectedPartId.value = ''
    quantity.value = 1
    adding.value = false
    emit('changed')
  } catch (e) {
    // El 409 de existencia insuficiente dice cuánto queda: se muestra tal cual.
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

async function remove(line: WorkOrderPart) {
  if (!confirm(`¿Quitar ${line.partName} y devolverlo a la bodega?`)) return

  busy.value = true
  error.value = ''
  try {
    await workOrdersApi.removePart(props.workOrderId, line.id)
    emit('changed')
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

let searchTimer: ReturnType<typeof setTimeout> | undefined
watch(search, () => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(loadCatalog, 250)
})
</script>

<template>
  <article class="card">
    <header>
      <h2>Repuestos</h2>
      <button v-if="canEdit && !adding" type="button" @click="open">Agregar</button>
    </header>

    <p v-if="error" class="error">{{ error }}</p>

    <p v-if="!parts.length && !adding" class="muted">
      Todavía no se ha cargado ningún repuesto a esta orden.
    </p>

    <table v-if="parts.length">
      <tbody>
        <tr v-for="line in parts" :key="line.id">
          <td>
            <strong>{{ line.partName }}</strong>
            <div class="muted small">
              {{ line.sku }}
              <template v-if="line.taskTitle"> · {{ line.taskTitle }}</template>
              <template v-if="showCost && line.unitCost">
                · costo {{ formatMoney(line.unitCost) }}
              </template>
            </div>
          </td>
          <td class="num muted">
            {{ formatQuantity(line.quantity) }} {{ line.unit }} × {{ formatMoney(line.unitPrice) }}
          </td>
          <td class="num">{{ formatMoney(line.total) }}</td>
          <td v-if="canEdit" class="num">
            <button type="button" class="link" :disabled="busy" @click="remove(line)">Quitar</button>
          </td>
        </tr>
      </tbody>
      <tfoot>
        <tr>
          <td colspan="2">Total en repuestos</td>
          <td class="num"><strong>{{ formatMoney(total) }}</strong></td>
          <td v-if="canEdit"></td>
        </tr>
      </tfoot>
    </table>

    <form v-if="adding" class="add" @submit.prevent="add">
      <input v-model="search" placeholder="Buscar por SKU, nombre o marca…" />

      <select v-model="selectedPartId" size="6">
        <option v-for="p in catalog" :key="p.id" :value="p.id">
          {{ p.sku }} · {{ p.name }} — {{ formatQuantity(p.totalQuantity) }} {{ p.unit }} ·
          {{ formatMoney(p.salePrice) }}
        </option>
      </select>

      <div class="row">
        <label>
          Cantidad
          <input v-model.number="quantity" type="number" step="0.001" min="0.001" required />
        </label>
        <span v-if="selectedPart" class="muted">= {{ formatMoney(preview) }}</span>
      </div>

      <div class="row">
        <button type="submit" :disabled="busy || !selectedPartId">Agregar y descontar</button>
        <button type="button" @click="adding = false">Cancelar</button>
      </div>

      <p class="muted small">
        Sale de la bodega de la sucursal de esta orden y queda registrado en el kardex.
      </p>
    </form>
  </article>
</template>

<style scoped>
.card {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 0.75rem;
}

h2 {
  margin: 0;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
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

tfoot td {
  border-bottom: none;
  padding-top: 0.625rem;
  color: var(--text-muted);
}

.num {
  text-align: right;
  white-space: nowrap;
}

.link {
  padding: 0;
  border: none;
  background: none;
  color: var(--danger);
  font-size: 0.75rem;
  cursor: pointer;
}

.add {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

.add select {
  width: 100%;
  font-family: ui-monospace, monospace;
  font-size: 0.8125rem;
}

.row {
  display: flex;
  align-items: flex-end;
  gap: 0.75rem;
}

.row label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.row input {
  width: 7rem;
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

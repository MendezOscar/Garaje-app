<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { usersApi, workOrdersApi } from '@/api/garaj'
import { useAuthStore } from '@/stores/auth'
import {
  KANBAN_COLUMNS,
  WORK_ORDER_STATUS_LABEL,
  VEHICLE_TYPE_LABEL,
  WorkOrderStatus,
  type User,
  type WorkOrderListItem,
} from '@/types/domain'
import { relativeTime } from '@/utils/format'

const auth = useAuthStore()

const orders = ref<WorkOrderListItem[]>([])
const technicians = ref<User[]>([])
const search = ref('')
const branchId = ref<string>('')
const verCerradas = ref(false)
const loading = ref(false)
const error = ref('')

/**
 * Con una búsqueda escrita se dejan de filtrar las cerradas aunque el interruptor esté
 * apagado: quien escribe una placa la quiere encontrar aunque el carro ya salió del taller,
 * y hasta ahora no aparecía por ningún lado.
 */
const soloAbiertas = computed(() => !verCerradas.value && !search.value.trim())

/**
 * Trae las órdenes de una vez y agrupa en memoria. Un taller tiene decenas de órdenes vivas,
 * no miles: siete peticiones —una por columna— sería peor.
 */
async function load() {
  loading.value = true
  error.value = ''

  try {
    const result = await workOrdersApi.list({
      onlyOpen: soloAbiertas.value,
      branchId: branchId.value || undefined,
      search: search.value || undefined,
      pageSize: 200,
    })
    orders.value = result.items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar las órdenes.')
  } finally {
    loading.value = false
  }
}

/** Entregada y Cancelada van al final, y solo cuando hay algo que poner en ellas. */
const CERRADAS: WorkOrderStatus[] = [WorkOrderStatus.Delivered, WorkOrderStatus.Cancelled]

const columns = computed(() => {
  const visibles = [
    ...KANBAN_COLUMNS,
    ...CERRADAS.filter((status) => orders.value.some((o) => o.status === status)),
  ]

  return visibles.map((status) => ({
    status,
    label: WORK_ORDER_STATUS_LABEL[status],
    items: orders.value.filter((o) => o.status === status),
  }))
})

const abiertas = computed(
  () => orders.value.filter((o) => !CERRADAS.includes(o.status)).length,
)

const branches = computed(() => auth.user?.branches ?? [])

async function assign(order: WorkOrderListItem, technicianId: string) {
  try {
    await workOrdersApi.assign(order.id, technicianId || null)
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo asignar el técnico.')
  }
}

/** Solo se ofrecen los técnicos de la sucursal de la orden: el backend rechaza los demás. */
function techniciansFor(order: WorkOrderListItem) {
  return technicians.value.filter((t) => t.isActive && t.branchIds.includes(order.branchId))
}

function isLate(order: WorkOrderListItem) {
  // Una orden entregada ya no se atrasa: marcarla en rojo sería alarmar por algo cerrado.
  if (CERRADAS.includes(order.status)) return false

  return order.promisedAt != null && new Date(order.promisedAt) < new Date()
}

onMounted(async () => {
  technicians.value = await usersApi.list('Technician').catch(() => [])
  await load()
})

watch([search, branchId, verCerradas], load)
</script>

<template>
  <section>
    <header class="toolbar">
      <h1>Órdenes de trabajo</h1>

      <input v-model="search" type="search" placeholder="Número, placa o cliente" />

      <select v-if="branches.length > 1" v-model="branchId">
        <option value="">Todas las sucursales</option>
        <option v-for="b in branches" :key="b.id" :value="b.id">{{ b.name }}</option>
      </select>

      <label class="checkbox" title="Las entregadas y las canceladas salen del tablero">
        <input v-model="verCerradas" type="checkbox" />
        Ver entregadas
      </label>

      <span class="count">{{ abiertas }} abiertas</span>
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="loading" class="muted">Cargando…</p>

    <div class="board">
      <div v-for="column in columns" :key="column.status" class="column">
        <h2>
          {{ column.label }}
          <span class="count">{{ column.items.length }}</span>
        </h2>

        <RouterLink
          v-for="order in column.items"
          :key="order.id"
          class="card"
          :to="{ name: 'work-order', params: { id: order.id } }"
        >
          <div class="card-head">
            <strong>{{ order.number }}</strong>
            <span v-if="isLate(order)" class="late" title="Pasó la fecha prometida">Atrasada</span>
          </div>

          <div class="vehicle">
            {{ order.vehicleLabel }}
            <span v-if="order.plate" class="plate">{{ order.plate }}</span>
          </div>

          <div class="muted small">
            {{ VEHICLE_TYPE_LABEL[order.vehicleType] }} · {{ order.customerName }}
          </div>

          <p class="description">{{ order.description }}</p>

          <div v-if="order.taskCount > 0" class="progress" :title="`${order.tasksDone} de ${order.taskCount} pasos`">
            <div class="track">
              <div class="bar" :style="{ width: `${(order.tasksDone / order.taskCount) * 100}%` }" />
            </div>
            <span class="small muted">{{ order.tasksDone }}/{{ order.taskCount }}</span>
          </div>

          <select
            class="assign"
            :value="order.assignedTechnicianId ?? ''"
            @click.prevent.stop
            @change="assign(order, ($event.target as HTMLSelectElement).value)"
          >
            <option value="">Sin asignar</option>
            <option v-for="t in techniciansFor(order)" :key="t.id" :value="t.id">
              {{ t.fullName }}
            </option>
          </select>

          <div class="muted small">Abierta {{ relativeTime(order.openedAt) }}</div>
        </RouterLink>

        <p v-if="!column.items.length && !loading" class="empty">—</p>
      </div>
    </div>
  </section>
</template>

<style scoped>
.toolbar {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
  margin-bottom: 1rem;
}

.toolbar h1 {
  margin: 0;
  margin-right: auto;
}

.board {
  display: grid;
  grid-auto-flow: column;
  grid-auto-columns: minmax(15rem, 1fr);
  gap: 0.75rem;
  overflow-x: auto;
  padding-bottom: 0.5rem;
}

.column {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  min-width: 15rem;
}

.column h2 {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin: 0;
  padding-bottom: 0.375rem;
  border-bottom: 2px solid var(--border);
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.count {
  padding: 0.0625rem 0.375rem;
  border-radius: 999px;
  background: var(--surface-alt);
  font-size: 0.75rem;
  color: var(--text-muted);
}

.card {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  padding: 0.625rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
  color: inherit;
  text-decoration: none;
}

.card:hover {
  border-color: var(--accent);
  text-decoration: none;
}

.card-head {
  display: flex;
  justify-content: space-between;
  gap: 0.5rem;
}

.late {
  padding: 0 0.375rem;
  border-radius: 999px;
  background: color-mix(in srgb, var(--danger) 20%, transparent);
  color: var(--danger);
  font-size: 0.6875rem;
}

.vehicle {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  flex-wrap: wrap;
}

.plate {
  padding: 0 0.25rem;
  border: 1px solid var(--border);
  border-radius: 3px;
  font-family: ui-monospace, monospace;
  font-size: 0.75rem;
}

.description {
  margin: 0;
  font-size: 0.8125rem;
  /* La descripción del ingreso puede ser larga; en la tarjeta bastan dos líneas. */
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.progress {
  display: flex;
  align-items: center;
  gap: 0.375rem;
}

.track {
  flex: 1;
  height: 4px;
  border-radius: 2px;
  background: var(--surface-alt);
  overflow: hidden;
}

.bar {
  height: 100%;
  border-radius: 2px;
  background: var(--accent);
}

.assign {
  padding: 0.25rem;
  font-size: 0.75rem;
}

.small {
  font-size: 0.75rem;
}

.muted {
  color: var(--text-muted);
}

.checkbox {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.875rem;
  color: var(--text-muted);
}

.empty {
  margin: 0;
  padding: 0.5rem;
  text-align: center;
  color: var(--text-muted);
}

.error {
  color: var(--danger);
}
</style>

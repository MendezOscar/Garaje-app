<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { usersApi, workOrdersApi } from '@/api/garaj'
import StatusBadge from '@/components/StatusBadge.vue'
import { useAuthStore } from '@/stores/auth'
import {
  VEHICLE_TYPE_LABEL,
  WorkOrderStatus,
  type User,
  type WorkOrderListItem,
} from '@/types/domain'
import { formatDateTime, relativeTime } from '@/utils/format'

const auth = useAuthStore()

const orders = ref<WorkOrderListItem[]>([])
const technicians = ref<User[]>([])
const search = ref('')
const branchId = ref<string>('')
const verCerradas = ref(false)
const loading = ref(false)
const error = ref('')

/** Tablero o lista. Se recuerda: cada taller tiene su forma de mirar el trabajo. */
const vista = ref<'tablero' | 'lista'>(
  (localStorage.getItem('garaj.vista-ordenes') as 'tablero' | 'lista') || 'tablero',
)
watch(vista, (valor) => localStorage.setItem('garaj.vista-ordenes', valor))

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

/**
 * Cuatro carriles en vez de nueve columnas.
 *
 * El tablero tenía una columna por estado, con scroll horizontal y mínimo de 15rem cada una:
 * en un taller pequeño la mayoría de las tarjetas vive en dos o tres, y las demás eran
 * columnas vacías que empujaban el trabajo fuera de la pantalla. El estado exacto no se
 * pierde —va en la insignia de cada tarjeta—, que es donde de verdad hace falta.
 */
const CARRILES: { clave: string; titulo: string; tono: string; estados: WorkOrderStatus[] }[] = [
  {
    clave: 'entrando',
    titulo: 'Entrando',
    tono: 'activo',
    estados: [WorkOrderStatus.Received, WorkOrderStatus.Diagnosing],
  },
  {
    clave: 'trabajo',
    titulo: 'En trabajo',
    tono: 'activo',
    estados: [WorkOrderStatus.InProgress, WorkOrderStatus.Testing],
  },
  {
    clave: 'detenidas',
    titulo: 'Detenidas',
    tono: 'detenido',
    estados: [WorkOrderStatus.WaitingApproval, WorkOrderStatus.WaitingParts],
  },
  {
    clave: 'listas',
    titulo: 'Listas para entrega',
    tono: 'listo',
    estados: [WorkOrderStatus.Ready],
  },
  {
    clave: 'cerradas',
    titulo: 'Cerradas',
    tono: 'gris',
    estados: CERRADAS,
  },
]

const carriles = computed(() =>
  CARRILES.map((carril) => ({
    ...carril,
    items: orders.value.filter((o) => carril.estados.includes(o.status)),
  })).filter((carril) => carril.clave !== 'cerradas' || carril.items.length > 0),
)

/**
 * En el teléfono los carriles no caben uno al lado del otro, y apilarlos deja el trabajo a
 * cuatro pantallas de scroll. Se eligen con un chip y se ve una sola columna.
 */
const consulta = window.matchMedia('(max-width: 60rem)')
const angosto = ref(consulta.matches)
const carrilElegido = ref('entrando')

function alCambiarAncho(e: MediaQueryListEvent) {
  angosto.value = e.matches
}

const visibles = computed(() =>
  angosto.value && vista.value === 'tablero'
    ? carriles.value.filter((c) => c.clave === carrilElegido.value)
    : carriles.value,
)

/** La lista densa: todas las órdenes en una tabla, ordenadas por lo que urge. */
const filas = computed(() =>
  [...orders.value].sort((a, b) => {
    const atrasoA = isLate(a) ? 0 : 1
    const atrasoB = isLate(b) ? 0 : 1
    if (atrasoA !== atrasoB) return atrasoA - atrasoB

    return (a.promisedAt ?? '9999').localeCompare(b.promisedAt ?? '9999')
  }),
)

const abiertas = computed(
  () => orders.value.filter((o) => !CERRADAS.includes(o.status)).length,
)

const atrasadas = computed(() => orders.value.filter(isLate).length)

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
  consulta.addEventListener('change', alCambiarAncho)
  technicians.value = await usersApi.list('Technician').catch(() => [])
  await load()
})

onUnmounted(() => consulta.removeEventListener('change', alCambiarAncho))

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

      <!-- Tablero para ver el patio; lista para cuando hay treinta órdenes vivas. -->
      <div class="segmento">
        <button type="button" :class="{ on: vista === 'tablero' }" @click="vista = 'tablero'">
          Tablero
        </button>
        <button type="button" :class="{ on: vista === 'lista' }" @click="vista = 'lista'">
          Lista
        </button>
      </div>

      <span class="count">{{ abiertas }} abiertas</span>
      <span v-if="atrasadas" class="count alerta">{{ atrasadas }} atrasadas</span>
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="loading" class="muted">Cargando…</p>

    <!-- En pantalla angosta el carril se elige con un chip: cuatro columnas no caben. -->
    <div v-if="angosto && vista === 'tablero'" class="chips">
      <button
        v-for="carril in carriles"
        :key="carril.clave"
        type="button"
        :class="{ on: carril.clave === carrilElegido }"
        @click="carrilElegido = carril.clave"
      >
        {{ carril.titulo }} {{ carril.items.length }}
      </button>
    </div>

    <div v-if="vista === 'tablero'" class="board" :class="{ angosto }">
      <div v-for="carril in visibles" :key="carril.clave" class="column">
        <h2>
          <span class="punto" :class="carril.tono" />
          {{ carril.titulo }}
          <span class="count">{{ carril.items.length }}</span>
        </h2>

        <RouterLink
          v-for="order in carril.items"
          :key="order.id"
          class="card"
          :to="{ name: 'work-order', params: { id: order.id } }"
        >
          <div class="card-head">
            <strong class="num">{{ order.number }}</strong>
            <span v-if="isLate(order)" class="late" title="Pasó la fecha prometida">Atrasada</span>
            <!-- El estado exacto: el carril agrupa, la insignia precisa. -->
            <StatusBadge :status="order.status" />
          </div>

          <div class="vehicle">
            {{ order.vehicleLabel }}
            <span v-if="order.plate" class="plate num">{{ order.plate }}</span>
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

          <div class="muted small">
            Abierta {{ relativeTime(order.openedAt) }}
            <template v-if="order.promisedAt">
              · prometida {{ relativeTime(order.promisedAt) }}
            </template>
          </div>
        </RouterLink>

        <p v-if="!carril.items.length && !loading" class="empty">—</p>
      </div>
    </div>

    <!-- La alternativa densa. Con treinta órdenes vivas el tablero se vuelve un paisaje. -->
    <div v-else class="tabla">
      <table>
        <thead>
          <tr>
            <th>Folio</th>
            <th>Vehículo</th>
            <th>Cliente</th>
            <th>Estado</th>
            <th>Técnico</th>
            <th>Prometida</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="order in filas" :key="order.id" :class="{ tarde: isLate(order) }">
            <td class="num">
              <RouterLink :to="{ name: 'work-order', params: { id: order.id } }">
                {{ order.number }}
              </RouterLink>
            </td>
            <td>
              {{ order.vehicleLabel }}
              <span v-if="order.plate" class="plate num">{{ order.plate }}</span>
            </td>
            <td>{{ order.customerName }}</td>
            <td><StatusBadge :status="order.status" /></td>
            <td class="muted">{{ order.assignedTechnicianName ?? 'Sin asignar' }}</td>
            <td class="muted small">
              {{ order.promisedAt ? formatDateTime(order.promisedAt) : '—' }}
            </td>
          </tr>
          <tr v-if="!filas.length && !loading">
            <td colspan="6" class="empty">No hay órdenes con esos filtros.</td>
          </tr>
        </tbody>
      </table>
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

.segmento {
  display: inline-flex;
  padding: 2px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface-alt);
}

.segmento button {
  padding: 0.25rem 0.75rem;
  border: none;
  border-radius: 4px;
  background: none;
  color: var(--text-muted);
  font-size: 0.8125rem;
}

.segmento button.on {
  background: var(--surface);
  color: var(--text);
  font-weight: 600;
}

.chips {
  display: flex;
  gap: 0.5rem;
  overflow-x: auto;
  margin-bottom: 0.75rem;
  padding-bottom: 0.25rem;
}

.chips button {
  flex-shrink: 0;
  padding: 0.3125rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
  color: var(--text-muted);
  font-size: 0.8125rem;
}

.chips button.on {
  border-color: transparent;
  background: color-mix(in srgb, var(--accent) 16%, transparent);
  color: var(--accent);
  font-weight: 600;
}

/* Cuatro carriles caben en la pantalla sin scroll horizontal. */
.board {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 0.75rem;
  align-items: start;
}

.board.angosto {
  grid-template-columns: 1fr;
}

.column {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  min-width: 0;
}

.column h2 {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin: 0;
  padding-bottom: 0.375rem;
  border-bottom: 2px solid var(--border);
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.column h2 .count {
  margin-left: auto;
}

.punto {
  width: 8px;
  height: 8px;
  border-radius: 999px;
  background: var(--text-muted);
}

.punto.activo {
  background: var(--accent);
}

.punto.detenido {
  background: var(--warning);
}

.punto.listo {
  background: var(--success);
}

.count {
  padding: 0.0625rem 0.375rem;
  border-radius: 999px;
  background: var(--surface-alt);
  font-size: 0.75rem;
  color: var(--text-muted);
}

.count.alerta {
  background: color-mix(in srgb, var(--danger) 16%, transparent);
  color: var(--danger);
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
  align-items: center;
  gap: 0.375rem;
  flex-wrap: wrap;
}

.card-head strong {
  font-size: 0.8125rem;
  white-space: nowrap;
  margin-right: auto;
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

/* ---------------------------------------------------------------- lista */

.tabla {
  overflow-x: auto;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}

th {
  padding: 0.625rem 0.75rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
  font-size: 0.6875rem;
  font-weight: 600;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--text-muted);
  white-space: nowrap;
}

td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid var(--border);
}

tbody tr:last-child td {
  border-bottom: none;
}

/* La fila atrasada se marca en el borde y no en el fondo: con veinte atrasadas, veinte
   franjas rojas dejan de decir nada. */
tr.tarde td:first-child {
  box-shadow: inset 3px 0 0 var(--danger);
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

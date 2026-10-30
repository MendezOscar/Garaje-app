<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { usersApi, workOrdersApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import FilterBar from '@/components/FilterBar.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import { useTaller } from '@/composables/useTaller'
import { useAuthStore } from '@/stores/auth'
import {
  VEHICLE_TYPE_LABEL,
  WorkOrderStatus,
  type User,
  type WorkOrderListItem,
} from '@/types/domain'
import { formatDateTime, relativeTime } from '@/utils/format'

const auth = useAuthStore()

const { vocabulario, cargar: cargarTaller } = useTaller()

const orders = ref<WorkOrderListItem[]>([])
const technicians = ref<User[]>([])
const search = ref('')
/** La sucursal sale del menú, no de esta pantalla: es la misma para todo el panel. */
const branchId = computed(() => auth.activeBranchId ?? '')
const tecnicoId = ref<string>('')

/**
 * La pestaña que se está mirando. «Todas» es la única que pide al servidor las entregadas y
 * las canceladas: en un taller con historia son miles y no se traen para contarlas.
 */
const pestana = ref<'taller' | 'listas' | 'atrasadas' | 'todas'>('taller')

/** Cuántas hay en total según el servidor, para el renglón de «Todas». */
const totalDelServidor = ref(0)
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
const soloAbiertas = computed(() => pestana.value !== 'todas' && !search.value.trim())

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
    totalDelServidor.value = result.total
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
const CARRILES: {
  clave: string
  titulo: string
  tono: string
  estados: WorkOrderStatus[]
  /** Qué cae en este carril. Se enseña cuando está vacío, que es cuando hace falta. */
  vacio: string
}[] = [
  {
    clave: 'entrando',
    titulo: 'Entrando',
    tono: 'activo',
    estados: [WorkOrderStatus.Received, WorkOrderStatus.Diagnosing],
    vacio: 'Aquí caen las recién recibidas y las que están en diagnóstico.',
  },
  {
    clave: 'trabajo',
    titulo: 'En trabajo',
    tono: 'activo',
    estados: [WorkOrderStatus.InProgress, WorkOrderStatus.Testing],
    vacio: 'Aquí caen las que están en proceso y las que se están probando.',
  },
  {
    clave: 'detenidas',
    titulo: 'Detenidas',
    tono: 'detenido',
    estados: [WorkOrderStatus.WaitingApproval, WorkOrderStatus.WaitingParts],
    vacio: 'Aquí caen las que esperan aprobación del cliente o un repuesto.',
  },
  {
    clave: 'listas',
    titulo: 'Listas para entrega',
    tono: 'listo',
    estados: [WorkOrderStatus.Ready],
    vacio: 'Aquí caen las terminadas, esperando que el cliente las venga a traer.',
  },
  {
    clave: 'cerradas',
    titulo: 'Cerradas',
    tono: 'gris',
    estados: CERRADAS,
    vacio: 'Aquí caen las entregadas y las canceladas.',
  },
]

/**
 * Días que el vehículo lleva listo sin que nadie lo retire. Cero si todavía no está listo o
 * si lo avisaron hoy: el distintivo es para el que ya está ocupando lugar.
 */
function diasSinRetirar(order: WorkOrderListItem) {
  if (order.status !== WorkOrderStatus.Ready || !order.readyNotifiedAt) return 0
  return Math.floor((Date.now() - new Date(order.readyNotifiedAt).getTime()) / 86_400_000)
}

const carriles = computed(() =>
  CARRILES.map((carril) => ({
    ...carril,
    items: filtradas.value.filter((o) => carril.estados.includes(o.status)),
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

/** Lo que cada pestaña enseña de lo ya cargado. */
const porPestana = computed(() => {
  const todas = orders.value

  switch (pestana.value) {
    case 'listas':
      return todas.filter((o) => o.status === WorkOrderStatus.Ready)
    case 'atrasadas':
      return todas.filter(isLate)
    default:
      return todas
  }
})

/**
 * Lo que se está mirando: la pestaña y el técnico elegidos. Lo comparten el tablero y la
 * tabla, para que cambiar de vista no cambie lo que se ve.
 */
const filtradas = computed(() =>
  porPestana.value.filter((o) => !tecnicoId.value || o.assignedTechnicianId === tecnicoId.value),
)

/** La lista densa: las órdenes de la pestaña en una tabla, ordenadas por lo que urge. */
const filas = computed(() =>
  [...filtradas.value]
    .sort((a, b) => {
    const atrasoA = isLate(a) ? 0 : 1
    const atrasoB = isLate(b) ? 0 : 1
    if (atrasoA !== atrasoB) return atrasoA - atrasoB

      return (a.promisedAt ?? '9999').localeCompare(b.promisedAt ?? '9999')
    }),
)

/** Los renglones de las pestañas, con su cuenta. */
const pestanas = computed(() => [
  {
    clave: 'taller' as const,
    titulo: 'En el taller',
    cuantas: orders.value.filter((o) => !CERRADAS.includes(o.status)).length,
  },
  {
    clave: 'listas' as const,
    titulo: 'Listas para entrega',
    cuantas: orders.value.filter((o) => o.status === WorkOrderStatus.Ready).length,
  },
  {
    clave: 'atrasadas' as const,
    titulo: 'Atrasadas',
    cuantas: atrasadas.value,
    tono: 'alerta',
  },
  { clave: 'todas' as const, titulo: 'Todas', cuantas: totalDelServidor.value },
])

const atrasadas = computed(() => orders.value.filter(isLate).length)

const branches = computed(() => auth.user?.branches ?? [])

/** Las tarjetas que tienen la lista de técnicos abierta. */
const asignando = ref(new Set<string>())

async function assign(order: WorkOrderListItem, technicianId: string) {
  try {
    await workOrdersApi.assign(order.id, technicianId || null)
    asignando.value.delete(order.id)
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo asignar el técnico.')
  }
}

/** Solo se ofrecen los técnicos de la sucursal de la orden: el backend rechaza los demás. */
function techniciansFor(order: WorkOrderListItem) {
  return technicians.value.filter((t) => t.isActive && t.branchIds.includes(order.branchId))
}

/**
 * Cuántos días lleva tarde. Es lo que el Dueño pregunta —«¿cuánto hace que debía estar?»—
 * y lo que antes había que calcular leyendo «prometida hace 2 meses».
 */
function diasDeAtraso(order: WorkOrderListItem) {
  if (!isLate(order)) return 0

  const dias = Math.floor(
    (Date.now() - new Date(order.promisedAt!).getTime()) / 86_400_000,
  )
  return Math.max(dias, 1)
}

function isLate(order: WorkOrderListItem) {
  // Una orden entregada ya no se atrasa: marcarla en rojo sería alarmar por algo cerrado.
  if (CERRADAS.includes(order.status)) return false

  return order.promisedAt != null && new Date(order.promisedAt) < new Date()
}

onMounted(async () => {
  await cargarTaller()
  consulta.addEventListener('change', alCambiarAncho)
  technicians.value = await usersApi.list('Technician').catch(() => [])
  await load()
})

onUnmounted(() => consulta.removeEventListener('change', alCambiarAncho))

// La pestaña «Todas» cambia lo que se le pide al servidor; las demás filtran lo ya cargado.
watch([search, branchId], load)
watch(pestana, (ahora, antes) => {
  if (ahora === 'todas' || antes === 'todas') load()
})
</script>

<template>
  <section>
    <header class="cabecera">
      <div>
        <h1>Órdenes de trabajo</h1>
        <p class="muted small">
          <template v-if="auth.user && auth.user.branches.length > 1">
            <strong>{{ auth.activeBranchName }}</strong> ·
          </template>
          Todo lo que entra al taller, desde que se recibe hasta que se entrega.
        </p>
      </div>
      <div class="cabecera-acciones">
        <RouterLink class="boton" :to="{ name: 'receive-vehicle' }">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
            <path d="M12 5v14M5 12h14" stroke-linecap="round" />
          </svg>
          {{ vocabulario.recibir }}
        </RouterLink>
      </div>
    </header>

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

    <FilterBar
      v-model:search="search"
      :placeholder="`Número, ${vocabulario.identificador.toLowerCase()} o cliente`"
    >
      <select v-model="tecnicoId">
        <option value="">Todos los técnicos</option>
        <option v-for="t in technicians.filter((x) => x.isActive)" :key="t.id" :value="t.id">
          {{ t.fullName }}
        </option>
      </select>

      <span v-if="search" class="muted small">
        Buscando también entre las entregadas.
      </span>

      <!-- Tablero para ver el patio; lista para cuando hay treinta órdenes vivas. -->
      <template #vista>
        <div class="segmento">
          <button type="button" :class="{ on: vista === 'lista' }" @click="vista = 'lista'">
            Lista
          </button>
          <button type="button" :class="{ on: vista === 'tablero' }" @click="vista = 'tablero'">
            Tablero
          </button>
        </div>
      </template>
    </FilterBar>

    <ErrorNote v-if="error" :message="error" />
    <SkeletonList v-if="loading" :rows="4" variant="tabla" />

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
            <span v-if="isLate(order)" class="late llena" title="Pasó la fecha prometida">
              {{ diasDeAtraso(order) }} {{ diasDeAtraso(order) === 1 ? 'día' : 'días' }} tarde
            </span>
            <span
              v-else-if="diasSinRetirar(order) > 0"
              class="late"
              title="Está listo y nadie lo ha venido a traer"
            >
              Sin retirar · {{ diasSinRetirar(order) }} d
            </span>
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
            <span class="small muted">
              {{ order.tasksDone }} de {{ order.taskCount }}
              {{ order.taskCount === 1 ? 'paso' : 'pasos' }}
            </span>
          </div>

          <!-- El técnico se lee, no se elige: veinte selectores azules en pantalla pesan
               más que el dato que llevan dentro. Solo el que falta pide atención, en ámbar,
               y al tocarlo aparece la lista. -->
          <div class="tecnico" @click.prevent.stop>
            <select
              v-if="asignando.has(order.id)"
              class="assign"
              :value="order.assignedTechnicianId ?? ''"
              @change="assign(order, ($event.target as HTMLSelectElement).value)"
            >
              <option value="">Sin asignar</option>
              <option v-for="t in techniciansFor(order)" :key="t.id" :value="t.id">
                {{ t.fullName }}
              </option>
            </select>
            <button
              v-else
              type="button"
              class="quien"
              :class="{ falta: !order.assignedTechnicianName }"
              @click="asignando.add(order.id)"
            >
              {{ order.assignedTechnicianName ?? 'Sin técnico · Asignar' }}
            </button>
          </div>

          <div class="muted small">
            Abierta {{ relativeTime(order.openedAt) }}
            <!-- Cuando va tarde, el atraso ya lo dice la etiqueta roja de arriba: repetirlo
                 aquí como «prometida hace 2 meses» es el mismo dato con otras palabras. -->
            <template v-if="order.promisedAt && !isLate(order)">
              · prometida {{ relativeTime(order.promisedAt) }}
            </template>
          </div>
        </RouterLink>

        <p v-if="!carril.items.length && !loading" class="empty">{{ carril.vacio }}</p>
      </div>
    </div>

    <!-- La alternativa densa. Con treinta órdenes vivas el tablero se vuelve un paisaje. -->
    <div v-else class="tabla">
      <table>
        <thead>
          <tr>
            <th scope="col">Orden</th>
            <th scope="col">Vehículo</th>
            <th scope="col">Cliente y motivo</th>
            <th scope="col">Estado</th>
            <th scope="col">Técnico</th>
            <th scope="col">Entrega</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="order in filas" :key="order.id">
            <td>
              <RouterLink class="folio num" :to="{ name: 'work-order', params: { id: order.id } }">
                {{ order.number }}
              </RouterLink>
              <span class="muted small bloque">{{ relativeTime(order.openedAt) }}</span>
            </td>
            <td>
              <span v-if="order.plate" class="plate num">{{ order.plate }}</span>
              <span class="muted small bloque">
                {{ order.vehicleLabel }} · {{ VEHICLE_TYPE_LABEL[order.vehicleType] }}
              </span>
            </td>
            <td class="motivo">
              <strong>{{ order.customerName }}</strong>
              <span class="muted small bloque">{{ order.description }}</span>
            </td>
            <td>
              <StatusBadge :status="order.status" />
              <!-- El avance en tramos: cuántos pasos van de los que lleva la orden. -->
              <span v-if="order.taskCount > 0" class="tramos" :title="`${order.tasksDone} de ${order.taskCount} pasos`">
                <span
                  v-for="n in order.taskCount"
                  :key="n"
                  :class="{ hecho: n <= order.tasksDone }"
                />
              </span>
            </td>
            <td class="muted">{{ order.assignedTechnicianName ?? 'Sin asignar' }}</td>
            <td>
              <template v-if="order.promisedAt">
                <span :class="{ tarde: isLate(order) }">{{ formatDateTime(order.promisedAt) }}</span>
                <span v-if="isLate(order)" class="muted small bloque tarde">Atrasada</span>
                <span
                  v-else-if="diasSinRetirar(order) > 0"
                  class="muted small bloque"
                  title="Está listo y nadie lo ha venido a traer"
                >
                  Sin retirar · {{ diasSinRetirar(order) }} d
                </span>
              </template>
              <span v-else class="muted">—</span>
            </td>
          </tr>
          <tr v-if="!filas.length && !loading">
            <td colspan="6" class="empty">No hay órdenes con esos filtros.</td>
          </tr>
        </tbody>
      </table>
      <p class="pie-tabla muted small">
        {{ filas.length }} {{ filas.length === 1 ? 'orden' : 'órdenes' }}
      </p>
    </div>

  </section>
</template>

<style scoped>
.cabecera {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
  margin-bottom: 1.25rem;
}

.cabecera h1 {
  margin: 0;
  font-size: 1.625rem;
}

.cabecera p {
  margin: 0.25rem 0 0;
}

.cabecera-acciones {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.boton {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.55rem 0.9rem;
  border-radius: var(--radius-sm);
  background: var(--accent);
  color: var(--on-accent);
  font-weight: 600;
}

.boton:hover {
  text-decoration: none;
  background: var(--brand-deep);
}

.boton svg {
  width: 1rem;
  height: 1rem;
}

/* ------------------------------------------------------------------ pestañas */

.pestanas {
  display: flex;
  gap: 0.25rem;
  overflow-x: auto;
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

/* ------------------------------------------------------------------ filtros */

.filtros {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  flex-wrap: wrap;
  padding: 0.875rem 0;
}

.buscador {
  position: relative;
  display: block;
  width: min(22rem, 100%);
}

.buscador svg {
  position: absolute;
  top: 50%;
  left: 0.625rem;
  translate: 0 -50%;
  width: 1.125rem;
  height: 1.125rem;
  color: var(--text-muted);
}

.buscador input {
  width: 100%;
  padding-left: 2.125rem;
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

/* El atraso es lo único que se pinta lleno en la tarjeta: con veinte tarjetas en pantalla,
   lo que lleva fondo sólido es lo que hay que mirar primero. */
.late.llena {
  background: var(--danger);
  color: var(--on-danger);
  font-weight: 600;
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
  padding: 0.625rem 0.875rem;
  background: var(--surface-alt);
  text-align: left;
  font-size: 0.6875rem;
  font-weight: 600;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--text-muted);
  white-space: nowrap;
}

td {
  padding: 0.75rem 0.875rem;
  border-top: 1px solid var(--border);
  vertical-align: top;
}

tbody tr:hover td {
  background: var(--surface-alt);
}

.folio {
  font-weight: 600;
  white-space: nowrap;
}

/* La fecha prometida no se parte en dos renglones: «11-oct, 11:57 a. m.» leído en dos
   líneas parecen dos datos. */
tbody td:last-child {
  white-space: nowrap;
}

.bloque {
  display: block;
  margin-top: 0.125rem;
}

.motivo .bloque {
  max-width: 24rem;
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
}

/* El avance en tramos, uno por paso: con cuatro pasos dice más que una barra que avanza
   a saltos del 25%. */
.tramos {
  display: flex;
  gap: 2px;
  margin-top: 0.4rem;
  max-width: 7rem;
}

.tramos span {
  height: 0.25rem;
  flex: 1;
  min-width: 0.375rem;
  border-radius: 999px;
  background: var(--border);
}

.tramos span.hecho {
  background: var(--brand);
}

/* El nombre del técnico se lee como texto; solo cuando falta tiene aire de botón, en el
   ámbar que en toda la app quiere decir «esto espera algo». */
.tecnico .quien {
  padding: 0.125rem 0;
  border: 0;
  background: none;
  color: var(--text-muted);
  font-size: 0.8125rem;
  text-align: left;
}

.tecnico .quien.falta {
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  background: color-mix(in srgb, var(--warning) 20%, transparent);
  color: var(--warning-text);
  font-weight: 500;
}

/* El atraso se dice con el color de la fecha y una línea debajo: con veinte atrasadas,
   veinte franjas rojas dejan de decir nada. */
.tarde {
  color: var(--danger);
}

.pie-tabla {
  margin: 0;
  padding: 0.625rem 0.875rem;
  border-top: 1px solid var(--border);
}

.empty {
  margin: 0;
  padding: 0.5rem;
  text-align: center;
  color: var(--text-muted);
}

</style>

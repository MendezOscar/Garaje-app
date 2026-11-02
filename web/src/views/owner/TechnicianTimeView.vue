<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { reportsApi, usersApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import FilterBar from '@/components/FilterBar.vue'
import PeriodSelect from '@/components/PeriodSelect.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import ViewTabs from '@/components/ViewTabs.vue'
import { useAuthStore } from '@/stores/auth'
import { useTaller } from '@/composables/useTaller'
import type { TechnicianTimeReport, User } from '@/types/domain'
import { formatDate } from '@/utils/format'

/**
 * Cuánto tarda cada técnico.
 *
 * El tiempo sale de los tramos del cronómetro que el técnico abre y cierra en la app, no de
 * las horas de reloj entre que el carro entró y salió: un trabajo que esperó tres días un
 * repuesto no es un trabajo de tres días. Las noches tampoco cuentan, porque cada tramo se
 * cierra solo.
 *
 * Lo que se compara es contra las horas de los pasos. El trabajo de precio fijo no las tiene
 * y queda sin comparación, que es lo honesto: no hay nada contra qué medirlo.
 */
const auth = useAuthStore()
const { vocabulario, cargar: cargarTaller } = useTaller()

const report = ref<TechnicianTimeReport | null>(null)
const technicians = ref<User[]>([])

const technicianId = ref('')
const from = ref('')
const to = ref('')

const loading = ref(false)
const error = ref('')

const branchId = computed(() => auth.activeBranchId ?? '')

const query = computed(() => ({
  from: from.value || undefined,
  to: to.value || undefined,
  branchId: branchId.value || undefined,
  technicianId: technicianId.value || undefined,
}))

/** «2 h 35 min», como lo dice el taller: en minutos sueltos no se lee de un vistazo. */
function enHoras(minutos: number): string {
  if (minutos < 60) return `${minutos} min`

  const horas = Math.floor(minutos / 60)
  const resto = minutos % 60

  return resto === 0 ? `${horas} h` : `${horas} h ${resto} min`
}

/** Lo que el técnico tardó de más —o de menos— contra lo presupuestado. */
function diferencia(minutos: number, horas: number): string {
  if (horas <= 0) return '—'

  const minutosDeMas = minutos - horas * 60
  if (Math.abs(minutosDeMas) < 6) return 'justo'

  return minutosDeMas > 0 ? `+${enHoras(minutosDeMas)}` : `−${enHoras(-minutosDeMas)}`
}

function seFueDeTiempo(minutos: number, horas: number): boolean {
  return horas > 0 && minutos > horas * 60
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    report.value = await reportsApi.technicianTime(query.value)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el tiempo de los técnicos.')
  } finally {
    loading.value = false
  }
}

watch(query, load)

onMounted(async () => {
  await cargarTaller()
  technicians.value = await usersApi.list('Technician').catch(() => [])
  await load()
})
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Reportes</h1>
        <p class="muted small">
          {{ auth.activeBranchName }} · el tiempo sale del cronómetro de cada orden, no de las
          horas entre que el {{ vocabulario.activo }} entró y salió: lo que esperó un repuesto
          no es trabajo.
        </p>
      </div>
    </header>

    <ViewTabs
      :pestanas="[
        { to: { name: 'reports' }, label: 'Ingresos' },
        { to: { name: 'income-statement' }, label: 'Gastos y utilidad' },
        { to: { name: 'technician-time' }, label: 'Tiempo por técnico' },
      ]"
    />

    <ErrorNote v-if="error" :message="error" />

    <FilterBar>
      <PeriodSelect v-model:from="from" v-model:to="to" />
      <label>
        Técnico
        <select v-model="technicianId">
          <option value="">Todos</option>
          <option v-for="t in technicians" :key="t.id" :value="t.id">{{ t.fullName }}</option>
        </select>
      </label>
    </FilterBar>

    <SkeletonList v-if="loading" :rows="4" variant="tabla" />

    <template v-else-if="report">
      <p v-if="!report.rows.length" class="muted">
        Nadie ha usado el cronómetro en este rango. Se enciende desde la app, en la orden:
        «Empezar» al ponerse con el trabajo y «Parar» al dejarlo.
      </p>

      <template v-else>
        <h2>Por técnico</h2>
        <table>
          <thead>
            <tr>
              <th scope="col">Técnico</th>
              <th scope="col" class="num">Órdenes</th>
              <th scope="col" class="num">Trabajado</th>
              <th scope="col" class="num">Presupuestado</th>
              <th scope="col" class="num">Diferencia</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="fila in report.rows" :key="fila.userId">
              <td>
                {{ fila.name }}
                <!-- El cronómetro que nadie paró se cierra solo a las 12 horas: ese número es
                     un techo, no el dato, y conviene decirlo antes de que alguien lo use para
                     reclamarle a alguien. -->
                <div v-if="fila.autoClosed" class="muted small">
                  {{ fila.autoClosed }}
                  {{ fila.autoClosed === 1 ? 'tramo quedó' : 'tramos quedaron' }} sin parar
                </div>
              </td>
              <td class="num">{{ fila.orders }}</td>
              <td class="num">{{ enHoras(fila.minutes) }}</td>
              <td class="num">{{ fila.budgetedHours > 0 ? `${fila.budgetedHours} h` : '—' }}</td>
              <td class="num" :class="{ tarde: seFueDeTiempo(fila.minutes, fila.budgetedHours) }">
                {{ diferencia(fila.minutes, fila.budgetedHours) }}
              </td>
            </tr>
          </tbody>
        </table>

        <h2>Orden por orden</h2>
        <table>
          <thead>
            <tr>
              <th scope="col">Orden</th>
              <th scope="col">Quién</th>
              <th scope="col">Abierta</th>
              <th scope="col" class="num">Trabajado</th>
              <th scope="col" class="num">Presupuestado</th>
              <th scope="col" class="num">Diferencia</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="o in report.orders" :key="o.workOrderId">
              <td>
                <RouterLink :to="{ name: 'work-order', params: { id: o.workOrderId } }">
                  {{ o.number }}
                </RouterLink>
                <div class="muted small">{{ o.assetLabel }}</div>
              </td>
              <td>{{ o.technicians }}</td>
              <td>{{ formatDate(o.openedAt) }}</td>
              <td class="num">
                {{ enHoras(o.minutes) }}
                <span v-if="o.isRunning" class="corriendo">· ahora</span>
              </td>
              <td class="num">{{ o.budgetedHours > 0 ? `${o.budgetedHours} h` : '—' }}</td>
              <td class="num" :class="{ tarde: seFueDeTiempo(o.minutes, o.budgetedHours) }">
                {{ diferencia(o.minutes, o.budgetedHours) }}
              </td>
            </tr>
          </tbody>
        </table>
      </template>
    </template>
  </section>
</template>

<style scoped>
h1 {
  margin: 0;
}

h2 {
  margin: 1.25rem 0 0.25rem;
  font-size: 1rem;
}

.top {
  margin-bottom: 1rem;
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
  color: var(--text-muted);
  font-size: 0.8125rem;
  font-weight: 500;
}

.num {
  text-align: right;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

/* Se fue de tiempo: se ve, no estorba. Nadie se bloquea ni recibe un aviso por esto. */
.tarde {
  color: var(--danger);
}

.corriendo {
  color: var(--accent);
}
</style>

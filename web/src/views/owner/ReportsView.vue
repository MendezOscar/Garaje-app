<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, reportsApi, usersApi } from '@/api/garaj'
import {
  RevenueGrouping,
  WORK_ORDER_STATUS_LABEL,
  type Branch,
  type Dashboard,
  type RevenueReport,
  type User,
} from '@/types/domain'
import { formatMoney, formatQuantity } from '@/utils/format'

const dashboard = ref<Dashboard | null>(null)
const report = ref<RevenueReport | null>(null)
const branches = ref<Branch[]>([])
const technicians = ref<User[]>([])

const groupBy = ref<RevenueGrouping>(RevenueGrouping.Day)
const branchId = ref('')
const technicianId = ref('')
const from = ref('')
const to = ref('')

const error = ref('')
const loading = ref(false)

const query = computed(() => ({
  from: from.value || undefined,
  to: to.value || undefined,
  groupBy: groupBy.value,
  branchId: branchId.value || undefined,
  technicianId: technicianId.value || undefined,
}))

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [d, r] = await Promise.all([
      // El tablero es del taller entero: filtrarlo por técnico dejaría "repuestos bajo
      // mínimo" y "pendientes de atender" contando cosas que no dependen de nadie.
      reportsApi.dashboard(branchId.value || undefined),
      reportsApi.revenue(query.value),
    ])
    dashboard.value = d
    report.value = r
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar los reportes.')
  } finally {
    loading.value = false
  }
}

/** El CSV va detrás de la sesión, así que se baja con axios y no con un enlace. */
async function downloadCsv() {
  try {
    await reportsApi.downloadCsv(query.value)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo generar el CSV.')
  }
}

/**
 * Barras apiladas en SVG. No hay librería de gráficas a propósito: son dos rectángulos por
 * periodo y meter 200 KB de dependencia para esto no se justifica.
 */
const chart = computed(() => {
  const points = report.value?.points ?? []
  const max = Math.max(...points.map((p) => p.total), 1)

  return points.map((p) => ({
    ...p,
    partsHeight: (p.partsRevenue / max) * 100,
    laborHeight: (p.laborRevenue / max) * 100,
  }))
})

watch([groupBy, branchId, technicianId], load)

onMounted(async () => {
  branches.value = await branchesApi.list().catch(() => [])
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
          El desglose sale del tipo de línea de cada venta, así que siempre cuadra con lo facturado.
        </p>
      </div>
      <select v-model="branchId">
        <option value="">Todas las sucursales</option>
        <option v-for="b in branches" :key="b.id" :value="b.id">{{ b.name }}</option>
      </select>
    </header>

    <p v-if="error" class="error">{{ error }}</p>

    <!-- Tablero del día -->
    <div v-if="dashboard" class="cards">
      <article>
        <span>Hoy</span>
        <strong>{{ formatMoney(dashboard.revenueToday) }}</strong>
        <!-- Facturado, no cobrado: lo segundo se cuadra en Caja, y conviene que no se
             confundan. -->
        <RouterLink :to="{ name: 'cash-close' }" class="small">Lo cobrado hoy</RouterLink>
      </article>
      <article>
        <span>Esta semana</span>
        <strong>{{ formatMoney(dashboard.revenueWeek) }}</strong>
      </article>
      <article>
        <span>Este mes</span>
        <strong>{{ formatMoney(dashboard.revenueMonth) }}</strong>
        <small class="muted">margen {{ formatMoney(dashboard.marginMonth) }}</small>
      </article>
      <article>
        <span>Órdenes abiertas</span>
        <strong>{{ dashboard.openWorkOrders }}</strong>
        <small v-if="dashboard.lateWorkOrders" class="danger">
          {{ dashboard.lateWorkOrders }} atrasadas
        </small>
      </article>
      <article>
        <span>Pendientes de atender</span>
        <strong>{{ dashboard.pendingRequests }}</strong>
        <small v-if="dashboard.quotesAwaitingResponse" class="muted">
          {{ dashboard.quotesAwaitingResponse }} cotizaciones sin respuesta
        </small>
      </article>
      <article>
        <span>Por cobrar</span>
        <strong :class="{ danger: (dashboard.overdueReceivables ?? 0) > 0 }">
          {{ formatMoney(dashboard.receivables) }}
        </strong>
        <small v-if="dashboard.overdueReceivables > 0" class="danger">
          {{ formatMoney(dashboard.overdueReceivables) }} vencido
        </small>
        <!-- El detalle vive en su propia sección: aquí es un indicador, allá se trabaja. -->
        <RouterLink :to="{ name: 'receivables' }" class="small">Ver el detalle</RouterLink>
      </article>
      <article>
        <span>Repuestos bajo mínimo</span>
        <strong :class="{ danger: dashboard.partsBelowMinimum > 0 }">
          {{ dashboard.partsBelowMinimum }}
        </strong>
      </article>
    </div>

    <div v-if="dashboard?.workOrdersByStatus.length" class="chips">
      <span v-for="s in dashboard.workOrdersByStatus" :key="s.status">
        {{ WORK_ORDER_STATUS_LABEL[s.status] }}: <strong>{{ s.count }}</strong>
      </span>
    </div>

    <!-- Ingresos -->
    <article class="card">
      <header class="filters">
        <h2>Ingresos</h2>
        <div class="row">
          <label>Desde<input v-model="from" type="date" /></label>
          <label>Hasta<input v-model="to" type="date" /></label>
          <select v-model="technicianId">
            <option value="">Todos los técnicos</option>
            <option v-for="t in technicians" :key="t.id" :value="t.id">{{ t.fullName }}</option>
          </select>
          <select v-model.number="groupBy">
            <option :value="RevenueGrouping.Day">Por día</option>
            <option :value="RevenueGrouping.Week">Por semana</option>
            <option :value="RevenueGrouping.Month">Por mes</option>
          </select>
          <button type="button" :disabled="loading" @click="load">Aplicar</button>
          <button type="button" :disabled="loading" @click="downloadCsv">CSV</button>
        </div>
      </header>

      <p v-if="loading" class="muted">Cargando…</p>

      <template v-else-if="report">
        <div class="summary">
          <div>
            <span>Repuestos</span>
            <strong>{{ formatMoney(report.partsRevenue) }}</strong>
          </div>
          <div>
            <span>Mano de obra</span>
            <strong>{{ formatMoney(report.laborRevenue) }}</strong>
          </div>
          <div>
            <span>Total</span>
            <strong class="big">{{ formatMoney(report.total) }}</strong>
          </div>
          <div>
            <span>Margen</span>
            <strong>{{ formatMoney(report.margin) }}</strong>
            <small class="muted">{{ report.marginPercent }}%</small>
          </div>
          <div>
            <span>Ventas</span>
            <strong>{{ report.saleCount }}</strong>
          </div>
        </div>

        <p v-if="!report.points.length" class="muted">No hubo ventas en el rango.</p>

        <div v-else class="chart">
          <div v-for="p in chart" :key="p.periodStart" class="bar" :title="`${p.periodLabel}: ${formatMoney(p.total)}`">
            <div class="stack">
              <div class="labor" :style="{ height: `${p.laborHeight}%` }" />
              <div class="parts" :style="{ height: `${p.partsHeight}%` }" />
            </div>
            <span class="label">{{ p.periodLabel }}</span>
          </div>
        </div>

        <p v-if="report.points.length" class="legend">
          <span><i class="swatch parts" /> Repuestos</span>
          <span><i class="swatch labor" /> Mano de obra</span>
        </p>

        <div class="tables">
          <div v-if="report.branches.length > 1">
            <h3>Por sucursal</h3>
            <table>
              <tbody>
                <tr v-for="b in report.branches" :key="b.branchId">
                  <td>{{ b.branchName }}</td>
                  <td class="num muted small">
                    {{ formatMoney(b.partsRevenue) }} + {{ formatMoney(b.laborRevenue) }}
                  </td>
                  <td class="num"><strong>{{ formatMoney(b.total) }}</strong></td>
                </tr>
              </tbody>
            </table>
          </div>

          <div v-if="report.technicians.length">
            <h3>Por técnico</h3>
            <table>
              <tbody>
                <tr v-for="t in report.technicians" :key="t.technicianId ?? 'mostrador'">
                  <td>
                    {{ t.technicianName }}
                    <div class="muted small">{{ t.saleCount }} venta(s)</div>
                  </td>
                  <td class="num muted small">
                    {{ formatMoney(t.partsRevenue) }} + {{ formatMoney(t.laborRevenue) }}
                  </td>
                  <td class="num">
                    <strong>{{ formatMoney(t.total) }}</strong>
                    <div class="muted small">margen {{ formatMoney(t.margin) }}</div>
                  </td>
                </tr>
              </tbody>
            </table>
            <p class="muted small note">
              Se atribuye al técnico responsable de la orden. Lo vendido en mostrador no pasó
              por nadie y aparece como «Sin técnico».
            </p>
          </div>

          <div v-if="report.topParts.length">
            <h3>Repuestos más vendidos</h3>
            <table>
              <tbody>
                <tr v-for="p in report.topParts" :key="p.partId">
                  <td>
                    {{ p.name }}
                    <div class="muted small">{{ p.sku }} · {{ formatQuantity(p.quantity) }} u</div>
                  </td>
                  <td class="num">
                    {{ formatMoney(p.revenue) }}
                    <div class="muted small">margen {{ formatMoney(p.margin) }}</div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <details v-if="report.points.length" class="detail-table">
          <summary>Ver el detalle por periodo</summary>
          <table>
            <thead>
              <tr>
                <th>Periodo</th>
                <th class="num">Repuestos</th>
                <th class="num">Mano de obra</th>
                <th class="num">Total</th>
                <th class="num">Margen</th>
                <th class="num">Ventas</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="p in report.points" :key="p.periodStart">
                <td>{{ p.periodLabel }}</td>
                <td class="num">{{ formatMoney(p.partsRevenue) }}</td>
                <td class="num">{{ formatMoney(p.laborRevenue) }}</td>
                <td class="num"><strong>{{ formatMoney(p.total) }}</strong></td>
                <td class="num muted">{{ formatMoney(p.margin) }}</td>
                <td class="num muted">{{ p.saleCount }}</td>
              </tr>
            </tbody>
          </table>
        </details>
      </template>
    </article>
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

.cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
  gap: 0.75rem;
  margin-bottom: 0.75rem;
}

.cards article {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  padding: 0.75rem 0.875rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.cards span {
  font-size: 0.75rem;
  color: var(--text-muted);
}

.cards strong {
  font-size: 1.25rem;
}

.cards small {
  font-size: 0.75rem;
}

.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-bottom: 1rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.chips span {
  padding: 0.125rem 0.5rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  background: var(--surface);
}

.card {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.filters {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.filters h2 {
  margin: 0;
  font-size: 1rem;
}

.row {
  display: flex;
  align-items: flex-end;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.row label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.summary {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
  gap: 0.75rem;
  margin-bottom: 1.25rem;
}

.summary div {
  display: flex;
  flex-direction: column;
}

.summary span {
  font-size: 0.75rem;
  color: var(--text-muted);
}

.summary strong {
  font-size: 1.125rem;
}

.summary .big {
  font-size: 1.5rem;
}

.chart {
  display: flex;
  align-items: flex-end;
  gap: 0.25rem;
  height: 12rem;
  padding-bottom: 1.25rem;
  overflow-x: auto;
}

.bar {
  flex: 1 0 1.75rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  height: 100%;
  position: relative;
}

.stack {
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
  width: 100%;
  height: 100%;
}

.parts {
  background: var(--accent, #2563eb);
  border-radius: 0 0 2px 2px;
}

.labor {
  background: #f59e0b;
  border-radius: 2px 2px 0 0;
}

.label {
  position: absolute;
  bottom: -1.125rem;
  font-size: 0.625rem;
  color: var(--text-muted);
  white-space: nowrap;
}

.legend {
  display: flex;
  gap: 1rem;
  margin: 0 0 1rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.swatch {
  display: inline-block;
  width: 0.625rem;
  height: 0.625rem;
  border-radius: 2px;
  margin-right: 0.25rem;
}

.tables {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
  gap: 1.5rem;
}

h3 {
  margin: 0 0 0.375rem;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}

th,
td {
  padding: 0.375rem 0.25rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
  vertical-align: top;
}

th {
  font-size: 0.6875rem;
  text-transform: uppercase;
  color: var(--text-muted);
}

.num {
  text-align: right;
  white-space: nowrap;
}

.detail-table {
  margin-top: 1.25rem;
}

summary {
  cursor: pointer;
  font-size: 0.875rem;
  color: var(--text-muted);
}

.small {
  font-size: 0.75rem;
}

.muted {
  color: var(--text-muted);
}

.danger {
  color: var(--danger);
}

.error {
  color: var(--danger);
}
</style>

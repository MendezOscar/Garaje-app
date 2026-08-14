<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, reportsApi } from '@/api/garaj'
import { PAYMENT_METHOD_LABEL, type Branch, type CashClose } from '@/types/domain'
import { formatMoney } from '@/utils/format'

/**
 * Cierre de caja: lo **cobrado** en el día, que no es lo facturado.
 *
 * Una venta a crédito suma en Reportes el día que se emite, y aquí el día que el cliente paga;
 * el abono de una factura de hace tres meses solo aparece aquí. Es la hoja que se cuadra contra
 * el efectivo del cajón al cerrar, así que lo primero que se ve es el total por forma de pago.
 */
const cierre = ref<CashClose | null>(null)
const branches = ref<Branch[]>([])

/** El día en formato `yyyy-MM-dd`, que es lo que entiende `<input type="date">`. */
const dia = ref(hoyLocal())
const branchId = ref('')

const loading = ref(false)
const error = ref('')

function hoyLocal(): string {
  const ahora = new Date()
  const local = new Date(ahora.getTime() - ahora.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 10)
}

/**
 * Se manda a mediodía y no a medianoche: el backend resuelve el día del taller a partir de
 * este instante, y las 00:00 locales de aquí podrían caer en el día anterior allá.
 */
const query = computed(() => ({
  date: dia.value ? `${dia.value}T12:00:00` : undefined,
  branchId: branchId.value || undefined,
}))

const esHoy = computed(() => dia.value === hoyLocal())

async function load() {
  loading.value = true
  error.value = ''
  try {
    cierre.value = await reportsApi.cashClose(query.value)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el cierre de caja.')
  } finally {
    loading.value = false
  }
}

async function bajarPdf() {
  error.value = ''
  try {
    await reportsApi.downloadCashClosePdf(query.value, dia.value)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo generar el PDF.')
  }
}

function hora(value: string): string {
  // En 24 horas, como el PDF: «01:08 p. m.» ocupa el doble y la columna se lee peor cuando
  // hay veinte filas seguidas.
  return new Date(value).toLocaleTimeString('es-HN', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })
}

onMounted(async () => {
  branches.value = await branchesApi.list().catch(() => [])
  await load()
})
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Cierre de caja</h1>
        <p class="muted small">
          Lo cobrado en el día. No es lo facturado: aquí entra el abono de una factura vieja, y
          no entra lo que se facturó a crédito y todavía nadie pagó.
        </p>
      </div>
      <div v-if="cierre" class="totals">
        <div>
          <span class="muted small">Cobrado {{ esHoy ? 'hoy' : 'ese día' }}</span>
          <strong>{{ formatMoney(cierre.total) }}</strong>
          <span class="muted small">{{ cierre.paymentCount }} abono(s)</span>
        </div>
      </div>
    </header>

    <form class="filters" @submit.prevent="load">
      <input v-model="dia" type="date" @change="load" />

      <select v-if="branches.length > 1" v-model="branchId" @change="load">
        <option value="">Todas las sucursales</option>
        <option v-for="b in branches" :key="b.id" :value="b.id">{{ b.name }}</option>
      </select>

      <button type="button" :disabled="loading || !cierre" @click="bajarPdf">PDF</button>
    </form>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="loading" class="muted">Cargando…</p>

    <template v-else-if="cierre">
      <p class="muted small dia">{{ cierre.dayLabel }}</p>

      <p v-if="!cierre.paymentCount" class="muted vacio">
        No se recibió ningún pago este día.
      </p>

      <template v-else>
        <div class="resumen">
          <article class="card">
            <h2>Por forma de pago</h2>
            <ul>
              <li v-for="m in cierre.byMethod" :key="m.method">
                <span>
                  {{ PAYMENT_METHOD_LABEL[m.method] }}
                  <span class="muted small">· {{ m.count }}</span>
                </span>
                <strong class="num">{{ formatMoney(m.total) }}</strong>
              </li>
            </ul>
          </article>

          <article class="card">
            <h2>Quién lo recibió</h2>
            <ul>
              <li v-for="r in cierre.byReceiver" :key="r.receiverName">
                <span>
                  {{ r.receiverName }}
                  <span class="muted small">· {{ r.count }}</span>
                </span>
                <strong class="num">{{ formatMoney(r.total) }}</strong>
              </li>
            </ul>
          </article>
        </div>

        <h2 class="detalle-titulo">Detalle</h2>
        <table>
          <thead>
            <tr>
              <th>Hora</th>
              <th>Factura</th>
              <th>Cliente</th>
              <th>Forma</th>
              <th class="num">Monto</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(p, i) in cierre.payments" :key="i">
              <td>{{ hora(p.paidAt) }}</td>
              <td>{{ p.saleNumber }}</td>
              <td>
                {{ p.customerName ?? 'Mostrador' }}
                <div class="muted small">
                  {{ p.branchName }}
                  <template v-if="p.reference"> · {{ p.reference }}</template>
                </div>
              </td>
              <td>
                {{ PAYMENT_METHOD_LABEL[p.method] }}
                <div class="muted small">{{ p.receiverName }}</div>
              </td>
              <td class="num">{{ formatMoney(p.amount) }}</td>
            </tr>
          </tbody>
          <tfoot>
            <tr>
              <td colspan="4"><strong>Total cobrado</strong></td>
              <td class="num"><strong>{{ formatMoney(cierre.total) }}</strong></td>
            </tr>
          </tfoot>
        </table>
      </template>

      <!-- Se dice, no se esconde: una caja que no explica lo que dejó fuera no sirve para
           cuadrar. -->
      <p v-if="cierre.voidedCount" class="muted small aviso">
        Se dejaron fuera {{ cierre.voidedCount }} abono(s) por
        {{ formatMoney(cierre.voidedAmount) }} de ventas anuladas.
      </p>
    </template>
  </section>
</template>

<style scoped>
.top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

h1 {
  margin: 0 0 0.25rem;
}

.totals div {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
}

.totals strong {
  font-size: 1.5rem;
}

.filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  margin: 1rem 0 0.5rem;
}

.dia {
  margin: 0 0 1rem;
}

/* Solo la primera letra: `capitalize` ponía «Viernes 14 De Agosto De 2026». */
.dia::first-letter {
  text-transform: uppercase;
}

.vacio {
  padding: 1rem 0;
}

.resumen {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.card {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

h2 {
  margin: 0 0 0.5rem;
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.card ul {
  margin: 0;
  padding: 0;
  list-style: none;
}

.card li {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.25rem 0;
}

.detalle-titulo {
  margin-bottom: 0.5rem;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9375rem;
}

th {
  padding: 0.375rem 0.25rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
  font-weight: 500;
}

td {
  padding: 0.5rem 0.25rem;
  border-bottom: 1px solid var(--border);
  vertical-align: top;
}

tfoot td {
  border-bottom: none;
}

.aviso {
  margin-top: 1rem;
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

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, salesApi } from '@/api/garaj'
import {
  PAYMENT_METHOD_LABEL,
  PaymentMethod,
  type Branch,
  type SaleListItem,
} from '@/types/domain'
import { formatDate, formatMoney } from '@/utils/format'

/**
 * Cuentas por cobrar. Vive aparte de Reportes a propósito: un reporte se mira de vez en
 * cuando, y esto se trabaja —se busca al cliente que llamó, se ve cuánto debe y se le anota
 * el abono—. En Reportes queda solo el total, que sí es un indicador.
 */
const sales = ref<SaleListItem[]>([])
const branches = ref<Branch[]>([])

const search = ref('')
const branchId = ref('')
/** Vencidas / por vencer / todas. Sin fecha acordada cuenta como «por vencer»: no se atrasó. */
const vencimiento = ref<'todas' | 'vencidas' | 'porVencer'>('todas')

const loading = ref(false)
const error = ref('')

/** La venta a la que se le está anotando un abono, o null. */
const cobrando = ref<SaleListItem | null>(null)
const abono = ref({ amount: 0, method: PaymentMethod.Cash as PaymentMethod, reference: '' })
const busy = ref(false)
const aviso = ref('')

/**
 * El total de lo que está a la vista. No es el total del taller cuando hay filtros puestos,
 * y por eso el encabezado dice «de lo filtrado»: dar un número que no cuadre con la lista de
 * abajo es peor que no darlo.
 */
const total = computed(() => sales.value.reduce((sum, s) => sum + s.balance, 0))
const vencido = computed(() =>
  sales.value.filter((s) => s.isOverdue).reduce((sum, s) => sum + s.balance, 0),
)

async function load() {
  loading.value = true
  error.value = ''
  try {
    const page = await salesApi.list({
      onlyUnpaid: true,
      search: search.value.trim() || undefined,
      branchId: branchId.value || undefined,
      overdue:
        vencimiento.value === 'todas' ? undefined : vencimiento.value === 'vencidas',
      pageSize: 200,
    })
    sales.value = page.items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar lo que está por cobrar.')
  } finally {
    loading.value = false
  }
}

function cobrar(sale: SaleListItem) {
  cobrando.value = sale
  // Preseleccionado al saldo completo: lo más común es que el cliente venga a terminar de
  // pagar, y si abona menos se corrige el número.
  abono.value = { amount: sale.balance, method: PaymentMethod.Cash, reference: '' }
  aviso.value = ''
}

async function guardarAbono() {
  const sale = cobrando.value
  if (!sale) return

  const amount = Number(abono.value.amount)
  if (!amount || amount <= 0) return

  busy.value = true
  error.value = ''
  try {
    await salesApi.registerPayment(sale.id, {
      amount,
      method: abono.value.method,
      reference: abono.value.reference.trim() || undefined,
    })
    aviso.value = `Abono de ${formatMoney(amount)} anotado en ${sale.number}.`
    cobrando.value = null
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo anotar el abono.')
  } finally {
    busy.value = false
  }
}

/** Cuántos días lleva vencida, para poner primero a quién hay que llamar hoy. */
function atraso(sale: SaleListItem) {
  if (!sale.dueDate || !sale.isOverdue) return 0
  return Math.floor((Date.now() - new Date(sale.dueDate).getTime()) / 86_400_000)
}

let timer: ReturnType<typeof setTimeout> | undefined
watch(search, () => {
  clearTimeout(timer)
  timer = setTimeout(load, 250)
})

onMounted(async () => {
  branches.value = await branchesApi.list().catch(() => [])
  await load()
})
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Por cobrar</h1>
        <p class="muted small">
          Facturas con saldo, la que vence antes primero. Las que se entregaron sin fecha
          acordada van al final.
        </p>
      </div>
      <div class="totals">
        <div>
          <span class="muted small">Total de lo filtrado</span>
          <strong>{{ formatMoney(total) }}</strong>
        </div>
        <div v-if="vencido">
          <span class="muted small">Vencido</span>
          <strong class="danger">{{ formatMoney(vencido) }}</strong>
        </div>
      </div>
    </header>

    <form class="filters" @submit.prevent="load">
      <input
        v-model="search"
        type="search"
        placeholder="Cliente, teléfono, número de venta o de orden"
      />

      <select v-if="branches.length > 1" v-model="branchId" @change="load">
        <option value="">Todas las sucursales</option>
        <option v-for="b in branches" :key="b.id" :value="b.id">{{ b.name }}</option>
      </select>

      <div class="chips">
        <button
          type="button"
          class="chip"
          :class="{ on: vencimiento === 'todas' }"
          @click="vencimiento = 'todas'; load()"
        >
          Todas
        </button>
        <button
          type="button"
          class="chip"
          :class="{ on: vencimiento === 'vencidas' }"
          @click="vencimiento = 'vencidas'; load()"
        >
          Vencidas
        </button>
        <button
          type="button"
          class="chip"
          :class="{ on: vencimiento === 'porVencer' }"
          @click="vencimiento = 'porVencer'; load()"
        >
          Por vencer
        </button>
      </div>
    </form>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="aviso" class="notice">{{ aviso }}</p>

    <p v-if="loading" class="muted">Cargando…</p>
    <p v-else-if="!sales.length" class="muted">
      {{
        search || branchId || vencimiento !== 'todas'
          ? 'Nada con esos filtros.'
          : 'No hay nada por cobrar. Todo lo facturado está pagado.'
      }}
    </p>

    <table v-else>
      <thead>
        <tr>
          <th>Cliente</th>
          <th>Vence</th>
          <th class="num">Abonado</th>
          <th class="num">Saldo</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <template v-for="sale in sales" :key="sale.id">
          <tr :class="{ overdue: sale.isOverdue }">
            <td>
              <strong>{{ sale.customerName ?? 'Mostrador' }}</strong>
              <div class="muted small">
                {{ sale.number }}
                <template v-if="sale.workOrderNumber"> · {{ sale.workOrderNumber }}</template>
                · {{ sale.branchName }}
              </div>
            </td>
            <td>
              <template v-if="sale.dueDate">
                {{ formatDate(sale.dueDate) }}
                <div v-if="sale.isOverdue" class="danger small">
                  hace {{ atraso(sale) }} {{ atraso(sale) === 1 ? 'día' : 'días' }}
                </div>
              </template>
              <span v-else class="muted">sin fecha acordada</span>
            </td>
            <td class="num muted">
              {{ formatMoney(sale.amountPaid) }} de {{ formatMoney(sale.total) }}
            </td>
            <td class="num"><strong>{{ formatMoney(sale.balance) }}</strong></td>
            <td class="num">
              <button type="button" class="link" @click="cobrar(sale)">Abonar</button>
            </td>
          </tr>

          <tr v-if="cobrando?.id === sale.id" class="cobro">
            <td colspan="5">
              <form @submit.prevent="guardarAbono">
                <label>
                  Monto
                  <input
                    v-model.number="abono.amount"
                    type="number"
                    step="0.01"
                    min="0.01"
                    :max="sale.balance"
                    required
                  />
                </label>
                <label>
                  Forma de pago
                  <select v-model="abono.method">
                    <option
                      v-for="(label, value) in PAYMENT_METHOD_LABEL"
                      :key="value"
                      :value="Number(value)"
                    >
                      {{ label }}
                    </option>
                  </select>
                </label>
                <label>
                  Referencia
                  <input v-model="abono.reference" placeholder="Opcional" />
                </label>
                <button type="submit" :disabled="busy">Anotar</button>
                <button type="button" @click="cobrando = null">Cancelar</button>
              </form>
            </td>
          </tr>
        </template>
      </tbody>
    </table>
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

.totals {
  display: flex;
  gap: 1.5rem;
}

.totals div {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
}

.totals strong {
  font-size: 1.25rem;
}

.filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  margin: 1rem 0;
}

.filters input[type='search'] {
  flex: 1 1 18rem;
}

.chips {
  display: flex;
  gap: 0.375rem;
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
  color: #fff;
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

.overdue td {
  background: color-mix(in srgb, var(--danger) 6%, transparent);
}

.cobro form {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 0.75rem;
  padding: 0.5rem 0;
}

.cobro label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.cobro input {
  width: 9rem;
}

.num {
  text-align: right;
  white-space: nowrap;
}

.link {
  padding: 0;
  border: none;
  background: none;
  color: var(--accent);
  font-size: 0.8125rem;
  cursor: pointer;
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

.notice {
  color: var(--accent);
}
</style>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { salesApi, statementsApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import ViewTabs from '@/components/ViewTabs.vue'
import FilterBar from '@/components/FilterBar.vue'
import FilterChip from '@/components/FilterChip.vue'
import RowMenu from '@/components/RowMenu.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import {
  PAYMENT_METHOD_LABEL,
  PaymentMethod,
  type SaleDetail,
  type SaleListItem,
} from '@/types/domain'
import { useAuthStore } from '@/stores/auth'
import { formatDate, formatDateTime, formatMoney } from '@/utils/format'

/**
 * Cuentas por cobrar. Vive aparte de Reportes a propósito: un reporte se mira de vez en
 * cuando, y esto se trabaja —se busca al cliente que llamó, se ve cuánto debe y se le anota
 * el abono—. En Reportes queda solo el total, que sí es un indicador.
 */
const auth = useAuthStore()

const sales = ref<SaleListItem[]>([])

const search = ref('')
/** La sucursal sale del menú, no de esta pantalla: es la misma para todo el panel. */
const branchId = computed(() => auth.activeBranchId ?? '')
/** Vencidas / por vencer / todas. Sin fecha acordada cuenta como «por vencer»: no se atrasó. */
const vencimiento = ref<'todas' | 'vencidas' | 'porVencer'>('todas')

const loading = ref(false)
const error = ref('')

/**
 * La venta desplegada para ver sus abonos, con el detalle ya traído. El listado no los trae
 * —serían todos los abonos de todas las filas en cada carga— así que se piden al desplegar.
 */
const abierta = ref<string | null>(null)
const detalle = ref<SaleDetail | null>(null)

/** Recordarle por WhatsApp lo que debe, desde la fila de su factura. */
async function recordar(sale: SaleListItem) {
  if (!sale.customerId) return
  await mandarEstadoDeCuenta(sale)
}

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

/**
 * Las facturas agrupadas por cliente.
 *
 * Un cliente con cuatro facturas eran cuatro filas, y para saber cuánto debía en total había
 * que sumarlas de cabeza. Ahora es una fila con su saldo, que se despliega para ver cuáles
 * son. El de mostrador sin cliente en el padrón va junto, que es lo que es: ventas sueltas.
 */
const grupos = computed(() => {
  const porCliente = new Map<
    string,
    {
      clave: string
      cliente: string
      customerId: string | null
      saldo: number
      vencido: number
      diasDelMasViejo: number
      facturas: SaleListItem[]
    }
  >()

  for (const venta of sales.value) {
    const clave = venta.customerId ?? 'mostrador'
    const grupo = porCliente.get(clave) ?? {
      clave,
      cliente: venta.customerName ?? 'Mostrador',
      customerId: venta.customerId ?? null,
      saldo: 0,
      vencido: 0,
      diasDelMasViejo: 0,
      facturas: [],
    }

    grupo.saldo += venta.balance
    if (venta.isOverdue) grupo.vencido += venta.balance
    grupo.diasDelMasViejo = Math.max(grupo.diasDelMasViejo, atraso(venta))
    grupo.facturas.push(venta)

    porCliente.set(clave, grupo)
  }

  // El que más debe primero: es a quien hay que llamar hoy.
  return [...porCliente.values()].sort((a, b) => b.saldo - a.saldo)
})

/** Quién debe más y qué tan vieja es la factura más atrasada. Van en el encabezado. */
const quienDebeMas = computed(() => grupos.value[0] ?? null)

const masVieja = computed(() =>
  sales.value.reduce((dias, venta) => Math.max(dias, atraso(venta)), 0),
)

/** Los clientes desplegados. Con uno solo, se abre solo: no hay nada que elegir. */
const desplegados = ref(new Set<string>())

function desplegar(clave: string) {
  if (desplegados.value.has(clave)) desplegados.value.delete(clave)
  else desplegados.value.add(clave)
}
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

async function abrir(sale: SaleListItem) {
  if (abierta.value === sale.id) {
    abierta.value = null
    return
  }

  abierta.value = sale.id
  detalle.value = null
  error.value = ''

  try {
    detalle.value = await salesApi.get(sale.id)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar los abonos.')
  }
}

/**
 * Manda el estado de cuenta del cliente por WhatsApp. La pestaña se abre antes del await:
 * Safari bloquea `window.open` si no viene del gesto del usuario.
 */
async function mandarEstadoDeCuenta(sale: SaleListItem) {
  if (!sale.customerId) return

  const tab = window.open('', '_blank')
  error.value = ''
  aviso.value = ''

  try {
    const link = await statementsApi.whatsappLink(sale.customerId)
    if (tab) tab.location.href = link.url
    else window.location.href = link.url
  } catch (e) {
    tab?.close()
    error.value = errorMessage(e, 'No se pudo armar el enlace del estado de cuenta.')
  }
}

async function bajarEstadoDeCuenta(sale: SaleListItem) {
  if (!sale.customerId) return

  error.value = ''
  try {
    await statementsApi.downloadPdf(sale.customerId, sale.customerName ?? 'cliente')
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo bajar el estado de cuenta.')
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

    // Si estaba desplegada, se relee para que el abono nuevo aparezca en la lista.
    if (abierta.value === sale.id) detalle.value = await salesApi.get(sale.id)

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

// Al cambiar de sucursal en el menú, esta pantalla vuelve a pedir lo suyo.

watch(branchId, load)


onMounted(async () => {
  await load()
})
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Por cobrar</h1>
        <p class="muted small">
          <template v-if="auth.user && auth.user.branches.length > 1">
            <strong>{{ auth.activeBranchName }}</strong> ·
          </template>
          Facturas con saldo, la que vence antes primero. Las que se entregaron sin fecha
          acordada van al final.
        </p>
      </div>
      <div class="totals">
        <div>
          <span class="muted small">Total de lo filtrado</span>
          <strong>{{ formatMoney(total) }}</strong>
          <span v-if="vencido" class="danger small">{{ formatMoney(vencido) }} vencido</span>
        </div>
        <div v-if="quienDebeMas">
          <span class="muted small">Quien más debe</span>
          <strong>{{ quienDebeMas.cliente }}</strong>
          <span class="muted small">{{ formatMoney(quienDebeMas.saldo) }}</span>
        </div>
        <div v-if="masVieja">
          <span class="muted small">La más vieja</span>
          <strong class="danger">{{ masVieja }} días</strong>
          <span class="muted small">de atraso</span>
        </div>
      </div>
    </header>

    <ViewTabs
      :pestanas="[
        { to: { name: 'sales' }, label: 'Ventas' },
        { to: { name: 'receivables' }, label: 'Por cobrar' },
      ]"
    />

    <FilterBar
      v-model:search="search"
      placeholder="Cliente, teléfono, número de venta o de orden"
    >
      <!-- Excluyentes: una factura o está vencida o no lo está. -->
      <FilterChip
        :model-value="vencimiento === 'todas'"
        @update:model-value="vencimiento = 'todas'; load()"
      >
        Todas
      </FilterChip>
      <FilterChip
        :model-value="vencimiento === 'vencidas'"
        @update:model-value="vencimiento = 'vencidas'; load()"
      >
        Vencidas
      </FilterChip>
      <FilterChip
        :model-value="vencimiento === 'porVencer'"
        @update:model-value="vencimiento = 'porVencer'; load()"
      >
        Por vencer
      </FilterChip>
    </FilterBar>

    <ErrorNote v-if="error" :message="error" />
    <p v-if="aviso" class="notice">{{ aviso }}</p>

    <SkeletonList v-if="loading" :rows="4" variant="tabla" />
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
          <th scope="col">Cliente</th>
          <th scope="col">Vence</th>
          <th scope="col" class="num">Abonado</th>
          <th scope="col" class="num">Saldo</th>
          <th scope="col"><span class="sr-only">Acciones</span></th>
        </tr>
      </thead>
      <tbody>
        <template v-for="grupo in grupos" :key="grupo.clave">
          <!-- Una fila por cliente: lo que debe en total, y cuántas facturas lo componen. -->
          <tr class="grupo" :class="{ overdue: grupo.vencido > 0 }" @click="desplegar(grupo.clave)">
            <td>
              <strong>{{ grupo.cliente }}</strong>
              <div class="muted small">
                {{ grupo.facturas.length }}
                {{ grupo.facturas.length === 1 ? 'factura' : 'facturas' }}
                <template v-if="grupo.diasDelMasViejo">
                  · la más vieja, {{ grupo.diasDelMasViejo }}
                  {{ grupo.diasDelMasViejo === 1 ? 'día' : 'días' }} de atraso
                </template>
              </div>
            </td>
            <td colspan="2" class="muted small">
              {{ desplegados.has(grupo.clave) ? 'Toque para cerrar' : 'Toque para ver sus facturas' }}
            </td>
            <td class="num"><strong>{{ formatMoney(grupo.saldo) }}</strong></td>
            <td class="num acciones">
              <button
                v-if="grupo.customerId"
                type="button"
                class="link"
                @click.stop="recordar(grupo.facturas[0])"
              >
                Recordar
              </button>
            </td>
          </tr>

          <template v-if="desplegados.has(grupo.clave)">
            <tr v-for="sale in grupo.facturas" :key="sale.id" class="factura" :class="{ overdue: sale.isOverdue }">
              <td>
                {{ sale.number }}
                <div class="muted small">
                  <template v-if="sale.workOrderNumber">{{ sale.workOrderNumber }} · </template>
                  {{ sale.branchName }}
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
              <td class="num acciones">
                <!-- La acción común a la vista; el resto, en el menú. -->
                <button type="button" class="link" @click="cobrar(sale)">Abonar</button>
                <RowMenu>
                  <button type="button" @click="abrir(sale)">
                    {{ abierta === sale.id ? 'Ocultar abonos' : 'Ver abonos' }}
                  </button>
                  <button
                    v-if="sale.customerId"
                    type="button"
                    @click="mandarEstadoDeCuenta(sale)"
                  >
                    Mandar estado de cuenta
                  </button>
                  <button
                    v-if="sale.customerId"
                    type="button"
                    @click="bajarEstadoDeCuenta(sale)"
                  >
                    Bajar el estado en PDF
                  </button>
                  <RouterLink
                    v-if="sale.workOrderId"
                    :to="{ name: 'work-order', params: { id: sale.workOrderId } }"
                  >
                    Ir a la orden
                  </RouterLink>
                </RowMenu>
              </td>
            </tr>

            <!-- Los abonos que ya se le hicieron a esa factura. -->
            <tr v-if="abierta && grupo.facturas.some((f) => f.id === abierta)" class="detalle">
              <td colspan="5">
                <p v-if="!detalle" class="muted small">Cargando abonos…</p>

                <template v-else>
                  <p v-if="!detalle.payments.length" class="muted small">
                    Todavía no ha abonado nada a esa factura.
                  </p>

                  <ul v-else>
                    <li v-for="pago in detalle.payments" :key="pago.id">
                      <span>
                        {{ formatDateTime(pago.paidAt) }}
                        · {{ PAYMENT_METHOD_LABEL[pago.method] }}
                        <template v-if="pago.reference"> · {{ pago.reference }}</template>
                        <template v-if="pago.registeredByName">
                          · lo recibió {{ pago.registeredByName }}
                        </template>
                      </span>
                      <strong>{{ formatMoney(pago.amount) }}</strong>
                    </li>
                  </ul>
                </template>
              </td>
            </tr>

            <tr v-if="cobrando && grupo.facturas.some((f) => f.id === cobrando?.id)" class="cobro">
              <td colspan="5">
                <form @submit.prevent="guardarAbono">
                  <label>
                    Monto
                    <input
                      v-model.number="abono.amount"
                      type="number"
                      step="0.01"
                      min="0.01"
                      :max="cobrando?.balance"
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
                  <button type="submit" :disabled="busy">Anotar el abono</button>
                  <button type="button" class="btn-ghost" @click="cobrando = null">
                    Cancelar
                  </button>
                </form>
              </td>
            </tr>
          </template>
        </template>
      </tbody>
    </table>
  </section>
</template>

<style scoped>
/* La fila del cliente se toca para abrirla; las de sus facturas van metidas un poco. */
.grupo {
  cursor: pointer;
}

.grupo:hover td {
  background: var(--surface-alt);
}

.factura td:first-child {
  padding-left: var(--space-5);
}

.acciones {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: nowrap;
  gap: var(--space-2);
  white-space: nowrap;
}

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
  color: var(--on-accent);
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

.acciones {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.125rem;
}

.detalle ul {
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: 0.875rem;
}

.detalle li {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.25rem 0;
}

.estado {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  margin: 0.5rem 0 0;
}

.link.muted {
  color: var(--text-muted);
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

.danger {
  color: var(--danger);
}

.notice {
  color: var(--accent);
}
</style>

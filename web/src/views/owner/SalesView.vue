<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { salesApi, workOrdersApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import FilterBar from '@/components/FilterBar.vue'
import RowMenu from '@/components/RowMenu.vue'
import FilterChip from '@/components/FilterChip.vue'
import PeriodSelect from '@/components/PeriodSelect.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import {
  PAYMENT_METHOD_LABEL,
  type OrderMessageKind,
  type SaleDetail,
  type SaleListItem,
} from '@/types/domain'
import { formatMoney, whatsappLink } from '@/utils/format'
import { useAuthStore } from '@/stores/auth'

/**
 * El registro de ventas: todo lo facturado, factura por factura.
 *
 * Hasta hoy una venta solo se veía desde adentro de su orden, o en Caja —que es lo cobrado de
 * un día, no lo facturado— y en Por cobrar —que es solo lo que tiene saldo—. Una venta de
 * mostrador, que no tiene orden, no se veía en ninguna parte después de hacerla: ni para
 * volver a imprimirle el comprobante, ni para anularla si salió mal.
 *
 * Cada renglón dice de dónde salió: de una orden de trabajo, con su enlace, o del mostrador.
 * Es la diferencia que interesa al final del mes.
 */
const auth = useAuthStore()

const hoy = new Date()
const primeroDelMes = new Date(hoy.getFullYear(), hoy.getMonth(), 1)

function comoInput(fecha: Date) {
  return fecha.toISOString().slice(0, 10)
}

/**
 * La fecha corta —«18/08/26»—, no «18 de ago de 2026»: son veinte renglones de fechas, y la
 * larga se lleva el ancho que necesitan el total y los botones de cada venta.
 */
const dia = new Intl.DateTimeFormat('es-HN', { day: '2-digit', month: '2-digit', year: '2-digit' })

function fechaCorta(valor: string) {
  return dia.format(new Date(valor))
}

const desde = ref(comoInput(primeroDelMes))
const hasta = ref(comoInput(hoy))

/** El periodo lo pone el selector de la barra; aquí solo se guarda y se recarga. */
function cambiarPeriodo(rango: { desde: string; hasta: string }) {
  desde.value = rango.desde
  hasta.value = rango.hasta
  buscar()
}
const busqueda = ref('')
const conAnuladas = ref(false)
const soloConSaldo = ref(false)
/** La sucursal sale del menú, no de esta pantalla: es la misma para todo el panel. */
const branchId = computed(() => auth.activeBranchId ?? '')

const ventas = ref<SaleListItem[]>([])

/**
 * La venta abierta, a la derecha. Antes tocar la fila no abría nada: para saber qué llevaba
 * una venta había que bajar el PDF, y para eso hay que tener dónde abrirlo.
 */
const abierta = ref<SaleDetail | null>(null)
const abriendo = ref(false)

async function abrir(venta: SaleListItem) {
  abriendo.value = true
  try {
    abierta.value = await salesApi.get(venta.id)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo abrir la venta.')
  } finally {
    abriendo.value = false
  }
}

/**
 * Mandarle al cliente lo que compró. Si la venta salió de una orden, el enlace es el de
 * seguimiento, que ya le entrega la factura; si fue de mostrador, se le escribe al teléfono
 * que quedó en la venta.
 */
async function mandarPorWhatsApp(venta: SaleListItem) {
  const tab = window.open('', '_blank')

  try {
    if (venta.workOrderId) {
      const factura: OrderMessageKind = 'invoice'
      const link = await workOrdersApi.whatsappLink(venta.workOrderId, factura)
      if (tab) tab.location.href = link.url
      return
    }

    const detalle = abierta.value?.id === venta.id ? abierta.value : await salesApi.get(venta.id)

    if (!detalle.customerPhone) {
      tab?.close()
      error.value = 'Esa venta no tiene teléfono del cliente.'
      return
    }

    const texto = `Su comprobante ${detalle.number} por ${formatMoney(detalle.total)}.`
    if (tab) tab.location.href = whatsappLink(detalle.customerPhone, texto)
  } catch (e) {
    tab?.close()
    error.value = errorMessage(e, 'No se pudo armar el mensaje.')
  }
}
const total = ref(0)
const page = ref(1)
const pageSize = 25
const cargando = ref(false)
const error = ref('')


const paginas = computed(() => Math.max(1, Math.ceil(total.value / pageSize)))

/**
 * Lo que suma lo que se está viendo. No es el total del rango —eso es Reportes—, y decirlo
 * evita que alguien cuadre el mes con la suma de una página.
 */
const sumaPagina = computed(() =>
  ventas.value.filter((v) => !v.isVoided).reduce((suma, v) => suma + v.total, 0),
)

const deMostrador = computed(() => ventas.value.filter((v) => !v.workOrderId && !v.isVoided))

async function cargar() {
  cargando.value = true
  error.value = ''
  try {
    const pagina = await salesApi.list({
      // Con el desplazamiento de Honduras a la vista: el día del taller no es el día UTC del
      // servidor, y sin decirlo una venta de las seis de la tarde caería en el día siguiente.
      from: desde.value ? `${desde.value}T00:00:00-06:00` : undefined,
      to: hasta.value ? `${hasta.value}T23:59:59-06:00` : undefined,
      search: busqueda.value.trim() || undefined,
      includeVoided: conAnuladas.value,
      onlyUnpaid: soloConSaldo.value || undefined,
      branchId: branchId.value || undefined,
      page: page.value,
      pageSize,
    })
    ventas.value = pagina.items
    total.value = pagina.total
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar las ventas.')
  } finally {
    cargando.value = false
  }
}

function buscar() {
  page.value = 1
  cargar()
}

/**
 * Lo filtrado se pide solo. Medio segundo de espera en la búsqueda para no mandar una
 * consulta por letra; los chips y el periodo no esperan, que son un solo toque.
 */
let tecleando: ReturnType<typeof setTimeout> | undefined

watch(busqueda, () => {
  clearTimeout(tecleando)
  tecleando = setTimeout(buscar, 500)
})

watch([soloConSaldo, conAnuladas], buscar)

function irA(destino: number) {
  page.value = Math.min(paginas.value, Math.max(1, destino))
  cargar()
}

async function comprobante(venta: SaleListItem) {
  try {
    await salesApi.downloadPdf(venta.id, venta.number)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo bajar el comprobante.')
  }
}

/**
 * Anular pide motivo y no borra nada: la venta queda con su número y su motivo a la vista, y
 * los repuestos vuelven a la bodega. Es lo que exige el régimen y lo que hace que la caja
 * siga cuadrando después de un error.
 */
async function anular(venta: SaleListItem) {
  const motivo = window.prompt(`Anular la venta ${venta.number}. ¿Por qué?`)
  if (!motivo?.trim()) return

  // El dinero ya cobrado existe y tiene que ir a alguna parte: o se devolvió, o se aplica a
  // la factura nueva. Sin esa línea escrita, el descuadre aparece un mes después.
  const abonado = venta.total - venta.balance
  let nota: string | undefined
  if (abonado > 0) {
    const escrito = window.prompt(
      `Esta venta tiene ${formatMoney(abonado)} ya cobrados. ¿Qué se hizo con ese dinero?`,
    )
    if (!escrito?.trim()) return
    nota = escrito.trim()
  }

  error.value = ''
  try {
    await salesApi.void(venta.id, motivo.trim(), nota)
    await cargar()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo anular la venta.')
  }
}

// Al cambiar de sucursal en el menú, esta pantalla vuelve a pedir lo suyo.
watch(branchId, () => buscar())

onMounted(async () => {
  await cargar()
})
</script>

<template>
  <section>
    <header class="titulo">
      <div>
        <h1>Ventas</h1>
        <p class="muted small">
          <template v-if="auth.user && auth.user.branches.length > 1">
            <strong>{{ auth.activeBranchName }}</strong> ·
          </template>
          Lo facturado, venga de una orden o del mostrador. Para lo cobrado de un día está
          <RouterLink :to="{ name: 'cash-close' }">Caja</RouterLink>, y para lo que falta cobrar,
          <RouterLink :to="{ name: 'receivables' }">Por cobrar</RouterLink>.
        </p>
      </div>
      <!-- La venta de mostrador se hace desde aquí y ya no es una entrada aparte del menú:
           eran dos lugares para lo mismo —una en TRABAJO y otra en DINERO—, y no se
           entendía en qué se diferenciaban. -->
      <RouterLink class="boton" :to="{ name: 'counter-sale' }">Nueva venta</RouterLink>
    </header>

    <FilterBar v-model:search="busqueda" placeholder="Cliente, número de venta o de orden">
      <PeriodSelect @cambio="cambiarPeriodo" />
      <FilterChip v-model="soloConSaldo">Solo con saldo</FilterChip>
      <FilterChip v-model="conAnuladas">Incluir anuladas</FilterChip>
    </FilterBar>

    <ErrorNote v-if="error" :message="error" />
    <SkeletonList v-if="cargando" :rows="4" variant="tabla" />

    <template v-else>
      <div class="resumen">
        <div>
          <span>Ventas</span>
          <strong>{{ total }}</strong>
        </div>
        <div>
          <span>De mostrador</span>
          <strong>{{ deMostrador.length }}</strong>
          <small class="muted">en esta página</small>
        </div>
        <div>
          <span>Suma de la página</span>
          <strong class="num">{{ formatMoney(sumaPagina) }}</strong>
          <small class="muted">el total del rango está en Reportes</small>
        </div>
      </div>

      <p v-if="!ventas.length" class="muted">No hay ventas en ese rango.</p>

      <div v-else class="tabla">
        <table>
          <thead>
            <tr>
              <th scope="col">Fecha</th>
              <th scope="col">Venta</th>
              <th scope="col">De dónde</th>
              <th scope="col">Cliente</th>
              <th scope="col" class="num">Total</th>
              <th scope="col" class="num">Saldo</th>
              <th scope="col"><span class="sr-only">Acciones</span></th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="venta in ventas"
              :key="venta.id"
              class="clicable"
              :class="{ anulada: venta.isVoided, abierta: abierta?.id === venta.id }"
              @click="abrir(venta)"
            >
              <td class="num">{{ fechaCorta(venta.saleDate) }}</td>
              <td class="num">{{ venta.number }}</td>
              <td>
                <RouterLink
                  v-if="venta.workOrderId"
                  :to="{ name: 'work-order', params: { id: venta.workOrderId } }"
                >
                  {{ venta.workOrderNumber }}
                </RouterLink>
                <span v-else class="mostrador">Mostrador</span>
              </td>
              <td>{{ venta.customerName ?? 'De paso' }}</td>
              <td class="num">{{ formatMoney(venta.total) }}</td>
              <td class="num" :class="{ vencido: venta.isOverdue }">
                {{ venta.balance ? formatMoney(venta.balance) : '—' }}
              </td>
              <td class="col-menu">
                <span v-if="venta.isVoided" class="muted small">Anulada</span>
                <RowMenu v-else>
                  <button type="button" @click="comprobante(venta)">Ver la factura (PDF)</button>
                  <button type="button" @click="mandarPorWhatsApp(venta)">
                    Mandar por WhatsApp
                  </button>
                  <RouterLink
                    v-if="venta.workOrderId"
                    :to="{ name: 'work-order', params: { id: venta.workOrderId } }"
                  >
                    Ir a la orden
                  </RouterLink>
                  <button type="button" class="peligro" @click="anular(venta)">
                    Anular la venta…
                  </button>
                </RowMenu>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- El detalle a la derecha, como en Cotizaciones: se mira una venta sin salir de la
           lista ni bajar un PDF para saber qué llevaba. -->
      <aside v-if="abierta" class="detalle">
        <header>
          <div>
            <strong class="num">{{ abierta.number }}</strong>
            <div class="muted small">
              {{ fechaCorta(abierta.saleDate) }} · {{ abierta.branchName }}
              <template v-if="abierta.customerName"> · {{ abierta.customerName }}</template>
            </div>
          </div>
          <button type="button" class="btn-ghost btn-sm" @click="abierta = null">Cerrar</button>
        </header>

        <p v-if="abierta.fiscalNumber" class="muted small">
          Factura {{ abierta.fiscalNumber }} · CAI {{ abierta.fiscalCai }}
        </p>
        <p v-else class="muted small">Comprobante de entrega, sin CAI.</p>

        <table class="lineas">
          <tbody>
            <tr v-for="linea in abierta.lines" :key="linea.id">
              <td>
                {{ linea.description }}
                <span v-if="linea.quantity !== 1" class="muted small">
                  × {{ linea.quantity }}
                </span>
              </td>
              <td class="num">{{ formatMoney(linea.total) }}</td>
            </tr>
          </tbody>
        </table>

        <dl class="totales">
          <div v-if="abierta.discountTotal">
            <dt>Descuento</dt>
            <dd class="num">−{{ formatMoney(abierta.discountTotal) }}</dd>
          </div>
          <div v-if="abierta.taxTotal">
            <dt>ISV {{ abierta.taxRate }}%</dt>
            <dd class="num">{{ formatMoney(abierta.taxTotal) }}</dd>
          </div>
          <div class="total">
            <dt>Total</dt>
            <dd class="num">{{ formatMoney(abierta.total) }}</dd>
          </div>
        </dl>

        <h3>Pagos</h3>
        <p v-if="!abierta.payments.length" class="muted small">
          Sin abonos. Queda {{ formatMoney(abierta.balance ?? abierta.total) }} por cobrar.
        </p>
        <ul v-else class="pagos">
          <li v-for="pago in abierta.payments" :key="pago.id">
            <span>
              {{ fechaCorta(pago.paidAt) }} · {{ PAYMENT_METHOD_LABEL[pago.method] }}
              <span v-if="pago.registeredByName" class="muted small">
                · {{ pago.registeredByName }}
              </span>
            </span>
            <span class="num">{{ formatMoney(pago.amount) }}</span>
          </li>
        </ul>
      </aside>

      <div v-if="paginas > 1" class="paginas">
        <button type="button" :disabled="page <= 1" @click="irA(page - 1)">Anterior</button>
        <span class="muted small">Página {{ page }} de {{ paginas }}</span>
        <button type="button" :disabled="page >= paginas" @click="irA(page + 1)">Siguiente</button>
      </div>
    </template>
  </section>
</template>

<style scoped>
/* Lo abierto se marca en la lista: sin esto, el panel de la derecha parece de cualquiera. */
.clicable {
  cursor: pointer;
}

tr.abierta td {
  background: color-mix(in srgb, var(--accent) 10%, transparent);
}

.col-menu {
  text-align: right;
  width: 1%;
}

.detalle {
  margin-top: var(--space-4);
  padding: var(--space-4);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.detalle header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-3);
  margin-bottom: var(--space-2);
}

.detalle h3 {
  margin: var(--space-4) 0 var(--space-2);
  font-size: 0.9375rem;
}

.lineas {
  width: 100%;
  margin-top: var(--space-3);
  border-collapse: collapse;
}

.lineas td {
  padding: 0.3rem 0;
  border-bottom: 1px solid var(--border);
}

.totales {
  margin: var(--space-3) 0 0;
  margin-left: auto;
  width: min(18rem, 100%);
}

.totales div {
  display: flex;
  justify-content: space-between;
  gap: var(--space-3);
}

.totales dt,
.totales dd {
  margin: 0;
}

.totales .total {
  padding-top: 0.25rem;
  border-top: 1px solid var(--border);
  font-weight: 600;
}

.pagos {
  margin: 0;
  padding: 0;
  list-style: none;
}

.pagos li {
  display: flex;
  justify-content: space-between;
  gap: var(--space-3);
  padding: 0.25rem 0;
  border-bottom: 1px solid var(--border);
  font-size: 0.9375rem;
}

.titulo {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.titulo h1 {
  margin: 0 0 0.25rem;
}

.titulo p {
  margin: 0;
  max-width: 40rem;
}

.boton {
  padding: 0.5rem 0.875rem;
  border-radius: var(--radius-sm);
  background: var(--accent);
  color: var(--on-accent);
  font-size: 0.9375rem;
}

.boton:hover {
  text-decoration: none;
  filter: brightness(1.05);
}

.filtros {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.filtros label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.filtros .ancho {
  flex: 1;
  min-width: 12rem;
}

.filtros .checkbox {
  flex-direction: row;
  align-items: center;
  gap: 0.375rem;
  padding-bottom: 0.375rem;
  color: var(--text);
  font-size: 0.9375rem;
}

.resumen {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.resumen div {
  display: flex;
  flex-direction: column;
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.resumen span {
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.resumen strong {
  font-size: 1.25rem;
}

.resumen small {
  font-size: 0.75rem;
}

/* La tabla es ancha: en el teléfono corre dentro de su caja y no arrastra la página. */
.tabla {
  overflow-x: auto;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

table {
  width: 100%;
  min-width: 44rem;
  border-collapse: collapse;
  font-size: 0.875rem;
}

th,
td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
  white-space: nowrap;
}

tbody tr:last-child td {
  border-bottom: 0;
}

th {
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

th.num,
td.num {
  text-align: right;
}

.anulada {
  color: var(--text-muted);
  text-decoration: line-through;
}

.mostrador {
  padding: 0.0625rem 0.375rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.vencido {
  color: var(--danger);
}

td.acciones {
  display: flex;
  gap: 0.375rem;
}

.paginas {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-top: 0.75rem;
}

</style>

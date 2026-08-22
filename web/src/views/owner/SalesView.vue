<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, salesApi } from '@/api/garaj'
import { type Branch, type SaleListItem } from '@/types/domain'
import { formatMoney } from '@/utils/format'
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
const busqueda = ref('')
const conAnuladas = ref(false)
const soloConSaldo = ref(false)
const branchId = ref('')

const ventas = ref<SaleListItem[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = 25
const cargando = ref(false)
const error = ref('')

const branches = ref<Branch[]>([])
const sucursalesPermitidas = computed(() =>
  auth.user?.branches.length
    ? branches.value.filter((b) => auth.user?.branches.some((suya) => suya.id === b.id))
    : branches.value,
)

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

  error.value = ''
  try {
    await salesApi.void(venta.id, motivo.trim())
    await cargar()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo anular la venta.')
  }
}

onMounted(async () => {
  branches.value = await branchesApi.list().catch(() => [])
  await cargar()
})
</script>

<template>
  <section>
    <header class="titulo">
      <div>
        <h1>Ventas</h1>
        <p class="muted small">
          Lo facturado, venga de una orden o del mostrador. Para lo cobrado de un día está
          <RouterLink :to="{ name: 'cash-close' }">Caja</RouterLink>, y para lo que falta cobrar,
          <RouterLink :to="{ name: 'receivables' }">Por cobrar</RouterLink>.
        </p>
      </div>
      <RouterLink class="boton" :to="{ name: 'counter-sale' }">Vender repuesto</RouterLink>
    </header>

    <div class="filtros">
      <label>
        Desde
        <input v-model="desde" type="date" />
      </label>
      <label>
        Hasta
        <input v-model="hasta" type="date" />
      </label>
      <label v-if="sucursalesPermitidas.length > 1">
        Sucursal
        <select v-model="branchId">
          <option value="">Todas</option>
          <option v-for="branch in sucursalesPermitidas" :key="branch.id" :value="branch.id">
            {{ branch.name }}
          </option>
        </select>
      </label>
      <label class="ancho">
        Buscar
        <input
          v-model="busqueda"
          type="search"
          placeholder="Cliente, número de venta o de orden"
          @keydown.enter.prevent="buscar"
        />
      </label>
      <label class="checkbox">
        <input v-model="soloConSaldo" type="checkbox" />
        Solo con saldo
      </label>
      <label class="checkbox">
        <input v-model="conAnuladas" type="checkbox" />
        Incluir anuladas
      </label>
      <button type="button" :disabled="cargando" @click="buscar">Aplicar</button>
    </div>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="cargando" class="muted">Cargando…</p>

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
              <th>Fecha</th>
              <th>Venta</th>
              <th>De dónde</th>
              <th>Cliente</th>
              <th class="num">Total</th>
              <th class="num">Saldo</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="venta in ventas" :key="venta.id" :class="{ anulada: venta.isVoided }">
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
              <td class="acciones">
                <button type="button" class="suave" @click="comprobante(venta)">PDF</button>
                <button
                  v-if="!venta.isVoided"
                  type="button"
                  class="suave peligro"
                  @click="anular(venta)"
                >
                  Anular
                </button>
                <span v-else class="muted small">Anulada</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="paginas > 1" class="paginas">
        <button type="button" :disabled="page <= 1" @click="irA(page - 1)">Anterior</button>
        <span class="muted small">Página {{ page }} de {{ paginas }}</span>
        <button type="button" :disabled="page >= paginas" @click="irA(page + 1)">Siguiente</button>
      </div>
    </template>
  </section>
</template>

<style scoped>
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
  color: #fff;
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

.suave {
  padding: 0.25rem 0.5rem;
  background: var(--surface-alt);
  color: var(--text);
  border: 1px solid var(--border);
  font-size: 0.8125rem;
}

.peligro:hover {
  border-color: var(--danger);
  color: var(--danger);
}

.paginas {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-top: 0.75rem;
}

.error {
  color: var(--danger);
}
</style>

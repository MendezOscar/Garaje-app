<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, customersApi, salesApi, stockApi, tenantApi } from '@/api/garaj'
import {
  LineType,
  PAYMENT_METHOD_LABEL,
  PaymentMethod,
  type Branch,
  type Customer,
  type FiscalRange,
  type SaleDetail,
  type StockItem,
} from '@/types/domain'
import { formatMoney } from '@/utils/format'
import { useAuthStore } from '@/stores/auth'

/**
 * Vender un repuesto sin recibir el vehículo.
 *
 * Pasa todos los días: alguien entra por un filtro o un litro de aceite y se va. Hasta hoy la
 * única forma de registrarlo era abrirle una orden de trabajo a una moto que nunca entró al
 * taller, o no registrarlo —y entonces el repuesto sale de la bodega sin que la caja lo sepa—.
 * `POST /api/sales` existía desde el principio y descuenta la existencia; lo que faltaba era
 * la puerta.
 *
 * La venta se arma desde las existencias de la sucursal, no desde el catálogo: lo que importa
 * en el mostrador es lo que hay para entregar hoy, con su precio y su cantidad.
 */
const auth = useAuthStore()

const branches = ref<Branch[]>([])
const branchId = ref('')

const busqueda = ref('')
const resultados = ref<StockItem[]>([])
const buscando = ref(false)

/** Las líneas de la venta. El precio y el descuento se pueden tocar: en el mostrador se regatea. */
const lineas = ref<
  { partId: string; sku: string; nombre: string; unidad: string; disponible: number; cantidad: number; precio: number; descuento: number }[]
>([])

/**
 * A quién se le vende. Es opcional a propósito: la mayoría de estas ventas son a alguien que
 * pasaba por la calle, y obligar a crearle una ficha para venderle un empaque de L 40 es lo
 * que hace que la venta no se registre. Se vuelve necesario para facturar con su RTN o para
 * dejarle la compra en su historial.
 */
const cliente = ref<Customer | null>(null)
const buscaCliente = ref('')
const clientes = ref<Customer[]>([])
const buscandoCliente = ref(false)

const paymentMethod = ref<PaymentMethod>(PaymentMethod.Cash)
const notas = ref('')

const tasaImpuesto = ref(0)
const rangos = ref<FiscalRange[]>([])

/**
 * Factura con CAI. Apagada por defecto: cada factura fiscal quema un número del rango
 * autorizado y quien compra un empaque casi nunca la pide. Con CAI la venta lleva ISV; sin
 * CAI, no —la regla es del servidor, esta pantalla solo la enseña—.
 */
const conCai = ref(false)
const rtnFactura = ref('')
const nombreFactura = ref('')

const guardando = ref(false)
const error = ref('')

/** La venta recién hecha. Se queda a la vista para imprimirla y para empezar la siguiente. */
const hecha = ref<SaleDetail | null>(null)

const sucursalesPermitidas = computed(() =>
  auth.user?.branches.length
    ? branches.value.filter((b) => auth.user?.branches.some((suya) => suya.id === b.id))
    : branches.value,
)

const rangoFiscal = computed(
  () => rangos.value.find((r) => r.branchId === branchId.value && r.isActive) ?? null,
)

/** Por qué no se puede emitir con CAI en esta sucursal, o null si sí se puede. */
const impedimentoCai = computed(() => {
  if (!rangoFiscal.value) return 'Esta sucursal no tiene CAI registrado (Taller → Facturación).'
  if (rangoFiscal.value.isExpired) return 'El CAI de esta sucursal venció.'
  if (rangoFiscal.value.isExhausted) return 'Se agotó el rango autorizado de esta sucursal.'
  return null
})

const cuenta = computed(() => {
  const subtotal = lineas.value.reduce((suma, l) => suma + l.cantidad * l.precio, 0)
  const descuento = lineas.value.reduce((suma, l) => suma + l.descuento, 0)
  const base = Math.max(0, subtotal - descuento)
  const isv = Math.round((base * tasaImpuesto.value) / 100 * 100) / 100
  const impuesto = conCai.value ? isv : 0
  return { subtotal, descuento, base, impuesto, total: base + impuesto, conIsv: base + isv }
})

/** Lo que se pide de más de lo que hay. El servidor lo rechaza, así que se avisa antes. */
const sinExistencia = computed(() => lineas.value.filter((l) => l.cantidad > l.disponible))

const puedeGuardar = computed(
  () => lineas.value.length > 0 && !sinExistencia.value.length && !!branchId.value && !guardando.value,
)

/**
 * Busca en la bodega de la sucursal. No se precarga la lista entera: en el mostrador uno ya
 * sabe qué le están pidiendo, y ocho resultados empujaban el resto de la hoja fuera de la
 * pantalla del teléfono antes de que nadie escribiera nada.
 */
async function buscar() {
  const texto = busqueda.value.trim()
  if (!branchId.value || texto.length < 2) {
    resultados.value = []
    return
  }
  buscando.value = true
  try {
    const page = await stockApi.list({ branchId: branchId.value, search: texto, pageSize: 6 })
    resultados.value = page.items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo buscar en la bodega.')
  } finally {
    buscando.value = false
  }
}

function agregar(item: StockItem) {
  const ya = lineas.value.find((l) => l.partId === item.partId)
  if (ya) {
    ya.cantidad += 1
    return
  }
  lineas.value.push({
    partId: item.partId,
    sku: item.sku,
    nombre: item.partName,
    unidad: item.unit,
    disponible: item.quantity,
    cantidad: 1,
    precio: item.salePrice,
    descuento: 0,
  })
}

function quitar(partId: string) {
  lineas.value = lineas.value.filter((l) => l.partId !== partId)
}

async function buscarCliente() {
  const texto = buscaCliente.value.trim()
  if (texto.length < 2) {
    clientes.value = []
    return
  }
  buscandoCliente.value = true
  try {
    clientes.value = (await customersApi.list({ search: texto, pageSize: 6 })).items
  } catch {
    // Es una ayuda para encontrarlo: si falla, la venta se registra igual sin cliente.
  } finally {
    buscandoCliente.value = false
  }
}

function elegirCliente(elegido: Customer | null) {
  cliente.value = elegido
  clientes.value = []
  buscaCliente.value = ''
  // La factura sale con lo que tenga su ficha, y se puede cambiar aquí para esta venta.
  rtnFactura.value = elegido?.taxId ?? ''
  nombreFactura.value = elegido?.billingName ?? elegido?.fullName ?? ''
}

async function guardar() {
  if (!puedeGuardar.value) return
  guardando.value = true
  error.value = ''
  try {
    hecha.value = await salesApi.create({
      branchId: branchId.value,
      customerId: cliente.value?.id,
      paymentMethod: paymentMethod.value,
      notes: notas.value.trim() || undefined,
      lines: lineas.value.map((l) => ({
        lineType: LineType.Part,
        partId: l.partId,
        quantity: l.cantidad,
        unitPrice: l.precio,
        discount: l.descuento,
      })),
      fiscal: conCai.value,
      customerTaxId: conCai.value ? rtnFactura.value.trim() || undefined : undefined,
      customerName: conCai.value ? nombreFactura.value.trim() || undefined : undefined,
    })
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo registrar la venta.')
  } finally {
    guardando.value = false
  }
}

async function descargar() {
  const venta = hecha.value
  if (!venta) return
  try {
    await salesApi.downloadPdf(venta.id, venta.number)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo bajar el comprobante.')
  }
}

/** Otra venta: se limpia lo de la anterior y se deja la sucursal, que es la misma todo el día. */
function otra() {
  hecha.value = null
  lineas.value = []
  cliente.value = null
  notas.value = ''
  conCai.value = false
  rtnFactura.value = ''
  nombreFactura.value = ''
  busqueda.value = ''
  buscar()
}

watch(branchId, () => {
  // Las existencias son de la sucursal: cambiarla invalida lo que estaba en la lista.
  lineas.value = []
  conCai.value = false
  resultados.value = []
  buscar()
})

onMounted(async () => {
  branches.value = await branchesApi.list().catch(() => [])
  branchId.value = sucursalesPermitidas.value[0]?.id ?? ''
  tasaImpuesto.value = (await tenantApi.get().catch(() => null))?.defaultTaxRate ?? 0
  rangos.value = await tenantApi.fiscalRanges().catch(() => [])
})
</script>

<template>
  <section>
    <header>
      <RouterLink :to="{ name: 'home' }" class="volver" aria-label="Volver al inicio">‹</RouterLink>
      <div>
        <h1>Vender repuesto</h1>
        <p class="muted small">
          Sin recibir el vehículo. Sale de la bodega de la sucursal y entra a la caja del día.
        </p>
      </div>
    </header>

    <p v-if="error" class="error">{{ error }}</p>

    <!-- Ya se registró: lo que queda es el comprobante y empezar la siguiente. -->
    <article v-if="hecha" class="marco recien">
      <h2>Venta {{ hecha.number }}</h2>
      <p class="total-hecho num">{{ formatMoney(hecha.total) }}</p>
      <p v-if="hecha.fiscalNumber" class="muted small">
        Factura fiscal <strong>{{ hecha.fiscalNumber }}</strong> · CAI {{ hecha.fiscalCai }}
      </p>
      <p v-else class="muted small">
        Comprobante de entrega, sin CAI: no lleva ISV.
      </p>
      <p class="muted small">Los repuestos ya salieron de la bodega.</p>
      <div class="acciones">
        <button type="button" @click="descargar">Bajar el comprobante</button>
        <button type="button" class="suave" @click="otra">Otra venta</button>
      </div>
    </article>

    <form v-else class="hoja-y-lado" @submit.prevent="guardar">
      <article class="marco">
        <label v-if="sucursalesPermitidas.length > 1" class="campo">
          Sucursal
          <select v-model="branchId">
            <option v-for="branch in sucursalesPermitidas" :key="branch.id" :value="branch.id">
              {{ branch.name }}
            </option>
          </select>
        </label>

        <section class="paso">
          <h2><span class="numero">1</span> Qué se vende</h2>
          <div class="buscador">
            <input
              v-model="busqueda"
              type="search"
              placeholder="Buscar por nombre o código"
              @input="buscar"
              @keydown.enter.prevent="buscar"
            />
            <button type="button" class="suave" @click="buscar">Buscar</button>
          </div>

          <p v-if="buscando" class="muted small">Buscando…</p>
          <ul v-else-if="resultados.length" class="resultados">
            <li v-for="item in resultados" :key="item.partId">
              <button type="button" class="resultado" @click="agregar(item)">
                <span class="nombre">{{ item.partName }}</span>
                <span class="meta muted small">
                  <span class="num">{{ item.sku }}</span>
                  · <span class="num">{{ formatMoney(item.salePrice) }}</span>
                  ·
                  <span :class="{ nada: item.quantity <= 0 }">
                    quedan {{ item.quantity }} {{ item.unit }}
                  </span>
                </span>
              </button>
            </li>
          </ul>
          <p v-else-if="busqueda.trim().length >= 2" class="muted small">
            Nada con ese nombre en esta sucursal.
          </p>
          <p v-else class="muted small">Escriba el nombre o el código del repuesto.</p>

          <div v-if="lineas.length" class="tabla">
            <table>
              <thead>
                <tr>
                  <th>Repuesto</th>
                  <th class="num">Cantidad</th>
                  <th class="num">Precio</th>
                  <th class="num">Descuento</th>
                  <th class="num">Total</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="linea in lineas" :key="linea.partId">
                  <td>
                    {{ linea.nombre }}
                    <span class="muted small num">{{ linea.sku }}</span>
                  </td>
                  <td class="num">
                    <input v-model.number="linea.cantidad" type="number" min="0.01" step="0.01" />
                    <span v-if="linea.cantidad > linea.disponible" class="falta small">
                      hay {{ linea.disponible }}
                    </span>
                  </td>
                  <td class="num">
                    <input v-model.number="linea.precio" type="number" min="0" step="0.01" />
                  </td>
                  <td class="num">
                    <input v-model.number="linea.descuento" type="number" min="0" step="0.01" />
                  </td>
                  <td class="num">
                    {{ formatMoney(Math.max(0, linea.cantidad * linea.precio - linea.descuento)) }}
                  </td>
                  <td>
                    <button type="button" class="quitar" @click="quitar(linea.partId)">×</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-else class="muted small">Todavía no hay nada en la venta.</p>
        </section>

        <section class="paso">
          <h2><span class="numero">2</span> Quién compra</h2>
          <p v-if="cliente" class="elegido">
            <strong>{{ cliente.fullName }}</strong>
            <span class="muted small"> · {{ cliente.phone }}</span>
            <button type="button" class="quitar" @click="elegirCliente(null)">×</button>
          </p>
          <template v-else>
            <div class="buscador">
              <input
                v-model="buscaCliente"
                type="search"
                placeholder="Buscar por nombre o teléfono"
                @input="buscarCliente"
              />
            </div>
            <ul v-if="clientes.length" class="resultados">
              <li v-for="posible in clientes" :key="posible.id">
                <button type="button" class="resultado" @click="elegirCliente(posible)">
                  <span class="nombre">{{ posible.fullName }}</span>
                  <span class="meta muted small">{{ posible.phone }}</span>
                </button>
              </li>
            </ul>
            <p v-else-if="buscandoCliente" class="muted small">Buscando…</p>
            <p v-else class="muted small">
              Opcional. Sin cliente la venta es a alguien de paso; con cliente le queda en su
              historial y se le puede facturar con su RTN.
            </p>
          </template>
        </section>

        <section class="paso">
          <h2><span class="numero">3</span> Cómo paga</h2>
          <div class="campos">
            <label class="campo">
              Forma de pago
              <select v-model.number="paymentMethod">
                <option
                  v-for="(label, value) in PAYMENT_METHOD_LABEL"
                  :key="value"
                  :value="Number(value)"
                >
                  {{ label }}
                </option>
              </select>
            </label>
          </div>

          <label class="checkbox" :class="{ apagado: impedimentoCai }">
            <input v-model="conCai" type="checkbox" :disabled="!!impedimentoCai" />
            Factura con CAI
            <span v-if="rangoFiscal && !impedimentoCai" class="muted small">
              · siguiente {{ rangoFiscal.nextFiscalNumber }}<template v-if="tasaImpuesto">
              · le suma el ISV {{ tasaImpuesto }}%</template>
            </span>
          </label>
          <p v-if="impedimentoCai" class="muted small">{{ impedimentoCai }}</p>

          <label v-if="conCai" class="campo">
            A nombre de
            <input v-model="nombreFactura" placeholder="Nombre o razón social" />
          </label>
          <label v-if="conCai" class="campo">
            RTN del cliente
            <input v-model="rtnFactura" placeholder="Sin RTN: consumidor final" />
          </label>

          <label class="campo">
            Nota
            <input v-model="notas" placeholder="Opcional" />
          </label>
        </section>

        <div class="acciones">
          <button type="submit" :disabled="!puedeGuardar">
            {{ guardando ? 'Registrando…' : 'Registrar la venta' }}
          </button>
        </div>
        <p v-if="sinExistencia.length" class="falta small">
          No hay tanto de: {{ sinExistencia.map((l) => l.nombre).join(', ') }}. Registre la entrada
          o ajuste el inventario antes de venderlo.
        </p>
      </article>

      <aside>
        <article class="card">
          <h2>Cobro</h2>
          <dl class="cuentas">
            <dt>Repuestos</dt>
            <dd class="num">{{ formatMoney(cuenta.subtotal) }}</dd>
            <template v-if="cuenta.descuento">
              <dt>Descuento</dt>
              <dd class="num">−{{ formatMoney(cuenta.descuento) }}</dd>
            </template>
            <template v-if="tasaImpuesto && conCai">
              <dt>ISV {{ tasaImpuesto }}%</dt>
              <dd class="num">{{ formatMoney(cuenta.impuesto) }}</dd>
            </template>
            <dt class="fuerte">Total</dt>
            <dd class="fuerte num grande">{{ formatMoney(cuenta.total) }}</dd>
          </dl>
          <p v-if="tasaImpuesto && !conCai && cuenta.base" class="muted small">
            Sin CAI no lleva ISV. Con factura: {{ formatMoney(cuenta.conIsv) }}.
          </p>
        </article>

        <article class="card">
          <h2>Lo que va a pasar</h2>
          <ol>
            <li>Los repuestos salen de la bodega de la sucursal</li>
            <li>La venta entra a la caja del día como pagada</li>
            <li v-if="conCai">Se emite la factura con el siguiente número del CAI</li>
            <li v-else>Sale un comprobante de entrega, sin CAI y sin ISV</li>
            <li v-if="cliente">Le queda en el historial de {{ cliente.fullName }}</li>
          </ol>
        </article>
      </aside>
    </form>
  </section>
</template>

<style scoped>
header {
  display: flex;
  align-items: flex-start;
  gap: 0.625rem;
  margin-bottom: 1.25rem;
}

header h1 {
  margin: 0 0 0.25rem;
}

.volver {
  padding: 0 0.375rem;
  font-size: 1.5rem;
  line-height: 1.2;
  color: var(--text-muted);
}

.volver:hover {
  color: var(--accent);
  text-decoration: none;
}

.hoja-y-lado {
  display: grid;
  grid-template-columns: minmax(0, 1.6fr) minmax(0, 1fr);
  gap: 1rem;
  align-items: start;
}

@media (max-width: 60rem) {
  .hoja-y-lado {
    grid-template-columns: minmax(0, 1fr);
  }
}

.marco,
.card {
  padding: 1.25rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.recien {
  border-color: var(--accent);
}

.total-hecho {
  margin: 0.25rem 0;
  font-size: 1.75rem;
  font-weight: 600;
}

aside {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  min-width: 0;
}

.card {
  padding: 1rem;
}

.card h2 {
  margin: 0 0 0.625rem;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

/* Los pasos numerados: el mostrador va de arriba hacia abajo, no de tarjeta en tarjeta. */
.paso {
  padding-top: 1rem;
  margin-top: 1rem;
  border-top: 1px solid var(--border);
}

.paso:first-of-type {
  padding-top: 0;
  margin-top: 0;
  border-top: 0;
}

.paso h2 {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin: 0 0 0.75rem;
  font-size: 1rem;
}

.numero {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 1.5rem;
  height: 1.5rem;
  border-radius: 50%;
  background: var(--surface-alt);
  color: var(--text-muted);
  font-size: 0.8125rem;
}

.campos {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
  gap: 0.75rem;
}

.campo {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  margin-bottom: 0.75rem;
  font-size: 0.875rem;
  color: var(--text-muted);
}

.buscador {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 0.5rem;
}

.buscador input {
  flex: 1;
  min-width: 0;
}

.resultados {
  list-style: none;
  margin: 0 0 0.75rem;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.resultado {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.125rem;
  width: 100%;
  padding: 0.5rem 0.625rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: inherit;
  text-align: left;
  font: inherit;
}

.resultado:hover {
  border-color: var(--accent);
}

.resultado .nombre {
  min-width: 0;
}

.nada {
  color: var(--danger);
}

/* La tabla se desborda en el teléfono si no se la deja correr dentro de su caja. */
.tabla {
  overflow-x: auto;
}

table {
  width: 100%;
  min-width: 22rem;
  border-collapse: collapse;
  font-size: 0.875rem;
}

th,
td {
  padding: 0.375rem 0.5rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
}

th.num,
td.num {
  text-align: right;
}

/* El nombre del repuesto necesita su ancho: sin esto parte en tres renglones de dos letras. */
th:first-child,
td:first-child {
  min-width: 9rem;
}

/* El código debajo del nombre, no peleando con él por la misma línea. */
td:first-child .small {
  display: block;
}

td input {
  width: 5.5rem;
  text-align: right;
}

.quitar {
  padding: 0 0.375rem;
  border: 0;
  background: none;
  color: var(--text-muted);
  font-size: 1.125rem;
  line-height: 1;
}

.quitar:hover {
  color: var(--danger);
}

.elegido {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  margin: 0;
}

.checkbox {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
  font-size: 0.9375rem;
}

.checkbox.apagado {
  color: var(--text-muted);
}

.cuentas {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 0.25rem 1rem;
  margin: 0;
  font-size: 0.9375rem;
}

.cuentas dt,
.cuentas dd {
  margin: 0;
}

.cuentas .fuerte {
  padding-top: 0.375rem;
  border-top: 1px solid var(--border);
  font-weight: 600;
}

.cuentas .grande {
  font-size: 1.125rem;
}

.acciones {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 1rem;
}

.suave {
  background: var(--surface-alt);
  color: var(--text);
  border: 1px solid var(--border);
}

.falta {
  color: var(--danger);
}

.error {
  margin: 0 0 0.75rem;
  color: var(--danger);
}
</style>

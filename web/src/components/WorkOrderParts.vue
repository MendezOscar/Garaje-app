<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { partsApi, workOrdersApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import type { Part, QuoteLine, WorkOrderPart } from '@/types/domain'
import { formatMoney, formatQuantity } from '@/utils/format'

const props = defineProps<{
  workOrderId: string
  parts: WorkOrderPart[]
  total: number
  /** Falso para el Cliente: ve lo que le pusieron, no lo modifica. */
  canEdit: boolean
  /** El taller ve el costo; el cliente, solo el precio. */
  showCost: boolean
  /**
   * El vehículo de la orden, para poder ofrecer solo lo que le sirve. Null en la orden sin
   * vehículo —la venta de mostrador— y entonces el interruptor ni aparece.
   */
  vehicleId?: string | null
  /**
   * Las cotizaciones de esta orden que llevan repuestos. Son las que se pueden traer de un
   * tirón, para no volver a teclear lo que ya se le cotizó al cliente.
   */
  quotesWithParts?: { id: string; number: string; parts: QuoteLine[] }[]
  /** Solo el Dueño puede llevar un precio corregido al catálogo del taller. */
  canTouchCatalog?: boolean
}>()

const emit = defineEmits<{ changed: [] }>()

const catalog = ref<Part[]>([])
const search = ref('')
const selectedPartId = ref('')
const quantity = ref(1)
const error = ref('')
const aviso = ref('')

/** La línea a la que se le está corrigiendo el precio, y con qué. */
const enPrecio = ref<WorkOrderPart | null>(null)
const precioNuevo = ref<number | ''>('')
const cantidadNueva = ref<number | ''>('')
const tambienElCatalogo = ref(false)

/** Corregir la cantidad de una línea ya confirmada mueve la bodega, y hay que decirlo. */
const mueveBodega = computed(
  () =>
    !!enPrecio.value?.partId &&
    !enPrecio.value.porConfirmar &&
    Number(cantidadNueva.value) !== enPrecio.value.quantity,
)
const busy = ref(false)
const adding = ref(false)

/**
 * De dónde sale el repuesto. El manual es para lo que se compró de encargo y nunca estuvo en
 * bodega: no hay existencia que descontar ni precio de catálogo del que partir, así que el
 * precio se escribe.
 */
const origen = ref<'catalogo' | 'manual'>('catalogo')

/**
 * «Solo lo que le sirve a este carro».
 *
 * Empieza apagado y nunca se enciende solo: la columna de a qué vehículos aplica se llena a
 * mano y va a estar incompleta mucho tiempo. Encendido por defecto escondería repuestos que
 * sí sirven y nadie entendería por qué.
 */
const soloLoQueAplica = ref(false)
const concepto = ref('')
const precio = ref<number | ''>('')
const costo = ref<number | ''>('')

/**
 * Si se compró en una casa de repuestos. Se pregunta en vez de deducirse de que la línea sea
 * manual: a mano también se carga un repuesto del taller que no está en el catálogo, y ese sí
 * se factura. Lo comprado afuera se le cobra al cliente, pero el taller no lo factura.
 */
const afuera = ref(false)
const casa = ref('')

const selectedPart = computed(() => catalog.value.find((p) => p.id === selectedPartId.value))

/** Lo que se le va a cobrar al cliente por lo que se está por agregar. */
const preview = computed(() => {
  const cantidad = Number(quantity.value) || 0
  if (origen.value === 'manual') return (Number(precio.value) || 0) * cantidad
  return selectedPart.value ? selectedPart.value.salePrice * cantidad : 0
})

const listo = computed(() =>
  origen.value === 'manual'
    ? !!concepto.value.trim() && Number(precio.value) >= 0 && precio.value !== ''
    : !!selectedPartId.value,
)

/**
 * La línea de la orden que corresponde a una de la cotización. Es el mismo emparejamiento que
 * usa el servidor al traerlos: por repuesto del catálogo, y lo demás por concepto.
 */
function enLaOrden(linea: QuoteLine) {
  return linea.partId && !linea.boughtOutside
    ? props.parts.find((p) => p.partId === linea.partId)
    : props.parts.find(
        (p) =>
          !p.partId && p.partName.trim().toLowerCase() === linea.description.trim().toLowerCase(),
      )
}

/** El precio unitario que el cliente aprobó: el total de la línea, con su descuento, entre lo que lleva. */
function precioCotizado(linea: QuoteLine) {
  return linea.quantity > 0 ? Math.round((linea.total / linea.quantity) * 100) / 100 : 0
}

/**
 * Lo que se trajo de una cotización y después cambió en ella.
 *
 * La línea de la orden es una copia: se congela al traerla, porque mueve bodega. Si luego se
 * corrige la cotización, nadie se entera, y la factura saldría con el precio viejo. Por eso se
 * avisa aquí, que es donde se arregla.
 */
const desfasados = computed(() => {
  const avisos: { numero: string; concepto: string; cotizado: string; enOrden: string }[] = []

  for (const q of props.quotesWithParts ?? []) {
    for (const linea of q.parts) {
      const puesta = enLaOrden(linea)
      if (!puesta) continue

      const precio = precioCotizado(linea)
      const mismaCantidad = Math.abs(puesta.quantity - linea.quantity) < 0.0001
      const mismoPrecio = Math.abs(puesta.unitPrice - precio) < 0.005

      if (mismaCantidad && mismoPrecio) continue

      avisos.push({
        numero: q.number,
        concepto: puesta.partName,
        cotizado: `${formatQuantity(linea.quantity)} × ${formatMoney(precio)}`,
        enOrden: `${formatQuantity(puesta.quantity)} × ${formatMoney(puesta.unitPrice)}`,
      })
    }
  }

  return avisos
})

/**
 * Trae los repuestos de una cotización. Entran por confirmar: lo cotizado es la propuesta que
 * se le mandó al cliente, así que nada sale de bodega hasta que alguien confirme cada línea.
 */
async function traerDeLaCotizacion(quoteId: string) {
  busy.value = true
  error.value = ''
  aviso.value = ''
  try {
    const r = await workOrdersApi.importQuoteParts(props.workOrderId, quoteId)

    aviso.value = r.added.length
      ? `Entraron ${r.added.length} ${r.added.length === 1 ? 'repuesto' : 'repuestos'}, por confirmar.`
          + (r.skipped ? ` ${r.skipped} ya estaban cargados.` : '')
      : 'Esos repuestos ya estaban cargados en la orden.'

    emit('changed')
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron traer los repuestos de la cotización.')
  } finally {
    busy.value = false
  }
}

async function loadCatalog() {
  try {
    const page = await partsApi.list({
      search: search.value || undefined,
      vehicleId: soloLoQueAplica.value ? (props.vehicleId ?? undefined) : undefined,
      pageSize: 50,
    })
    catalog.value = page.items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el catálogo.')
  }
}

async function open() {
  adding.value = true
  error.value = ''
  if (!catalog.value.length) await loadCatalog()
}

async function add() {
  if (!listo.value) return

  busy.value = true
  error.value = ''
  try {
    await workOrdersApi.addPart(
      props.workOrderId,
      origen.value === 'manual'
        ? {
            description: concepto.value.trim(),
            quantity: Number(quantity.value),
            unitPrice: Number(precio.value),
            unitCost: costo.value === '' ? undefined : Number(costo.value),
            boughtOutside: afuera.value,
            supplierName: afuera.value && casa.value.trim() ? casa.value.trim() : undefined,
          }
        : { partId: selectedPartId.value, quantity: Number(quantity.value) },
    )
    selectedPartId.value = ''
    concepto.value = ''
    precio.value = ''
    costo.value = ''
    afuera.value = false
    casa.value = ''
    quantity.value = 1
    adding.value = false
    emit('changed')
  } catch (e) {
    // El 409 de existencia insuficiente dice cuánto queda: se muestra tal cual.
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

/**
 * Corrige una línea sin quitarla: cantidad y precio. Hace falta en tres casos —lo que el
 * técnico cargó sin poder valorarlo, lo que se trajo de una cotización que después cambió, y
 * la cantidad que se tecleó mal—.
 *
 * Si la línea ya salió de bodega, cambiar la cantidad **mueve la bodega**: la diferencia se
 * descuenta o se devuelve, con su movimiento. El histórico no se reescribe.
 */
function sePuedeCorregir(line: WorkOrderPart) {
  return props.canEdit && props.showCost && !!line
}

function abrirPrecio(line: WorkOrderPart) {
  enPrecio.value = line
  precioNuevo.value = line.unitPrice || ''
  cantidadNueva.value = line.quantity
  tambienElCatalogo.value = false
  error.value = ''
  aviso.value = ''
}

async function guardarPrecio() {
  const line = enPrecio.value
  const precio = Number(precioNuevo.value)
  const cantidad = Number(cantidadNueva.value)

  if (!line || precioNuevo.value === '' || !Number.isFinite(precio) || precio < 0) return
  if (cantidadNueva.value === '' || !Number.isFinite(cantidad) || cantidad <= 0) return

  busy.value = true
  error.value = ''
  try {
    await workOrdersApi.setPartPrice(props.workOrderId, line.id, {
      unitPrice: precio,
      quantity: cantidad,
      // Al catálogo solo va si lo pidieron y la línea de verdad salió de ahí.
      updateCatalog: tambienElCatalogo.value && !!line.partId,
    })

    aviso.value = tambienElCatalogo.value && line.partId
      ? `${line.partName} quedó en ${formatMoney(precio)}, también en el catálogo.`
      : ''

    enPrecio.value = null
    emit('changed')
  } catch (e) {
    // El 409 de existencia insuficiente dice cuánto queda: se muestra tal cual.
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

/** Los que están puestos en la orden pero todavía no salieron de bodega. */
const porConfirmar = computed(() => props.parts.filter((p) => p.porConfirmar))

/** Sin existencia no se puede confirmar: hay que comprarlo primero. */
function alcanza(line: WorkOrderPart): boolean {
  return line.partId === null || (line.available ?? 0) >= line.quantity
}

/** Confirmar es lo que descuenta la bodega. Por eso se pregunta, y se dice qué sale. */
async function confirmar(line: WorkOrderPart) {
  const pregunta = line.partId
    ? `¿Confirmar ${formatQuantity(line.quantity)} ${line.unit} de ${line.partName}? ` +
      'Sale de la bodega ahora.'
    : `¿Confirmar ${line.partName}?`
  if (!confirm(pregunta)) return

  await correr(() => workOrdersApi.confirmPart(props.workOrderId, line.id))
}

/**
 * Confirma de un golpe lo que se pueda. Va uno por uno a propósito: si a uno no le alcanza la
 * existencia, los demás entran igual y queda a la vista cuál falta comprar.
 */
async function confirmarTodos() {
  const listos = porConfirmar.value.filter(alcanza)
  if (listos.length === 0) return

  if (
    !confirm(
      `¿Confirmar ${listos.length} ${listos.length === 1 ? 'repuesto' : 'repuestos'}? ` +
        'Salen de la bodega ahora.'
    )
  )
    return

  busy.value = true
  error.value = ''
  try {
    for (const line of listos) {
      try {
        await workOrdersApi.confirmPart(props.workOrderId, line.id)
      } catch (e) {
        error.value = errorMessage(e)
      }
    }
    emit('changed')
  } finally {
    busy.value = false
  }
}

async function correr(accion: () => Promise<unknown>) {
  busy.value = true
  error.value = ''
  try {
    await accion()
    emit('changed')
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

async function remove(line: WorkOrderPart) {
  const pregunta = line.partId
    ? line.porConfirmar
      ? `¿Quitar ${line.partName}? No devuelve nada a bodega: todavía no había salido.`
      : `¿Quitar ${line.partName} y devolverlo a la bodega?`
    : `¿Quitar ${line.partName}?`
  if (!confirm(pregunta)) return

  busy.value = true
  error.value = ''
  try {
    await workOrdersApi.removePart(props.workOrderId, line.id)
    emit('changed')
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

let searchTimer: ReturnType<typeof setTimeout> | undefined
watch(search, () => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(loadCatalog, 250)
})

// El interruptor no espera: es un clic, no un tecleo.
watch(soloLoQueAplica, () => loadCatalog())
</script>

<template>
  <article class="card">
    <header>
      <h2>Repuestos</h2>
      <button v-if="canEdit && !adding" type="button" @click="open">Agregar</button>
    </header>

    <ErrorNote v-if="error" :message="error" />
    <p v-if="aviso" class="aviso small">{{ aviso }}</p>

    <!-- La cotización cambió después de traer sus repuestos: aquí no se enteró nadie. -->
    <div v-if="canEdit && desfasados.length" class="desfase small">
      <strong>Esto cambió en la cotización y aquí no:</strong>
      <ul>
        <li v-for="(d, i) in desfasados" :key="i">
          {{ d.concepto }} — {{ d.numero }} dice {{ d.cotizado }}, la orden tiene {{ d.enOrden }}
        </li>
      </ul>
      <span>Corríjalo también aquí: la factura sale con lo que diga la orden.</span>
    </div>

    <!-- Lo que ya se le cotizó al cliente no se vuelve a teclear. -->
    <p v-if="canEdit && quotesWithParts?.length" class="de-cotizacion small">
      <template v-for="(q, i) in quotesWithParts" :key="q.id">
        <span v-if="i"> · </span>
        <button type="button" class="link" :disabled="busy" @click="traerDeLaCotizacion(q.id)">
          Traer los {{ q.parts.length }} de {{ q.number }}
        </button>
      </template>
    </p>

    <p v-if="!parts.length && !adding" class="muted">
      Todavía no se ha cargado ningún repuesto a esta orden.
    </p>

    <!-- En el teléfono la fila no cabe: la tabla se recorre dentro de su marco en vez de
         empujar la pantalla de lado. -->
    <div v-if="parts.length" class="tabla">
      <table>
      <tbody>
        <tr v-for="line in parts" :key="line.id">
          <td>
            <strong>{{ line.partName }}</strong>
            <div class="muted small">
              <template v-if="line.boughtOutside">
                Se compra en {{ line.supplierName || 'casa de repuestos' }}
              </template>
              <template v-else>{{ line.sku || 'Cargado a mano' }}</template>
              <template v-if="line.taskTitle"> · {{ line.taskTitle }}</template>
              <template v-if="line.porConfirmar">
                ·
                <em class="por-confirmar">
                  por confirmar<template v-if="line.partId">
                    · quedan {{ formatQuantity(line.available ?? 0) }} en bodega</template>
                </em>
              </template>
              <template v-if="showCost && line.unitCost">
                · costo {{ formatMoney(line.unitCost) }}
              </template>
            </div>
          </td>
          <td class="num muted">
            {{ formatQuantity(line.quantity) }} {{ line.unit }} × {{ formatMoney(line.unitPrice) }}
          </td>
          <td class="num">{{ formatMoney(line.total) }}</td>
          <td v-if="canEdit" class="num">
            <!-- Aquí y en ningún otro lado es donde el repuesto sale de bodega. -->
            <button
              v-if="line.porConfirmar && alcanza(line)"
              type="button"
              class="link"
              :disabled="busy"
              @click="confirmar(line)"
            >
              Confirmar
            </button>
            <em v-else-if="line.porConfirmar" class="muted small">hay que comprarlo</em>
            <button
              v-if="sePuedeCorregir(line)"
              type="button"
              class="link"
              :disabled="busy"
              @click="abrirPrecio(line)"
            >
              {{ line.unitPrice === 0 ? 'Poner precio' : 'Editar' }}
            </button>
            <button type="button" class="link danger" :disabled="busy" @click="remove(line)">
              Quitar
            </button>
          </td>
        </tr>
      </tbody>
      <tfoot>
        <!-- Lo que está por confirmar cuenta en la cotización pero no ha salido de bodega, y
             la orden no se entrega así. Decirlo aquí evita descubrirlo al intentar entregar. -->
        <tr v-if="porConfirmar.length">
          <td colspan="4" class="falta small">
            {{ porConfirmar.length }}
            {{ porConfirmar.length === 1 ? 'repuesto está' : 'repuestos están' }}
            por confirmar: están en la orden y en la cotización, pero todavía no salieron de la
            bodega. La orden no se entrega hasta confirmarlos o quitarlos.
            <button
              v-if="canEdit && porConfirmar.some(alcanza)"
              type="button"
              class="link"
              :disabled="busy"
              @click="confirmarTodos"
            >
              Confirmar los que hay
            </button>
          </td>
        </tr>
        <tr v-if="showCost && parts.some((p) => p.unitPrice === 0)">
          <td colspan="4" class="falta small">
            Hay repuestos sin precio: el técnico los cargó pero no los valoró. Póngaselo antes
            de facturar.
          </td>
        </tr>
        <tr>
          <td colspan="2">Total en repuestos</td>
          <td class="num"><strong>{{ formatMoney(total) }}</strong></td>
          <td v-if="canEdit"></td>
        </tr>
      </tfoot>
      </table>
    </div>

    <!-- Corregir el precio de una línea que todavía no salió de bodega. -->
    <form v-if="enPrecio" class="precio" @submit.prevent="guardarPrecio">
      <p class="small">
        <strong>{{ enPrecio.partName }}</strong> ·
        {{ formatQuantity(enPrecio.quantity) }} {{ enPrecio.unit }}
      </p>

      <div class="campos">
        <label>
          Cantidad ({{ enPrecio.unit }})
          <input v-model="cantidadNueva" type="number" min="0.01" step="0.01" required />
        </label>
        <label>
          ¿Cuánto se le cobra por cada uno?
          <input v-model="precioNuevo" type="number" min="0" step="0.01" required />
        </label>
      </div>

      <!-- Esa línea ya salió de bodega: la diferencia se mueve, y hay que decirlo antes. -->
      <p v-if="mueveBodega" class="mueve small">
        Este repuesto ya salió de bodega. Al guardar, la diferencia se descuenta o se devuelve,
        con su movimiento en el kardex.
      </p>

      <!-- Solo lo del catálogo puede volver a él, y solo el Dueño toca el catálogo. -->
      <label v-if="enPrecio.partId && canTouchCatalog" class="check">
        <input v-model="tambienElCatalogo" type="checkbox" />
        Cambiarlo también en el catálogo, para las próximas órdenes
      </label>
      <p v-else-if="enPrecio.partId" class="muted small">
        En el catálogo lo cambia el Dueño, en Inventario.
      </p>
      <p v-else class="muted small">
        Este repuesto se cargó a mano: no está en el catálogo, así que solo cambia aquí.
      </p>

      <div class="acciones">
        <button type="submit" :disabled="busy || precioNuevo === ''">Guardar</button>
        <button type="button" class="link" :disabled="busy" @click="enPrecio = null">
          Cancelar
        </button>
      </div>
    </form>

    <form v-if="adding" class="add" @submit.prevent="add">
      <div class="origen">
        <label class="radio">
          <input v-model="origen" type="radio" value="catalogo" />
          Del inventario
        </label>
        <label class="radio">
          <input v-model="origen" type="radio" value="manual" />
          A mano
        </label>
      </div>

      <template v-if="origen === 'catalogo'">
        <input v-model="search" placeholder="Buscar por SKU, nombre o marca…" />

        <!-- Con el carro en pantalla, dejar de adivinar qué filtro le va. Es un interruptor
             y no algo automático porque el dato está incompleto: apagado se ve todo. -->
        <label v-if="vehicleId" class="checkbox solo-aplica">
          <input v-model="soloLoQueAplica" type="checkbox" />
          Solo lo que le sirve a este vehículo
        </label>

        <select v-model="selectedPartId" size="6">
          <option v-for="p in catalog" :key="p.id" :value="p.id">
            {{ p.sku }} · {{ p.name }} — {{ formatQuantity(p.totalQuantity) }} {{ p.unit }} ·
            {{ formatMoney(p.salePrice) }}
          </option>
        </select>

        <p v-if="soloLoQueAplica && !catalog.length" class="muted small">
          Ningún repuesto del catálogo dice servirle a este vehículo. Apague el filtro para
          ver todo, o complete «A qué vehículos les sirve» en Inventario.
        </p>
      </template>

      <template v-else>
        <input v-model="concepto" placeholder="Qué repuesto es" maxlength="200" required />

        <div class="row">
          <label>
            Precio
            <input v-model.number="precio" type="number" step="0.01" min="0" required />
          </label>
          <label v-if="showCost">
            Costo
            <input v-model.number="costo" type="number" step="0.01" min="0" placeholder="0.00" />
          </label>
        </div>

        <label class="check">
          <input v-model="afuera" type="checkbox" />
          Comprado en casa de repuestos
        </label>

        <input
          v-if="afuera"
          v-model="casa"
          placeholder="¿En cuál casa de repuestos? (opcional)"
          maxlength="120"
        />
      </template>

      <div class="row">
        <label>
          Cantidad
          <input v-model.number="quantity" type="number" step="0.001" min="0.001" required />
        </label>
        <span v-if="preview" class="muted">= {{ formatMoney(preview) }}</span>
      </div>

      <div class="row">
        <button type="submit" :disabled="busy || !listo">
          {{ origen === 'manual' ? 'Agregar' : 'Agregar y descontar' }}
        </button>
        <button type="button" @click="adding = false">Cancelar</button>
      </div>

      <p class="muted small">
        <template v-if="origen === 'manual'">
          No descuenta existencias: es para lo que se compró de encargo y nunca pasó por la
          bodega. Si no pone el costo queda en cero y el margen de esta orden sale inflado.
        </template>
        <template v-else>
          Sale de la bodega de la sucursal de esta orden y queda registrado en el kardex.
        </template>
      </p>
    </form>
  </article>
</template>

<style scoped>
.aviso {
  margin: 0 0 0.5rem;
  color: var(--success-text);
}

/* Los enlaces para traer lo ya cotizado. Van arriba de la tabla, antes de teclear nada. */
.de-cotizacion {
  margin: 0 0 0.75rem;
  color: var(--text-muted);
}

.precio {
  display: grid;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface-alt);
}

.precio p {
  margin: 0;
}

.precio .campos {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}

.precio .mueve {
  margin: 0;
  color: var(--warning-text);
}

.precio .check {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.precio .acciones {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.desfase {
  margin: 0 0 0.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid color-mix(in srgb, var(--warning) 45%, var(--border));
  border-radius: 8px;
  background: color-mix(in srgb, var(--warning) 10%, transparent);
  color: var(--warning-text);
}

.desfase ul {
  margin: 0.25rem 0;
  padding-left: 1.1rem;
}

.por-confirmar {
  color: var(--warning, #b45309);
  font-style: normal;
}

.card {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 0.75rem;
}

h2 {
  margin: 0;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.tabla {
  overflow-x: auto;
}

table {
  width: 100%;
  min-width: 20rem;
  border-collapse: collapse;
  font-size: 0.9375rem;
}

td {
  padding: 0.375rem 0.25rem;
  border-bottom: 1px solid var(--border);
  vertical-align: top;
}

tfoot td {
  border-bottom: none;
  padding-top: 0.625rem;
  color: var(--text-muted);
}

.num {
  text-align: right;
  white-space: nowrap;
}

.add {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

.origen {
  display: flex;
  gap: 1rem;
}

.radio,
.check {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.add select {
  width: 100%;
  font-family: ui-monospace, monospace;
  font-size: 0.8125rem;
}

.row {
  display: flex;
  align-items: flex-end;
  gap: 0.75rem;
}

.row label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.row input {
  width: 7rem;
}

</style>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, partsApi, stockApi } from '@/api/garaj'
import EmptyState from '@/components/EmptyState.vue'
import CatalogImport from '@/components/CatalogImport.vue'
import ErrorNote from '@/components/ErrorNote.vue'
import FilterBar from '@/components/FilterBar.vue'
import RowMenu from '@/components/RowMenu.vue'
import FilterChip from '@/components/FilterChip.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import { useEscape } from '@/composables/useEscape'
import { useAuthStore } from '@/stores/auth'
import {
  STOCK_MOVEMENT_LABEL,
  type Branch,
  type Part,
  type StockItem,
  type StockMovement,
} from '@/types/domain'
import { formatDateTime, formatMoney, formatQuantity } from '@/utils/format'

const auth = useAuthStore()

const branches = ref<Branch[]>([])
const categories = ref<string[]>([])
const items = ref<StockItem[]>([])
const alerts = ref<StockItem[]>([])
const parts = ref<Part[]>([])

/** La sucursal sale del menú, no de esta pantalla: es la misma para todo el panel. */
const branchId = computed(() => auth.activeBranchId ?? '')
const search = ref('')
const category = ref('')
const onlyBelowMinimum = ref(false)

const error = ref('')
const busy = ref(false)
const loading = ref(false)

/** Panel lateral: alta de repuesto, entrada, ajuste, traslado o kardex. */
const panel = ref<'part' | 'receive' | 'adjust' | 'transfer' | 'kardex' | null>(null)

useEscape(() => (panel.value = null))
const selected = ref<StockItem | null>(null)
const movements = ref<StockMovement[]>([])

const partForm = ref({
  branchId: '',
  quantity: 0,
  minQuantity: 0,
  location: '',
  id: '',
  sku: '',
  name: '',
  description: '',
  brand: '',
  category: '',
  unit: 'u',
  costPrice: 0,
  salePrice: 0,
  isActive: true,
})

const moveForm = ref({
  partId: '',
  quantity: 1,
  unitCost: 0,
  reference: '',
  countedQuantity: 0,
  reason: '',
  toBranchId: '',
  notes: '',
})

const canManage = computed(() => auth.isOwner)

/**
 * Las cifras de arriba, calculadas sobre lo que se está mirando: no hay endpoint de totales
 * de bodega, y pedir uno para pintar cuatro números sería un viaje más por pantalla.
 */
const cifras = computed(() => {
  const lista = items.value

  return {
    valor: lista.reduce((suma, i) => suma + i.quantity * i.costPrice, 0),
    precioVenta: lista.reduce((suma, i) => suma + i.quantity * i.salePrice, 0),
    renglones: lista.length,
    bajos: lista.filter((i) => i.isBelowMinimum).length,
    agotados: lista.filter((i) => i.quantity <= 0).length,
  }
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    const page = await stockApi.list({
      branchId: branchId.value || undefined,
      search: search.value || undefined,
      category: category.value || undefined,
      onlyBelowMinimum: onlyBelowMinimum.value || undefined,
      pageSize: 200,
    })
    items.value = page.items
    alerts.value = await stockApi.alerts(branchId.value || undefined)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el inventario.')
  } finally {
    loading.value = false
  }
}

async function run(action: () => Promise<unknown>) {
  busy.value = true
  error.value = ''
  try {
    await action()
    panel.value = null
    await load()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

function openNewPart() {
  partForm.value = {
    id: '', sku: '', name: '', description: '', brand: '', category: '',
    unit: 'u', costPrice: 0, salePrice: 0, isActive: true,
    // La bodega que se está mirando, que es a la que va a entrar lo que se dé de alta.
    branchId: branchId.value || branches.value[0]?.id || '',
    quantity: 0, minQuantity: 0, location: '',
  }
  panel.value = 'part'
}

async function openEditPart(item: StockItem) {
  const page = await partsApi.list({ search: item.sku, includeInactive: true, pageSize: 5 })
  const part = page.items.find((p) => p.id === item.partId)
  if (!part) return

  partForm.value = {
    ...partForm.value,
    id: part.id,
    sku: part.sku,
    name: part.name,
    description: part.description ?? '',
    brand: part.brand ?? '',
    category: part.category ?? '',
    unit: part.unit,
    costPrice: part.costPrice,
    salePrice: part.salePrice,
    isActive: part.isActive,
  }
  panel.value = 'part'
}

function savePart() {
  const form = partForm.value
  const body = {
    sku: form.sku.trim(),
    name: form.name.trim(),
    description: form.description || null,
    brand: form.brand || null,
    category: form.category || null,
    unit: form.unit || 'u',
    costPrice: Number(form.costPrice),
    salePrice: Number(form.salePrice),
    isActive: form.isActive,
  }

  return run(async () => {
    if (form.id) {
      await partsApi.update(form.id, body)
    } else {
      const creado = await partsApi.create(body)
      const bodega = form.branchId || branchId.value || branches.value[0]?.id

      // Lo de la bodega va después y solo si se llenó: el catálogo es del taller y la
      // existencia es de una sucursal, así que son dos cosas distintas que se guardan juntas
      // porque el que da de alta un repuesto las tiene las dos en la mano.
      if (bodega && (form.minQuantity > 0 || form.location.trim())) {
        await stockApi.saveSettings({
          branchId: bodega,
          partId: creado.id,
          minQuantity: Number(form.minQuantity) || 0,
          location: form.location.trim() || undefined,
        })
      }

      if (bodega && form.quantity > 0) {
        await stockApi.receive({
          branchId: bodega,
          partId: creado.id,
          quantity: Number(form.quantity),
          unitCost: Number(form.costPrice) || undefined,
          reference: 'Alta del repuesto',
        })
      }
    }

    categories.value = await partsApi.categories()
  })
}

/** Para entrada y traslado hace falta elegir repuesto: se carga el catálogo al abrir. */
async function openMovement(kind: 'receive' | 'adjust' | 'transfer', item?: StockItem) {
  selected.value = item ?? null
  moveForm.value = {
    partId: item?.partId ?? '',
    quantity: 1,
    unitCost: 0,
    reference: '',
    countedQuantity: item?.quantity ?? 0,
    reason: '',
    toBranchId: branches.value.find((b) => b.id !== targetBranchId(item))?.id ?? '',
    notes: '',
  }

  if (!parts.value.length) {
    parts.value = (await partsApi.list({ pageSize: 200 })).items
  }

  panel.value = kind
}

/** La sucursal sobre la que se opera: la de la fila, o la del filtro, o la primera. */
function targetBranchId(item?: StockItem | null): string {
  return item?.branchId || branchId.value || branches.value[0]?.id || ''
}

function receive() {
  const form = moveForm.value
  return run(() =>
    stockApi.receive({
      branchId: targetBranchId(selected.value),
      partId: form.partId,
      quantity: Number(form.quantity),
      unitCost: Number(form.unitCost) || undefined,
      reference: form.reference || undefined,
    }),
  )
}

function adjust() {
  const form = moveForm.value
  return run(() =>
    stockApi.adjust({
      branchId: targetBranchId(selected.value),
      partId: form.partId,
      countedQuantity: Number(form.countedQuantity),
      reason: form.reason,
    }),
  )
}

function transfer() {
  const form = moveForm.value
  return run(() =>
    stockApi.transfer({
      fromBranchId: targetBranchId(selected.value),
      toBranchId: form.toBranchId,
      partId: form.partId,
      quantity: Number(form.quantity),
      notes: form.notes || undefined,
    }),
  )
}

async function openKardex(item: StockItem) {
  selected.value = item
  panel.value = 'kardex'
  movements.value = []

  try {
    const page = await stockApi.movements({
      branchId: item.branchId,
      partId: item.partId,
      pageSize: 50,
    })
    movements.value = page.items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el kardex.')
  }
}

watch([branchId, category, onlyBelowMinimum], load)

/** Medio segundo de espera para no mandar una consulta por cada letra tecleada. */
let tecleando: ReturnType<typeof setTimeout> | undefined

watch(search, () => {
  clearTimeout(tecleando)
  tecleando = setTimeout(load, 500)
})

onMounted(async () => {
  branches.value = await branchesApi.list().catch(() => [])
  categories.value = await partsApi.categories().catch(() => [])
  await load()
})
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Inventario</h1>
        <p class="muted small">
          <template v-if="auth.user && auth.user.branches.length > 1">
            <strong>{{ auth.activeBranchName }}</strong> ·
          </template>
          El stock no se edita: se mueve. Cada cambio queda en el kardex con su responsable.
        </p>
      </div>
      <div class="acciones">
        <CatalogImport v-if="canManage" kind="parts" @imported="load" />
        <button v-if="canManage" type="button" @click="openNewPart">Nuevo repuesto</button>
      </div>
    </header>

    <ErrorNote v-if="error" :message="error" />

    <div class="cifras">
      <article v-if="canManage" class="cifra">
        <span class="rotulo">Valor de lo que hay</span>
        <strong class="num">{{ formatMoney(cifras.valor) }}</strong>
        <span class="muted small">{{ formatMoney(cifras.precioVenta) }} a precio de venta</span>
      </article>
      <article class="cifra">
        <span class="rotulo">Repuestos</span>
        <strong class="num">{{ cifras.renglones }}</strong>
        <span class="muted small">con existencia en esta vista</span>
      </article>
      <article class="cifra" :class="{ espera: cifras.bajos > 0 }">
        <span class="rotulo">Bajo mínimo</span>
        <strong class="num">{{ cifras.bajos }}</strong>
        <span class="muted small">hay que reponerlos</span>
      </article>
      <article class="cifra" :class="{ alerta: cifras.agotados > 0 }">
        <span class="rotulo">Agotados</span>
        <strong class="num">{{ cifras.agotados }}</strong>
        <span class="muted small">sin una sola unidad</span>
      </article>
    </div>

    <!-- Lo que hay que comprar, con el botón de la entrada al lado: es la lista con la que
         se va a la casa de repuestos. -->
    <article v-if="alerts.length" class="reponer">
      <header>
        <h2>Por reponer</h2>
        <span class="muted small">en o por debajo del mínimo</span>
      </header>
      <ul>
        <li v-for="a in alerts.slice(0, 8)" :key="a.partId + a.branchId">
          <span class="reponer-nombre">
            <strong>{{ a.partName }}</strong>
            <span class="muted small">{{ a.sku }} · {{ a.branchName }}</span>
          </span>
          <span class="reponer-cuanto">
            <strong class="num" :class="{ cero: a.quantity <= 0 }">
              {{ formatQuantity(a.quantity) }}
            </strong>
            <span class="muted small">mín. {{ formatQuantity(a.minQuantity) }}</span>
          </span>
          <button
            v-if="canManage"
            type="button"
            class="btn-ghost btn-sm"
            @click="openMovement('receive', a)"
          >
            Entrada
          </button>
        </li>
      </ul>
      <p v-if="alerts.length > 8" class="muted small mas">
        y {{ alerts.length - 8 }} más. Filtre por «solo bajo mínimo» para verlos todos.
      </p>
    </article>

    <FilterBar v-model:search="search" placeholder="SKU, nombre o marca…">
      <select v-model="category">
        <option value="">Todas las categorías</option>
        <option v-for="c in categories" :key="c" :value="c">{{ c }}</option>
      </select>
      <FilterChip v-model="onlyBelowMinimum" :cuantos="alerts.length">Por reponer</FilterChip>

      <!-- Apagados y a la derecha: la acción principal de la pantalla es «Nuevo repuesto»,
           arriba. Si los cuatro botones van llenos, ninguno es el principal. -->
      <template v-if="canManage" #vista>
        <button type="button" class="btn-ghost" @click="openMovement('receive')">Entrada</button>
        <button type="button" class="btn-ghost" @click="openMovement('transfer')">
          Traslado
        </button>
      </template>
    </FilterBar>

    <SkeletonList v-if="loading" :rows="4" variant="tabla" />
    <EmptyState v-else-if="!items.length" title="No hay existencias que coincidan" />

    <!-- La tabla en su propio marco con scroll: con las acciones a la derecha, en pantalla
         angosta es más honesto deslizarla que apilar cuatro botones por fila. -->
    <div v-else class="marco-tabla">
    <table>
      <thead>
        <tr>
          <th scope="col">Repuesto</th>
          <th scope="col">Sucursal</th>
          <th scope="col" class="num">Existencia</th>
          <th scope="col" class="num">Mínimo</th>
          <th scope="col" v-if="canManage" class="num">Costo</th>
          <th scope="col" class="num">Venta</th>
          <th scope="col">Ubicación</th>
          <th scope="col"><span class="sr-only">Acciones</span></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="item in items" :key="item.partId + item.branchId">
          <td>
            <strong>{{ item.partName }}</strong>
            <div class="muted small">{{ item.sku }} · {{ item.brand ?? '—' }}</div>
          </td>
          <td>{{ item.branchName }}</td>
          <td class="num">
            <span class="existencia" :class="{ low: item.isBelowMinimum, cero: item.quantity <= 0 }">
              {{ formatQuantity(item.quantity) }} {{ item.unit }}
            </span>
          </td>
          <td class="num muted">{{ formatQuantity(item.minQuantity) }}</td>
          <!-- El costo es del negocio: se le enseña al Dueño, no al técnico que consulta
               existencias antes de prometer una reparación. -->
          <td v-if="canManage" class="num muted">{{ formatMoney(item.costPrice) }}</td>
          <td class="num">{{ formatMoney(item.salePrice) }}</td>
          <td class="muted small">{{ item.location ?? '—' }}</td>
          <!-- A la vista, lo que se hace todos los días: darle entrada a lo que llegó. El
               resto —ajuste, traslado, kardex, editar— en el menú, que cuatro botones por
               fila en veinte filas dejan de decir qué es lo importante. -->
          <td class="col-acciones">
            <div class="acciones">
              <button
                v-if="canManage"
                type="button"
                class="btn-ghost btn-sm"
                @click="openMovement('receive', item)"
              >
                + Entrada
              </button>
              <RowMenu>
                <button v-if="canManage" type="button" @click="openMovement('adjust', item)">
                  Ajuste por conteo
                </button>
                <button v-if="canManage" type="button" @click="openMovement('transfer', item)">
                  Trasladar a otra sucursal
                </button>
                <button type="button" @click="openKardex(item)">Ver el kardex</button>
                <button v-if="canManage" type="button" @click="openEditPart(item)">
                  Editar el repuesto
                </button>
              </RowMenu>
            </div>
          </td>
        </tr>
      </tbody>
    </table>
    </div>

    <!-- Panel lateral -->
    <div v-if="panel" class="drawer-backdrop" @click.self="panel = null">
      <aside class="drawer">
        <header>
          <h2>
            {{
              panel === 'part' ? (partForm.id ? 'Editar repuesto' : 'Nuevo repuesto')
              : panel === 'receive' ? 'Entrada de repuestos'
              : panel === 'adjust' ? 'Ajuste por conteo'
              : panel === 'transfer' ? 'Traslado entre sucursales'
              : 'Kardex'
            }}
          </h2>
          <button type="button" class="btn-ghost" @click="panel = null">Cerrar</button>
        </header>

        <form v-if="panel === 'part'" @submit.prevent="savePart">
          <label>SKU<input v-model="partForm.sku" required /></label>
          <label>Nombre<input v-model="partForm.name" required /></label>
          <label>Marca<input v-model="partForm.brand" /></label>
          <label>Categoría<input v-model="partForm.category" list="categorias" /></label>
          <datalist id="categorias">
            <option v-for="c in categories" :key="c" :value="c" />
          </datalist>
          <label>Unidad<input v-model="partForm.unit" placeholder="u, lt, jgo" /></label>
          <label>Costo<input v-model.number="partForm.costPrice" type="number" step="0.01" min="0" /></label>
          <label>Precio de venta<input v-model.number="partForm.salePrice" type="number" step="0.01" min="0" /></label>
          <label class="checkbox">
            <input v-model="partForm.isActive" type="checkbox" /> Activo
          </label>

          <!-- Un repuesto nuevo sin existencia, sin mínimo y sin ubicación es media ficha:
               hay que volver a entrar a darle entrada y a decir dónde está. Al editar no
               aparece, que eso se cambia desde su fila. -->
          <template v-if="!partForm.id">
            <fieldset class="bodega">
              <legend>Lo que entra a la bodega</legend>
              <label v-if="branches.length > 1">
                Sucursal
                <select v-model="partForm.branchId">
                  <option v-for="b in branches" :key="b.id" :value="b.id">{{ b.name }}</option>
                </select>
              </label>
              <label>
                Existencia inicial
                <input v-model.number="partForm.quantity" type="number" step="0.001" min="0" />
              </label>
              <label>
                Mínimo
                <input v-model.number="partForm.minQuantity" type="number" step="0.001" min="0" />
              </label>
              <label>
                Ubicación
                <input v-model="partForm.location" placeholder="Estante A1" />
              </label>
              <p class="muted small">
                La existencia entra como un movimiento del kardex, con su fecha y a su nombre.
                Déjela en cero si todavía va a contar.
              </p>
            </fieldset>
          </template>

          <button type="submit" :disabled="busy">Guardar</button>
        </form>

        <form v-else-if="panel === 'receive'" @submit.prevent="receive">
          <p class="muted small">Entra a {{ branches.find((b) => b.id === targetBranchId(selected))?.name }}.</p>
          <label>
            Repuesto
            <select v-model="moveForm.partId" required :disabled="!!selected">
              <option value="" disabled>Elija…</option>
              <option v-for="p in parts" :key="p.id" :value="p.id">{{ p.sku }} · {{ p.name }}</option>
            </select>
          </label>
          <label>Cantidad<input v-model.number="moveForm.quantity" type="number" step="0.001" min="0.001" required /></label>
          <label>Costo unitario<input v-model.number="moveForm.unitCost" type="number" step="0.01" min="0" /></label>
          <label>Referencia<input v-model="moveForm.reference" placeholder="Nº de factura" /></label>
          <button type="submit" :disabled="busy">Registrar entrada</button>
        </form>

        <form v-else-if="panel === 'adjust'" @submit.prevent="adjust">
          <p class="muted small">
            {{ selected?.partName }} en {{ selected?.branchName }}. El sistema tiene
            {{ formatQuantity(selected?.quantity) }} {{ selected?.unit }}.
          </p>
          <label>
            Cantidad contada
            <input v-model.number="moveForm.countedQuantity" type="number" step="0.001" min="0" required />
          </label>
          <label>
            Motivo
            <input v-model="moveForm.reason" required placeholder="Conteo físico, rotura, error de captura…" />
          </label>
          <button type="submit" :disabled="busy">Ajustar</button>
        </form>

        <form v-else-if="panel === 'transfer'" @submit.prevent="transfer">
          <p class="muted small">Sale de {{ branches.find((b) => b.id === targetBranchId(selected))?.name }}.</p>
          <label>
            Repuesto
            <select v-model="moveForm.partId" required :disabled="!!selected">
              <option value="" disabled>Elija…</option>
              <option v-for="p in parts" :key="p.id" :value="p.id">{{ p.sku }} · {{ p.name }}</option>
            </select>
          </label>
          <label>
            Destino
            <select v-model="moveForm.toBranchId" required>
              <option
                v-for="b in branches.filter((b) => b.id !== targetBranchId(selected))"
                :key="b.id"
                :value="b.id"
              >
                {{ b.name }}
              </option>
            </select>
          </label>
          <label>Cantidad<input v-model.number="moveForm.quantity" type="number" step="0.001" min="0.001" required /></label>
          <label>Nota<input v-model="moveForm.notes" /></label>
          <button type="submit" :disabled="busy">Trasladar</button>
        </form>

        <div v-else-if="panel === 'kardex'">
          <p class="muted small">{{ selected?.partName }} en {{ selected?.branchName }}</p>
          <p v-if="!movements.length" class="muted">Sin movimientos.</p>
          <ol v-else class="kardex">
            <li v-for="m in movements" :key="m.id">
              <div class="line">
                <strong>{{ STOCK_MOVEMENT_LABEL[m.type] }}</strong>
                <span :class="m.signedQuantity < 0 ? 'out' : 'in'">
                  {{ m.signedQuantity > 0 ? '+' : '' }}{{ formatQuantity(m.signedQuantity) }}
                </span>
                <span class="muted">saldo {{ formatQuantity(m.resultingQuantity) }}</span>
              </div>
              <div class="muted small">
                {{ formatDateTime(m.movedAt) }} · {{ m.movedByName }}
                <template v-if="m.workOrderNumber"> · orden {{ m.workOrderNumber }}</template>
                <template v-if="m.counterpartBranchName"> · {{ m.counterpartBranchName }}</template>
                <template v-if="m.reference"> · {{ m.reference }}</template>
              </div>
              <p v-if="m.notes" class="small">{{ m.notes }}</p>
            </li>
          </ol>
        </div>
      </aside>
    </div>
  </section>
</template>

<style scoped>
.bodega {
  display: grid;
  gap: 0.75rem;
  margin: 0;
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
}

.bodega legend {
  padding: 0 0.3rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

h1 {
  margin: 0;
  font-size: 1.625rem;
}

.top p {
  margin: 0.25rem 0 0;
}

/* ------------------------------------------------------------------ cifras */

.cifras {
  display: grid;
  gap: 0.75rem;
  grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
  margin-bottom: 1rem;
}

.cifra {
  display: grid;
  justify-items: start;
  gap: 0.125rem;
  padding: 0.875rem 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.cifra .rotulo {
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.cifra strong {
  font-size: 1.375rem;
  font-family: var(--font-mono);
}

.cifra.espera strong {
  color: var(--warning-text);
}

.cifra.alerta strong {
  color: var(--danger);
}

/* ------------------------------------------------------------------ por reponer */

.reponer {
  margin-bottom: 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  overflow: hidden;
}

.reponer header {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
  padding: 0.875rem;
  border-bottom: 1px solid var(--border);
  background: var(--surface-alt);
}

.reponer h2 {
  margin: 0;
  font-size: 1rem;
}

.reponer ul {
  list-style: none;
  margin: 0;
  padding: 0;
}

.reponer li {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 0.625rem 0.875rem;
  border-top: 1px solid var(--border);
}

.reponer li:first-child {
  border-top: 0;
}

.reponer-nombre {
  display: grid;
  flex: 1;
  min-width: 0;
}

.reponer-cuanto {
  display: grid;
  justify-items: end;
  text-align: right;
}

.reponer-cuanto strong {
  font-size: 1rem;
  color: var(--warning-text);
}

.reponer-cuanto strong.cero {
  color: var(--danger);
}

.mas {
  margin: 0;
  padding: 0.625rem 0.875rem;
  border-top: 1px solid var(--border);
}

/* ------------------------------------------------------------------ filtros */

.filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.buscador {
  position: relative;
  display: block;
  width: min(20rem, 100%);
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

/* La existencia, en pastilla: el número suelto en una columna de veinte no se distingue. */
.existencia {
  display: inline-block;
  padding: 0.1rem 0.45rem;
  border-radius: 999px;
  background: var(--surface-alt);
}

.existencia.low {
  background: color-mix(in srgb, var(--warning) 20%, transparent);
  color: var(--warning-text);
  font-weight: 600;
}

.existencia.cero {
  background: color-mix(in srgb, var(--danger) 14%, transparent);
  color: var(--danger);
}

.filters input[type='text'],
.filters input:not([type]) {
  min-width: 14rem;
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
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.num {
  text-align: right;
  white-space: nowrap;
}

.low {
  color: var(--danger);
  font-weight: 600;
}

.col-acciones {
  width: 1%;
}

.top .acciones {
  display: flex;
  gap: var(--space-2);
  align-items: center;
}

.col-acciones .acciones {
  justify-content: flex-end;
  /* Sin envolver: la columna es estrecha y envolviendo salían cuatro renglones de botones
     que estiraban la fila a cien píxeles de alto. */
  flex-wrap: nowrap;
}

.marco-tabla {
  overflow-x: auto;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.marco-tabla th {
  background: var(--surface-alt);
  border-bottom: 0;
}

.marco-tabla td {
  border-bottom: 0;
  border-top: 1px solid var(--border);
  vertical-align: middle;
}

.marco-tabla tbody tr:hover td {
  background: var(--surface-alt);
}

.drawer-backdrop {
  position: fixed;
  inset: 0;
  z-index: 40;
  display: flex;
  justify-content: flex-end;
  background: rgb(0 0 0 / 0.4);
}

.drawer {
  width: min(26rem, 100%);
  height: 100%;
  overflow-y: auto;
  padding: 1rem 1.25rem 2rem;
  background: var(--surface);
  border-left: 1px solid var(--border);
}

.drawer header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.drawer h2 {
  margin: 0;
  font-size: 1rem;
}

.drawer form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.drawer label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.drawer label input,
.drawer label select {
  width: 100%;
}

.checkbox {
  flex-direction: row !important;
  align-items: center;
  gap: 0.5rem;
}

.kardex {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.875rem;
}

.kardex .line {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
}

.kardex p {
  margin: 0.125rem 0 0;
}

.in {
  color: var(--success-text);
  font-variant-numeric: tabular-nums;
}

.out {
  color: var(--danger);
  font-variant-numeric: tabular-nums;
}

</style>

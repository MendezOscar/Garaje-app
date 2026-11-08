<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { assemblyApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import { useAuthStore } from '@/stores/auth'
import type {
  AssemblyContractor,
  AssemblyModel,
  AssemblyWarehouse,
} from '@/types/domain'
import { formatMoney } from '@/utils/format'

/**
 * El catálogo del contrato de armado: con quién se trabaja, qué modelos se arman, en qué
 * bodegas, y los dos precios de cada modelo.
 *
 * Los dos números viven juntos a propósito. Lo que el taller gana por armar una moto es la
 * diferencia entre ellos, y con las columnas separadas en dos pantallas nadie la ve. Aquí
 * sale calculada al lado, que es la única cifra que de verdad contesta «¿me conviene este
 * modelo?».
 *
 * Nada se borra: un contratista o un modelo con motos armadas colgando es historia ya
 * facturada. Se desactivan.
 */
const auth = useAuthStore()

/**
 * Las sucursales del taller. Solo salen a la vista con más de una: con una sola, el contrato
 * queda apuntado a ella sin preguntar nada.
 */
const sucursales = computed(() => auth.user?.branches ?? [])

const contratistas = ref<AssemblyContractor[]>([])
const modelos = ref<AssemblyModel[]>([])
const bodegas = ref<AssemblyWarehouse[]>([])

const cargando = ref(true)
const busy = ref(false)
const error = ref('')

const pestana = ref<'contratistas' | 'modelos' | 'bodegas'>('contratistas')

/** El contratista abierto, con su tabla de precios en edición. */
const abierto = ref<string | null>(null)
const tarifas = ref<Record<string, { charge: string; pay: string }>>({})

const nuevoContratista = ref('')
const nuevoModelo = ref('')

/**
 * Los primeros caracteres del VIN de este modelo.
 *
 * Lo que significan esas letras es la tabla interna del fabricante —no hay decodificador
 * público que sirva—, así que lo escribe el dueño una vez con una caja delante. Con él, la
 * app propone el modelo desde la primera moto; sin él, lo aprende sola a partir de la
 * segunda de cada lote.
 */
const nuevoPrefijo = ref('')
const nuevaBodega = ref('')

/**
 * El gasto del taller por cada moto armada: lo que el armado se lleva aparte del pago al
 * técnico —transporte a la bodega, herramienta—. Uno solo para todas, porque hoy lo es.
 *
 * Se copia a cada moto al registrarla, así que cambiarlo aquí no toca lo ya armado: un mes
 * cerrado no puede cambiar de margen porque hoy suba el transporte.
 */
const gasto = ref('')
const gastoGuardado = ref(0)

const gastoCambio = computed(
  () => (Number(gasto.value) || 0) !== gastoGuardado.value && gasto.value.trim() !== '',
)

async function guardarGasto() {
  await correr(async () => {
    const r = await assemblyApi.saveSettings(Number(gasto.value) || 0)
    gastoGuardado.value = r.expensePerMotorcycle
    gasto.value = String(r.expensePerMotorcycle)
  })
}

const modelosActivos = computed(() => modelos.value.filter((m) => m.isActive))

onMounted(cargar)

async function cargar() {
  cargando.value = true
  error.value = ''

  try {
    const [c, m, b, ajustes] = await Promise.all([
      assemblyApi.contractors(),
      assemblyApi.models(),
      assemblyApi.warehouses(),
      assemblyApi.settings(),
    ])

    contratistas.value = c
    modelos.value = m
    bodegas.value = b
    gastoGuardado.value = ajustes.expensePerMotorcycle
    gasto.value = String(ajustes.expensePerMotorcycle)
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    cargando.value = false
  }
}

/**
 * Abre la tabla de un contratista con una fila por modelo activo: los que ya tienen precio
 * con el suyo, y los que no, en blanco. Si solo se enseñaran los que ya están, agregar un
 * modelo nuevo al contrato obligaría a adivinar dónde se hace.
 */
function abrir(c: AssemblyContractor) {
  if (abierto.value === c.id) {
    abierto.value = null
    return
  }

  abierto.value = c.id
  tarifas.value = {}

  for (const m of modelosActivos.value) {
    const fila = c.rates.find((r) => r.modelId === m.id)

    tarifas.value[m.id] = {
      charge: fila ? String(fila.charge) : '',
      pay: fila ? String(fila.technicianPay) : '',
    }
  }
}

function margen(modelId: string) {
  const fila = tarifas.value[modelId]
  if (!fila) return null

  const cobro = Number(fila.charge)
  const pago = Number(fila.pay)

  if (!fila.charge || !fila.pay || Number.isNaN(cobro) || Number.isNaN(pago)) return null

  return cobro - pago
}

async function guardarTarifas(c: AssemblyContractor) {
  busy.value = true
  error.value = ''

  try {
    // Solo las filas con los dos números puestos. Una a medio llenar no es una tarifa: es un
    // modelo que todavía no se negoció, y guardarla en cero haría que se armara gratis.
    const rates = Object.entries(tarifas.value)
      .filter(([, v]) => v.charge !== '' && v.pay !== '')
      .map(([modelId, v]) => ({
        modelId,
        charge: Number(v.charge),
        technicianPay: Number(v.pay),
      }))

    const guardado = await assemblyApi.saveRates(c.id, rates)
    contratistas.value = contratistas.value.map((x) => (x.id === guardado.id ? guardado : x))
    abierto.value = null
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

async function agregarContratista() {
  if (!nuevoContratista.value.trim()) return
  await correr(async () => {
    contratistas.value = [
      ...contratistas.value,
      await assemblyApi.createContractor({ name: nuevoContratista.value.trim(), notes: null }),
    ].sort((a, b) => a.name.localeCompare(b.name))
    nuevoContratista.value = ''
  })
}

async function agregarModelo() {
  if (!nuevoModelo.value.trim()) return
  await correr(async () => {
    const creado = await assemblyApi.createModel(
      nuevoModelo.value.trim(),
      nuevoPrefijo.value.trim() || undefined,
    )

    modelos.value = [...modelos.value, creado].sort((a, b) => a.name.localeCompare(b.name))
    nuevoModelo.value = ''
    nuevoPrefijo.value = ''
  })
}

/** Ponerle o corregirle el prefijo a un modelo que ya está. */
async function editarPrefijo(m: AssemblyModel) {
  const escrito = window.prompt(
    `Primeros caracteres del VIN de ${m.name}. Cópielos de la etiqueta de una caja: son los ` +
      'que comparten todas las motos de ese modelo, unos ocho. Vacío lo quita.',
    m.vinPrefix ?? '',
  )

  if (escrito === null) return

  await correr(async () => {
    const guardado = await assemblyApi.updateModel(m.id, m.name, m.isActive, escrito.trim())
    modelos.value = modelos.value.map((x) => (x.id === guardado.id ? guardado : x))
  })
}

async function agregarBodega() {
  if (!nuevaBodega.value.trim()) return
  await correr(async () => {
    bodegas.value = [
      ...bodegas.value,
      await assemblyApi.createWarehouse(nuevaBodega.value.trim()),
    ].sort((a, b) => a.name.localeCompare(b.name))
    nuevaBodega.value = ''
  })
}

async function alternarContratista(c: AssemblyContractor) {
  await correr(async () => {
    const guardado = await assemblyApi.updateContractor(c.id, {
      name: c.name,
      notes: c.notes,
      isActive: !c.isActive,
      branchId: c.branchId,
    })
    contratistas.value = contratistas.value.map((x) => (x.id === guardado.id ? guardado : x))
  })
}

/**
 * La sucursal que lleva el contrato. De aquí sale la de cada moto que se registre: antes se
 * le preguntaba a quien registraba, y al técnico con dos sucursales le quedaba un callejón
 * sin salida —está en la bodega del contratista y no tiene cómo contestar—.
 */
async function cambiarSucursal(c: AssemblyContractor, branchId: string) {
  await correr(async () => {
    const guardado = await assemblyApi.updateContractor(c.id, {
      name: c.name,
      notes: c.notes,
      isActive: c.isActive,
      branchId,
    })
    contratistas.value = contratistas.value.map((x) => (x.id === guardado.id ? guardado : x))
  })
}

async function alternarModelo(m: AssemblyModel) {
  await correr(async () => {
    const guardado = await assemblyApi.updateModel(m.id, m.name, !m.isActive, m.vinPrefix)
    modelos.value = modelos.value.map((x) => (x.id === guardado.id ? guardado : x))
  })
}

async function alternarBodega(b: AssemblyWarehouse) {
  await correr(async () => {
    const guardado = await assemblyApi.updateWarehouse(b.id, b.name, !b.isActive)
    bodegas.value = bodegas.value.map((x) => (x.id === guardado.id ? guardado : x))
  })
}

async function correr(accion: () => Promise<void>) {
  busy.value = true
  error.value = ''

  try {
    await accion()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="vista">
    <header>
      <div>
        <h1>Armado de motos</h1>
        <p class="muted small">
          Con quién se trabaja, qué modelos se arman y en qué bodegas. De aquí salen los dos
          números de cada moto que se registre: lo que se le cobra al contratista y lo que se
          le paga al técnico.
        </p>
      </div>
    </header>

    <ErrorNote v-if="error" :message="error" />

    <!-- Fuera de las pestañas a propósito: no es catálogo, es un número del taller y vale
         para todos los contratos. -->
    <fieldset class="gasto">
      <legend>El gasto del taller por cada moto</legend>
      <div class="gasto-fila">
        <input
          v-model="gasto"
          type="number"
          min="0"
          step="1"
          inputmode="decimal"
          placeholder="0.00"
        />
        <button type="button" :disabled="busy || !gastoCambio" @click="guardarGasto">
          {{ busy ? 'Guardando…' : 'Guardar' }}
        </button>
      </div>
      <p class="muted small">
        Lo que armar una moto le cuesta al taller aparte del pago al técnico: transporte a la
        bodega, herramienta, lo que no depende del modelo. Se copia a cada moto al
        registrarla, así que cambiarlo no toca lo ya armado. Sale en el reporte suyo —el de
        «Lo del taller»— y nunca en el que recibe el contratista ni en el del técnico.
      </p>
    </fieldset>

    <nav class="pestanas">
      <button
        type="button"
        :class="{ activa: pestana === 'contratistas' }"
        @click="pestana = 'contratistas'"
      >
        Contratistas
      </button>
      <button type="button" :class="{ activa: pestana === 'modelos' }" @click="pestana = 'modelos'">
        Modelos
      </button>
      <button type="button" :class="{ activa: pestana === 'bodegas' }" @click="pestana = 'bodegas'">
        Bodegas
      </button>
    </nav>

    <p v-if="cargando" class="muted">Cargando…</p>

    <template v-else-if="pestana === 'contratistas'">
      <p v-if="!modelosActivos.length" class="muted">
        Antes de poner precios hay que tener modelos. Empiece por la pestaña de Modelos.
      </p>

      <div class="alta">
        <input
          v-model="nuevoContratista"
          placeholder="Nombre del contratista"
          maxlength="160"
          @keyup.enter="agregarContratista"
        />
        <button type="button" :disabled="busy || !nuevoContratista.trim()" @click="agregarContratista">
          Agregar
        </button>
      </div>

      <p v-if="!contratistas.length" class="muted">Todavía no hay contratistas.</p>

      <article v-for="c in contratistas" :key="c.id" class="ficha" :class="{ inactivo: !c.isActive }">
        <div class="cabecera">
          <div>
            <strong>{{ c.name }}</strong>
            <span v-if="!c.isActive" class="muted small"> · inactivo</span>
            <p class="muted small">
              {{ c.rates.length }}
              {{ c.rates.length === 1 ? 'modelo con precio' : 'modelos con precio' }}
            </p>
            <label v-if="sucursales.length > 1" class="sucursal-contrato">
              <span class="muted small">Lo lleva</span>
              <select
                :value="c.branchId ?? ''"
                :disabled="busy"
                @change="cambiarSucursal(c, ($event.target as HTMLSelectElement).value)"
              >
                <option value="" disabled>Elija la sucursal</option>
                <option v-for="s in sucursales" :key="s.id" :value="s.id">{{ s.name }}</option>
              </select>
            </label>
          </div>
          <div class="acciones">
            <button type="button" class="chico" :disabled="busy" @click="abrir(c)">
              {{ abierto === c.id ? 'Cerrar' : 'Precios' }}
            </button>
            <button type="button" class="link" :disabled="busy" @click="alternarContratista(c)">
              {{ c.isActive ? 'Desactivar' : 'Reactivar' }}
            </button>
          </div>
        </div>

        <div v-if="abierto === c.id" class="tabla">
          <div class="fila encabezado">
            <span>Modelo</span>
            <span>Se le cobra</span>
            <span>Se le paga al técnico</span>
            <span>Queda</span>
          </div>

          <div v-for="m in modelosActivos" :key="m.id" class="fila">
            <span>{{ m.name }}</span>
            <input v-model="tarifas[m.id].charge" type="number" min="0" step="1" placeholder="—" />
            <input v-model="tarifas[m.id].pay" type="number" min="0" step="1" placeholder="—" />
            <span class="margen">
              {{ margen(m.id) === null ? '—' : formatMoney(margen(m.id)!) }}
            </span>
          </div>

          <div class="pie">
            <p class="muted small">
              Los modelos sin los dos números no se guardan: quedan como todavía no negociados.
              Lo que se registre después copia estos precios, así que subir la tarifa en marzo
              no cambia lo que ya se facturó en enero.
            </p>
            <button type="button" :disabled="busy" @click="guardarTarifas(c)">
              {{ busy ? 'Guardando…' : 'Guardar los precios' }}
            </button>
          </div>
        </div>
      </article>
    </template>

    <template v-else-if="pestana === 'modelos'">
      <div class="alta">
        <input
          v-model="nuevoModelo"
          placeholder="Modelo de moto, como lo nombra el contratista"
          maxlength="80"
          @keyup.enter="agregarModelo"
        />
        <input
          v-model="nuevoPrefijo"
          placeholder="Inicio del VIN (opcional)"
          maxlength="10"
          class="prefijo"
          @keyup.enter="agregarModelo"
        />
        <button type="button" :disabled="busy || !nuevoModelo.trim()" @click="agregarModelo">
          Agregar
        </button>
      </div>

      <p class="muted small">
        El inicio del VIN son los primeros caracteres que comparten todas las motos de ese
        modelo —unos ocho, cópielos de la etiqueta de una caja—. Con él, el técnico escanea y
        el modelo sale puesto desde la primera moto. Sin él también funciona, pero lo aprende
        a partir de la segunda de cada lote.
      </p>

      <p v-if="!modelos.length" class="muted">Todavía no hay modelos.</p>

      <ul class="lista">
        <li v-for="m in modelos" :key="m.id" :class="{ inactivo: !m.isActive }">
          <span>
            {{ m.name }}
            <span v-if="m.vinPrefix" class="prefijo-sello">{{ m.vinPrefix }}</span>
            <span v-else class="muted small"> · sin inicio de VIN</span>
            <span v-if="!m.isActive" class="muted small"> · inactivo</span>
          </span>
          <span class="acciones">
            <button type="button" class="link" :disabled="busy" @click="editarPrefijo(m)">
              {{ m.vinPrefix ? 'Cambiar el VIN' : 'Poner el inicio del VIN' }}
            </button>
            <button type="button" class="link" :disabled="busy" @click="alternarModelo(m)">
              {{ m.isActive ? 'Desactivar' : 'Reactivar' }}
            </button>
          </span>
        </li>
      </ul>
    </template>

    <template v-else>
      <div class="alta">
        <input
          v-model="nuevaBodega"
          placeholder="Bodega o lugar donde se arma"
          maxlength="160"
          @keyup.enter="agregarBodega"
        />
        <button type="button" :disabled="busy || !nuevaBodega.trim()" @click="agregarBodega">
          Agregar
        </button>
      </div>

      <p v-if="!bodegas.length" class="muted">Todavía no hay bodegas.</p>

      <ul class="lista">
        <li v-for="b in bodegas" :key="b.id" :class="{ inactivo: !b.isActive }">
          <span>{{ b.name }}<span v-if="!b.isActive" class="muted small"> · inactiva</span></span>
          <button type="button" class="link" :disabled="busy" @click="alternarBodega(b)">
            {{ b.isActive ? 'Desactivar' : 'Reactivar' }}
          </button>
        </li>
      </ul>
    </template>
  </section>
</template>

<style scoped>
.vista {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

header h1 {
  margin: 0 0 0.25rem;
}

.pestanas {
  display: flex;
  gap: 0.5rem;
  border-bottom: 1px solid var(--border);
}

.pestanas button {
  padding: 0.5rem 0.75rem;
  border: 0;
  border-bottom: 2px solid transparent;
  border-radius: 0;
  background: none;
  color: var(--text-muted);
  cursor: pointer;
}

.pestanas button.activa {
  border-bottom-color: var(--primary);
  color: var(--text);
  font-weight: 600;
}

.gasto {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 0.75rem 1rem 0.5rem;
  background: var(--surface);
}

.gasto legend {
  padding: 0 0.4rem;
  font-weight: 600;
  font-size: 0.9rem;
}

.gasto-fila {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}

.gasto-fila input {
  width: 10rem;
  font-variant-numeric: tabular-nums;
}

.gasto p {
  margin: 0.4rem 0 0;
  max-width: 62ch;
}

.alta {
  display: flex;
  gap: 0.5rem;
}

.alta input {
  flex: 1;
  max-width: 28rem;
}

/* Un trozo de VIN: se compara carácter a carácter contra la etiqueta. */
.alta input.prefijo {
  flex: 0 1 14rem;
  font-family: ui-monospace, monospace;
}

.alta input.prefijo:not(:placeholder-shown) {
  text-transform: uppercase;
}

.prefijo-sello {
  margin-left: 0.375rem;
  padding: 0 0.3rem;
  border: 1px solid var(--border);
  border-radius: 3px;
  font-family: ui-monospace, monospace;
  font-size: 0.8125rem;
}

.ficha {
  padding: 0.75rem 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.ficha.inactivo,
.lista li.inactivo {
  opacity: 0.6;
}

.cabecera {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.cabecera p {
  margin: 0.125rem 0 0;
}

.acciones {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.sucursal-contrato {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-top: 0.4rem;
}

.sucursal-contrato select {
  padding: 0.25rem 0.4rem;
  font-size: 0.85rem;
}

.tabla {
  margin-top: 0.75rem;
  border-top: 1px solid var(--border);
  padding-top: 0.75rem;
}

/* Cuatro columnas: el modelo, los dos precios y lo que queda. La última es la que contesta
   si el modelo conviene, así que va a la vista y no se calcula aparte. */
.fila {
  display: grid;
  grid-template-columns: 2fr 1fr 1fr 1fr;
  align-items: center;
  gap: 0.5rem;
  padding: 0.25rem 0;
}

.fila.encabezado {
  font-size: 0.8125rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.03em;
}

/* Campos de dinero, no de texto: anchos de banda completa invitan a teclear un párrafo. */
.fila input {
  max-width: 7rem;
  text-align: right;
  font-variant-numeric: tabular-nums;
}

.margen {
  font-variant-numeric: tabular-nums;
}

.pie {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 1rem;
  margin-top: 0.75rem;
}

.pie p {
  max-width: 34rem;
}

.lista {
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.lista li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--surface);
}

@media (max-width: 40rem) {
  .fila {
    grid-template-columns: 1fr 1fr;
  }

  .fila.encabezado {
    display: none;
  }

  .pie {
    flex-direction: column;
    align-items: stretch;
  }
}
</style>

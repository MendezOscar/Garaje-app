<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import {
  branchesApi,
  customersApi,
  equipmentApi,
  serviceRequestsApi,
  usersApi,
  vehiclesApi,
  workOrdersApi,
} from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import { useTaller } from '@/composables/useTaller'
import { useAuthStore } from '@/stores/auth'
import {
  VEHICLE_TYPE_LABEL,
  VehicleType,
  type Branch,
  type Customer,
  type Equipment,
  type EquipmentType,
  type User,
  type Vehicle,
  type WorkOrderDetail,
} from '@/types/domain'

/**
 * Recepción en el mostrador: el taller registra el requerimiento por el cliente.
 *
 * Es el caso normal, no la excepción. Casi nadie llega con la aplicación instalada, y las
 * primeras semanas nadie la tiene: si el requerimiento solo pudiera entrar desde el teléfono
 * del cliente, la bandeja estaría siempre vacía y el flujo de trabajo empezaría en el aire.
 *
 * Por eso el formulario permite crear cliente y vehículo sobre la marcha: obligar a darlos de
 * alta en otra pantalla antes de poder recibir la moto es exactamente lo que hace que nadie
 * use el sistema y todo acabe otra vez en un cuaderno.
 */
/**
 * Dos modos con la misma hoja de recepción, porque el mostrador es el mismo:
 *
 * - `request` registra un requerimiento, que alguien tendrá que aprobar. Es lo que se hace
 *   cuando el cliente llama para pedir cita.
 * - `order` abre la orden de trabajo directo. Es lo que pasa cuando el vehículo ya está en el
 *   patio: pedirle al mostrador que registre un requerimiento y luego lo apruebe es hacerle
 *   dar dos vueltas para llegar al mismo sitio.
 *
 * Y dos presentaciones: `panel` es el bloque plegable de la bandeja de requerimientos;
 * `page`, la pantalla propia de «Recibir vehículo», donde el formulario ya está abierto.
 */
const props = withDefaults(
  defineProps<{ mode?: 'request' | 'order'; variant?: 'panel' | 'page' }>(),
  { mode: 'request', variant: 'panel' },
)

/**
 * `created` lleva la orden recién abierta, o `null` cuando lo que se guardó fue un
 * requerimiento. `contexto` avisa de lo que se va eligiendo, para que la pantalla que aloja la
 * hoja pueda enseñar al lado lo que le va a pasar al vehículo.
 */
const emit = defineEmits<{
  created: [orden: WorkOrderDetail | null]
  contexto: [
    {
      vehicle: Vehicle | null
      /** Lo elegido, sea vehículo o equipo: con qué pedir su historial. */
      assetId: string | null
      technician: string | null
    },
  ]
}>()

const auth = useAuthStore()
const { esDeEquipos, vocabulario, cargar: cargarTaller } = useTaller()

const open = ref(props.variant === 'page')
const busy = ref(false)
const error = ref('')

const branches = ref<Branch[]>([])
const search = ref('')
const results = ref<Customer[]>([])
const searched = ref(false)

const customer = ref<Customer | null>(null)
const vehicles = ref<Vehicle[]>([])

/** En un taller de equipos, lo que tiene el cliente son equipos: la otra mitad de la hoja. */
const equipos = ref<Equipment[]>([])
const categorias = ref<EquipmentType[]>([])

const newEquipment = ref({ equipmentTypeId: '', brand: '', model: '', serialNumber: '' })

/** Lo que el cliente tiene, de un lado o del otro, para pintar las tarjetas con una sola lista. */
const fichas = computed(() =>
  esDeEquipos.value
    ? equipos.value.map((e) => ({
        id: e.id,
        titulo: `${e.brand} ${e.model}`,
        detalle: e.serialNumber ?? e.tag ?? e.equipmentTypeName,
      }))
    : vehicles.value.map((v) => ({
        id: v.id,
        titulo: `${v.brand} ${v.model}`,
        detalle: v.plate ?? VEHICLE_TYPE_LABEL[v.type],
      })),
)

const newCustomer = ref({ fullName: '', phone: '' })
const creatingCustomer = ref(false)

const newVehicle = ref({
  type: VehicleType.Motorcycle as VehicleType,
  brand: '',
  model: '',
  year: '' as string | number,
  plate: '',
})
const creatingVehicle = ref(false)

const form = ref({
  vehicleId: '',
  branchId: '',
  description: '',
  reportedSymptoms: '',
  mileage: '' as string | number,
  // Solo en modo orden: la orden nace con fecha prometida y con técnico si ya se sabe quién.
  promisedAt: '',
  technicianId: '',
})

/** Los técnicos, para asignar al recibir. Solo se piden en modo orden y solo los ve el Dueño. */
const technicians = ref<User[]>([])

const techniciansForBranch = computed(() =>
  technicians.value.filter((t) => t.isActive && t.branchIds.includes(form.value.branchId)),
)

const vehiculoElegido = computed(
  () => vehicles.value.find((v) => v.id === form.value.vehicleId) ?? null,
)

/** El kilometraje de la última visita, como propuesta: casi siempre es lo que hay que subir. */
const kmConocido = computed(() =>
  vehiculoElegido.value?.mileage ? vehiculoElegido.value.mileage.toLocaleString('es-HN') : '0',
)

// El mismo mensaje de «elija el vehículo y la sucursal» se quedaba en pantalla después de
// elegirlos, contradiciendo lo que se veía: se borra en cuanto uno de los dos cambia.
watch([() => form.value.vehicleId, () => form.value.branchId], () => {
  error.value = ''
})

watch(
  [vehiculoElegido, () => form.value.vehicleId, () => form.value.technicianId],
  ([vehiculo, elegidoId, tecnicoId]) => {
    emit('contexto', {
      vehicle: vehiculo,
      assetId: elegidoId || null,
      technician: technicians.value.find((t) => t.id === tecnicoId)?.fullName ?? null,
    })
  },
  { immediate: true },
)

/** El técnico solo recibe en sus sucursales; al Dueño se le muestran todas. */
const allowedBranches = computed(() =>
  auth.isOwner
    ? branches.value
    : branches.value.filter((b) => auth.user?.branches.some((own) => own.id === b.id)),
)

async function findCustomers() {
  if (!search.value.trim()) return

  busy.value = true
  error.value = ''
  try {
    results.value = (await customersApi.list({ search: search.value.trim(), pageSize: 20 })).items
    searched.value = true
    // Si la búsqueda no encontró a nadie, lo que sigue es darlo de alta: se propone el
    // texto buscado como nombre para no teclearlo dos veces.
    if (!results.value.length && !newCustomer.value.fullName) {
      newCustomer.value.fullName = search.value.trim()
      creatingCustomer.value = true
    }
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo buscar el cliente.')
  } finally {
    busy.value = false
  }
}

async function pick(selected: Customer) {
  customer.value = selected
  creatingCustomer.value = false
  busy.value = true

  try {
    if (esDeEquipos.value) {
      equipos.value = (await equipmentApi.list({ customerId: selected.id, pageSize: 50 })).items
    } else {
      vehicles.value = (await vehiclesApi.list({ customerId: selected.id, pageSize: 50 })).items
    }

    form.value.vehicleId = fichas.value[0]?.id ?? ''
    creatingVehicle.value = fichas.value.length === 0
  } catch (e) {
    error.value = errorMessage(e, `No se pudieron cargar los ${vocabulario.value.activos}.`)
  } finally {
    busy.value = false
  }
}

/** Alta de un equipo sobre la marcha, igual que la del vehículo y por la misma razón. */
async function saveEquipment() {
  if (!customer.value) return
  if (!newEquipment.value.brand.trim() || !newEquipment.value.model.trim()) {
    error.value = 'El equipo necesita marca y modelo.'
    return
  }
  if (!newEquipment.value.equipmentTypeId) {
    error.value = 'Diga qué clase de equipo es.'
    return
  }

  busy.value = true
  error.value = ''
  try {
    const created = await equipmentApi.create({
      customerId: customer.value.id,
      equipmentTypeId: newEquipment.value.equipmentTypeId,
      brand: newEquipment.value.brand.trim(),
      model: newEquipment.value.model.trim(),
      serialNumber: newEquipment.value.serialNumber.trim() || undefined,
    })

    equipos.value = [...equipos.value, created]
    form.value.vehicleId = created.id
    creatingVehicle.value = false
    newEquipment.value = {
      equipmentTypeId: categorias.value[0]?.id ?? '',
      brand: '',
      model: '',
      serialNumber: '',
    }
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo registrar el equipo.')
  } finally {
    busy.value = false
  }
}

async function saveCustomer() {
  if (!newCustomer.value.fullName.trim() || !newCustomer.value.phone.trim()) {
    error.value = 'El cliente necesita nombre y teléfono.'
    return
  }

  busy.value = true
  error.value = ''
  try {
    const created = await customersApi.create({
      fullName: newCustomer.value.fullName.trim(),
      phone: newCustomer.value.phone.trim(),
    })
    newCustomer.value = { fullName: '', phone: '' }
    await pick(created)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo registrar el cliente.')
  } finally {
    busy.value = false
  }
}

async function saveVehicle() {
  if (!customer.value) return
  if (!newVehicle.value.brand.trim() || !newVehicle.value.model.trim()) {
    error.value = 'El vehículo necesita marca y modelo.'
    return
  }

  busy.value = true
  error.value = ''
  try {
    const created = await vehiclesApi.create({
      customerId: customer.value.id,
      type: newVehicle.value.type,
      brand: newVehicle.value.brand.trim(),
      model: newVehicle.value.model.trim(),
      year: Number(newVehicle.value.year) || undefined,
      plate: newVehicle.value.plate.trim() || undefined,
    })

    vehicles.value = [...vehicles.value, created]
    form.value.vehicleId = created.id
    creatingVehicle.value = false
    newVehicle.value = { type: VehicleType.Motorcycle, brand: '', model: '', year: '', plate: '' }
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo registrar el vehículo.')
  } finally {
    busy.value = false
  }
}

/**
 * `modo` se puede forzar para el caso del mostrador: el cliente vino a preguntar, se llenó la
 * hoja y al final resulta que deja el vehículo la semana que viene. Guardarlo como
 * requerimiento evita teclear todo otra vez, y no abre una orden de un vehículo que no está.
 */
async function submit(modo: 'request' | 'order' = props.mode) {
  if (!form.value.vehicleId || !form.value.branchId) {
    error.value = `Elija el ${vocabulario.value.activo} y la sucursal.`
    return
  }
  if (!form.value.description.trim()) {
    error.value = `Escriba por qué entra el ${vocabulario.value.activo}.`
    return
  }

  busy.value = true
  error.value = ''
  try {
    let creada: WorkOrderDetail | null = null

    if (modo === 'order') {
      const orden = await workOrdersApi.create({
        branchId: form.value.branchId,
        ...(esDeEquipos.value
          ? { equipmentId: form.value.vehicleId }
          : { vehicleId: form.value.vehicleId }),
        description: form.value.description.trim(),
        assignedTechnicianId: form.value.technicianId || null,
        mileageIn: Number(form.value.mileage) || undefined,
        // El input da «2026-08-19T16:00»; el backend espera un instante con zona.
        promisedAt: form.value.promisedAt
          ? new Date(form.value.promisedAt).toISOString()
          : undefined,
      })
      creada = orden
    } else {
      await serviceRequestsApi.create({
        branchId: form.value.branchId,
        ...(esDeEquipos.value
          ? { equipmentId: form.value.vehicleId }
          : { vehicleId: form.value.vehicleId }),
        description: form.value.description.trim(),
        reportedSymptoms: form.value.reportedSymptoms.trim() || undefined,
        mileage: Number(form.value.mileage) || undefined,
      })
    }

    reset()
    if (props.variant === 'panel') open.value = false
    emit('created', creada)
  } catch (e) {
    error.value = errorMessage(
      e,
      modo === 'order' ? 'No se pudo abrir la orden.' : 'No se pudo registrar el requerimiento.',
    )
  } finally {
    busy.value = false
  }
}

function reset() {
  search.value = ''
  results.value = []
  searched.value = false
  customer.value = null
  vehicles.value = []
  equipos.value = []
  creatingCustomer.value = false
  creatingVehicle.value = false
  form.value = {
    vehicleId: '',
    branchId: allowedBranches.value[0]?.id ?? '',
    description: '',
    reportedSymptoms: '',
    mileage: '',
    promisedAt: '',
    technicianId: '',
  }
  error.value = ''
}

watch(allowedBranches, (list) => {
  if (!form.value.branchId) form.value.branchId = list[0]?.id ?? ''
})

onMounted(async () => {
  await cargarTaller()
  branches.value = await branchesApi.list().catch(() => [])
  form.value.branchId = allowedBranches.value[0]?.id ?? ''

  if (esDeEquipos.value) {
    categorias.value = await equipmentApi.types().catch(() => [])
    newEquipment.value.equipmentTypeId = categorias.value[0]?.id ?? ''
  }

  if (props.mode === 'order') {
    technicians.value = await usersApi.list('Technician').catch(() => [])
  }
})
</script>

<template>
  <article :class="variant === 'page' ? 'hoja' : 'panel'">
    <header v-if="variant === 'panel'">
      <div>
        <h2>Registrar un requerimiento</h2>
        <p class="muted small">
          Para el {{ vocabulario.activo }} que llega al mostrador sin que el cliente use la
          aplicación.
        </p>
      </div>
      <button type="button" @click="open = !open">{{ open ? 'Cerrar' : 'Nuevo' }}</button>
    </header>

    <form v-if="open" @submit.prevent="submit()">
      <ErrorNote v-if="error" :message="error" />

      <!-- Los tres pasos numerados y a la vista, no uno detrás de otro: en el mostrador se
           llenan en el orden en que el cliente habla, y volver atrás a corregir el vehículo
           no debería esconder lo que ya se escribió. -->
      <section class="paso">
        <p class="paso-titulo">1 · Cliente</p>

        <template v-if="!customer">
          <div class="row">
            <input
              v-model="search"
              :placeholder="`Buscar por nombre, teléfono o ${vocabulario.identificador.toLowerCase()}`"
              @keydown.enter.prevent="findCustomers"
            />
            <button type="button" :disabled="busy" @click="findCustomers">Buscar</button>
            <button type="button" class="secondary" @click="creatingCustomer = true">
              Nuevo cliente
            </button>
          </div>

          <ul v-if="results.length" class="results">
            <li v-for="found in results" :key="found.id">
              <button type="button" :disabled="busy" @click="pick(found)">
                <span class="ficha">
                  <strong>{{ found.fullName }}</strong>
                  <span class="muted small num">
                    {{ found.phone }} · {{ found.vehicleCount }} {{ vocabulario.activo }}(s)
                  </span>
                </span>
                <span v-if="found.hasAppAccess" class="etiqueta">Usa la app</span>
              </button>
            </li>
          </ul>

          <p v-else-if="searched && !creatingCustomer" class="muted small">
            Sin resultados.
            <button type="button" class="link" @click="creatingCustomer = true">
              Registrar cliente nuevo
            </button>
          </p>

          <fieldset v-if="creatingCustomer">
            <legend>Cliente nuevo</legend>
            <div class="row">
              <input v-model="newCustomer.fullName" placeholder="Nombre completo" />
              <input v-model="newCustomer.phone" placeholder="Teléfono (50499998888)" />
              <button type="button" :disabled="busy" @click="saveCustomer">Guardar</button>
            </div>
          </fieldset>
        </template>

        <div v-else class="elegido">
          <div class="ficha">
            <strong>{{ customer.fullName }}</strong>
            <span class="muted small num">
              {{ customer.phone }} · {{ customer.vehicleCount }} {{ vocabulario.activo }}(s)
            </span>
          </div>
          <span v-if="customer.hasAppAccess" class="etiqueta">Usa la app</span>
          <button type="button" class="link" @click="reset">Cambiar</button>
        </div>
      </section>

      <template v-if="customer">
        <section class="paso">
          <p class="paso-titulo">2 · {{ vocabulario.Activo }}</p>

          <!-- Tarjetas y no una lista desplegable: casi todo cliente tiene uno o dos, y la
               placa —o la serie— hay que verla para saber que es la que está en el patio. -->
          <div v-if="fichas.length" class="vehiculos">
            <button
              v-for="ficha in fichas"
              :key="ficha.id"
              type="button"
              class="vehiculo"
              :class="{ on: form.vehicleId === ficha.id }"
              @click="form.vehicleId = ficha.id"
            >
              <strong>{{ ficha.titulo }}</strong>
              <span class="muted small">
                <span class="placa">{{ ficha.detalle }}</span>
              </span>
            </button>
          </div>

          <button
            v-if="!creatingVehicle"
            type="button"
            class="link"
            @click="creatingVehicle = true"
          >
            Otro {{ vocabulario.activo }}
          </button>

          <fieldset v-if="creatingVehicle && !esDeEquipos">
            <legend>Vehículo nuevo</legend>
            <div class="row">
              <select v-model.number="newVehicle.type">
                <option v-for="(label, value) in VEHICLE_TYPE_LABEL" :key="value" :value="Number(value)">
                  {{ label }}
                </option>
              </select>
              <input v-model="newVehicle.brand" placeholder="Marca" />
              <input v-model="newVehicle.model" placeholder="Modelo" />
              <input v-model="newVehicle.year" type="number" placeholder="Año" class="narrow" />
              <input v-model="newVehicle.plate" placeholder="Placa" class="narrow" />
              <button type="button" :disabled="busy" @click="saveVehicle">Guardar</button>
            </div>
          </fieldset>

          <fieldset v-if="creatingVehicle && esDeEquipos">
            <legend>Equipo nuevo</legend>
            <div class="row">
              <select v-model="newEquipment.equipmentTypeId">
                <option value="" disabled>Qué es</option>
                <option v-for="categoria in categorias" :key="categoria.id" :value="categoria.id">
                  {{ categoria.name }}
                </option>
              </select>
              <input v-model="newEquipment.brand" placeholder="Marca" />
              <input v-model="newEquipment.model" placeholder="Modelo" />
              <input v-model="newEquipment.serialNumber" placeholder="Serie" class="narrow" />
              <button type="button" :disabled="busy" @click="saveEquipment">Guardar</button>
            </div>
            <p class="muted small">
              La serie es opcional: hay equipos que no la traen o la traen ilegible.
            </p>
          </fieldset>
        </section>

        <section class="paso">
          <p class="paso-titulo">3 · El ingreso</p>

          <div class="campos">
            <label v-if="!esDeEquipos">
              Kilometraje o millaje
              <input v-model="form.mileage" type="number" min="0" :placeholder="kmConocido" />
            </label>

            <label>
              Sucursal
              <select v-model="form.branchId">
                <option v-for="branch in allowedBranches" :key="branch.id" :value="branch.id">
                  {{ branch.name }}
                </option>
              </select>
            </label>

            <template v-if="mode === 'order'">
              <!-- Prometer una fecha al recibir es lo que después hace que el tablero pueda
                   decir qué está atrasado. -->
              <label>
                Prometida
                <input v-model="form.promisedAt" type="datetime-local" />
              </label>

              <label>
                Técnico
                <select v-model="form.technicianId">
                  <option value="">Asignar después</option>
                  <option v-for="t in techniciansForBranch" :key="t.id" :value="t.id">
                    {{ t.fullName }}
                  </option>
                </select>
              </label>
            </template>
          </div>

          <label>
            Motivo de ingreso
            <textarea
              v-model="form.description"
              rows="3"
              placeholder="Qué pide el cliente: cambio de aceite, revisión de frenos…"
            />
          </label>

          <label v-if="mode === 'request'">
            Qué ha notado el cliente (opcional)
            <textarea
              v-model="form.reportedSymptoms"
              rows="2"
              placeholder="Un ruido al frenar, se calienta en el tráfico…"
            />
          </label>
        </section>

        <div class="actions">
          <button type="submit" :disabled="busy">
            {{ mode === 'order' ? 'Abrir orden' : 'Registrar requerimiento' }}
          </button>
          <!-- El vehículo que no se quedó hoy: la hoja ya está llena, y guardarla como
               requerimiento evita teclearla otra vez cuando vuelva. -->
          <button
            v-if="mode === 'order'"
            type="button"
            class="secondary"
            :disabled="busy"
            @click="submit('request')"
          >
            Guardar como requerimiento
          </button>
          <button
            v-if="variant === 'panel'"
            type="button"
            class="secondary"
            @click="open = false"
          >
            Cancelar
          </button>
          <span v-if="variant === 'page'" class="muted small aparte">
            El cliente no necesita cuenta.
          </span>
        </div>
      </template>
    </form>
  </article>
</template>

<style scoped>
/* En pantalla propia el formulario no lleva marco ni la línea que lo separaba de la cabecera
   del panel: el marco lo pone la pantalla y la cabecera no existe. */
.hoja {
  padding: 0;
}

.hoja form {
  margin-top: 0;
  padding-top: 0;
  border-top: none;
}

.panel {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
  margin-bottom: 1rem;
}

header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

h2 {
  margin: 0;
  font-size: 1rem;
}

header p {
  margin: 0.125rem 0 0;
}

form {
  display: flex;
  flex-direction: column;
  gap: 1.125rem;
  margin-top: 1rem;
  padding-top: 1rem;
  border-top: 1px solid var(--border);
}

.paso {
  display: flex;
  flex-direction: column;
  gap: 0.625rem;
}

.paso-titulo {
  margin: 0;
  font-size: 0.6875rem;
  font-weight: 600;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--text-muted);
}

.campos {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
  gap: 0.625rem;
}

.vehiculos {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
  gap: 0.5rem;
}

/* La tarjeta elegida se marca con el borde, no con un relleno: el relleno de acento sobre
   texto pequeño se lee mal en oscuro. */
.vehiculo {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.125rem;
  padding: 0.625rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text);
  text-align: left;
}

.vehiculo.on {
  border-color: var(--accent);
  box-shadow: inset 0 0 0 1px var(--accent);
}

.placa {
  padding: 0 0.25rem;
  border: 1px solid var(--border);
  border-radius: 3px;
  font-family: ui-monospace, monospace;
}

.ficha {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  min-width: 0;
}

.elegido {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  padding: 0.625rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface-alt);
}

.elegido .link {
  margin-left: auto;
}

.etiqueta {
  padding: 0.0625rem 0.5rem;
  border-radius: 999px;
  background: color-mix(in srgb, var(--accent) 18%, transparent);
  color: var(--accent);
  font-size: 0.75rem;
  white-space: nowrap;
}

.aparte {
  margin-left: auto;
}

.num {
  font-variant-numeric: tabular-nums;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.row input,
.row select {
  flex: 1;
  min-width: 8rem;
}

.narrow {
  flex: 0 0 6rem;
  min-width: 6rem;
}

fieldset {
  margin: 0;
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: 6px;
}

legend {
  padding: 0 0.375rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.results {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  max-height: 14rem;
  overflow-y: auto;
}

.results button {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  width: 100%;
  padding: 0.5rem 0.625rem;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--surface-alt);
  color: var(--text);
  text-align: left;
}

.link {
  align-self: flex-start;
}

.secondary {
  background: transparent;
  border-color: var(--border);
  color: var(--text);
}

.actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.5rem;
}

textarea,
select,
input {
  font: inherit;
}

textarea {
  resize: vertical;
}

.error {
  margin: 0;
  color: var(--danger);
}
</style>

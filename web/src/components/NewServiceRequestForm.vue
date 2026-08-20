<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, customersApi, serviceRequestsApi, usersApi, vehiclesApi, workOrdersApi } from '@/api/garaj'
import { useAuthStore } from '@/stores/auth'
import {
  VEHICLE_TYPE_LABEL,
  VehicleType,
  type Branch,
  type Customer,
  type User,
  type Vehicle,
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

const emit = defineEmits<{ created: [id: string | null] }>()

const auth = useAuthStore()

const open = ref(props.variant === 'page')
const busy = ref(false)
const error = ref('')

const branches = ref<Branch[]>([])
const search = ref('')
const results = ref<Customer[]>([])
const searched = ref(false)

const customer = ref<Customer | null>(null)
const vehicles = ref<Vehicle[]>([])

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
    vehicles.value = (await vehiclesApi.list({ customerId: selected.id, pageSize: 50 })).items
    form.value.vehicleId = vehicles.value[0]?.id ?? ''
    creatingVehicle.value = vehicles.value.length === 0
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar los vehículos.')
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

async function submit() {
  if (!form.value.vehicleId || !form.value.branchId) {
    error.value = 'Elija el vehículo y la sucursal.'
    return
  }
  if (!form.value.description.trim()) {
    error.value = 'Escriba por qué entra el vehículo.'
    return
  }

  busy.value = true
  error.value = ''
  try {
    let creada: string | null = null

    if (props.mode === 'order') {
      const orden = await workOrdersApi.create({
        branchId: form.value.branchId,
        vehicleId: form.value.vehicleId,
        description: form.value.description.trim(),
        assignedTechnicianId: form.value.technicianId || null,
        mileageIn: Number(form.value.mileage) || undefined,
        // El input da «2026-08-19T16:00»; el backend espera un instante con zona.
        promisedAt: form.value.promisedAt
          ? new Date(form.value.promisedAt).toISOString()
          : undefined,
      })
      creada = orden.id
    } else {
      await serviceRequestsApi.create({
        branchId: form.value.branchId,
        vehicleId: form.value.vehicleId,
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
      props.mode === 'order'
        ? 'No se pudo abrir la orden.'
        : 'No se pudo registrar el requerimiento.',
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
  branches.value = await branchesApi.list().catch(() => [])
  form.value.branchId = allowedBranches.value[0]?.id ?? ''

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
          Para el vehículo que llega al mostrador sin que el cliente use la aplicación.
        </p>
      </div>
      <button type="button" @click="open = !open">{{ open ? 'Cerrar' : 'Nuevo' }}</button>
    </header>

    <form v-if="open" @submit.prevent="submit">
      <p v-if="error" class="error">{{ error }}</p>

      <!-- Paso 1: quién es -->
      <template v-if="!customer">
        <div class="row">
          <input
            v-model="search"
            placeholder="Buscar por nombre, teléfono o placa"
            @keydown.enter.prevent="findCustomers"
          />
          <button type="button" :disabled="busy" @click="findCustomers">Buscar</button>
        </div>

        <ul v-if="results.length" class="results">
          <li v-for="found in results" :key="found.id">
            <button type="button" :disabled="busy" @click="pick(found)">
              <strong>{{ found.fullName }}</strong>
              <span class="muted small">
                {{ found.phone }} · {{ found.vehicleCount }} vehículo(s)
              </span>
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

      <!-- Paso 2: qué vehículo y qué le pasa -->
      <template v-else>
        <p class="chosen">
          <strong>{{ customer.fullName }}</strong>
          <span class="muted small">{{ customer.phone }}</span>
          <button type="button" class="link" @click="reset">Cambiar</button>
        </p>

        <label>
          Vehículo
          <select v-model="form.vehicleId">
            <option v-for="vehicle in vehicles" :key="vehicle.id" :value="vehicle.id">
              {{ VEHICLE_TYPE_LABEL[vehicle.type] }} · {{ vehicle.brand }} {{ vehicle.model }}
              <template v-if="vehicle.plate"> · {{ vehicle.plate }}</template>
            </option>
          </select>
        </label>

        <button
          v-if="!creatingVehicle"
          type="button"
          class="link"
          @click="creatingVehicle = true"
        >
          Registrar otro vehículo
        </button>

        <fieldset v-if="creatingVehicle">
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

        <label>
          Sucursal
          <select v-model="form.branchId">
            <option v-for="branch in allowedBranches" :key="branch.id" :value="branch.id">
              {{ branch.name }}
            </option>
          </select>
        </label>

        <label>
          Motivo de ingreso
          <textarea
            v-model="form.description"
            rows="2"
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

        <div class="row">
          <label class="narrow-field">
            Kilometraje (opcional)
            <input v-model="form.mileage" type="number" min="0" />
          </label>

          <template v-if="mode === 'order'">
            <!-- Prometer una fecha al recibir es lo que después hace que el tablero pueda
                 decir qué está atrasado. -->
            <label class="narrow-field">
              Fecha prometida (opcional)
              <input v-model="form.promisedAt" type="datetime-local" />
            </label>

            <label class="narrow-field">
              Técnico (opcional)
              <select v-model="form.technicianId">
                <option value="">Asignar después</option>
                <option v-for="t in techniciansForBranch" :key="t.id" :value="t.id">
                  {{ t.fullName }}
                </option>
              </select>
            </label>
          </template>
        </div>

        <div class="actions">
          <button type="submit" :disabled="busy">
            {{ mode === 'order' ? 'Abrir la orden' : 'Registrar requerimiento' }}
          </button>
          <button
            v-if="variant === 'panel'"
            type="button"
            class="secondary"
            @click="open = false"
          >
            Cancelar
          </button>
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
  gap: 0.75rem;
  margin-top: 1rem;
  padding-top: 1rem;
  border-top: 1px solid var(--border);
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

.narrow-field {
  max-width: 12rem;
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
  flex-direction: column;
  align-items: flex-start;
  gap: 0.125rem;
  width: 100%;
  padding: 0.5rem 0.625rem;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--surface-alt);
  color: var(--text);
  text-align: left;
}

.chosen {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
  margin: 0;
  flex-wrap: wrap;
}

.link {
  align-self: flex-start;
  padding: 0;
  border: none;
  background: none;
  color: var(--accent);
  font-size: 0.8125rem;
  cursor: pointer;
}

.secondary {
  background: transparent;
  border-color: var(--border);
  color: var(--text);
}

.actions {
  display: flex;
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

.muted {
  color: var(--text-muted);
}

.small {
  font-size: 0.75rem;
}

.error {
  margin: 0;
  color: var(--danger);
}
</style>

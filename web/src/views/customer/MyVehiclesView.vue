<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, serviceRequestsApi, vehiclesApi } from '@/api/garaj'
import OrderListView from '@/views/OrderListView.vue'
import { SERVICE_REQUEST_STATUS_LABEL, type Branch, type ServiceRequest, type Vehicle } from '@/types/domain'
import { relativeTime } from '@/utils/format'

/**
 * El cliente no solo mira: desde aquí pide una cita. Antes tenía que llamar al taller, y
 * ese era el único paso del proceso que la aplicación no cubría.
 */
const vehicles = ref<Vehicle[]>([])
const branches = ref<Branch[]>([])
const requests = ref<ServiceRequest[]>([])

const showForm = ref(false)
const saving = ref(false)
const error = ref('')

const form = ref({
  vehicleId: '',
  branchId: '',
  description: '',
  reportedSymptoms: '',
  preferredDate: '',
  mileage: '' as string,
})

async function load() {
  const [v, b, r] = await Promise.all([
    vehiclesApi.list({ pageSize: 50 }),
    branchesApi.list(),
    serviceRequestsApi.list({ pageSize: 20 }),
  ])

  vehicles.value = v.items
  branches.value = b
  requests.value = r.items

  form.value.vehicleId ||= vehicles.value[0]?.id ?? ''
  form.value.branchId ||= branches.value[0]?.id ?? ''
}

onMounted(async () => {
  try {
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar su información.')
  }
})

async function submit() {
  error.value = ''
  saving.value = true

  try {
    await serviceRequestsApi.create({
      branchId: form.value.branchId,
      vehicleId: form.value.vehicleId,
      description: form.value.description.trim(),
      reportedSymptoms: form.value.reportedSymptoms.trim() || undefined,
      // El input date da "2026-08-20"; la API espera un instante, y el taller abre de día.
      preferredDate: form.value.preferredDate ? `${form.value.preferredDate}T09:00:00Z` : undefined,
      mileage: form.value.mileage ? Number(form.value.mileage) : undefined,
    })

    form.value.description = ''
    form.value.reportedSymptoms = ''
    form.value.preferredDate = ''
    form.value.mileage = ''
    showForm.value = false

    requests.value = (await serviceRequestsApi.list({ pageSize: 20 })).items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo enviar el requerimiento.')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <section class="wrap">
    <div class="head">
      <h1>Mis vehículos</h1>
      <button type="button" :disabled="!vehicles.length" @click="showForm = !showForm">
        {{ showForm ? 'Cancelar' : 'Pedir cita' }}
      </button>
    </div>

    <p v-if="error" class="error">{{ error }}</p>

    <form v-if="showForm" class="card" @submit.prevent="submit">
      <div class="grid">
        <label>
          Vehículo
          <select v-model="form.vehicleId" required>
            <option v-for="vehicle in vehicles" :key="vehicle.id" :value="vehicle.id">
              {{ vehicle.brand }} {{ vehicle.model }}{{ vehicle.plate ? ` · ${vehicle.plate}` : '' }}
            </option>
          </select>
        </label>

        <label>
          Sucursal
          <select v-model="form.branchId" required>
            <option v-for="branch in branches" :key="branch.id" :value="branch.id">
              {{ branch.name }}
            </option>
          </select>
        </label>

        <label>
          Fecha preferida
          <input v-model="form.preferredDate" type="date" />
        </label>

        <label>
          Kilometraje
          <input v-model="form.mileage" type="number" min="0" placeholder="Opcional" />
        </label>
      </div>

      <label>
        ¿Qué necesita?
        <textarea
          v-model="form.description"
          rows="2"
          required
          placeholder="Cambio de aceite, revisión de frenos…"
        />
      </label>

      <label>
        ¿Qué ha notado?
        <textarea
          v-model="form.reportedSymptoms"
          rows="2"
          placeholder="Un ruido al frenar, se calienta en el tráfico… (opcional)"
        />
      </label>

      <button type="submit" :disabled="saving || !form.description.trim()">
        {{ saving ? 'Enviando…' : 'Enviar al taller' }}
      </button>
    </form>

    <div v-if="requests.length" class="card requests">
      <h2>Mis solicitudes</h2>
      <ul>
        <li v-for="request in requests" :key="request.id">
          <div>
            <strong>{{ request.vehicleLabel }}</strong>
            <span class="muted"> · {{ relativeTime(request.createdAt) }}</span>
          </div>
          <p class="description">{{ request.description }}</p>
          <div class="muted small">
            {{ SERVICE_REQUEST_STATUS_LABEL[request.status] }}
            <template v-if="request.workOrderNumber">
              · orden {{ request.workOrderNumber }}
            </template>
            <template v-else-if="request.rejectionReason">
              · {{ request.rejectionReason }}
            </template>
          </div>
        </li>
      </ul>
    </div>

    <OrderListView title="En el taller" subtitle="Siga aquí el avance de las reparaciones." />
  </section>
</template>

<style scoped>
.wrap {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

h1,
h2 {
  margin: 0;
}

h2 {
  font-size: 1rem;
}

.card {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
  gap: 0.75rem;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.875rem;
}

.requests ul {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.requests li + li {
  border-top: 1px solid var(--border);
  padding-top: 0.75rem;
}

.description {
  margin: 0.125rem 0;
  font-size: 0.875rem;
}

.small {
  font-size: 0.8125rem;
}

.muted {
  color: var(--text-muted);
}

.error {
  color: var(--danger);
}
</style>

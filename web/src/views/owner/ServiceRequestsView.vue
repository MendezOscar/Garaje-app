<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { errorMessage } from '@/api/client'
import { serviceRequestsApi, usersApi } from '@/api/garaj'
import {
  SERVICE_REQUEST_STATUS_LABEL,
  ServiceRequestStatus,
  type ServiceRequest,
  type User,
} from '@/types/domain'
import { formatDate, relativeTime, whatsappLink } from '@/utils/format'

const router = useRouter()

const requests = ref<ServiceRequest[]>([])
const technicians = ref<User[]>([])
const loading = ref(false)
const error = ref('')
const busyId = ref<string | null>(null)

/** Técnico elegido por requerimiento, para poder asignar en el mismo gesto que se aprueba. */
const chosenTechnician = ref<Record<string, string>>({})

async function load() {
  loading.value = true
  try {
    const result = await serviceRequestsApi.list({ pageSize: 100 })
    requests.value = result.items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar los requerimientos.')
  } finally {
    loading.value = false
  }
}

async function approve(request: ServiceRequest) {
  busyId.value = request.id
  error.value = ''

  try {
    const workOrderId = await serviceRequestsApi.approve(request.id, {
      assignedTechnicianId: chosenTechnician.value[request.id] || undefined,
    })
    // Aprobar lleva directo a la orden recién creada: es el siguiente paso natural.
    await router.push({ name: 'work-order', params: { id: workOrderId } })
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo aprobar el requerimiento.')
  } finally {
    busyId.value = null
  }
}

async function reject(request: ServiceRequest) {
  const reason = window.prompt(`Motivo del rechazo para ${request.customerName}:`)
  if (!reason?.trim()) return

  busyId.value = request.id
  try {
    await serviceRequestsApi.reject(request.id, reason.trim())
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo rechazar el requerimiento.')
  } finally {
    busyId.value = null
  }
}

function techniciansFor(request: ServiceRequest) {
  return technicians.value.filter((t) => t.isActive && t.branchIds.includes(request.branchId))
}

onMounted(async () => {
  technicians.value = await usersApi.list('Technician').catch(() => [])
  await load()
})
</script>

<template>
  <section>
    <h1>Requerimientos</h1>
    <p class="muted">Los pendientes aparecen primero.</p>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="loading" class="muted">Cargando…</p>

    <ul class="list">
      <li v-for="request in requests" :key="request.id" class="card">
        <div class="head">
          <div>
            <strong>{{ request.vehicleLabel }}</strong>
            <div class="muted small">
              {{ request.customerName }} · {{ request.branchName }} ·
              {{ relativeTime(request.createdAt) }}
            </div>
          </div>
          <span class="badge" :data-pending="request.status === ServiceRequestStatus.Pending">
            {{ SERVICE_REQUEST_STATUS_LABEL[request.status] }}
          </span>
        </div>

        <p class="description">{{ request.description }}</p>
        <p v-if="request.reportedSymptoms" class="muted small">
          Síntomas: {{ request.reportedSymptoms }}
        </p>
        <p v-if="request.preferredDate" class="muted small">
          Fecha preferida: {{ formatDate(request.preferredDate) }}
        </p>

        <p v-if="request.rejectionReason" class="rejected">
          Rechazado: {{ request.rejectionReason }}
        </p>

        <div v-if="request.status === ServiceRequestStatus.Pending" class="actions">
          <select v-model="chosenTechnician[request.id]">
            <option value="">Asignar después</option>
            <option v-for="t in techniciansFor(request)" :key="t.id" :value="t.id">
              {{ t.fullName }}
            </option>
          </select>

          <button type="button" :disabled="busyId === request.id" @click="approve(request)">
            Aprobar y abrir orden
          </button>
          <button
            type="button"
            class="secondary"
            :disabled="busyId === request.id"
            @click="reject(request)"
          >
            Rechazar
          </button>
          <a
            :href="whatsappLink(request.customerPhone, `Hola ${request.customerName}, recibimos su solicitud para ${request.vehicleLabel}.`)"
            target="_blank"
            rel="noopener"
          >
            WhatsApp
          </a>
        </div>

        <RouterLink
          v-else-if="request.workOrderId"
          :to="{ name: 'work-order', params: { id: request.workOrderId } }"
        >
          Ver orden {{ request.workOrderNumber }}
        </RouterLink>
      </li>
    </ul>

    <p v-if="!requests.length && !loading" class="muted">No hay requerimientos.</p>
  </section>
</template>

<style scoped>
h1 {
  margin: 0;
}

.muted {
  color: var(--text-muted);
}

.list {
  list-style: none;
  margin: 1rem 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.card {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
}

.badge {
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  background: var(--surface-alt);
  color: var(--text-muted);
  font-size: 0.75rem;
  white-space: nowrap;
}

.badge[data-pending='true'] {
  background: color-mix(in srgb, var(--accent) 18%, transparent);
  color: var(--accent);
}

.description {
  margin: 0;
}

.rejected {
  margin: 0;
  color: var(--danger);
  font-size: 0.875rem;
}

.actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-top: 0.5rem;
}

.secondary {
  background: transparent;
  border-color: var(--border);
  color: var(--text);
}

.small {
  font-size: 0.8125rem;
}

.error {
  color: var(--danger);
}
</style>

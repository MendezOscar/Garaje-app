<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { errorMessage } from '@/api/client'
import { usersApi, workOrdersApi } from '@/api/garaj'
import PhotoGallery from '@/components/PhotoGallery.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import { useAuthStore } from '@/stores/auth'
import {
  VEHICLE_TYPE_LABEL,
  WORK_ORDER_STATUS_LABEL,
  type User,
  type WorkOrderDetail,
  type WorkOrderStatus,
} from '@/types/domain'
import { formatDateTime, relativeTime, whatsappLink } from '@/utils/format'

const route = useRoute()
const auth = useAuthStore()

const order = ref<WorkOrderDetail | null>(null)
const technicians = ref<User[]>([])
const error = ref('')
const busy = ref(false)

const statusNote = ref('')
const noteIsInternal = ref(false)
const newTaskTitle = ref('')

const id = computed(() => route.params.id as string)
const canEdit = computed(() => !auth.isCustomer)

async function load() {
  try {
    order.value = await workOrdersApi.get(id.value)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar la orden.')
  }
}

async function run(action: () => Promise<unknown>) {
  busy.value = true
  error.value = ''
  try {
    await action()
    await load()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

function changeStatus(status: WorkOrderStatus) {
  return run(async () => {
    await workOrdersApi.changeStatus(id.value, {
      status,
      note: statusNote.value || undefined,
      isVisibleToCustomer: !noteIsInternal.value,
    })
    statusNote.value = ''
    noteIsInternal.value = false
  })
}

function addTask() {
  const title = newTaskTitle.value.trim()
  if (!title) return

  return run(async () => {
    await workOrdersApi.addTask(id.value, { title })
    newTaskTitle.value = ''
  })
}

function toggleTask(taskId: string, isDone: boolean) {
  return run(() => workOrdersApi.completeTask(id.value, taskId, { isDone }))
}

function assign(technicianId: string) {
  return run(() => workOrdersApi.assign(id.value, technicianId || null))
}

/** Mensaje pre-armado para que el Dueño avise al cliente sin teclear el estado a mano. */
const whatsapp = computed(() => {
  if (!order.value) return '#'
  const o = order.value
  return whatsappLink(
    o.customerPhone,
    `Hola ${o.customerName}, le escribo del taller sobre su ${o.vehicleLabel}` +
      `${o.plate ? ` (${o.plate})` : ''}. Orden ${o.number}: ${WORK_ORDER_STATUS_LABEL[o.status]}.`,
  )
})

const availableTechnicians = computed(() =>
  technicians.value.filter((t) => t.isActive && t.branchIds.includes(order.value?.branchId ?? '')),
)

onMounted(async () => {
  if (auth.isOwner) technicians.value = await usersApi.list('Technician').catch(() => [])
  await load()
})
</script>

<template>
  <section v-if="order" class="detail">
    <header>
      <div>
        <h1>{{ order.number }}</h1>
        <p class="muted">
          {{ order.branchName }} · abierta {{ relativeTime(order.openedAt) }}
          <template v-if="order.promisedAt"> · prometida {{ formatDateTime(order.promisedAt) }}</template>
        </p>
      </div>
      <StatusBadge :status="order.status" />
    </header>

    <p v-if="error" class="error">{{ error }}</p>

    <div class="grid">
      <div class="col">
        <article class="card">
          <h2>Vehículo y cliente</h2>
          <dl>
            <dt>{{ VEHICLE_TYPE_LABEL[order.vehicleType] }}</dt>
            <dd>{{ order.vehicleLabel }} <span v-if="order.plate" class="plate">{{ order.plate }}</span></dd>
            <dt>Cliente</dt>
            <dd>
              {{ order.customerName }}
              <a :href="whatsapp" target="_blank" rel="noopener" class="wa">WhatsApp</a>
            </dd>
            <dt>Kilometraje al ingreso</dt>
            <dd>{{ order.mileageIn?.toLocaleString('es-HN') ?? '—' }}</dd>
          </dl>
        </article>

        <article class="card">
          <h2>Motivo de ingreso</h2>
          <p>{{ order.description }}</p>
          <template v-if="order.diagnosis">
            <h3>Diagnóstico</h3>
            <p>{{ order.diagnosis }}</p>
          </template>
        </article>

        <article class="card">
          <h2>Pasos de la reparación</h2>

          <ul class="tasks">
            <li v-for="task in order.tasks" :key="task.id">
              <label>
                <input
                  type="checkbox"
                  :checked="task.isDone"
                  :disabled="!canEdit || busy"
                  @change="toggleTask(task.id, ($event.target as HTMLInputElement).checked)"
                />
                <span :class="{ done: task.isDone }">{{ task.title }}</span>
              </label>
              <span class="muted small">
                {{ task.assignedTechnicianName ?? 'sin asignar' }}
                <template v-if="task.actualHours"> · {{ task.actualHours }} h</template>
              </span>
            </li>
          </ul>

          <p v-if="!order.tasks.length" class="muted">Todavía no hay pasos registrados.</p>

          <form v-if="canEdit" class="inline" @submit.prevent="addTask">
            <input v-model="newTaskTitle" placeholder="Agregar paso…" />
            <button type="submit" :disabled="busy || !newTaskTitle.trim()">Agregar</button>
          </form>
        </article>

        <PhotoGallery :work-order-id="order.id" :can-edit="canEdit" />
      </div>

      <div class="col">
        <article v-if="auth.isOwner" class="card">
          <h2>Técnico responsable</h2>
          <select
            :value="order.assignedTechnicianId ?? ''"
            :disabled="busy"
            @change="assign(($event.target as HTMLSelectElement).value)"
          >
            <option value="">Sin asignar</option>
            <option v-for="t in availableTechnicians" :key="t.id" :value="t.id">
              {{ t.fullName }}
            </option>
          </select>
        </article>

        <article v-if="canEdit && order.allowedNextStatuses.length" class="card">
          <h2>Cambiar estado</h2>
          <input v-model="statusNote" placeholder="Nota para la línea de tiempo (opcional)" />
          <label class="checkbox">
            <input v-model="noteIsInternal" type="checkbox" />
            Nota interna: el cliente no la ve
          </label>
          <div class="actions">
            <button
              v-for="next in order.allowedNextStatuses"
              :key="next"
              type="button"
              :disabled="busy"
              @click="changeStatus(next)"
            >
              {{ WORK_ORDER_STATUS_LABEL[next] }}
            </button>
          </div>
        </article>

        <article class="card">
          <h2>Línea de tiempo</h2>
          <ol class="timeline">
            <li v-for="(entry, i) in order.timeline" :key="i">
              <div class="dot" />
              <div>
                <strong>{{ WORK_ORDER_STATUS_LABEL[entry.toStatus] }}</strong>
                <span v-if="!entry.isVisibleToCustomer" class="internal">interna</span>
                <div class="muted small">
                  {{ formatDateTime(entry.changedAt) }} · {{ entry.changedByName }}
                </div>
                <p v-if="entry.note">{{ entry.note }}</p>
              </div>
            </li>
          </ol>
        </article>
      </div>
    </div>
  </section>

  <p v-else-if="error" class="error">{{ error }}</p>
  <p v-else class="muted">Cargando…</p>
</template>

<style scoped>
header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

header h1 {
  margin: 0;
}

header p {
  margin: 0.25rem 0 0;
  font-size: 0.875rem;
}

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(20rem, 1fr));
  gap: 1rem;
  align-items: start;
}

.col {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  min-width: 0;
}

.card {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.card h2 {
  margin: 0 0 0.75rem;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.card h3 {
  margin: 1rem 0 0.25rem;
  font-size: 0.875rem;
}

.card p {
  margin: 0;
}

dl {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.375rem 1rem;
  margin: 0;
}

dt {
  color: var(--text-muted);
  font-size: 0.875rem;
}

dd {
  margin: 0;
}

.plate {
  padding: 0 0.25rem;
  border: 1px solid var(--border);
  border-radius: 3px;
  font-family: ui-monospace, monospace;
  font-size: 0.8125rem;
}

.wa {
  margin-left: 0.5rem;
  font-size: 0.8125rem;
}

.tasks {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.tasks li {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.tasks label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
}

.done {
  text-decoration: line-through;
  color: var(--text-muted);
}

.inline {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

.inline input {
  flex: 1;
  min-width: 0;
}

.checkbox {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin: 0.5rem 0;
  font-size: 0.875rem;
  color: var(--text-muted);
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.timeline {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.875rem;
}

.timeline li {
  display: flex;
  gap: 0.625rem;
}

.dot {
  flex: none;
  width: 8px;
  height: 8px;
  margin-top: 0.375rem;
  border-radius: 50%;
  background: var(--accent);
}

.internal {
  margin-left: 0.375rem;
  padding: 0 0.375rem;
  border-radius: 999px;
  background: var(--surface-alt);
  color: var(--text-muted);
  font-size: 0.6875rem;
}

.small {
  font-size: 0.75rem;
}

.muted {
  color: var(--text-muted);
}

.error {
  color: var(--danger);
}

select,
.inline input {
  width: 100%;
}
</style>

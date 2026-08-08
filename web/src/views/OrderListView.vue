<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { workOrdersApi } from '@/api/garaj'
import StatusBadge from '@/components/StatusBadge.vue'
import { VEHICLE_TYPE_LABEL, type WorkOrderListItem } from '@/types/domain'
import { relativeTime } from '@/utils/format'

/**
 * Lista de órdenes compartida por el Técnico ("mis asignaciones") y el Cliente ("mis
 * vehículos"). El backend ya devuelve solo lo que a cada perfil le corresponde, así que la
 * pantalla es la misma y solo cambia el título.
 */
const props = defineProps<{ title: string; subtitle: string }>()

const orders = ref<WorkOrderListItem[]>([])
const loading = ref(true)
const error = ref('')

onMounted(async () => {
  try {
    orders.value = (await workOrdersApi.list({ pageSize: 100 })).items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar las órdenes.')
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <section>
    <h1>{{ props.title }}</h1>
    <p class="muted">{{ props.subtitle }}</p>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="loading" class="muted">Cargando…</p>

    <ul class="list">
      <li v-for="order in orders" :key="order.id">
        <RouterLink class="card" :to="{ name: 'work-order', params: { id: order.id } }">
          <div class="head">
            <strong>{{ order.number }}</strong>
            <StatusBadge :status="order.status" />
          </div>

          <div>
            {{ order.vehicleLabel }}
            <span v-if="order.plate" class="plate">{{ order.plate }}</span>
          </div>

          <div class="muted small">
            {{ VEHICLE_TYPE_LABEL[order.vehicleType] }} · {{ order.branchName }} ·
            {{ relativeTime(order.openedAt) }}
          </div>

          <p class="description">{{ order.description }}</p>

          <div v-if="order.taskCount" class="muted small">
            {{ order.tasksDone }} de {{ order.taskCount }} pasos completados
          </div>
        </RouterLink>
      </li>
    </ul>

    <p v-if="!orders.length && !loading" class="muted">Nada por aquí todavía.</p>
  </section>
</template>

<style scoped>
h1 {
  margin: 0;
}

.list {
  list-style: none;
  margin: 1rem 0 0;
  padding: 0;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(18rem, 1fr));
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
  color: inherit;
  text-decoration: none;
}

.card:hover {
  border-color: var(--accent);
  text-decoration: none;
}

.head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.5rem;
}

.plate {
  padding: 0 0.25rem;
  border: 1px solid var(--border);
  border-radius: 3px;
  font-family: ui-monospace, monospace;
  font-size: 0.8125rem;
}

.description {
  margin: 0;
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

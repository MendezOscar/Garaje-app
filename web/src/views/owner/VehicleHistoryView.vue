<script setup lang="ts">
import { computed, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { workOrdersApi } from '@/api/garaj'
import EmptyState from '@/components/EmptyState.vue'
import ErrorNote from '@/components/ErrorNote.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import type { WorkOrderListItem } from '@/types/domain'
import { formatDate } from '@/utils/format'

/**
 * Todo lo que se le ha hecho a un vehículo, buscándolo por placa o por cliente.
 *
 * Es la pregunta del mostrador cuando el cliente vuelve: «¿qué le hicieron la vez pasada?».
 * Hasta hoy había que encontrar primero una orden suya para llegar a su historial, y si la
 * última visita fue hace un año no aparecía en ninguna lista.
 */
const search = ref('')
const buscado = ref('')
const visitas = ref<WorkOrderListItem[]>([])
const loading = ref(false)
const error = ref('')

/** El vehículo elegido cuando la búsqueda trae varios. */
const vehiculoId = ref<string | null>(null)

/**
 * Los vehículos distintos que trajo la búsqueda. Mezclar la moto y el carro del mismo cliente
 * en una sola lista no es el historial de ninguno de los dos.
 */
const vehiculos = computed(() => {
  const porVehiculo = new Map<string, WorkOrderListItem>()
  for (const visita of visitas.value) {
    if (!porVehiculo.has(visita.vehicleId)) porVehiculo.set(visita.vehicleId, visita)
  }
  return [...porVehiculo.values()]
})

const elegido = computed(() =>
  vehiculoId.value ? vehiculos.value.find((v) => v.vehicleId === vehiculoId.value) : null,
)

/** Lo que se pinta: las visitas del vehículo elegido, o todas, de la más nueva a la más vieja. */
const lista = computed(() =>
  [...visitas.value]
    .filter((v) => !vehiculoId.value || v.vehicleId === vehiculoId.value)
    .sort((a, b) => new Date(b.openedAt).getTime() - new Date(a.openedAt).getTime()),
)

async function buscar() {
  const texto = search.value.trim()
  if (!texto) return

  loading.value = true
  error.value = ''
  vehiculoId.value = null
  try {
    // Sin filtrar por estado: el historial es justamente lo ya entregado.
    const page = await workOrdersApi.list({ search: texto, onlyOpen: false, pageSize: 100 })
    visitas.value = page.items
    buscado.value = texto
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el historial.')
  } finally {
    loading.value = false
  }
}

function limpiar() {
  search.value = ''
  buscado.value = ''
  visitas.value = []
  vehiculoId.value = null
}
</script>

<template>
  <section class="historial">
    <header class="encabezado">
      <div>
        <h1>Historial</h1>
        <p class="muted small">
          Qué se le ha hecho a un vehículo, visita por visita. Se busca por placa, por cliente
          o por número de orden.
        </p>
      </div>
    </header>

    <form class="buscador" @submit.prevent="buscar">
      <input v-model="search" placeholder="Placa, cliente o número de orden" />
      <button type="submit" :disabled="loading || !search.trim()">Buscar</button>
      <button v-if="buscado" type="button" class="btn-ghost" @click="limpiar">Limpiar</button>
    </form>

    <ErrorNote v-if="error" :message="error" />
    <SkeletonList v-if="loading" :rows="4" variant="lista" />

    <template v-else-if="buscado">
      <EmptyState
        v-if="!visitas.length"
        title="Ninguna visita con esa placa ni ese cliente"
        hint="Pruebe con el nombre del cliente, o con parte de la placa."
      />

      <template v-else>
        <!-- Varios vehículos: primero se elige cuál. -->
        <div v-if="vehiculos.length > 1" class="vehiculos">
          <button
            v-for="v in vehiculos"
            :key="v.vehicleId"
            type="button"
            class="chip"
            :class="{ activo: vehiculoId === v.vehicleId }"
            @click="vehiculoId = vehiculoId === v.vehicleId ? null : v.vehicleId"
          >
            {{ v.vehicleLabel }}
            <span class="muted">{{ v.plate ?? 'sin placa' }} · {{ v.customerName }}</span>
          </button>
        </div>

        <p class="muted small">
          {{ lista.length }} {{ lista.length === 1 ? 'visita' : 'visitas' }}
          <template v-if="elegido"> de {{ elegido.vehicleLabel }}</template>
        </p>

        <table class="tabla">
          <thead>
            <tr>
              <th>Orden</th>
              <th>Entró</th>
              <th>Vehículo</th>
              <th>Qué se hizo</th>
              <th>Técnico</th>
              <th>Estado</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="visita in lista" :key="visita.id">
              <td>
                <RouterLink :to="{ name: 'work-order', params: { id: visita.id } }">
                  {{ visita.number }}
                </RouterLink>
              </td>
              <td>{{ formatDate(visita.openedAt) }}</td>
              <td>
                {{ visita.vehicleLabel }}
                <span class="muted small">{{ visita.plate ?? 'sin placa' }}</span>
              </td>
              <td>{{ visita.description }}</td>
              <td>{{ visita.assignedTechnicianName ?? '—' }}</td>
              <td><StatusBadge :status="visita.status" /></td>
            </tr>
          </tbody>
        </table>
      </template>
    </template>

    <EmptyState
      v-else
      title="Busque un vehículo"
      hint="Escriba la placa o el nombre del cliente y verá todo lo que se le ha hecho."
    />
  </section>
</template>

<style scoped>
.historial {
  display: grid;
  gap: var(--space-4);
}

.buscador {
  display: flex;
  gap: var(--space-2);
  flex-wrap: wrap;
}

.buscador input {
  flex: 1 1 18rem;
}

.vehiculos {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}

.chip {
  display: grid;
  gap: 2px;
  text-align: left;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  padding: var(--space-2) var(--space-3);
}

.chip.activo {
  border-color: var(--brand);
  background: var(--surface-alt);
}
</style>

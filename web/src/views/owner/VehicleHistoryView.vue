<script setup lang="ts">
import { computed, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { historyApi, workOrdersApi } from '@/api/garaj'
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

/** El id de lo que entró: el vehículo o el equipo, según con qué trabaje el taller. */
function idDe(visita: WorkOrderListItem) {
  return visita.asset?.id ?? visita.vehicleId ?? visita.id
}

/**
 * Lo distinto que trajo la búsqueda. Mezclar la moto y el carro del mismo cliente en una sola
 * lista no es el historial de ninguno de los dos, y con equipos pasa igual.
 */
const vehiculos = computed(() => {
  const porVehiculo = new Map<string, WorkOrderListItem>()
  for (const visita of visitas.value) {
    if (!porVehiculo.has(idDe(visita))) porVehiculo.set(idDe(visita), visita)
  }
  return [...porVehiculo.values()]
})

const elegido = computed(() =>
  vehiculoId.value ? vehiculos.value.find((v) => idDe(v) === vehiculoId.value) : null,
)

/** Lo que se pinta: las visitas de lo elegido, o todas, de la más nueva a la más vieja. */
const lista = computed(() =>
  [...visitas.value]
    .filter((v) => !vehiculoId.value || idDe(v) === vehiculoId.value)
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

/**
 * Le manda el historial al cliente por WhatsApp. El enlace es el suyo —el mismo del estado
 * de cuenta— así que le enseña todo lo que trae al taller, no solo el vehículo que se está
 * mirando aquí.
 *
 * La pestaña se abre antes de pedir el enlace: abrirla después, ya con la respuesta, cae en
 * el bloqueador de ventanas emergentes.
 */
async function mandarHistorial(customerId: string) {
  const tab = window.open('', '_blank')
  error.value = ''

  try {
    const link = await historyApi.whatsappLink(customerId)
    if (tab) tab.location.href = link.url
    else window.location.href = link.url
  } catch (e) {
    tab?.close()
    error.value = errorMessage(e, 'No se pudo armar el enlace del historial.')
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
            :key="idDe(v)"
            type="button"
            class="chip"
            :class="{ activo: vehiculoId === idDe(v) }"
            @click="vehiculoId = vehiculoId === idDe(v) ? null : idDe(v)"
          >
            {{ v.vehicleLabel }}
            <span class="muted">{{ v.plate ?? 'sin placa' }} · {{ v.customerName }}</span>
          </button>
        </div>

        <div class="resumen">
          <p class="muted small">
            {{ lista.length }} {{ lista.length === 1 ? 'visita' : 'visitas' }}
            <template v-if="elegido"> de {{ elegido.vehicleLabel }}</template>
          </p>
          <!-- Es la pregunta de después de enseñarle el historial en pantalla: «¿me lo puede
               mandar?». Va aquí, donde ya se está mirando. -->
          <button
            v-if="lista[0]?.customerId"
            type="button"
            class="btn-ghost"
            @click="mandarHistorial(lista[0].customerId)"
          >
            Mandárselo al cliente
          </button>
        </div>

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
.resumen {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  flex-wrap: wrap;
}

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

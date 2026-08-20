<script setup lang="ts">
import { ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { errorMessage } from '@/api/client'
import { workOrdersApi } from '@/api/garaj'
import NewServiceRequestForm from '@/components/NewServiceRequestForm.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import {
  WorkOrderStatus,
  type Vehicle,
  type WorkOrderDetail,
  type WorkOrderListItem,
} from '@/types/domain'
import { formatDate } from '@/utils/format'

/**
 * Recibir un vehículo: la hoja de recepción en una pantalla.
 *
 * Hasta hoy el único camino en el panel era Requerimientos → «Aprobar y abrir orden»: dos
 * pantallas y una aprobación para la acción más frecuente del mostrador, cuando el vehículo ya
 * está en el patio. `POST /api/work-orders` existía y era del Dueño; lo que faltaba era la
 * puerta.
 *
 * Al lado de la hoja va lo que el mostrador necesita saber sin preguntar: qué va a pasar al
 * darle a «Abrir orden» y qué se le ha hecho antes a ese vehículo. Es la información que
 * evita abrir la orden dos veces o repetir un trabajo que ya se hizo el mes pasado.
 */
const router = useRouter()

const vehiculo = ref<Vehicle | null>(null)
const tecnico = ref<string | null>(null)

/** Las visitas anteriores del vehículo elegido, la última primero. */
const visitas = ref<WorkOrderListItem[]>([])
const totalVisitas = ref(0)
const buscandoVisitas = ref(false)

/** Si al abrir la orden se le manda el enlace de seguimiento. */
const avisar = ref(true)

/** La orden recién abierta, mientras se le manda el enlace. */
const abiertaAhora = ref<WorkOrderDetail | null>(null)
const requerimiento = ref(false)
const error = ref('')

function contexto(elegido: { vehicle: Vehicle | null; technician: string | null }) {
  vehiculo.value = elegido.vehicle
  tecnico.value = elegido.technician
}

watch(vehiculo, async (v) => {
  visitas.value = []
  totalVisitas.value = 0
  if (!v) return

  buscandoVisitas.value = true
  try {
    const page = await workOrdersApi.list({ vehicleId: v.id, onlyOpen: false, pageSize: 4 })
    visitas.value = page.items
    totalVisitas.value = page.total
  } catch {
    // Es un dato de apoyo: si falla, la hoja sigue sirviendo para recibir el vehículo.
  } finally {
    buscandoVisitas.value = false
  }
})

/**
 * Al abrir la orden no se sale de la pantalla si toca mandar el enlace: la pestaña de WhatsApp
 * tiene que abrirse dentro del clic que la pide o Safari la bloquea, así que se ofrece el
 * botón aquí y se entra a la orden después.
 */
function abierta(orden: WorkOrderDetail | null) {
  if (!orden) {
    requerimiento.value = true
    return
  }

  requerimiento.value = false
  if (avisar.value) abiertaAhora.value = orden
  else router.push({ name: 'work-order', params: { id: orden.id } })
}

async function mandarEnlace() {
  const orden = abiertaAhora.value
  if (!orden) return

  const tab = window.open('', '_blank')
  error.value = ''
  try {
    const link = await workOrdersApi.whatsappLink(orden.id, 'received')
    if (tab) tab.location.href = link.url
    else window.location.href = link.url
    await router.push({ name: 'work-order', params: { id: orden.id } })
  } catch (e) {
    tab?.close()
    error.value = errorMessage(e, 'No se pudo armar el enlace.')
  }
}
</script>

<template>
  <section>
    <header>
      <RouterLink :to="{ name: 'work-orders' }" class="volver" aria-label="Volver al tablero">
        ‹
      </RouterLink>
      <div>
        <h1>Recibir vehículo</h1>
        <p class="muted small">
          Se abre la orden y queda en Recibida. Sin pasar por
          <RouterLink :to="{ name: 'service-requests' }">requerimientos</RouterLink>.
        </p>
      </div>
    </header>

    <div class="hoja-y-lado">
      <article class="marco">
        <NewServiceRequestForm
          mode="order"
          variant="page"
          @created="abierta"
          @contexto="contexto"
        />
      </article>

      <aside>
        <p v-if="error" class="error">{{ error }}</p>

        <!-- Ya se abrió: lo único que queda es mandarle el enlace, y eso pide un clic del
             mostrador para que el navegador deje abrir WhatsApp. -->
        <article v-if="abiertaAhora" class="card recien">
          <h2>Se abrió la orden</h2>
          <p class="folio">
            <strong class="num">{{ abiertaAhora.number }}</strong>
            <StatusBadge :status="abiertaAhora.status" />
          </p>
          <p class="muted small">
            Le queda mandar el enlace de seguimiento. El cliente lo abre sin instalar nada.
          </p>
          <div class="acciones">
            <button type="button" @click="mandarEnlace">Mandar el enlace</button>
            <RouterLink
              class="boton-suave"
              :to="{ name: 'work-order', params: { id: abiertaAhora.id } }"
            >
              Ir a la orden
            </RouterLink>
          </div>
        </article>

        <article v-else-if="requerimiento" class="card recien">
          <h2>Quedó como requerimiento</h2>
          <p class="muted small">
            No se abrió ninguna orden. Cuando traiga el vehículo se aprueba y la orden nace con
            todo lo que ya se escribió.
          </p>
          <div class="acciones">
            <RouterLink class="boton-suave" :to="{ name: 'service-requests' }">
              Ver requerimientos
            </RouterLink>
          </div>
        </article>

        <template v-else>
          <article class="card">
            <h2>Lo que va a pasar</h2>
            <ol>
              <li>
                Se abre la orden en
                <StatusBadge :status="WorkOrderStatus.Received" />
              </li>
              <li v-if="tecnico">Queda con {{ tecnico }} como técnico</li>
              <li v-else>Queda sin técnico: se asigna desde el tablero</li>
              <li>Se le manda al cliente el enlace de seguimiento por WhatsApp</li>
            </ol>
            <label class="checkbox">
              <input v-model="avisar" type="checkbox" />
              Mandar el enlace ahora
            </label>
          </article>

          <article class="card">
            <h2>Este vehículo antes</h2>
            <p v-if="!vehiculo" class="muted small">
              Elija el vehículo y aquí sale lo que se le ha hecho.
            </p>
            <p v-else-if="buscandoVisitas" class="muted small">Buscando…</p>
            <template v-else-if="visitas.length">
              <ul class="visitas">
                <li v-for="previa in visitas" :key="previa.id">
                  <RouterLink :to="{ name: 'work-order', params: { id: previa.id } }">
                    {{ previa.number }}
                  </RouterLink>
                  <span class="muted"> · {{ previa.description }}</span>
                  <span class="cuando muted small">{{ formatDate(previa.openedAt) }}</span>
                </li>
              </ul>
              <p v-if="totalVisitas > visitas.length" class="muted small">
                Y {{ totalVisitas - visitas.length }} visita(s) más.
              </p>
              <p v-if="vehiculo.mileage" class="muted small">
                La última lectura fue {{ vehiculo.mileage.toLocaleString('es-HN') }} km.
              </p>
            </template>
            <p v-else class="muted small">Primera vez en el taller.</p>
          </article>
        </template>
      </aside>
    </div>
  </section>
</template>

<style scoped>
header {
  display: flex;
  align-items: flex-start;
  gap: 0.625rem;
  margin-bottom: 1.25rem;
}

header h1 {
  margin: 0 0 0.25rem;
}

.volver {
  padding: 0 0.375rem;
  font-size: 1.5rem;
  line-height: 1.2;
  color: var(--text-muted);
}

.volver:hover {
  color: var(--accent);
  text-decoration: none;
}

/* La hoja a la izquierda y ancha; al lado, lo que solo se consulta. */
.hoja-y-lado {
  display: grid;
  grid-template-columns: minmax(0, 1.6fr) minmax(0, 1fr);
  gap: 1rem;
  align-items: start;
}

@media (max-width: 60rem) {
  .hoja-y-lado {
    grid-template-columns: 1fr;
  }
}

.marco,
.card {
  padding: 1.25rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

aside {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  min-width: 0;
}

.card {
  padding: 1rem;
}

.card h2 {
  margin: 0 0 0.625rem;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.recien {
  border-color: var(--accent);
}

.folio {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin: 0 0 0.375rem;
  font-size: 1.125rem;
}

ol {
  margin: 0;
  padding-left: 1.125rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  font-size: 0.9375rem;
}

.checkbox {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-top: 0.875rem;
  font-size: 0.875rem;
}

.visitas {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  font-size: 0.875rem;
}

.visitas li {
  display: flex;
  align-items: baseline;
  gap: 0.25rem;
  min-width: 0;
}

.visitas a {
  white-space: nowrap;
}

.visitas li > span:first-of-type {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.cuando {
  margin-left: auto;
  white-space: nowrap;
}

.acciones {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

.boton-suave {
  padding: 0.5rem 0.875rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text);
  font-size: 0.9375rem;
  font-weight: 500;
}

.boton-suave:hover {
  border-color: var(--accent);
  color: var(--accent);
  text-decoration: none;
}

.num {
  font-variant-numeric: tabular-nums;
}

.muted {
  color: var(--text-muted);
}

.small {
  font-size: 0.875rem;
}

.error {
  margin: 0;
  color: var(--danger);
}
</style>

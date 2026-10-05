<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { errorMessage } from '@/api/client'
import { claimsApi, salesApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import {
  CLAIM_STATUS_LABEL,
  ClaimStatus,
  type ClaimDetail,
  type ClaimListItem,
  type SaleListItem,
} from '@/types/domain'
import { formatDate, formatMoney } from '@/utils/format'

/**
 * Reclamos: el cliente volvió diciendo que el trabajo quedó mal.
 *
 * Los abiertos arriba porque son los que hay que atender. Lo que se busca al abrir esta
 * pantalla es una de dos cosas: anotar el que acaba de entrar por la puerta, o cerrar el que
 * quedó pendiente de la semana pasada.
 */
const claims = ref<ClaimListItem[]>([])
const router = useRouter()
const selected = ref<ClaimDetail | null>(null)
const soloAbiertos = ref(true)

const error = ref('')
const loading = ref(false)
const busy = ref(false)

/** El trabajo sobre el que se reclama. Se busca por número de venta, orden o cliente. */
const buscaTrabajo = ref('')
const trabajos = ref<SaleListItem[]>([])
const nuevo = ref({ saleId: '', reason: '' })

const resolucion = ref({
  status: ClaimStatus.RepairedUnderWarranty as ClaimStatus,
  resolution: '',
  openRepairOrder: false,
})

const trabajoElegido = computed(() => trabajos.value.find((s) => s.id === nuevo.value.saleId))

async function load() {
  loading.value = true
  error.value = ''
  try {
    claims.value = (await claimsApi.list({ onlyOpen: soloAbiertos.value, pageSize: 50 })).items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar los reclamos.')
  } finally {
    loading.value = false
  }
}

async function abrir(id: string) {
  error.value = ''
  try {
    selected.value = await claimsApi.get(id)
    resolucion.value = {
      status: selected.value.wasUnderWarranty
        ? ClaimStatus.RepairedUnderWarranty
        : ClaimStatus.RepairedAndCharged,
      resolution: selected.value.resolution ?? '',
      openRepairOrder: false,
    }
  } catch (e) {
    error.value = errorMessage(e)
  }
}

async function run(action: () => Promise<ClaimDetail | void>) {
  busy.value = true
  error.value = ''
  try {
    const result = await action()
    if (result) selected.value = result
    await load()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

/** Busca el trabajo facturado: por número de venta, de orden, o por cliente. */
async function buscar() {
  const texto = buscaTrabajo.value.trim()
  if (texto.length < 2) {
    trabajos.value = []
    return
  }
  try {
    trabajos.value = (await salesApi.list({ search: texto, pageSize: 8 })).items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo buscar el trabajo.')
  }
}

function crear() {
  if (!nuevo.value.saleId || !nuevo.value.reason.trim()) return

  return run(async () => {
    const creado = await claimsApi.create({
      saleId: nuevo.value.saleId,
      reason: nuevo.value.reason.trim(),
    })
    nuevo.value = { saleId: '', reason: '' }
    buscaTrabajo.value = ''
    trabajos.value = []
    return creado
  })
}

/**
 * Abre la orden con la que se va a reparar y lleva a ella.
 *
 * Lleva, no se queda: lo siguiente que se hace con esa orden es recibir el vehículo y
 * diagnosticarlo, y era un clic de más buscarla en la lista de órdenes.
 */
async function abrirOrden() {
  if (!selected.value) return

  const actualizado = await claimsApi
    .openRepairOrder(selected.value.id)
    .catch((e: unknown) => {
      error.value = errorMessage(e)
      return null
    })

  if (!actualizado?.repairWorkOrderId) return
  await router.push({ name: 'work-order', params: { id: actualizado.repairWorkOrderId } })
}

function resolver() {
  if (!selected.value || !resolucion.value.resolution.trim()) return

  return run(() =>
    claimsApi.resolve(selected.value!.id, {
      status: resolucion.value.status,
      resolution: resolucion.value.resolution.trim(),
      openRepairOrder: resolucion.value.openRepairOrder,
    }),
  )
}

function reabrir() {
  if (!selected.value) return
  return run(() => claimsApi.reopen(selected.value!.id))
}

onMounted(load)
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Reclamos</h1>
        <p class="muted small">
          Cuando el cliente vuelve diciendo que el trabajo quedó mal. Queda escrito qué pasó,
          qué se hizo y si entró en garantía.
        </p>
      </div>
      <label class="checkbox">
        <input v-model="soloAbiertos" type="checkbox" @change="load" />
        Solo los abiertos
      </label>
    </header>

    <ErrorNote v-if="error" :message="error" />

    <div class="dos-columnas">
      <article class="card">
        <h2>Anotar un reclamo</h2>

        <template v-if="!nuevo.saleId">
          <input
            v-model="buscaTrabajo"
            type="search"
            placeholder="Número de factura, de orden, o nombre del cliente"
            @input="buscar"
          />
          <ul v-if="trabajos.length" class="resultados">
            <li v-for="venta in trabajos" :key="venta.id">
              <button type="button" class="resultado" @click="nuevo.saleId = venta.id">
                <span>{{ venta.number }}</span>
                <span class="muted small">
                  {{ venta.customerName ?? 'Sin cliente' }} · {{ formatDate(venta.saleDate) }}
                  <template v-if="venta.warrantyUntil">
                    · en garantía hasta el {{ formatDate(venta.warrantyUntil) }}
                  </template>
                </span>
              </button>
            </li>
          </ul>
          <p v-else-if="buscaTrabajo.trim().length >= 2" class="muted small">
            No se encontró ese trabajo.
          </p>
        </template>

        <template v-else>
          <p class="elegido">
            <strong>{{ trabajoElegido?.number }}</strong>
            <span class="muted small"> · {{ trabajoElegido?.customerName ?? 'Sin cliente' }}</span>
            <button
              type="button"
              class="quitar"
              aria-label="Quitar la factura elegida"
              @click="nuevo.saleId = ''"
            >
              <span aria-hidden="true">×</span>
            </button>
          </p>
          <textarea
            v-model="nuevo.reason"
            rows="3"
            maxlength="2000"
            placeholder="Qué dice el cliente que pasó, con sus palabras"
          ></textarea>
          <button type="button" :disabled="busy || !nuevo.reason.trim()" @click="crear">
            Anotar el reclamo
          </button>
        </template>
      </article>

      <article class="card">
        <h2>{{ soloAbiertos ? 'Abiertos' : 'Todos' }}</h2>
        <SkeletonList v-if="loading" :rows="4" variant="lista" />
        <p v-else-if="!claims.length" class="muted small">
          {{ soloAbiertos ? 'No hay reclamos abiertos.' : 'Todavía no hay reclamos.' }}
        </p>
        <ul v-else class="lista">
          <li v-for="claim in claims" :key="claim.id">
            <button type="button" class="resultado" @click="abrir(claim.id)">
              <span>
                {{ claim.number }}
                <em class="estado" :class="{ abierto: claim.status === ClaimStatus.Open }">
                  {{ CLAIM_STATUS_LABEL[claim.status] }}
                </em>
              </span>
              <span class="muted small">
                {{ claim.customerName ?? 'Sin cliente' }} · {{ claim.saleNumber }} ·
                {{ formatDate(claim.receivedAt) }}
                <template v-if="claim.wasUnderWarranty"> · estaba en garantía</template>
              </span>
            </button>
          </li>
        </ul>
      </article>
    </div>

    <article v-if="selected" class="card detalle">
      <header class="top">
        <div>
          <h2>{{ selected.number }}</h2>
          <p class="muted small">
            {{ selected.saleNumber }}
            <template v-if="selected.workOrderNumber"> · orden {{ selected.workOrderNumber }}</template>
            <template v-if="selected.vehicleLabel"> · {{ selected.vehicleLabel }}</template>
          </p>
        </div>
        <button type="button" class="quitar" aria-label="Cerrar" @click="selected = null">
          <span aria-hidden="true">×</span>
        </button>
      </header>

      <dl class="cuentas">
        <dt>Estado</dt>
        <dd>{{ CLAIM_STATUS_LABEL[selected.status] }}</dd>
        <dt>Lo que dice el cliente</dt>
        <dd>{{ selected.reason }}</dd>
        <dt>Garantía</dt>
        <dd>
          <template v-if="selected.wasUnderWarranty">
            Sí, estaba en garantía cuando se recibió el reclamo
            <template v-if="selected.warrantyUntil">
              (hasta el {{ formatDate(selected.warrantyUntil) }})
            </template>
          </template>
          <template v-else>No estaba en garantía</template>
        </dd>
        <dt>Recibido</dt>
        <dd>
          {{ formatDate(selected.receivedAt) }}
          <template v-if="selected.receivedByName"> por {{ selected.receivedByName }}</template>
        </dd>
        <template v-if="selected.resolution">
          <dt>Qué se hizo</dt>
          <dd>{{ selected.resolution }}</dd>
        </template>
        <template v-if="selected.repairWorkOrderNumber">
          <dt>Orden de la reparación</dt>
          <dd>
            <RouterLink :to="{ name: 'work-order', params: { id: selected.repairWorkOrderId } }">
              {{ selected.repairWorkOrderNumber }}
            </RouterLink>
            · costó {{ formatMoney(selected.repairCost) }}
            <div class="muted small">
              {{
                selected.repairWarrantyCovered === null
                  ? 'Falta decir si la cubre la garantía. Se decide en esa orden, con el diagnóstico hecho.'
                  : selected.repairWarrantyCovered
                    ? 'La paga el taller: no se le factura al cliente.'
                    : 'Es un servicio nuevo: se le cobra al cliente.'
              }}
            </div>
          </dd>
        </template>
      </dl>

      <!-- Abrir la orden sin cerrar el reclamo. Es el orden real de las cosas: primero entra
           el carro y se repara, y hasta que se sabe qué pasó se cierra el reclamo. -->
      <div v-if="selected.status === ClaimStatus.Open && !selected.repairWorkOrderId" class="abrir">
        <button type="button" :disabled="busy" @click="abrirOrden">
          Abrir la orden de reparación
        </button>
        <p class="muted small">
          Recibe el vehículo con su propia orden, sus pasos y sus repuestos. Si la cubre la
          garantía se decide ahí, con el diagnóstico hecho: cubierta la paga el taller, y si no,
          se cobra como un servicio nuevo.
        </p>
      </div>

      <form v-if="selected.status === ClaimStatus.Open" class="resolver" @submit.prevent="resolver">
        <h3>Cerrar el reclamo</h3>
        <label>
          Cómo termina
          <select v-model.number="resolucion.status">
            <option :value="ClaimStatus.RepairedUnderWarranty">Reparado en garantía, sin cobrar</option>
            <option :value="ClaimStatus.RepairedAndCharged">Reparado y cobrado</option>
            <option :value="ClaimStatus.Rejected">No procede</option>
          </select>
        </label>
        <label>
          Qué se hizo
          <textarea v-model="resolucion.resolution" rows="3" maxlength="2000"></textarea>
        </label>
        <template v-if="!selected.repairWorkOrderId">
          <label class="checkbox">
            <input v-model="resolucion.openRepairOrder" type="checkbox" />
            Abrir una orden para la reparación
          </label>
          <p class="muted small">
            La orden va ligada a este reclamo y lleva sus propios pasos y repuestos. Es lo que
            después dice cuánto le costó la garantía al taller.
          </p>
        </template>
        <button type="submit" :disabled="busy || !resolucion.resolution.trim()">
          Cerrar el reclamo
        </button>
      </form>

      <p v-else class="muted small">
        Cerrado el {{ formatDate(selected.resolvedAt!) }}
        <template v-if="selected.resolvedByName"> por {{ selected.resolvedByName }}</template>.
        <button type="button" class="link" :disabled="busy" @click="reabrir">Reabrir</button>
      </p>
    </article>
  </section>
</template>

<style scoped>
.abrir {
  display: grid;
  gap: var(--space-2);
  justify-items: start;
  margin-top: var(--space-3);
}

.top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.dos-columnas {
  display: grid;
  gap: 1rem;
  grid-template-columns: 1fr 1fr;
}

@media (max-width: 760px) {
  .dos-columnas {
    grid-template-columns: 1fr;
  }
}

.card {
  display: grid;
  gap: 0.6rem;
  align-content: start;
}

.lista,
.resultados {
  display: grid;
  gap: 0.35rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.resultado {
  display: grid;
  gap: 0.15rem;
  width: 100%;
  padding: 0.5rem;
  text-align: left;
  background: none;
  border: 1px solid var(--border, rgba(127, 127, 127, 0.25));
  border-radius: 8px;
  cursor: pointer;
}

.estado {
  margin-left: 0.4rem;
  font-size: 0.75rem;
  font-style: normal;
  color: var(--text-muted);
}

.estado.abierto {
  color: var(--warn, #b26a00);
}

.elegido {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.checkbox {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.detalle {
  margin-top: 1rem;
}

.resolver {
  display: grid;
  gap: 0.6rem;
  margin-top: 0.75rem;
  padding-top: 0.75rem;
  border-top: 1px solid var(--border, rgba(127, 127, 127, 0.25));
}
</style>

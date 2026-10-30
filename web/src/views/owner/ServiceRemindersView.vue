<script setup lang="ts">
import { onMounted, ref, computed, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { workOrdersApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import FilterBar from '@/components/FilterBar.vue'
import FilterChip from '@/components/FilterChip.vue'
import { useTaller } from '@/composables/useTaller'
import SkeletonList from '@/components/SkeletonList.vue'
import { useAuthStore } from '@/stores/auth'
import type { ServiceReminder } from '@/types/domain'
import { formatDate } from '@/utils/format'

/**
 * A quién le toca servicio. Es trabajo que hoy se pierde por no acordarse: el cliente vuelve
 * cuando algo suena, no cuando le toca.
 *
 * La lista sale de lo que el taller anotó al entregar cada vehículo, y se cae sola cuando el
 * carro vuelve: si hay una orden abierta después de ese cierre, deja de aparecer. Lo que
 * dispara el aviso es la fecha; el kilometraje se enseña como contexto, porque hasta que el
 * vehículo no vuelve nadie sabe cuánto ha rodado.
 */
const { vocabulario, cargar: cargarTaller } = useTaller()

const auth = useAuthStore()

const reminders = ref<ServiceReminder[]>([])

const search = ref('')
/** La sucursal sale del menú, no de esta pantalla: es la misma para todo el panel. */
const branchId = computed(() => auth.activeBranchId ?? '')
/** Qué se está mirando: lo del mes, solo lo atrasado, o los que ya se recordaron. */
const filtro = ref<'mes' | 'atrasados' | 'recordados'>('mes')

const loading = ref(false)
const error = ref('')
const aviso = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    reminders.value = await workOrdersApi.reminders({
      search: search.value.trim() || undefined,
      branchId: branchId.value || undefined,
      overdue: filtro.value === 'atrasados' ? true : undefined,
      includeReminded: filtro.value === 'recordados',
      // Los ya recordados se buscan hacia atrás, sin recortar por fecha.
      withinDays: filtro.value === 'recordados' ? 365 : 30,
    })
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar a quién le toca servicio.')
  } finally {
    loading.value = false
  }
}

function elegir(valor: typeof filtro.value) {
  filtro.value = valor
  return load()
}

/**
 * Manda el recordatorio por WhatsApp. La pestaña se abre antes del await —Safari bloquea
 * `window.open` si no viene del gesto— y al volver se recarga: el que ya se avisó sale de la
 * lista, que es lo que evita llamar dos veces al mismo cliente.
 */
async function recordar(reminder: ServiceReminder) {
  const tab = window.open('', '_blank')
  error.value = ''
  aviso.value = ''

  try {
    const link = await workOrdersApi.serviceReminderLink(reminder.workOrderId)
    if (tab) tab.location.href = link.url
    else window.location.href = link.url

    aviso.value = `Anotado: ya se le recordó a ${reminder.customerName}.`
    await load()
  } catch (e) {
    tab?.close()
    error.value = errorMessage(e, 'No se pudo armar el recordatorio.')
  }
}

function cuando(reminder: ServiceReminder): string {
  if (reminder.daysUntil < 0) {
    const dias = Math.abs(reminder.daysUntil)
    return `hace ${dias} ${dias === 1 ? 'día' : 'días'}`
  }
  if (reminder.daysUntil === 0) return 'hoy'
  return `en ${reminder.daysUntil} ${reminder.daysUntil === 1 ? 'día' : 'días'}`
}

// Al cambiar de sucursal en el menú, esta pantalla vuelve a pedir lo suyo.

watch(branchId, load)


onMounted(async () => {
  await cargarTaller()
  await load()
})
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Recordatorios</h1>
        <p class="muted small">
          <template v-if="auth.user && auth.user.branches.length > 1">
            <strong>{{ auth.activeBranchName }}</strong> ·
          </template>
          A quién le toca servicio, según lo que se anotó al entregar. Los que ya volvieron al
          taller salen de la lista solos.
        </p>
      </div>
      <div v-if="reminders.length" class="totals">
        <div>
          <span class="muted small">Por llamar</span>
          <strong>{{ reminders.length }}</strong>
        </div>
      </div>
    </header>

    <FilterBar
      v-model:search="search"
      :placeholder="`Cliente, teléfono o ${vocabulario.identificador.toLowerCase()}`"
    >
      <!-- Son excluyentes: no se filtra por «este mes» y «ya recordados» a la vez. -->
      <FilterChip :model-value="filtro === 'mes'" @update:model-value="elegir('mes')">
        Este mes
      </FilterChip>
      <FilterChip
        :model-value="filtro === 'atrasados'"
        @update:model-value="elegir('atrasados')"
      >
        Ya les tocaba
      </FilterChip>
      <FilterChip
        :model-value="filtro === 'recordados'"
        @update:model-value="elegir('recordados')"
      >
        Ya recordados
      </FilterChip>
    </FilterBar>

    <ErrorNote v-if="error" :message="error" />
    <p v-if="aviso" class="notice">{{ aviso }}</p>

    <SkeletonList v-if="loading" :rows="4" variant="tabla" />
    <p v-else-if="!reminders.length" class="muted">
      {{
        search || branchId || filtro !== 'mes'
          ? 'Nada con esos filtros.'
          : 'Nadie tiene servicio pendiente este mes.'
      }}
    </p>

    <table v-else>
      <thead>
        <tr>
          <th scope="col">Cliente y {{ vocabulario.activo }}</th>
          <th scope="col">Le toca</th>
          <th scope="col">Última visita</th>
          <th scope="col"><span class="sr-only">Acciones</span></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="r in reminders" :key="r.workOrderId" :class="{ atrasado: r.daysUntil < 0 }">
          <td>
            <strong>{{ r.customerName }}</strong>
            <div class="muted small">
              {{ r.vehicleLabel }}
              <span v-if="r.plate" class="plate">{{ r.plate }}</span>
              · {{ r.branchName }}
            </div>
          </td>
          <td>
            {{ formatDate(r.nextServiceAt) }}
            <div class="small" :class="r.daysUntil < 0 ? 'danger' : 'muted'">
              {{ cuando(r) }}
            </div>
            <div v-if="r.nextServiceMileage" class="muted small">
              o a los {{ r.nextServiceMileage.toLocaleString('es-HN') }} km
              <template v-if="r.lastMileage">
                · última lectura {{ r.lastMileage.toLocaleString('es-HN') }}
              </template>
            </div>
          </td>
          <td>
            <span class="muted">{{ r.lastService }}</span>
            <div class="muted small">
              {{ r.orderNumber }}
              <template v-if="r.closedAt"> · {{ formatDate(r.closedAt) }}</template>
            </div>
          </td>
          <td class="col-accion">
            <!-- El div por dentro y no `display: flex` en el `td`: una celda con flex deja de
                 ser celda, la columna pierde la alineación y el botón se sale de la fila. -->
            <div class="accion">
              <!-- Apagado: con cinco filas eran cinco botones principales a la vez, y
                   ninguno lo era. El ícono dice por dónde sale el mensaje. -->
              <button type="button" class="btn-ghost btn-sm" @click="recordar(r)">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
                  <path d="M20 11.5a8 8 0 0 1-11.9 7L4 20l1.6-4A8 8 0 1 1 20 11.5Z" stroke-linejoin="round" />
                </svg>
                Recordar por WhatsApp
              </button>
              <span v-if="r.remindedAt" class="muted small">
                recordado {{ formatDate(r.remindedAt) }}
              </span>
            </div>
          </td>
        </tr>
      </tbody>
    </table>
  </section>
</template>

<style scoped>
.accion button svg {
  width: 0.9rem;
  height: 0.9rem;
  margin-right: 0.3rem;
  vertical-align: -2px;
}

.top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

h1 {
  margin: 0 0 0.25rem;
}

.totals div {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
}

.totals strong {
  font-size: 1.5rem;
}

.filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  margin: 1rem 0;
}

.filters input[type='search'] {
  flex: 1 1 16rem;
}

.chips {
  display: flex;
  gap: 0.375rem;
}

.chip {
  padding: 0.25rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  background: var(--surface);
  color: var(--text-muted);
  font-size: 0.8125rem;
  cursor: pointer;
}

.chip.on {
  border-color: var(--accent);
  background: var(--accent);
  color: var(--on-accent);
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9375rem;
}

th {
  padding: 0.375rem 0.25rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
  font-weight: 500;
}

td {
  padding: 0.5rem 0.25rem;
  border-bottom: 1px solid var(--border);
  vertical-align: top;
}

.atrasado td {
  background: color-mix(in srgb, var(--danger) 6%, transparent);
}

.plate {
  display: inline-block;
  padding: 0 0.25rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  font-weight: 600;
  color: var(--text);
}

.col-accion {
  width: 1%;
  white-space: nowrap;
}

.accion {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: var(--space-1);
}

.notice {
  color: var(--accent);
}

.danger {
  color: var(--danger);
}

</style>

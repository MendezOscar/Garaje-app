<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, workOrdersApi } from '@/api/garaj'
import type { Branch, ServiceReminder } from '@/types/domain'
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
const reminders = ref<ServiceReminder[]>([])
const branches = ref<Branch[]>([])

const search = ref('')
const branchId = ref('')
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

onMounted(async () => {
  branches.value = await branchesApi.list().catch(() => [])
  await load()
})
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Recordatorios</h1>
        <p class="muted small">
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

    <form class="filters" @submit.prevent="load">
      <input v-model="search" type="search" placeholder="Cliente, teléfono o placa" />

      <select v-if="branches.length > 1" v-model="branchId" @change="load">
        <option value="">Todas las sucursales</option>
        <option v-for="b in branches" :key="b.id" :value="b.id">{{ b.name }}</option>
      </select>

      <div class="chips">
        <button type="button" class="chip" :class="{ on: filtro === 'mes' }" @click="elegir('mes')">
          Este mes
        </button>
        <button
          type="button"
          class="chip"
          :class="{ on: filtro === 'atrasados' }"
          @click="elegir('atrasados')"
        >
          Ya les tocaba
        </button>
        <button
          type="button"
          class="chip"
          :class="{ on: filtro === 'recordados' }"
          @click="elegir('recordados')"
        >
          Ya recordados
        </button>
      </div>
    </form>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="aviso" class="notice">{{ aviso }}</p>

    <p v-if="loading" class="muted">Cargando…</p>
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
          <th>Cliente y vehículo</th>
          <th>Le toca</th>
          <th>Última visita</th>
          <th></th>
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
          <td class="acciones">
            <button type="button" @click="recordar(r)">Recordar por WhatsApp</button>
            <span v-if="r.remindedAt" class="muted small">
              recordado {{ formatDate(r.remindedAt) }}
            </span>
          </td>
        </tr>
      </tbody>
    </table>
  </section>
</template>

<style scoped>
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
  color: #fff;
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

.acciones {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.25rem;
}

.notice {
  color: var(--accent);
}

.small {
  font-size: 0.75rem;
}

.muted {
  color: var(--text-muted);
}

.danger {
  color: var(--danger);
}

.error {
  color: var(--danger);
}
</style>

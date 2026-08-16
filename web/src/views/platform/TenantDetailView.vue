<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { errorMessage } from '@/api/client'
import { platformApi } from '@/api/garaj'
import type { PlatformTenantDetail } from '@/types/domain'
import { formatDay, formatMoney } from '@/utils/format'

/**
 * La ficha de cobro de un taller: registrar el pago del mes, darle un acuerdo si quedó
 * debiendo, y —solo como último recurso— suspenderlo.
 */
const route = useRoute()
const id = route.params.id as string

const data = ref<PlatformTenantDetail | null>(null)
const loading = ref(false)
const busy = ref(false)
const error = ref('')

const tenant = computed(() => data.value?.tenant ?? null)

const pago = ref({ amount: 0, paidOn: '', method: 'Transferencia', reference: '', months: 1 })
const acuerdo = ref({ unblockedThrough: '', note: '' })
const plan = ref({ planName: '', monthlyFee: 0, paidThrough: '', graceDays: 5 })

/** Qué le está pasando al taller ahora mismo, dicho como se lo diríamos por teléfono. */
const situacion = computed(() => {
  const t = tenant.value
  if (!t) return ''
  if (!t.isActive) return 'Suspendido: no puede ni entrar al sistema.'
  if (t.unblockedThrough)
    return `Trabajando por acuerdo de pago hasta el ${formatDay(t.unblockedThrough)}.`
  if (t.state === 'ReadOnly')
    return 'Solo puede consultar: no registra órdenes ni factura hasta ponerse al día.'
  if (t.state === 'Grace')
    return `Vencido, pero trabajando: se le corta el ${formatDay(t.readOnlyOn)}.`
  if (t.state === 'DueSoon')
    return t.daysLeft === 0 ? 'Le vence hoy.' : `Le vencen ${t.daysLeft} días.`
  return 'Al día.'
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    data.value = await platformApi.get(id)

    const t = data.value.tenant
    pago.value.amount = t.monthlyFee
    plan.value = {
      planName: t.planName ?? '',
      monthlyFee: t.monthlyFee,
      paidThrough: t.paidThrough ?? '',
      graceDays: t.graceDays,
    }
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el taller.')
  } finally {
    loading.value = false
  }
}

async function accion(fn: () => Promise<unknown>, fallo: string) {
  busy.value = true
  error.value = ''
  try {
    await fn()
    await load()
  } catch (e) {
    error.value = errorMessage(e, fallo)
  } finally {
    busy.value = false
  }
}

function registrarPago() {
  if (!pago.value.amount) return

  return accion(
    () =>
      platformApi.registerPayment(id, {
        amount: Number(pago.value.amount),
        paidOn: pago.value.paidOn || null,
        method: pago.value.method || null,
        reference: pago.value.reference.trim() || null,
        months: Number(pago.value.months) || 1,
      }),
    'No se pudo registrar el pago.',
  )
}

function darAcuerdo() {
  if (!acuerdo.value.unblockedThrough) return

  return accion(
    () =>
      platformApi.setAgreement(id, {
        unblockedThrough: acuerdo.value.unblockedThrough,
        note: acuerdo.value.note.trim() || null,
      }),
    'No se pudo registrar el acuerdo.',
  )
}

function quitarAcuerdo() {
  return accion(() => platformApi.clearAgreement(id), 'No se pudo quitar el acuerdo.')
}

function guardarPlan() {
  return accion(
    () =>
      platformApi.updateSubscription(id, {
        planName: plan.value.planName.trim() || null,
        monthlyFee: Number(plan.value.monthlyFee) || 0,
        paidThrough: plan.value.paidThrough || null,
        graceDays: Number(plan.value.graceDays) || 0,
      }),
    'No se pudo guardar el plan.',
  )
}

function suspender() {
  if (!confirm('El taller dejará de poder entrar al sistema. ¿Suspenderlo?')) return

  return accion(() => platformApi.suspend(id), 'No se pudo suspender.')
}

function reactivar() {
  return accion(() => platformApi.reactivate(id), 'No se pudo reactivar.')
}

onMounted(load)
</script>

<template>
  <section v-if="tenant">
    <header class="top">
      <div>
        <RouterLink :to="{ name: 'platform-tenants' }" class="volver">← Talleres</RouterLink>
        <h1>{{ tenant.name }}</h1>
        <p class="muted small">
          {{ tenant.planName ?? 'Sin plan' }} · {{ formatMoney(tenant.monthlyFee) }} al mes ·
          cliente desde {{ formatDay(tenant.createdAt) }}
        </p>
      </div>
      <button v-if="tenant.isActive" type="button" class="peligro" @click="suspender">
        Suspender
      </button>
      <button v-else type="button" @click="reactivar">Reactivar</button>
    </header>

    <p v-if="error" class="error">{{ error }}</p>

    <p class="situacion" :class="tenant.state.toLowerCase()">{{ situacion }}</p>

    <div class="grid">
      <form class="card" @submit.prevent="registrarPago">
        <h2>Registrar pago</h2>

        <div class="row">
          <label>
            Monto
            <input v-model.number="pago.amount" type="number" min="0" step="0.01" required />
          </label>
          <label>
            Meses
            <input v-model.number="pago.months" type="number" min="1" max="24" />
          </label>
        </div>

        <div class="row">
          <label>
            Fecha del pago
            <input v-model="pago.paidOn" type="date" />
          </label>
          <label>
            Forma
            <select v-model="pago.method">
              <option>Transferencia</option>
              <option>Depósito</option>
              <option>Efectivo</option>
            </select>
          </label>
        </div>

        <label>
          Número de referencia
          <input v-model="pago.reference" placeholder="Número de depósito" />
        </label>

        <div class="actions">
          <button type="submit" :disabled="busy">Registrar</button>
        </div>

        <p class="muted small">
          La fecha se corre desde el vencimiento anterior, no desde hoy: quien paga con atraso
          no pierde los días que ya había pagado.
        </p>
      </form>

      <form class="card" @submit.prevent="darAcuerdo">
        <h2>Acuerdo de pago</h2>

        <p v-if="tenant.unblockedThrough" class="vigente">
          Vigente hasta el {{ formatDay(tenant.unblockedThrough) }}.
          <span v-if="tenant.unblockNote" class="muted">{{ tenant.unblockNote }}</span>
        </p>

        <label>
          Desbloqueado hasta
          <input v-model="acuerdo.unblockedThrough" type="date" required />
        </label>
        <label>
          Motivo
          <input v-model="acuerdo.note" placeholder="Paga el 30, habló con él el 15" />
        </label>

        <div class="actions">
          <button type="submit" :disabled="busy">Dar acuerdo</button>
          <button
            v-if="tenant.unblockedThrough"
            type="button"
            class="link"
            :disabled="busy"
            @click="quitarAcuerdo"
          >
            Quitarlo
          </button>
        </div>

        <p class="muted small">
          El taller sigue trabajando aunque deba. Se cancela solo al registrar el pago.
        </p>
      </form>

      <form class="card" @submit.prevent="guardarPlan">
        <h2>Plan y vencimiento</h2>

        <div class="row">
          <label>
            Plan
            <input v-model="plan.planName" placeholder="Básico" />
          </label>
          <label>
            Cuota mensual
            <input v-model.number="plan.monthlyFee" type="number" min="0" step="0.01" />
          </label>
        </div>

        <div class="row">
          <label>
            Pagado hasta
            <input v-model="plan.paidThrough" type="date" />
          </label>
          <label>
            Días de gracia
            <input v-model.number="plan.graceDays" type="number" min="0" max="60" />
          </label>
        </div>

        <div class="actions">
          <button type="submit" :disabled="busy">Guardar</button>
        </div>

        <p class="muted small">
          Sin fecha de «pagado hasta», el taller nunca se bloquea.
        </p>
      </form>
    </div>

    <h2 class="historial">Pagos</h2>

    <p v-if="!data?.payments.length" class="muted">Todavía no ha pagado ninguna mensualidad.</p>

    <table v-else>
      <thead>
        <tr>
          <th>Pagó el</th>
          <th class="num">Monto</th>
          <th>Forma</th>
          <th>Referencia</th>
          <th>Cubrió hasta</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="p in data?.payments" :key="p.id">
          <td>{{ formatDay(p.paidOn) }}</td>
          <td class="num">{{ formatMoney(p.amount) }}</td>
          <td>{{ p.method ?? '—' }}</td>
          <td class="mono">{{ p.reference ?? '—' }}</td>
          <td>{{ formatDay(p.coversThrough) }}</td>
        </tr>
      </tbody>
    </table>
  </section>

  <p v-else-if="loading" class="muted">Cargando…</p>
  <p v-else class="error">{{ error }}</p>
</template>

<style scoped>
h1 {
  margin: 0.25rem 0 0;
}

.volver {
  font-size: 0.875rem;
}

.top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.situacion {
  margin: 0 0 1rem;
  padding: 0.625rem 0.875rem;
  border-left: 3px solid currentcolor;
  border-radius: var(--radius-sm);
  background: var(--surface-alt);
  font-size: 0.9375rem;
}

.situacion.grace,
.situacion.duesoon {
  color: var(--warning);
  background: color-mix(in srgb, var(--warning) 12%, transparent);
}

.situacion.readonly,
.situacion.suspended {
  color: var(--danger);
  background: color-mix(in srgb, var(--danger) 12%, transparent);
}

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(18rem, 1fr));
  gap: 1rem;
  align-items: start;
}

.card {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.card h2 {
  margin: 0 0 0.75rem;
  font-size: 1rem;
}

.row {
  display: flex;
  gap: 0.75rem;
}

/* Sin `min-width: 0` el ancho mínimo del input desborda la tarjeta en la columna angosta. */
.row label {
  flex: 1;
  min-width: 0;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  margin-bottom: 0.5rem;
  font-size: 0.875rem;
}

.actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-top: 0.25rem;
}

.vigente {
  margin: 0 0 0.75rem;
  padding: 0.5rem 0.625rem;
  border-radius: var(--radius-sm);
  background: var(--surface-alt);
  font-size: 0.875rem;
}

.peligro {
  background: var(--danger);
}

.historial {
  margin: 1.5rem 0 0.75rem;
  font-size: 1rem;
}

table {
  width: 100%;
  border-collapse: collapse;
}

th,
td {
  padding: 0.5rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
}

.num {
  text-align: right;
}

.mono {
  font-family: var(--font-mono);
}

.error {
  color: var(--danger);
}

.muted {
  color: var(--text-muted);
}

.small {
  font-size: 0.8125rem;
}
</style>

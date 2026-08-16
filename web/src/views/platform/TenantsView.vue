<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { platformApi } from '@/api/garaj'
import type { CreatedTenant, PlatformTenant } from '@/types/domain'
import { formatDay, formatMoney } from '@/utils/format'

/**
 * Los talleres como clientes nuestros: quién está al día, a quién hay que cobrarle y quién
 * dejó de pagar. Es la única pantalla del perfil Plataforma junto con el detalle.
 *
 * No hay una sola orden ni un solo cliente del taller aquí, y no por olvido: este perfil no
 * tiene taller, así que el filtro del backend le devuelve vacío todo lo que sea trabajo ajeno.
 */
const tenants = ref<PlatformTenant[]>([])
const loading = ref(false)
const busy = ref(false)
const error = ref('')

const creando = ref(false)
const creado = ref<CreatedTenant | null>(null)

const vacio = {
  name: '',
  ownerEmail: '',
  ownerName: '',
  branchName: '',
  branchCode: '',
  city: '',
  address: '',
  phone: '',
  planName: '',
  monthlyFee: 0,
  paidThrough: '',
  password: '',
}

const form = ref({ ...vacio })

/** Cuántos hay que atender ya. Es el número que importa al abrir la pantalla. */
const porCobrar = computed(
  () => tenants.value.filter((t) => t.state !== 'Active').length,
)

const etiquetas: Record<PlatformTenant['state'], string> = {
  Active: 'Al día',
  DueSoon: 'Por vencer',
  Grace: 'En gracia',
  ReadOnly: 'Solo lectura',
  Suspended: 'Suspendido',
}

function tono(t: PlatformTenant) {
  if (t.unblockedThrough) return 'acuerdo'
  return t.state === 'Active'
    ? 'ok'
    : t.state === 'DueSoon'
      ? 'aviso'
      : t.state === 'Grace'
        ? 'urgente'
        : 'cortado'
}

function etiqueta(t: PlatformTenant) {
  return t.unblockedThrough ? 'Acuerdo de pago' : etiquetas[t.state]
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    tenants.value = await platformApi.list()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar la lista de talleres.')
  } finally {
    loading.value = false
  }
}

async function crear() {
  const f = form.value
  if (!f.name.trim() || !f.ownerEmail.trim() || !f.ownerName.trim() || !f.branchName.trim()) return

  busy.value = true
  error.value = ''
  try {
    creado.value = await platformApi.create({
      name: f.name.trim(),
      ownerEmail: f.ownerEmail.trim(),
      ownerName: f.ownerName.trim(),
      branchName: f.branchName.trim(),
      branchCode: f.branchCode.trim() || null,
      city: f.city.trim() || null,
      address: f.address.trim() || null,
      phone: f.phone.trim() || null,
      planName: f.planName.trim() || null,
      monthlyFee: Number(f.monthlyFee) || 0,
      paidThrough: f.paidThrough || null,
      // Vacía, la genera el servidor. Sale igual en la tarjeta de abajo en los dos casos.
      password: f.password.trim() || null,
    })

    form.value = { ...vacio }
    creando.value = false
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo dar de alta el taller.')
  } finally {
    busy.value = false
  }
}

onMounted(load)
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Talleres</h1>
        <p class="muted small">
          <template v-if="porCobrar">
            {{ porCobrar }} {{ porCobrar === 1 ? 'taller necesita' : 'talleres necesitan' }}
            atención de cobro.
          </template>
          <template v-else>Todos al día.</template>
        </p>
      </div>
      <button type="button" @click="creando = !creando">
        {{ creando ? 'Cancelar' : 'Nuevo taller' }}
      </button>
    </header>

    <p v-if="error" class="error">{{ error }}</p>

    <!-- Se enseña una sola vez: la contraseña no se guarda en claro en ninguna parte. -->
    <article v-if="creado" class="card credenciales">
      <h2>Taller creado</h2>
      <p>
        Entrégueselas al Dueño ahora. <strong>No se pueden volver a consultar</strong>: no
        quedan guardadas en ningún lado.
      </p>
      <dl>
        <dt>Correo</dt>
        <dd class="mono">{{ creado.ownerEmail }}</dd>
        <dt>Contraseña</dt>
        <dd class="mono">{{ creado.password }}</dd>
      </dl>
      <button type="button" class="link" @click="creado = null">Ya las anoté</button>
    </article>

    <form v-if="creando" class="card alta" @submit.prevent="crear">
      <h2>Nuevo taller</h2>

      <div class="row">
        <label>
          Nombre del taller
          <input v-model="form.name" placeholder="Taller Martínez" required />
        </label>
        <label>
          Teléfono
          <input v-model="form.phone" placeholder="9888-1111" />
        </label>
      </div>

      <div class="row">
        <label>
          Nombre del Dueño
          <input v-model="form.ownerName" placeholder="Juan Martínez" required />
        </label>
        <label>
          Correo del Dueño
          <input v-model="form.ownerEmail" type="email" placeholder="juan@taller.hn" required />
        </label>
        <label>
          Contraseña
          <input
            v-model="form.password"
            type="text"
            placeholder="La escribe él, o se genera"
            minlength="8"
          />
          <span class="ayuda">
            Con el Dueño al lado, que la escriba él: se la lleva sabida. En blanco, se genera.
          </span>
        </label>
      </div>

      <div class="row">
        <label>
          Primera sucursal
          <input v-model="form.branchName" placeholder="Casa matriz" required />
        </label>
        <label>
          Código
          <input v-model="form.branchCode" placeholder="MTZ" />
        </label>
        <label>
          Ciudad
          <input v-model="form.city" placeholder="Tegucigalpa" />
        </label>
      </div>

      <div class="row">
        <label>
          Plan
          <input v-model="form.planName" placeholder="Básico" />
        </label>
        <label>
          Cuota mensual
          <input v-model.number="form.monthlyFee" type="number" min="0" step="0.01" />
        </label>
        <label>
          Pagado hasta
          <input v-model="form.paidThrough" type="date" />
        </label>
      </div>

      <div class="actions">
        <button type="submit" :disabled="busy">Dar de alta</button>
      </div>

      <p class="muted small">
        Si no se pone fecha, queda pagado hasta dentro de un mes: lo normal es que el taller
        pague la instalación el día que se instala. La contraseña —la escriba él o se genere—
        se enseña una sola vez al guardar, y el Dueño puede cambiarla en Usuarios.
      </p>
    </form>

    <p v-if="loading" class="muted">Cargando…</p>
    <p v-else-if="!tenants.length" class="muted">Todavía no hay talleres.</p>

    <table v-else>
      <thead>
        <tr>
          <th>Taller</th>
          <th>Estado</th>
          <th>Pagado hasta</th>
          <th class="num">Cuota</th>
          <th>Último pago</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="t in tenants" :key="t.id">
          <td>
            {{ t.name }}
            <div class="muted small">
              {{ t.planName ?? 'Sin plan' }} ·
              {{ t.branchCount }} {{ t.branchCount === 1 ? 'sucursal' : 'sucursales' }}
            </div>
          </td>
          <td>
            <span class="chip" :class="tono(t)">{{ etiqueta(t) }}</span>
          </td>
          <td>
            {{ formatDay(t.paidThrough) }}
            <div v-if="t.state === 'DueSoon' && t.daysLeft !== null" class="muted small">
              {{ t.daysLeft === 0 ? 'vence hoy' : `faltan ${t.daysLeft} días` }}
            </div>
          </td>
          <td class="num">{{ formatMoney(t.monthlyFee) }}</td>
          <td>{{ formatDay(t.lastPaymentOn) }}</td>
          <td>
            <RouterLink :to="{ name: 'platform-tenant', params: { id: t.id } }">Ver</RouterLink>
          </td>
        </tr>
      </tbody>
    </table>
  </section>
</template>

<style scoped>
h1 {
  margin: 0;
}

.top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.card {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  margin-bottom: 1rem;
}

.card h2 {
  margin: 0 0 0.5rem;
  font-size: 1rem;
}

.credenciales {
  border-color: var(--success);
}

.credenciales dl {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.25rem 0.75rem;
  margin: 0.75rem 0;
}

.credenciales dt {
  color: var(--text-muted);
  font-size: 0.875rem;
}

.mono {
  font-family: var(--font-mono);
  margin: 0;
}

.alta .row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.alta .row label {
  flex: 1 1 12rem;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.875rem;
}

.ayuda {
  color: var(--text-muted);
  font-size: 0.75rem;
}

.actions {
  margin-top: 0.5rem;
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
  vertical-align: top;
}

.num {
  text-align: right;
}

.chip {
  display: inline-block;
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 600;
}

.chip.ok {
  background: color-mix(in srgb, var(--success) 15%, transparent);
  color: var(--success);
}

.chip.aviso {
  background: color-mix(in srgb, var(--warning) 18%, transparent);
  color: var(--warning);
}

.chip.urgente {
  background: color-mix(in srgb, var(--warning) 30%, transparent);
  color: var(--warning);
}

.chip.cortado {
  background: color-mix(in srgb, var(--danger) 15%, transparent);
  color: var(--danger);
}

.chip.acuerdo {
  background: var(--surface-alt);
  color: var(--text-muted);
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

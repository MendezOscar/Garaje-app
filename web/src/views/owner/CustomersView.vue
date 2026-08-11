<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { customersApi, vehiclesApi } from '@/api/garaj'
import { useAuthStore } from '@/stores/auth'
import { VEHICLE_TYPE_LABEL, type Customer, type Vehicle } from '@/types/domain'
import { whatsappLink } from '@/utils/format'

const auth = useAuthStore()

const customers = ref<Customer[]>([])
const search = ref('')
const loading = ref(false)
const error = ref('')

/** Vehículos del cliente expandido. Se cargan al abrir, no antes: la lista sería enorme. */
const expandedId = ref<string | null>(null)
const vehicles = ref<Vehicle[]>([])

const busy = ref(false)
const notice = ref('')

/** Alta de acceso a la app, por cliente. Se abre con el botón de su fila. */
const access = ref<{ customer: Customer; email: string; password: string } | null>(null)

/**
 * RTN del cliente expandido. Se edita aquí porque es donde se pregunta —«¿va a querer factura
 * con CAI?»— y así la factura sale con él sin que nadie lo dicte de nuevo en la entrega.
 */
const rtn = ref('')

async function saveRtn(customer: Customer) {
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    const value = rtn.value.trim()
    const updated = await customersApi.update(customer.id, {
      fullName: customer.fullName,
      phone: customer.phone,
      email: customer.email,
      documentId: customer.documentId,
      taxId: value || null,
      address: customer.address,
      notes: customer.notes,
      isActive: customer.isActive,
    })
    Object.assign(customer, updated)
    notice.value = value
      ? `RTN guardado: sus facturas con CAI saldrán a nombre de ${customer.fullName}.`
      : 'RTN quitado.'
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo guardar el RTN.')
  } finally {
    busy.value = false
  }
}

let searchToken = 0

async function load() {
  loading.value = true
  // Marca de secuencia: al teclear rápido, una respuesta vieja no debe pisar a la nueva.
  const token = ++searchToken

  try {
    const result = await customersApi.list({ search: search.value || undefined, pageSize: 50 })
    if (token === searchToken) customers.value = result.items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar los clientes.')
  } finally {
    if (token === searchToken) loading.value = false
  }
}

async function toggle(customer: Customer) {
  if (expandedId.value === customer.id) {
    expandedId.value = null
    return
  }

  expandedId.value = customer.id
  rtn.value = customer.taxId ?? ''
  vehicles.value = []
  vehicles.value = (await vehiclesApi.list({ customerId: customer.id })).items
}

/**
 * Le abre acceso a la app a un cliente. Es opcional a propósito: la mayoría no lo va a usar,
 * y crearle un usuario a cada uno llenaría la lista de accesos que nadie abre nunca.
 */
function openAccess(customer: Customer) {
  notice.value = ''
  error.value = ''
  access.value = { customer, email: customer.email ?? '', password: '' }
}

async function grantAccess() {
  const form = access.value
  if (!form || !form.email.trim() || !form.password.trim()) return

  busy.value = true
  error.value = ''
  try {
    await customersApi.grantAppAccess(form.customer.id, {
      email: form.email.trim(),
      password: form.password.trim(),
    })
    notice.value = `${form.customer.fullName} ya puede entrar con ${form.email.trim()}.`
    access.value = null
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo crear el acceso.')
  } finally {
    busy.value = false
  }
}

onMounted(load)

let timer: ReturnType<typeof setTimeout>
watch(search, () => {
  clearTimeout(timer)
  timer = setTimeout(load, 250)
})
</script>

<template>
  <section>
    <header class="toolbar">
      <h1>Clientes</h1>
      <input v-model="search" type="search" placeholder="Nombre, teléfono o placa" />
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="notice" class="notice">{{ notice }}</p>

    <table>
      <thead>
        <tr>
          <th>Cliente</th>
          <th>Teléfono</th>
          <th>Vehículos</th>
          <th>App</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <template v-for="customer in customers" :key="customer.id">
          <tr>
            <td>
              <button type="button" class="link" @click="toggle(customer)">
                {{ customer.fullName }}
              </button>
              <div v-if="customer.email" class="muted small">{{ customer.email }}</div>
            </td>
            <td>{{ customer.phone }}</td>
            <td>{{ customer.vehicleCount }}</td>
            <td>
              <span v-if="customer.hasAppAccess" class="access">
                Sí
                <div class="muted small">{{ customer.appUserEmail }}</div>
              </span>
              <button
                v-else-if="auth.isOwner"
                type="button"
                class="link"
                @click="openAccess(customer)"
              >
                Dar acceso
              </button>
              <span v-else class="muted small">—</span>
            </td>
            <td>
              <a :href="whatsappLink(customer.phone)" target="_blank" rel="noopener">WhatsApp</a>
            </td>
          </tr>

          <tr v-if="access?.customer.id === customer.id">
            <td colspan="5">
              <form class="access-form" @submit.prevent="grantAccess">
                <label>
                  Correo con el que entra
                  <input v-model="access.email" type="email" required />
                </label>
                <label>
                  Contraseña
                  <input v-model="access.password" type="text" placeholder="Mínimo 8 caracteres" required />
                </label>
                <button type="submit" :disabled="busy">Crear acceso</button>
                <button type="button" class="link" @click="access = null">Cancelar</button>
              </form>
              <p class="muted small">
                Podrá ver sus vehículos, seguir la reparación y aprobar cotizaciones. Nada más.
              </p>
            </td>
          </tr>
          <tr v-if="expandedId === customer.id">
            <td colspan="5" class="ficha">
              <form class="rtn" @submit.prevent="saveRtn(customer)">
                <label>
                  RTN
                  <input v-model="rtn" placeholder="08019995123456" />
                </label>
                <button type="submit" :disabled="busy">Guardar</button>
                <span class="muted small">
                  Para la factura con CAI. Se puede corregir al facturar.
                </span>
              </form>
            </td>
          </tr>

          <tr v-if="expandedId === customer.id">
            <td colspan="5" class="vehicles">
              <ul v-if="vehicles.length">
                <li v-for="v in vehicles" :key="v.id">
                  <span class="plate" v-if="v.plate">{{ v.plate }}</span>
                  {{ v.brand }} {{ v.model }}
                  <span class="muted small">
                    {{ VEHICLE_TYPE_LABEL[v.type] }}
                    <template v-if="v.year"> · {{ v.year }}</template>
                    <template v-if="v.mileage"> · {{ v.mileage.toLocaleString('es-HN') }} km</template>
                  </span>
                </li>
              </ul>
              <p v-else class="muted">Sin vehículos registrados.</p>
            </td>
          </tr>
        </template>
      </tbody>
    </table>

    <p v-if="!customers.length && !loading" class="muted">Sin resultados.</p>
  </section>
</template>

<style scoped>
.access {
  font-size: 0.875rem;
}

.ficha {
  background: var(--surface-alt);
}

.rtn {
  display: flex;
  align-items: flex-end;
  gap: 0.75rem;
  flex-wrap: wrap;
  padding: 0.5rem 0;
}

.rtn label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.access-form {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 0.5rem;
}

.access-form label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.notice {
  color: var(--success, #15803d);
}

.toolbar {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
  margin-bottom: 1rem;
}

.toolbar h1 {
  margin: 0;
  margin-right: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
}

th,
td {
  padding: 0.5rem 0.625rem;
  text-align: left;
  border-bottom: 1px solid var(--border);
  vertical-align: top;
}

th {
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.link {
  padding: 0;
  border: none;
  background: none;
  color: var(--accent);
  font: inherit;
  cursor: pointer;
}

.vehicles ul {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.plate {
  padding: 0 0.25rem;
  margin-right: 0.375rem;
  border: 1px solid var(--border);
  border-radius: 3px;
  font-family: ui-monospace, monospace;
  font-size: 0.8125rem;
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

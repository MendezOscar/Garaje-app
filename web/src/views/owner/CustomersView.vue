<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { customersApi, equipmentApi, vehiclesApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import ViewTabs from '@/components/ViewTabs.vue'
import { useTaller } from '@/composables/useTaller'
import { useAuthStore } from '@/stores/auth'
import {
  VEHICLE_TYPE_LABEL,
  type Customer,
  type Equipment,
  type Vehicle,
} from '@/types/domain'
import { whatsappLink } from '@/utils/format'

const auth = useAuthStore()
const { esDeEquipos, vocabulario, cargar: cargarTaller } = useTaller()

const customers = ref<Customer[]>([])
const search = ref('')
const loading = ref(false)
const error = ref('')

/** Vehículos del cliente expandido. Se cargan al abrir, no antes: la lista sería enorme. */
const expandedId = ref<string | null>(null)
const vehicles = ref<Vehicle[]>([])

/** En un taller de equipos, la ficha del cliente enseña sus equipos en el mismo lugar. */
const equipos = ref<Equipment[]>([])

const fichas = computed(() =>
  esDeEquipos.value
    ? equipos.value.map((e) => ({
        id: e.id,
        identificador: e.serialNumber ?? e.tag,
        titulo: `${e.brand} ${e.model}`,
        detalle: e.equipmentTypeName,
      }))
    : vehicles.value.map((v) => ({
        id: v.id,
        identificador: v.plate,
        titulo: `${v.brand} ${v.model}`,
        detalle:
          VEHICLE_TYPE_LABEL[v.type] +
          (v.year ? ` · ${v.year}` : '') +
          (v.mileage ? ` · ${v.mileage.toLocaleString('es-HN')} km` : ''),
      })),
)

const busy = ref(false)
const notice = ref('')

/** Alta de acceso a la app, por cliente. Se abre con el botón de su fila. */
const access = ref<{ customer: Customer; email: string; password: string } | null>(null)

/**
 * RTN del cliente expandido. Se edita aquí porque es donde se pregunta —«¿va a querer factura
 * con CAI?»— y así la factura sale con él sin que nadie lo dicte de nuevo en la entrega.
 */
const rtn = ref('')

/**
 * A nombre de quién salen sus facturas. Vacío significa que van a su propio nombre; se llena
 * cuando el cliente las pide a nombre de su empresa, que es de quien suele ser el RTN.
 */
const nombreFactura = ref('')

async function saveRtn(customer: Customer) {
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    const value = rtn.value.trim()
    const aNombreDe = nombreFactura.value.trim()
    const updated = await customersApi.update(customer.id, {
      fullName: customer.fullName,
      phone: customer.phone,
      email: customer.email,
      documentId: customer.documentId,
      taxId: value || null,
      billingName: aNombreDe || null,
      address: customer.address,
      notes: customer.notes,
      isActive: customer.isActive,
    })
    Object.assign(customer, updated)
    notice.value = value
      ? `Guardado: sus facturas con CAI saldrán a nombre de ${aNombreDe || customer.fullName}.`
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
  nombreFactura.value = customer.billingName ?? ''
  vehicles.value = []
  equipos.value = []

  if (esDeEquipos.value) {
    equipos.value = (await equipmentApi.list({ customerId: customer.id })).items
  } else {
    vehicles.value = (await vehiclesApi.list({ customerId: customer.id })).items
  }
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

/** Las iniciales para el círculo de la fila: dos letras, que es lo que cabe. */
function iniciales(nombre: string) {
  return nombre
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((parte) => parte[0]?.toUpperCase() ?? '')
    .join('')
}

onMounted(async () => {
  await cargarTaller()
  await load()
})

let timer: ReturnType<typeof setTimeout>
watch(search, () => {
  clearTimeout(timer)
  timer = setTimeout(load, 250)
})
</script>

<template>
  <section>
    <header class="cabecera">
      <div>
        <h1>
          Clientes
          <span v-if="customers.length" class="cuantos">{{ customers.length }}</span>
        </h1>
        <p class="muted small">
          Las personas y empresas que atiende su taller, con sus {{ vocabulario.activos }} y su RTN.
        </p>
      </div>
      <!-- Dice lo que hace desde aquí: dar de alta a un cliente. El alta vive en la
           pantalla de recibir, que empieza justamente por el cliente. -->
      <RouterLink class="boton" :to="{ name: 'receive-vehicle' }">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
          <path d="M12 5v14M5 12h14" stroke-linecap="round" />
        </svg>
        Nuevo cliente
      </RouterLink>
    </header>

    <ViewTabs
      :pestanas="[
        { to: { name: 'customers' }, label: 'Clientes' },
        { to: { name: 'vehicle-history' }, label: 'Historial' },
      ]"
    />


    <ErrorNote v-if="error" :message="error" />
    <p v-if="notice" class="notice">{{ notice }}</p>

    <div class="panel">
      <div class="filtros">
        <span class="buscador">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
            <circle cx="11" cy="11" r="6.5" /><path d="m16 16 4 4" stroke-linecap="round" />
          </svg>
          <input
            v-model="search"
            type="search"
            :placeholder="`Nombre, teléfono o ${vocabulario.identificador.toLowerCase()}`"
          />
        </span>
        <span v-if="loading" class="muted small">Buscando…</span>
      </div>

      <table>
        <thead>
          <tr>
            <th scope="col">Cliente</th>
            <th scope="col">Contacto</th>
            <th scope="col">{{ vocabulario.Activos }}</th>
            <th scope="col">App</th>
            <th scope="col"><span class="sr-only">Acciones</span></th>
          </tr>
        </thead>
        <tbody>
          <template v-for="customer in customers" :key="customer.id">
            <tr class="fila" :class="{ abierta: expandedId === customer.id }">
              <td>
                <button type="button" class="cliente" @click="toggle(customer)">
                  <span class="avatar">{{ iniciales(customer.fullName) }}</span>
                  <span class="cliente-texto">
                    <strong>{{ customer.fullName }}</strong>
                    <span class="muted small">
                      {{ customer.taxId ? `RTN ${customer.taxId}` : 'sin RTN registrado' }}
                    </span>
                  </span>
                </button>
              </td>
              <td>
                <span class="num">{{ customer.phone }}</span>
                <span v-if="customer.email" class="muted small bloque">{{ customer.email }}</span>
              </td>
              <td>
                <span class="pastilla">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
                    <path d="M3 13.5 5 8h14l2 5.5V18h-3v-1.5H6V18H3z" stroke-linejoin="round" />
                  </svg>
                  {{ customer.vehicleCount }}
                </span>
              </td>
              <td>
                <span v-if="customer.hasAppAccess" class="acceso">
                  <span class="punto" />
                  <span class="muted small">{{ customer.appUserEmail }}</span>
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
              <td class="acciones">
                <a
                  class="icono wa"
                  :href="whatsappLink(customer.phone)"
                  target="_blank"
                  rel="noopener"
                  title="Escribirle por WhatsApp"
                >
                  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                    <path
                      d="M17.47 14.38c-.3-.15-1.76-.87-2.03-.97-.27-.1-.47-.15-.67.15-.2.3-.77.97-.94 1.16-.17.2-.35.22-.64.08-.3-.15-1.26-.46-2.39-1.48-.88-.79-1.48-1.76-1.65-2.06-.18-.3-.02-.46.13-.6.13-.14.35-.35.52-.52.17-.18.23-.3.35-.5.11-.2.05-.37-.03-.52-.09-.15-.66-1.61-.91-2.21-.23-.58-.47-.5-.65-.51h-.57c-.2 0-.52.07-.79.37-.27.3-1.04 1.02-1.04 2.48s1.07 2.87 1.21 3.07c.15.2 2.1 3.2 5.08 4.49.71.3 1.26.49 1.69.62.71.23 1.36.2 1.87.12.57-.09 1.76-.72 2.01-1.41.25-.7.25-1.29.17-1.42-.07-.12-.27-.2-.57-.35M12.05 21.8a9.87 9.87 0 0 1-5.03-1.38l-.36-.22-3.74.99 1-3.65-.24-.38a9.86 9.86 0 0 1-1.51-5.26c0-5.45 4.44-9.89 9.89-9.89 2.64 0 5.12 1.03 6.99 2.9a9.83 9.83 0 0 1 2.89 6.99c0 5.45-4.43 9.89-9.89 9.89m8.41-18.3A11.82 11.82 0 0 0 12.05 0C5.5 0 .16 5.34.16 11.89c0 2.1.55 4.15 1.59 5.95L.06 24l6.3-1.65a11.88 11.88 0 0 0 5.69 1.45c6.55 0 11.89-5.34 11.89-11.9 0-3.17-1.24-6.16-3.48-8.41Z"
                    />
                  </svg>
                </a>
                <button
                  type="button"
                  class="icono"
                  :title="expandedId === customer.id ? 'Cerrar la ficha' : 'Ver la ficha'"
                  @click="toggle(customer)"
                >
                  <svg
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="2"
                    :style="{ rotate: expandedId === customer.id ? '180deg' : 'none' }"
                  >
                    <path d="m6 9 6 6 6-6" stroke-linecap="round" stroke-linejoin="round" />
                  </svg>
                </button>
              </td>
            </tr>

            <tr v-if="access?.customer.id === customer.id" class="desplegado">
              <td colspan="5">
                <form class="access-form" @submit.prevent="grantAccess">
                  <label>
                    Correo con el que entra
                    <input v-model="access.email" type="email" required />
                  </label>
                  <label>
                    Contraseña
                    <input
                      v-model="access.password"
                      type="text"
                      placeholder="Mínimo 8 caracteres"
                      required
                    />
                  </label>
                  <button type="submit" :disabled="busy">Crear acceso</button>
                  <button type="button" class="link" @click="access = null">Cancelar</button>
                </form>
                <p class="muted small">
                  Podrá ver sus {{ vocabulario.activos }}, seguir la reparación y aprobar
                  cotizaciones. Nada más.
                </p>
              </td>
            </tr>

            <tr v-if="expandedId === customer.id" class="desplegado">
              <td colspan="5">
                <div class="ficha">
                  <form class="rtn" @submit.prevent="saveRtn(customer)">
                    <label>
                      RTN
                      <input v-model="rtn" placeholder="08019995123456" />
                    </label>
                    <label>
                      A nombre de
                      <input v-model="nombreFactura" :placeholder="customer.fullName" />
                    </label>
                    <button type="submit" :disabled="busy">Guardar</button>
                    <span class="muted small pie-rtn">
                      Para la factura con CAI. Deje el nombre vacío si la factura va a nombre
                      del cliente; llénelo cuando la pida a nombre de su empresa. Se puede
                      corregir al facturar.
                    </span>
                  </form>

                  <div class="vehiculos">
                    <p class="rotulo">Sus {{ vocabulario.activos }}</p>
                    <ul v-if="fichas.length">
                      <li v-for="ficha in fichas" :key="ficha.id">
                        <span v-if="ficha.identificador" class="plate num">
                          {{ ficha.identificador }}
                        </span>
                        <strong>{{ ficha.titulo }}</strong>
                        <span class="muted small">{{ ficha.detalle }}</span>
                      </li>
                    </ul>
                    <p v-else class="muted small">Sin {{ vocabulario.activos }} registrados.</p>
                  </div>
                </div>
              </td>
            </tr>
          </template>
        </tbody>
      </table>

      <p v-if="!customers.length && !loading" class="vacio muted">
        {{ search ? 'Ningún cliente coincide con esa búsqueda.' : 'Todavía no hay clientes.' }}
      </p>
    </div>
  </section>
</template>

<style scoped>
.cabecera {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
  margin-bottom: 1.25rem;
}

h1 {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin: 0;
  font-size: 1.625rem;
}

.cuantos {
  padding: 0.1rem 0.5rem;
  border-radius: 999px;
  background: var(--surface-alt);
  color: var(--text-muted);
  font-family: var(--font-mono);
  font-size: 0.875rem;
}

.cabecera p {
  margin: 0.25rem 0 0;
}

.boton {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.55rem 0.9rem;
  border-radius: var(--radius-sm);
  background: var(--accent);
  color: var(--on-accent);
  font-weight: 600;
}

.boton:hover {
  text-decoration: none;
  background: var(--brand-deep);
}

.boton svg {
  width: 1rem;
  height: 1rem;
}

.notice {
  color: var(--success-text);
}

/* ------------------------------------------------------------------ panel y filtros */

.panel {
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  overflow: hidden;
}

.filtros {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 0.875rem;
  border-bottom: 1px solid var(--border);
}

.buscador {
  position: relative;
  display: block;
  width: min(24rem, 100%);
}

.buscador svg {
  position: absolute;
  top: 50%;
  left: 0.625rem;
  translate: 0 -50%;
  width: 1.125rem;
  height: 1.125rem;
  color: var(--text-muted);
}

.buscador input {
  width: 100%;
  padding-left: 2.125rem;
}

/* ------------------------------------------------------------------ tabla */

table {
  width: 100%;
  border-collapse: collapse;
}

th,
td {
  padding: 0.75rem 0.875rem;
  text-align: left;
  vertical-align: middle;
}

th {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-muted);
  background: var(--surface-alt);
}

tbody .fila {
  border-top: 1px solid var(--border);
}

tbody .fila:hover {
  background: var(--surface-alt);
}

tbody .fila.abierta {
  background: var(--surface-alt);
}

.cliente {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0;
  border: 0;
  background: none;
  color: inherit;
  text-align: left;
  cursor: pointer;
  font: inherit;
}

.avatar {
  display: grid;
  place-items: center;
  width: 2.25rem;
  height: 2.25rem;
  flex: none;
  border-radius: 50%;
  background: color-mix(in srgb, var(--brand) 12%, transparent);
  color: var(--brand);
  font-size: 0.8125rem;
  font-weight: 700;
}

.cliente-texto {
  display: grid;
}

.cliente:hover strong {
  color: var(--brand);
}

.bloque {
  display: block;
}

.pastilla {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.2rem 0.5rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  font-family: var(--font-mono);
  font-size: 0.8125rem;
}

.pastilla svg {
  width: 1rem;
  height: 1rem;
  color: var(--text-muted);
}

.acceso {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
}

.acceso .punto {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
  background: var(--success);
}

.acciones {
  display: flex;
  justify-content: flex-end;
  gap: 0.25rem;
}

.icono {
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2rem;
  padding: 0;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text-muted);
  cursor: pointer;
}

.icono:hover {
  color: var(--brand);
  border-color: var(--brand);
}

.icono svg {
  width: 1.0625rem;
  height: 1.0625rem;
  transition: rotate 0.15s ease;
}

.icono.wa:hover {
  color: #1fa971;
  border-color: #1fa971;
}

/* ------------------------------------------------------------------ ficha desplegada */

.desplegado td {
  padding: 0;
  border-top: 1px solid var(--border);
  background: var(--surface-alt);
}

.desplegado > td > * {
  padding: 0.875rem;
}

.ficha {
  display: grid;
  gap: 1.25rem;
}

@media (min-width: 60rem) {
  .ficha {
    grid-template-columns: 1fr 1fr;
  }
}

.rtn {
  display: flex;
  align-items: flex-end;
  gap: 0.75rem;
  flex-wrap: wrap;
  padding: 0;
}

.rtn label,
.access-form label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.pie-rtn {
  flex-basis: 100%;
  line-height: 1.5;
}

.access-form {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 0.5rem;
  padding-bottom: 0;
}

.rotulo {
  margin: 0 0 0.5rem;
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-muted);
}

.vehiculos ul {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.vehiculos li {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.plate {
  padding: 0.1rem 0.35rem;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--surface);
  font-size: 0.8125rem;
}

.vacio {
  margin: 0;
  padding: 2rem 0.875rem;
  text-align: center;
}
</style>

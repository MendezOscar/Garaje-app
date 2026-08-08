<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { customersApi, vehiclesApi } from '@/api/garaj'
import { VEHICLE_TYPE_LABEL, type Customer, type Vehicle } from '@/types/domain'
import { whatsappLink } from '@/utils/format'

const customers = ref<Customer[]>([])
const search = ref('')
const loading = ref(false)
const error = ref('')

/** Vehículos del cliente expandido. Se cargan al abrir, no antes: la lista sería enorme. */
const expandedId = ref<string | null>(null)
const vehicles = ref<Vehicle[]>([])

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
  vehicles.value = []
  vehicles.value = (await vehiclesApi.list({ customerId: customer.id })).items
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

    <table>
      <thead>
        <tr>
          <th>Cliente</th>
          <th>Teléfono</th>
          <th>Vehículos</th>
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
              <a :href="whatsappLink(customer.phone)" target="_blank" rel="noopener">WhatsApp</a>
            </td>
          </tr>
          <tr v-if="expandedId === customer.id">
            <td colspan="4" class="vehicles">
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

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { laborServicesApi } from '@/api/garaj'
import type { LaborService } from '@/types/domain'
import { formatMoney } from '@/utils/format'

/**
 * Maestro de mano de obra: la lista de trabajos que el taller cobra, con su tarifa.
 *
 * Es lo que hace que "cambio de aceite" valga lo mismo el martes que el viernes, y lo que
 * permite separar el ingreso por mano de obra del de repuestos en los reportes.
 */
const services = ref<LaborService[]>([])
const includeInactive = ref(false)
const search = ref('')

const loading = ref(false)
const busy = ref(false)
const error = ref('')

const empty = {
  id: '',
  code: '',
  name: '',
  description: '',
  category: '',
  standardHours: 1,
  hourlyRate: 0,
  isFixedPrice: false,
  fixedPrice: 0,
  isActive: true,
}

const form = ref({ ...empty })
const editing = ref(false)

/** Lo que se cobraría con lo que hay escrito en el formulario, para verlo antes de guardar. */
const preview = computed(() =>
  form.value.isFixedPrice
    ? form.value.fixedPrice
    : form.value.standardHours * form.value.hourlyRate,
)

const visible = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return services.value

  return services.value.filter(
    (s) =>
      s.name.toLowerCase().includes(term) ||
      s.code.toLowerCase().includes(term) ||
      (s.category ?? '').toLowerCase().includes(term),
  )
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    services.value = await laborServicesApi.list(includeInactive.value)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el catálogo.')
  } finally {
    loading.value = false
  }
}

function edit(service: LaborService) {
  editing.value = true
  form.value = {
    id: service.id,
    code: service.code,
    name: service.name,
    description: service.description ?? '',
    category: service.category ?? '',
    standardHours: service.standardHours,
    hourlyRate: service.hourlyRate,
    isFixedPrice: service.isFixedPrice,
    fixedPrice: service.fixedPrice,
    isActive: service.isActive,
  }
}

function reset() {
  editing.value = false
  form.value = { ...empty }
}

async function save() {
  const f = form.value
  if (!f.code.trim() || !f.name.trim()) return

  busy.value = true
  error.value = ''
  try {
    const body = {
      code: f.code.trim().toUpperCase(),
      name: f.name.trim(),
      description: f.description.trim() || null,
      category: f.category.trim() || null,
      standardHours: Number(f.standardHours) || 0,
      hourlyRate: Number(f.hourlyRate) || 0,
      isFixedPrice: f.isFixedPrice,
      fixedPrice: Number(f.fixedPrice) || 0,
      isActive: f.isActive,
    }

    if (editing.value) await laborServicesApi.update(f.id, body)
    else await laborServicesApi.create(body)

    reset()
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo guardar el servicio.')
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
        <h1>Mano de obra</h1>
        <p class="muted small">
          Lo que el taller cobra por trabajar, aparte de los repuestos. Cada paso de una orden
          se cobra por el servicio que lleve de esta lista.
        </p>
      </div>
    </header>

    <p v-if="error" class="error">{{ error }}</p>

    <div class="layout">
      <div>
        <div class="filters">
          <input v-model="search" placeholder="Buscar por nombre, código o categoría" />
          <label class="checkbox">
            <input v-model="includeInactive" type="checkbox" @change="load" />
            Ver los dados de baja
          </label>
        </div>

        <p v-if="loading" class="muted">Cargando…</p>
        <p v-else-if="!visible.length" class="muted">
          No hay servicios que coincidan. Agregue el primero en el formulario de al lado.
        </p>

        <table v-else>
          <thead>
            <tr>
              <th>Código</th>
              <th>Servicio</th>
              <th>Cómo se cobra</th>
              <th class="num">Precio</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="s in visible" :key="s.id" :class="{ inactive: !s.isActive }">
              <td class="code">{{ s.code }}</td>
              <td>
                {{ s.name }}
                <div v-if="s.category" class="muted small">{{ s.category }}</div>
              </td>
              <td class="muted small">
                <template v-if="s.isFixedPrice">Precio fijo</template>
                <template v-else>
                  {{ s.standardHours }} h × {{ formatMoney(s.hourlyRate) }}
                </template>
              </td>
              <td class="num">{{ formatMoney(s.price) }}</td>
              <td>
                <button type="button" class="link" @click="edit(s)">Editar</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <form class="card" @submit.prevent="save">
        <h2>{{ editing ? 'Editar servicio' : 'Nuevo servicio' }}</h2>

        <label>
          Código
          <input v-model="form.code" placeholder="MO-ACE" :disabled="editing" required />
        </label>
        <label>
          Nombre
          <input v-model="form.name" placeholder="Cambio de aceite y filtro" required />
        </label>
        <label>
          Categoría
          <input v-model="form.category" placeholder="Mantenimiento" />
        </label>

        <label class="checkbox">
          <input v-model="form.isFixedPrice" type="checkbox" />
          Precio fijo, sin importar las horas
        </label>

        <template v-if="form.isFixedPrice">
          <label>
            Precio
            <input v-model.number="form.fixedPrice" type="number" min="0" step="0.01" />
          </label>
        </template>
        <template v-else>
          <div class="row">
            <label>
              Horas estándar
              <input v-model.number="form.standardHours" type="number" min="0" step="0.25" />
            </label>
            <label>
              Tarifa por hora
              <input v-model.number="form.hourlyRate" type="number" min="0" step="0.01" />
            </label>
          </div>
        </template>

        <p class="preview">Se cobrará <strong>{{ formatMoney(preview) }}</strong></p>

        <label v-if="editing" class="checkbox">
          <input v-model="form.isActive" type="checkbox" />
          Activo
        </label>

        <div class="actions">
          <button type="submit" :disabled="busy">{{ editing ? 'Guardar' : 'Agregar' }}</button>
          <button v-if="editing" type="button" class="link" @click="reset">Cancelar</button>
        </div>

        <p class="muted small">
          Un servicio no se borra: se da de baja. Las órdenes viejas siguen apuntando a él y
          borrarlo dejaría sin explicación lo que ya se cobró.
        </p>
      </form>
    </div>
  </section>
</template>

<style scoped>
h1 {
  margin: 0;
}

.top {
  margin-bottom: 1rem;
}

.layout {
  display: grid;
  grid-template-columns: 1fr 20rem;
  gap: 1rem;
  align-items: start;
}

@media (max-width: 60rem) {
  .layout {
    grid-template-columns: 1fr;
  }
}

.filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
}

.filters input[type='text'],
.filters input:not([type]) {
  flex: 1;
  min-width: 12rem;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}

th,
td {
  padding: 0.5rem 0.5rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
  vertical-align: top;
}

th {
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.num {
  text-align: right;
}

.code {
  font-family: ui-monospace, monospace;
}

.inactive {
  opacity: 0.55;
}

.card {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.card h2 {
  margin: 0;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.card label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.card input {
  width: 100%;
}

.row {
  display: flex;
  gap: 0.5rem;
}

.checkbox {
  flex-direction: row !important;
  align-items: center;
  gap: 0.5rem;
}

.checkbox input {
  width: auto;
}

.preview {
  margin: 0.25rem 0;
  font-size: 0.875rem;
}

.actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.link {
  padding: 0;
  border: none;
  background: none;
  color: var(--accent);
  cursor: pointer;
}

.muted {
  color: var(--text-muted);
}

.small {
  font-size: 0.75rem;
}

.error {
  color: var(--danger);
}
</style>

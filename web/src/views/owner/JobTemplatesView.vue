<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { jobTemplatesApi, laborServicesApi, partsApi } from '@/api/garaj'
import type { JobTemplate, LaborService, Part } from '@/types/domain'
import { formatMoney } from '@/utils/format'

/**
 * Trabajos frecuentes: el cambio de aceite, las pastillas de adelante, lo que el taller repite
 * cientos de veces al año.
 *
 * La mayoría se crean solos desde una orden ya hecha —el botón «Guardar como trabajo
 * frecuente»—, que es el camino bueno: ahí los pasos y los repuestos ya están y ya están bien.
 * Esta pantalla es para corregirlos, renombrarlos y darlos de baja.
 */
const templates = ref<JobTemplate[]>([])
const services = ref<LaborService[]>([])
const parts = ref<Part[]>([])

const includeInactive = ref(false)
const search = ref('')

const loading = ref(false)
const busy = ref(false)
const error = ref('')

type TaskForm = {
  title: string
  laborServiceId: string
  estimatedHours: number | null
}

type PartForm = {
  partId: string
  description: string
  quantity: number
}

const empty = () => ({
  id: '',
  name: '',
  description: '',
  isActive: true,
  tasks: [] as TaskForm[],
  parts: [] as PartForm[],
})

const form = ref(empty())
const editing = ref(false)

const visible = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return templates.value

  return templates.value.filter((t) => t.name.toLowerCase().includes(term))
})

/** Lo que costaría el trabajo con lo que hay escrito, para verlo antes de guardar. */
const preview = computed(() => {
  const labor = form.value.tasks.reduce((sum, t) => {
    const service = services.value.find((s) => s.id === t.laborServiceId)
    if (!service) return sum

    return sum + (service.isFixedPrice
      ? service.fixedPrice
      : (t.estimatedHours ?? service.standardHours) * service.hourlyRate)
  }, 0)

  const pieces = form.value.parts.reduce((sum, p) => {
    const part = parts.value.find((c) => c.id === p.partId)
    return part ? sum + part.salePrice * (Number(p.quantity) || 0) : sum
  }, 0)

  return labor + pieces
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    templates.value = await jobTemplatesApi.list(includeInactive.value)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar los trabajos frecuentes.')
  } finally {
    loading.value = false
  }
}

async function loadCatalogs() {
  try {
    services.value = await laborServicesApi.list()
    // El catálogo de un taller son decenas de repuestos, no miles: cabe entero en el selector
    // y evita un buscador para elegir el empaque de siempre.
    parts.value = (await partsApi.list({ pageSize: 500 })).items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el catálogo.')
  }
}

function edit(template: JobTemplate) {
  editing.value = true
  form.value = {
    id: template.id,
    name: template.name,
    description: template.description ?? '',
    isActive: template.isActive,
    tasks: template.tasks.map((t) => ({
      title: t.title,
      laborServiceId: t.laborServiceId ?? '',
      estimatedHours: t.estimatedHours,
    })),
    parts: template.parts.map((p) => ({
      partId: p.partId ?? '',
      description: p.partId ? '' : p.partName,
      quantity: p.quantity,
    })),
  }
}

function reset() {
  editing.value = false
  form.value = empty()
}

function addTask() {
  form.value.tasks.push({ title: '', laborServiceId: '', estimatedHours: null })
}

function addPart() {
  form.value.parts.push({ partId: '', description: '', quantity: 1 })
}

/** Al elegir el servicio se copia su nombre si el paso no tiene título: es el 90% de los casos. */
function serviceChosen(task: TaskForm) {
  if (task.title.trim()) return

  const service = services.value.find((s) => s.id === task.laborServiceId)
  if (service) task.title = service.name
}

async function save() {
  const f = form.value
  if (!f.name.trim()) return

  busy.value = true
  error.value = ''
  try {
    const body = {
      name: f.name.trim(),
      description: f.description.trim() || null,
      isActive: f.isActive,
      tasks: f.tasks
        .filter((t) => t.title.trim() || t.laborServiceId)
        .map((t) => ({
          title: t.title.trim(),
          laborServiceId: t.laborServiceId || null,
          estimatedHours: t.estimatedHours,
        })),
      parts: f.parts
        .filter((p) => p.partId || p.description.trim())
        .map((p) => ({
          partId: p.partId || null,
          description: p.partId ? null : p.description.trim(),
          quantity: Number(p.quantity) || 0,
        })),
    }

    if (editing.value) await jobTemplatesApi.update(f.id, body)
    else await jobTemplatesApi.create(body)

    reset()
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo guardar el trabajo frecuente.')
  } finally {
    busy.value = false
  }
}

async function deactivate(template: JobTemplate) {
  if (!confirm(`¿Dar de baja «${template.name}»? Dejará de aparecer al armar una orden.`)) return

  busy.value = true
  error.value = ''
  try {
    await jobTemplatesApi.deactivate(template.id)
    if (form.value.id === template.id) reset()
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo dar de baja.')
  } finally {
    busy.value = false
  }
}

onMounted(async () => {
  await Promise.all([load(), loadCatalogs()])
})
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Trabajos frecuentes</h1>
        <p class="muted small">
          Lo que el taller repite —el cambio de aceite, las pastillas de adelante— guardado con
          sus pasos y sus repuestos, para armar la orden de un toque en vez de teclearla.
        </p>
      </div>
    </header>

    <p v-if="error" class="error">{{ error }}</p>

    <div class="layout">
      <div>
        <div class="filters">
          <input v-model="search" placeholder="Buscar por nombre" />
          <label class="checkbox">
            <input v-model="includeInactive" type="checkbox" @change="load" />
            Ver los dados de baja
          </label>
        </div>

        <p v-if="loading" class="muted">Cargando…</p>
        <p v-else-if="!visible.length" class="muted vacio">
          Todavía no hay ninguno. El camino corto es no escribirlos aquí: abra una orden que ya
          esté armada y pulse <strong>Guardar como trabajo frecuente</strong>. Sus pasos y sus
          repuestos ya están bien porque salieron de un trabajo real.
        </p>

        <table v-else>
          <thead>
            <tr>
              <th>Trabajo</th>
              <th>Lleva</th>
              <th class="num">Precio de hoy</th>
              <th class="num">Usado</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="t in visible" :key="t.id" :class="{ inactive: !t.isActive }">
              <td>
                {{ t.name }}
                <div v-if="t.description" class="muted small">{{ t.description }}</div>
              </td>
              <td class="muted small">
                {{ t.tasks.length }} paso{{ t.tasks.length === 1 ? '' : 's' }} ·
                {{ t.parts.length }} repuesto{{ t.parts.length === 1 ? '' : 's' }}
              </td>
              <td class="num">{{ formatMoney(t.total) }}</td>
              <td class="num muted">{{ t.usageCount }}</td>
              <td class="acciones">
                <button type="button" class="link" @click="edit(t)">Editar</button>
                <button
                  v-if="t.isActive"
                  type="button"
                  class="link"
                  :disabled="busy"
                  @click="deactivate(t)"
                >
                  Dar de baja
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <form class="card" @submit.prevent="save">
        <h2>{{ editing ? 'Editar trabajo' : 'Nuevo trabajo' }}</h2>

        <label>
          Nombre
          <input v-model="form.name" placeholder="Cambio de aceite y filtro" required />
        </label>
        <label>
          Nota
          <input v-model="form.description" placeholder="Para motos de 150 cc" />
        </label>

        <h3>Pasos</h3>
        <div v-for="(task, i) in form.tasks" :key="i" class="linea">
          <select v-model="task.laborServiceId" @change="serviceChosen(task)">
            <option value="">Sin cobro de mano de obra</option>
            <option v-for="s in services" :key="s.id" :value="s.id">{{ s.name }}</option>
          </select>
          <input v-model="task.title" placeholder="Qué se hace" />
          <div class="fila">
            <input
              v-model.number="task.estimatedHours"
              type="number"
              min="0"
              step="0.25"
              placeholder="Horas"
            />
            <button type="button" class="link" @click="form.tasks.splice(i, 1)">Quitar</button>
          </div>
        </div>
        <button type="button" class="link" @click="addTask">+ Agregar paso</button>

        <h3>Repuestos</h3>
        <div v-for="(part, i) in form.parts" :key="i" class="linea">
          <select v-model="part.partId">
            <option value="">Fuera del catálogo</option>
            <option v-for="p in parts" :key="p.id" :value="p.id">
              {{ p.sku }} · {{ p.name }}
            </option>
          </select>
          <input
            v-if="!part.partId"
            v-model="part.description"
            placeholder="De qué se trata"
          />
          <div class="fila">
            <input v-model.number="part.quantity" type="number" min="0" step="0.01" />
            <button type="button" class="link" @click="form.parts.splice(i, 1)">Quitar</button>
          </div>
        </div>
        <button type="button" class="link" @click="addPart">+ Agregar repuesto</button>

        <p class="preview">
          A precios de hoy: <strong>{{ formatMoney(preview) }}</strong>
        </p>

        <label v-if="editing" class="checkbox">
          <input v-model="form.isActive" type="checkbox" />
          Activo
        </label>

        <div class="actions">
          <button type="submit" :disabled="busy">{{ editing ? 'Guardar' : 'Agregar' }}</button>
          <button v-if="editing" type="button" class="link" @click="reset">Cancelar</button>
        </div>

        <p class="muted small">
          El trabajo guarda a qué apunta, no cuánto cuesta: si mañana sube el precio de un
          repuesto, sube aquí también. Y no se borra, se da de baja, porque hay órdenes que se
          armaron con él.
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
  grid-template-columns: 1fr 22rem;
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

.filters input:not([type='checkbox']) {
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
  padding: 0.5rem;
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

.acciones {
  display: flex;
  gap: 0.75rem;
}

.inactive {
  opacity: 0.55;
}

.vacio {
  max-width: 34rem;
  line-height: 1.5;
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

.card h3 {
  margin: 0.5rem 0 0;
  font-size: 0.8125rem;
}

.card label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.card input,
.card select {
  width: 100%;
}

.linea {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  padding: 0.5rem;
  border: 1px solid var(--border);
  border-radius: 6px;
}

.fila {
  display: flex;
  align-items: center;
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

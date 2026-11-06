<script setup lang="ts">
import { computed, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { workOrdersApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import type { AcceptWorkStepsResult, ProposedTask, TaskHistory, WorkStepsProposal } from '@/types/domain'
import { formatMoney } from '@/utils/format'

/**
 * «Proponer los pasos»: del diagnóstico sale una lista que el dueño edita y acepta.
 *
 * Todo lo que se ve aquí es un borrador. Nada entra a la orden hasta que se toca «Aceptar»,
 * y por eso el botón dice eso y no «Guardar»: lo que se guarda es lo que la persona corrigió,
 * no lo que el asistente propuso.
 */
const props = defineProps<{
  workOrderId: string
  /** Si el taller tiene el asistente. Sin esto el botón sencillamente no existe. */
  enabled: boolean
  canEdit: boolean
  /** Sin diagnóstico no hay de dónde sacar los pasos, y el servidor ni llama al modelo. */
  hasDiagnosis: boolean
}>()

const emit = defineEmits<{ accepted: [AcceptWorkStepsResult] }>()

const propuesta = ref<WorkStepsProposal | null>(null)
const pasos = ref<(ProposedTask & { incluido: boolean })[]>([])
const pensando = ref(false)
const guardando = ref(false)
const error = ref('')

const incluidos = computed(() => pasos.value.filter((p) => p.incluido))

/** Los repuestos que siguen en pie: los de los pasos que no se tacharon. */
const repuestos = computed(() => {
  const vivos = new Set(incluidos.value.map((p) => p.title))
  return (propuesta.value?.parts ?? []).filter((r) => vivos.has(r.forTask) || vivos.size === 0)
})

async function proponer() {
  pensando.value = true
  error.value = ''

  try {
    const r = await workOrdersApi.proposeSteps(props.workOrderId)
    propuesta.value = r
    pasos.value = r.tasks.map((t) => ({ ...t, incluido: true }))
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    pensando.value = false
  }
}

async function aceptar() {
  if (!propuesta.value?.id || incluidos.value.length === 0) return

  guardando.value = true
  error.value = ''

  try {
    const r = await workOrdersApi.acceptSteps(
      props.workOrderId,
      propuesta.value.id,
      incluidos.value.map((p) => ({
        title: p.title,
        description: p.description,
        laborServiceId: p.laborServiceId,
        estimatedHours: p.hours || null,
      })),
    )

    emit('accepted', r)
    cerrar()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    guardando.value = false
  }
}

/**
 * Lo que el taller ha cobrado por ese trabajo antes. Sale del servidor, de sus propias órdenes
 * entregadas: es lo que se enseña en vez de un precio inventado.
 */
function cobradoAntes(h: TaskHistory): string {
  const veces = `${h.times} ${h.times === 1 ? 'vez' : 'veces'}`

  if (h.priceFrom === null || h.priceTo === null) {
    return h.typicalHours === null
      ? `Hecho ${veces} antes`
      : `Hecho ${veces} antes · ${h.typicalHours} h`
  }

  const precio =
    h.priceFrom === h.priceTo
      ? formatMoney(h.priceFrom)
      : `${formatMoney(h.priceFrom)} a ${formatMoney(h.priceTo)}`

  return `${veces} antes · cobrado ${precio}`
}

function cerrar() {
  propuesta.value = null
  pasos.value = []
  error.value = ''
}
</script>

<template>
  <button
    v-if="enabled && canEdit"
    type="button"
    class="chico"
    :disabled="pensando || !hasDiagnosis"
    :title="hasDiagnosis ? '' : 'Escriba primero el diagnóstico'"
    @click="proponer"
  >
    {{ pensando ? 'Pensando…' : 'Proponer los pasos' }}
  </button>

  <div v-if="propuesta" class="propuesta">
    <ErrorNote v-if="error" :message="error" />

    <!-- Que no se pueda no es un error: es que se acabaron las consultas del día o que falta
         el diagnóstico. Se dice con esas palabras y la orden sigue funcionando igual. -->
    <p v-if="!propuesta.available" class="muted">{{ propuesta.message }}</p>

    <template v-else>
      <p class="muted small">
        Es una propuesta. Tache lo que no, corrija las horas y acepte: hasta entonces la orden
        no cambia.
      </p>

      <p v-if="propuesta.note" class="nota">{{ propuesta.note }}</p>

      <p v-if="!pasos.length" class="muted">
        El asistente no propuso pasos con lo que hay escrito.
      </p>

      <ul v-else class="pasos">
        <li v-for="(paso, i) in pasos" :key="i" :class="{ fuera: !paso.incluido }">
          <label>
            <input v-model="paso.incluido" type="checkbox" />
            <span>
              <strong>{{ paso.title }}</strong>
              <em v-if="paso.description" class="muted small">{{ paso.description }}</em>
              <em class="muted small">
                {{ paso.laborServiceName ?? 'Sin servicio del catálogo' }}
              </em>
              <!-- El número viene de las órdenes que este taller ya cobró, no del asistente. -->
              <em v-if="paso.history" class="antes">{{ cobradoAntes(paso.history) }}</em>
            </span>
          </label>
          <label class="horas">
            <input v-model.number="paso.hours" type="number" min="0" max="40" step="0.25" />
            h
          </label>
        </li>
      </ul>

      <!-- Los repuestos no se cargan al aceptar: cargarlos descuenta la bodega y el trabajo
           todavía no se ha hecho. Se enseñan para que se vea qué va a hacer falta. -->
      <div v-if="repuestos.length" class="repuestos">
        <p class="muted small">Repuestos que llevaría, para cargarlos cuando se usen:</p>
        <ul>
          <li v-for="(r, i) in repuestos" :key="i">
            {{ r.quantity }} {{ r.unit }} · {{ r.name }}
            <em v-if="r.available < r.quantity" class="sin-existencia">
              quedan {{ r.available }}
            </em>
          </li>
        </ul>
      </div>

      <!-- Lo que el trabajo lleva y no está en el catálogo. Separado de lo de arriba a
           propósito: de esto no hay existencia, hay que comprarlo o darlo de alta. -->
      <div v-if="propuesta.missing?.length" class="faltantes">
        <p class="muted small">Esto haría falta y no está en su catálogo:</p>
        <ul>
          <li v-for="(f, i) in propuesta.missing" :key="i">
            {{ f.quantity }} · {{ f.name }}
          </li>
        </ul>
      </div>

      <div class="acciones">
        <button
          type="button"
          :disabled="guardando || incluidos.length === 0"
          @click="aceptar"
        >
          Aceptar {{ incluidos.length }} {{ incluidos.length === 1 ? 'paso' : 'pasos' }}
        </button>
        <button type="button" class="link" :disabled="guardando" @click="cerrar">Descartar</button>
        <span class="muted small">
          Quedan {{ propuesta.creditsLeftToday }} consultas hoy
        </span>
      </div>
    </template>

    <button v-if="!propuesta.available" type="button" class="link" @click="cerrar">Cerrar</button>
  </div>
</template>

<style scoped>
.propuesta {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 0.75rem;
  margin-top: 0.75rem;
  background: var(--surface-alt, transparent);
}

.nota {
  font-size: 0.85rem;
  border-left: 3px solid var(--accent);
  padding-left: 0.6rem;
  margin: 0.5rem 0;
}

.pasos {
  list-style: none;
  padding: 0;
  margin: 0.5rem 0;
}

.pasos li {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.5rem;
  padding: 0.4rem 0;
  border-top: 1px solid var(--border);
}

.pasos li:first-child {
  border-top: 0;
}

.pasos li.fuera {
  opacity: 0.45;
}

.pasos label {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
}

.pasos label span {
  display: flex;
  flex-direction: column;
}

.pasos em {
  font-style: normal;
}

.horas {
  white-space: nowrap;
  font-size: 0.85rem;
}

.horas input {
  width: 4.5rem;
  text-align: right;
}

.antes {
  color: var(--accent);
  font-size: 0.8rem;
}

.faltantes {
  border: 1px dashed var(--border);
  border-radius: 6px;
  padding: 0.5rem 0.75rem;
  margin-top: 0.75rem;
}

.faltantes ul {
  list-style: none;
  padding: 0;
  margin: 0.25rem 0 0;
  font-size: 0.85rem;
}

.repuestos ul {
  list-style: none;
  padding: 0;
  margin: 0.25rem 0 0;
  font-size: 0.85rem;
}

.sin-existencia {
  color: var(--danger);
  font-style: normal;
  font-size: 0.8rem;
}

.acciones {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-top: 0.75rem;
  flex-wrap: wrap;
}
</style>

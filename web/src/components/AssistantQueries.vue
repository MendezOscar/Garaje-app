<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { workOrdersApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import type { Conversation, ConversationListItem } from '@/types/domain'
import { formatDateTime } from '@/utils/format'

/**
 * Preguntar con el vehículo delante, con el contexto de la orden ya puesto.
 *
 * Lo que vale y nadie más puede dar es el historial: qué le hizo **este** taller a **este**
 * modelo con el mismo síntoma. Eso vive dentro de órdenes cerradas que nadie va a abrir.
 */
const props = defineProps<{ workOrderId: string; enabled: boolean }>()

const anteriores = ref<ConversationListItem[]>([])
const abierta = ref<Conversation | null>(null)
const pregunta = ref('')
const pensando = ref(false)
const cargando = ref(true)
const error = ref('')

onMounted(async () => {
  try {
    anteriores.value = await workOrdersApi.queries(props.workOrderId)
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    cargando.value = false
  }
})

async function abrir(id: string) {
  error.value = ''
  try {
    abierta.value = await workOrdersApi.query(props.workOrderId, id)
  } catch (e) {
    error.value = errorMessage(e)
  }
}

async function preguntar() {
  const texto = pregunta.value.trim()
  if (!texto || pensando.value) return

  pensando.value = true
  error.value = ''

  try {
    const r = await workOrdersApi.ask(props.workOrderId, texto, abierta.value?.id ?? undefined)
    abierta.value = r
    // Solo se limpia si la pregunta llegó a entrar: si no, lo escrito no se pierde.
    if (r.turns.length) pregunta.value = ''
    anteriores.value = await workOrdersApi.queries(props.workOrderId)
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    pensando.value = false
  }
}

function nueva() {
  abierta.value = null
  pregunta.value = ''
  error.value = ''
}
</script>

<template>
  <section v-if="enabled" class="consultas">
    <ErrorNote v-if="error" :message="error" />

    <p v-if="cargando" class="muted small">Cargando…</p>

    <template v-else>
      <!-- Las anteriores de esta orden. Cada una es una pregunta concreta, no un hilo largo. -->
      <div v-if="anteriores.length && !abierta" class="anteriores">
        <button
          v-for="c in anteriores"
          :key="c.id"
          type="button"
          class="anterior"
          @click="abrir(c.id)"
        >
          <span class="titulo">{{ c.title }}</span>
          <span class="muted small">
            {{ formatDateTime(c.createdAt) }}
            <template v-if="c.isClosed"> · cerrada</template>
          </span>
        </button>
      </div>

      <div v-if="abierta" class="hilo">
        <div v-for="(t, i) in abierta.turns" :key="i" class="turno" :class="{ mio: t.fromUser }">
          <p>{{ t.content }}</p>
        </div>

        <p v-if="abierta.message" class="muted small">{{ abierta.message }}</p>

        <p v-if="abierta.isClosed" class="muted small">
          <button type="button" class="link" @click="nueva">Abrir otra consulta</button>
        </p>
      </div>

      <form v-if="!abierta?.isClosed" class="preguntar" @submit.prevent="preguntar">
        <textarea
          v-model="pregunta"
          rows="2"
          :disabled="pensando"
          placeholder="¿Qué quiere preguntar? Una cosa concreta se contesta mejor."
        ></textarea>
        <div class="pie">
          <button type="submit" :disabled="pensando || !pregunta.trim()">
            {{ pensando ? 'Pensando…' : 'Preguntar' }}
          </button>
          <button v-if="abierta" type="button" class="link" @click="nueva">Empezar de nuevo</button>
          <span v-if="abierta" class="muted small">
            Quedan {{ abierta.creditsLeftToday }} consultas hoy
          </span>
        </div>
      </form>

      <p class="muted small aviso">
        No da torques ni capacidades del fabricante: eso se busca en el manual de la marca. Lo
        que sí sabe es qué hizo este taller antes con el mismo modelo.
      </p>
    </template>
  </section>
</template>

<style scoped>
.consultas {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.anteriores {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.anterior {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.1rem;
  text-align: left;
  background: none;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 0.5rem 0.7rem;
  cursor: pointer;
  color: inherit;
}

.anterior .titulo {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 100%;
}

.hilo {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.turno p {
  margin: 0;
  white-space: pre-wrap;
  border-radius: 8px;
  padding: 0.6rem 0.8rem;
  border: 1px solid var(--border);
}

/* Lo que escribió la persona va marcado y alineado a la derecha: en una conversación corta,
   saber de un vistazo quién dijo qué es la mitad de poder leerla. */
.turno.mio p {
  background: var(--surface-alt, transparent);
  border-color: var(--accent);
  margin-left: 2rem;
}

.preguntar textarea {
  width: 100%;
  resize: vertical;
}

.pie {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-top: 0.5rem;
  flex-wrap: wrap;
}

.aviso {
  border-top: 1px solid var(--border);
  padding-top: 0.5rem;
}
</style>

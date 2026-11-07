<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { businessReadingsApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import { useAuthStore } from '@/stores/auth'
import type { BusinessReading, BusinessReadingListItem } from '@/types/domain'
import { formatDate } from '@/utils/format'

/**
 * La lectura del negocio: qué pasó con las ventas, el inventario y los técnicos, y qué
 * conviene hacer.
 *
 * El tablero enseña números, y los números no dicen qué hacer. El dueño ve que el margen bajó
 * y no sabe si fue el repuesto que subió, el técnico que se tardó o el descuento que dio el
 * encargado.
 *
 * Se arma sola de madrugada los lunes. El botón está para no esperar al lunes.
 */
const auth = useAuthStore()

const lecturas = ref<BusinessReadingListItem[]>([])
const abierta = ref<BusinessReading | null>(null)
const cargando = ref(true)
const armando = ref(false)
const error = ref('')

onMounted(async () => {
  try {
    lecturas.value = await businessReadingsApi.list()
    if (lecturas.value.length) await abrir(lecturas.value[0].id)
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    cargando.value = false
  }
})

async function abrir(id: string) {
  error.value = ''
  try {
    abierta.value = await businessReadingsApi.get(id)
  } catch (e) {
    error.value = errorMessage(e)
  }
}

async function armar(kind: 1 | 2) {
  armando.value = true
  error.value = ''

  try {
    const r = await businessReadingsApi.generate(kind)
    abierta.value = r
    if (r.id) lecturas.value = await businessReadingsApi.list()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    armando.value = false
  }
}

/**
 * El texto llega en Markdown sencillo —tres marcas, las que el prompt pide— y se parte en
 * bloques aquí en vez de pintarlo con `v-html`.
 *
 * Es texto que escribió un modelo: meterlo como HTML en la página sería abrir una puerta a
 * cambio de ahorrarse veinte líneas.
 */
type Bloque = { tipo: 'titulo' | 'vineta' | 'parrafo'; partes: { texto: string; fuerte: boolean }[] }

const bloques = computed<Bloque[]>(() => {
  const texto = abierta.value?.content ?? ''

  return texto
    .replace(/\r/g, '')
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l.length > 0)
    .map((linea) => {
      if (linea.startsWith('## ')) return { tipo: 'titulo' as const, partes: negritas(linea.slice(3)) }
      if (linea.startsWith('# ')) return { tipo: 'titulo' as const, partes: negritas(linea.slice(2)) }
      if (linea.startsWith('- ') || linea.startsWith('* '))
        return { tipo: 'vineta' as const, partes: negritas(linea.slice(2)) }
      return { tipo: 'parrafo' as const, partes: negritas(linea) }
    })
})

/** `**así**` queda en negrita. Es la única marca dentro de una línea. */
function negritas(texto: string) {
  return texto
    .split('**')
    .map((parte, i) => ({ texto: parte, fuerte: i % 2 === 1 }))
    .filter((p) => p.texto.length > 0)
}

const titulo = computed(() => {
  const l = abierta.value
  if (!l) return ''

  const que = l.kind === 1 ? 'Semana' : 'Mes'
  return `${que} del ${formatDate(l.periodFrom)} al ${formatDate(l.periodTo)}`
})

const bajando = ref(false)

async function bajarPdf() {
  // Sin id no hay PDF: es la lectura que no se armó por falta de volumen, que solo trae el
  // motivo.
  const lectura = abierta.value
  if (!lectura?.id) return

  bajando.value = true
  error.value = ''

  try {
    // El nombre se arma con las fechas como vienen del servidor y no con `titulo`: ese lleva
    // la fecha con barras, y una barra en el nombre de un archivo no es un nombre.
    const que = lectura.kind === 1 ? 'Semana' : 'Mes'

    await businessReadingsApi.downloadPdf(
      lectura.id,
      `Lectura ${que} ${lectura.periodFrom} a ${lectura.periodTo}`,
    )
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    bajando.value = false
  }
}
</script>

<template>
  <section class="vista">
    <header>
      <div>
        <h1>La lectura del negocio</h1>
        <p class="muted small">
          Qué pasó con las ventas, el inventario y los técnicos, y tres cosas que conviene
          hacer. Se arma sola los lunes de madrugada.
        </p>
      </div>
      <div v-if="auth.hasAssistant" class="acciones">
        <button type="button" :disabled="armando" @click="armar(1)">
          {{ armando ? 'Armando…' : 'La semana pasada' }}
        </button>
        <button type="button" class="link" :disabled="armando" @click="armar(2)">
          El mes pasado
        </button>
      </div>
    </header>

    <ErrorNote v-if="error" :message="error" />

    <p v-if="!auth.hasAssistant" class="muted">
      Esta función va con el plan Pro. Escríbanos si quiere activarla.
    </p>

    <p v-else-if="cargando" class="muted">Cargando…</p>

    <div v-else class="cuerpo">
      <!-- Las anteriores a la izquierda: el dueño compara la de esta semana con la de hace un
           mes, y por eso ninguna se rehace. -->
      <nav v-if="lecturas.length" class="anteriores">
        <button
          v-for="l in lecturas"
          :key="l.id"
          type="button"
          class="anterior"
          :class="{ activa: l.id === abierta?.id }"
          @click="abrir(l.id)"
        >
          <strong>{{ l.kind === 1 ? 'Semana' : 'Mes' }}</strong>
          <span class="muted small">{{ formatDate(l.periodFrom) }} — {{ formatDate(l.periodTo) }}</span>
        </button>
      </nav>

      <article class="lectura">
        <template v-if="abierta">
          <div class="cabecera">
            <h2>{{ titulo }}</h2>
            <button
              v-if="abierta.id"
              type="button"
              class="link"
              :disabled="bajando"
              @click="bajarPdf"
            >
              {{ bajando ? 'Armando el PDF…' : 'Bajar en PDF' }}
            </button>
          </div>

          <!-- Sin volumen no se genera, y se dice por qué: inventar una tendencia con cuatro
               datos es peor que no decir nada. -->
          <p v-if="abierta.message" class="muted">{{ abierta.message }}</p>

          <template v-for="(b, i) in bloques" :key="i">
            <h3 v-if="b.tipo === 'titulo'">
              <template v-for="(p, j) in b.partes" :key="j">{{ p.texto }}</template>
            </h3>
            <li v-else-if="b.tipo === 'vineta'" class="vineta">
              <template v-for="(p, j) in b.partes" :key="j">
                <strong v-if="p.fuerte">{{ p.texto }}</strong>
                <template v-else>{{ p.texto }}</template>
              </template>
            </li>
            <p v-else>
              <template v-for="(p, j) in b.partes" :key="j">
                <strong v-if="p.fuerte">{{ p.texto }}</strong>
                <template v-else>{{ p.texto }}</template>
              </template>
            </p>
          </template>
        </template>

        <p v-else class="muted">
          Todavía no hay ninguna lectura. Toque «La semana pasada» para armar la primera.
        </p>
      </article>
    </div>
  </section>
</template>

<style scoped>
.vista {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
  flex-wrap: wrap;
}

.acciones {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.cuerpo {
  display: flex;
  gap: 1.25rem;
  align-items: flex-start;
}

.anteriores {
  flex: none;
  width: 13rem;
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
  border: 1px solid transparent;
  border-radius: 6px;
  padding: 0.4rem 0.6rem;
  cursor: pointer;
  color: inherit;
}

.anterior.activa {
  border-color: var(--border);
  background: var(--surface-alt, transparent);
}

.lectura {
  flex: 1;
  min-width: 0;
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 1.25rem 1.5rem;
  max-width: 48rem;
}

.cabecera {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 1rem;
  margin-bottom: 0.75rem;
}

.lectura h3 {
  margin: 1.25rem 0 0.4rem;
  font-size: 1rem;
  color: var(--accent);
}

.lectura h3:first-of-type {
  margin-top: 0;
}

.lectura p {
  margin: 0.4rem 0;
}

.vineta {
  margin: 0.25rem 0 0.25rem 1.25rem;
}

/* En una pantalla angosta las anteriores van arriba, en fila: la lectura es lo que se lee. */
@media (max-width: 860px) {
  .cuerpo {
    flex-direction: column;
  }

  .anteriores {
    width: 100%;
    flex-direction: row;
    overflow-x: auto;
  }
}
</style>

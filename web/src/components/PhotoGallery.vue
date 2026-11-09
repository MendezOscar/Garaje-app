<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { mediaApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import { useEscape } from '@/composables/useEscape'
import { MediaOwnerType, type MediaAttachment, type QuotePhotoCandidate } from '@/types/domain'
import { formatDateTime } from '@/utils/format'

/**
 * Si el panel deja adjuntar PDF. En falso hasta que esté publicada en las tiendas una versión
 * que sepa enseñar un PDF —la 1.8.0 (20) fue la primera—: la que los talleres tienen hoy
 * instalada, la 1.7.0 (19), pinta cualquier adjunto como foto, así que un informe subido desde
 * el panel se le vería como una imagen rota dentro de la orden. El servidor sí los acepta ya;
 * lo único cerrado es esta puerta.
 *
 * Para abrirlo: poner `true` y desplegar. No hace falta tocar nada más.
 */
const PDF_DESDE_EL_PANEL: boolean = false

const props = defineProps<{
  /** Fotos de una orden: las suyas y las de sus pasos, agrupadas por paso. */
  workOrderId?: string
  /** Fotos de una cotización: el daño que justifica el precio y que el cliente ve en su link. */
  quoteId?: string
  /** El comprobante de un gasto: es lo que lo respalda cuando alguien lo discute. */
  expenseId?: string
  /** Cómo entró el vehículo: el id de la orden, que es de quien es la recepción. */
  receptionId?: string
  /** Falso para el Cliente: mira el proceso, no lo documenta. */
  canEdit: boolean
}>()

/** A qué se adjunta lo que se suba aquí. */
const owner = computed(() => {
  if (props.quoteId) return { ownerType: MediaOwnerType.Quote, ownerId: props.quoteId }
  if (props.expenseId) return { ownerType: MediaOwnerType.Expense, ownerId: props.expenseId }
  if (props.receptionId) return { ownerType: MediaOwnerType.Reception, ownerId: props.receptionId }
  return { ownerType: MediaOwnerType.WorkOrder, ownerId: props.workOrderId! }
})

/**
 * Cómo se llama esto según a qué se le están poniendo fotos.
 *
 * Antes decía «Fotos del proceso» y «Todavía no hay fotos de esta orden» también en el
 * comprobante de un gasto, donde no hay orden ni proceso: el gasto lleva una foto del recibo
 * y el texto no tenía nada que ver con lo que se estaba viendo.
 */
const titulo = computed(() => {
  if (props.quoteId) return 'Fotos de la cotización'
  if (props.expenseId) return 'Comprobante'
  if (props.receptionId) return 'Fotos de cómo entró'
  return 'Fotos del proceso'
})

const vacio = computed(() => {
  if (props.quoteId) return 'Sin fotos. Las que agregue aquí las verá el cliente junto al precio.'
  if (props.expenseId) return 'Sin comprobante. Un gasto sin recibo es el que después se discute.'
  if (props.receptionId)
    return 'Sin fotos de cómo entró. Son las que salen en la ficha y las que contestan después si el golpe ya venía.'
  return 'Todavía no hay fotos de esta orden.'
})

const photos = ref<MediaAttachment[]>([])
const error = ref('')
const busy = ref(false)
const uploading = ref(0)
const preview = ref<MediaAttachment | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)

useEscape(() => (preview.value = null))

const esImagen = (m: MediaAttachment) => m.contentType.startsWith('image/')

/**
 * Lo que no es imagen —el informe del escáner, normalmente— va aparte y como lista: no tiene
 * miniatura que mirar, se abre y se lee.
 */
const documentos = computed(() => photos.value.filter((m) => !esImagen(m)))

/** Las fotos de un paso van agrupadas bajo su título; las de la orden, juntas al principio. */
const groups = computed(() => {
  const byGroup = new Map<string, MediaAttachment[]>()

  for (const photo of photos.value.filter(esImagen)) {
    const key = photo.taskTitle ?? 'General'
    byGroup.set(key, [...(byGroup.get(key) ?? []), photo])
  }

  return [...byGroup.entries()].map(([title, items]) => ({ title, items }))
})

async function load() {
  try {
    // La orden trae en una llamada las fotos de sus pasos; la cotización solo tiene las suyas.
    photos.value = props.workOrderId
      ? await mediaApi.listForWorkOrder(props.workOrderId)
      : await mediaApi.list(owner.value.ownerType, owner.value.ownerId)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar las fotos.')
  }
}

async function onFilesPicked(event: Event) {
  const input = event.target as HTMLInputElement
  const files = [...(input.files ?? [])]
  input.value = ''
  if (!files.length) return

  error.value = ''

  // El navegador no respeta `accept` si el archivo llega arrastrado o si el usuario escoge
  // «todos los archivos»: la puerta de verdad es esta.
  const admitidos = PDF_DESDE_EL_PANEL
    ? files
    : files.filter((f) => f.type !== 'application/pdf')

  if (admitidos.length < files.length) {
    error.value =
      'Todavía no se pueden adjuntar PDF desde el panel. Se habilita cuando la nueva versión ' +
      'de la app esté publicada, para que a quien tenga la anterior no se le vea como una ' +
      'foto rota.'
  }

  if (!admitidos.length) return

  uploading.value = admitidos.length

  for (const file of admitidos) {
    try {
      await mediaApi.upload(file, owner.value)
    } catch (e) {
      // Una foto que falla no cancela las demás: se avisa y se sigue con el resto.
      error.value = errorMessage(e, `No se pudo subir ${file.name}.`)
    } finally {
      uploading.value--
    }
  }

  await load()
}

// ---------- Traer a la cotización lo que el técnico fotografió en la orden ----------

/**
 * El técnico documenta el daño en la orden: es lo único que puede hacer, porque a
 * Cotizaciones no entra —ahí están los precios— y así tiene que seguir siendo. Pero esa
 * evidencia es justo la que convence al cliente de autorizar el trabajo, y antes de esto
 * había que volver a subirla a mano, foto por foto, desde el panel.
 *
 * Se copia y no se enlaza: lo que el cliente ya miró no puede cambiar porque alguien borró
 * después una foto de la orden.
 */
const eligiendo = ref(false)
const candidatas = ref<QuotePhotoCandidate[]>([])
const elegidas = ref<string[]>([])
const cargandoCandidatas = ref(false)

useEscape(() => (eligiendo.value = false))

/** Cuántas van a salir en el PDF. Son seis: pasada esa cuenta, el resto solo está en el link. */
const CABEN_EN_EL_PDF = 6

const traibles = computed(() => candidatas.value.filter((c) => !c.alreadyCopied))

async function abrirSelector() {
  if (!props.quoteId) return

  eligiendo.value = true
  cargandoCandidatas.value = true
  elegidas.value = []
  error.value = ''

  try {
    candidatas.value = await mediaApi.quotePhotoCandidates(props.quoteId)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar las fotos de la orden.')
    eligiendo.value = false
  } finally {
    cargandoCandidatas.value = false
  }
}

function alternar(id: string) {
  elegidas.value = elegidas.value.includes(id)
    ? elegidas.value.filter((x) => x !== id)
    : [...elegidas.value, id]
}

async function traerElegidas() {
  if (!props.quoteId || !elegidas.value.length) return

  busy.value = true
  error.value = ''

  try {
    await mediaApi.copyToQuote(props.quoteId, elegidas.value)
    eligiendo.value = false
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron traer las fotos.')
  } finally {
    busy.value = false
  }
}

/** Kilobytes o megas, lo que se lea mejor: el informe del escáner suele pesar un par de megas. */
function peso(bytes: number) {
  return bytes >= 1024 * 1024
    ? `${(bytes / (1024 * 1024)).toFixed(1)} MB`
    : `${Math.max(1, Math.round(bytes / 1024))} KB`
}

async function remove(photo: MediaAttachment) {
  const esDoc = !esImagen(photo)

  if (
    !confirm(
      esDoc
        ? '¿Eliminar este documento? No se puede deshacer.'
        : '¿Eliminar esta foto? No se puede deshacer.',
    )
  )
    return

  busy.value = true
  try {
    await mediaApi.remove(photo.id)
    if (preview.value?.id === photo.id) preview.value = null
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo eliminar la foto.')
  } finally {
    busy.value = false
  }
}

onMounted(load)
</script>

<template>
  <article class="card">
    <header>
      <h2>{{ titulo }}</h2>
      <div class="acciones">
        <!-- Solo en la cotización, y solo si salió de una orden: el servidor contesta que no
             hay fotos que traer cuando no la tiene, y este botón no debería ni aparecer. -->
        <button
          v-if="canEdit && quoteId"
          type="button"
          class="btn-ghost"
          :disabled="busy"
          @click="abrirSelector"
        >
          Traer de la orden
        </button>
        <button v-if="canEdit" type="button" :disabled="uploading > 0" @click="fileInput?.click()">
          {{
            uploading > 0 ? `Subiendo ${uploading}…` : expenseId ? 'Agregar recibo' : 'Agregar fotos'
          }}
        </button>
      </div>
      <!-- El PDF va a entrar por aquí también —el informe del escáner puede llegar al correo
           del taller—, pero está cerrado hasta que la 1.8.0 esté en las dos tiendas: la
           versión instalada hoy en los teléfonos pinta todo adjunto como foto, y un PDF se
           le vería como una imagen rota en la orden. Ver `PDF_DESDE_EL_PANEL`. -->
      <input
        ref="fileInput"
        type="file"
        :accept="PDF_DESDE_EL_PANEL ? 'image/jpeg,image/png,image/webp,application/pdf' : 'image/jpeg,image/png,image/webp'"
        multiple
        hidden
        @change="onFilesPicked"
      />
    </header>

    <ErrorNote v-if="error" :message="error" />

    <p v-if="!photos.length && uploading === 0" class="muted">{{ vacio }}</p>

    <div v-for="group in groups" :key="group.title" class="group">
      <h3 v-if="groups.length > 1 || group.title !== 'General'">{{ group.title }}</h3>
      <div class="grid">
        <figure v-for="photo in group.items" :key="photo.id">
          <button type="button" class="thumb" @click="preview = photo">
            <img :src="photo.thumbnailUrl" :alt="photo.caption ?? 'Foto del proceso'" loading="lazy" />
            <span v-if="!photo.isVisibleToCustomer" class="internal">interna</span>
          </button>
          <figcaption v-if="photo.caption">{{ photo.caption }}</figcaption>
        </figure>
      </div>
    </div>

    <!-- Los documentos: el informe del escáner y lo que venga por el mismo camino. -->
    <div v-if="documentos.length" class="docs">
      <h3>Documentos</h3>
      <div v-for="doc in documentos" :key="doc.id" class="doc">
        <a :href="doc.url" target="_blank" rel="noopener">
          <span class="icono" aria-hidden="true">PDF</span>
          <span class="nombre">
            {{ doc.caption ?? doc.fileName ?? 'Informe' }}
            <span class="muted small">{{ peso(doc.sizeBytes) }} · {{ doc.uploadedByName }}</span>
          </span>
        </a>
        <button v-if="canEdit" type="button" class="btn-ghost" :disabled="busy" @click="remove(doc)">
          Quitar
        </button>
      </div>
    </div>
  </article>

  <!-- El selector: la galería de la orden, con lo que ya se trajo marcado y fuera de juego. -->
  <div v-if="eligiendo" class="lightbox" @click.self="eligiendo = false">
    <div class="selector">
      <header>
        <div>
          <h2>Fotos y documentos de la orden</h2>
          <p class="muted small">
            Lo que puso el técnico, fotos e informes del escáner. El PDF enseña
            {{ CABEN_EN_EL_PDF }} fotos; el resto, y los informes, los ve el cliente en el enlace.
          </p>
        </div>
        <button type="button" @click="eligiendo = false">Cerrar</button>
      </header>

      <p v-if="cargandoCandidatas" class="muted">Cargando…</p>

      <p v-else-if="!candidatas.length" class="muted">
        La orden todavía no tiene fotos. Las toma el técnico desde la app, en la orden o en
        cada paso.
      </p>

      <p v-else-if="!traibles.length" class="muted">
        Ya están todas en la cotización.
      </p>

      <div v-else class="grid elegibles">
        <figure v-for="c in candidatas" :key="c.photo.id">
          <button
            type="button"
            class="thumb"
            :class="{ puesta: elegidas.includes(c.photo.id), traida: c.alreadyCopied }"
            :disabled="c.alreadyCopied"
            @click="alternar(c.photo.id)"
          >
            <!-- Un informe no tiene miniatura: se enseña como documento, con su nombre. -->
            <span v-if="!c.photo.contentType.startsWith('image/')" class="doc-thumb">
              <span class="icono" aria-hidden="true">PDF</span>
              <span class="small">{{ c.photo.fileName ?? 'Informe' }}</span>
            </span>
            <img
              v-else
              :src="c.photo.thumbnailUrl"
              :alt="c.photo.caption ?? 'Foto de la orden'"
              loading="lazy"
            />
            <span v-if="c.alreadyCopied" class="internal">ya está</span>
            <span v-else-if="elegidas.includes(c.photo.id)" class="marca">✓</span>
          </button>
          <figcaption v-if="c.photo.taskTitle || c.photo.caption">
            {{ c.photo.caption ?? c.photo.taskTitle }}
          </figcaption>
        </figure>
      </div>

      <footer v-if="traibles.length">
        <span class="muted small">
          {{ elegidas.length }}
          {{ elegidas.length === 1 ? 'foto elegida' : 'fotos elegidas' }}
        </span>
        <button type="button" :disabled="busy || !elegidas.length" @click="traerElegidas">
          {{ busy ? 'Trayendo…' : 'Traer a la cotización' }}
        </button>
      </footer>
    </div>
  </div>

  <!-- Lightbox: la foto a tamaño real, que es lo que se necesita para ver un daño. -->
  <div v-if="preview" class="lightbox" @click.self="preview = null">
    <div class="viewer">
      <img :src="preview.url" :alt="preview.caption ?? 'Foto del proceso'" />
      <footer>
        <div>
          <strong v-if="preview.caption">{{ preview.caption }}</strong>
          <div class="muted small">
            {{ preview.uploadedByName }} · {{ formatDateTime(preview.takenAt) }}
          </div>
        </div>
        <div class="actions">
          <button v-if="canEdit" type="button" :disabled="busy" @click="remove(preview)">
            Eliminar
          </button>
          <button type="button" @click="preview = null">Cerrar</button>
        </div>
      </footer>
    </div>
  </div>
</template>

<style scoped>
.acciones {
  display: flex;
  gap: 0.5rem;
}

.selector {
  width: min(900px, 92vw);
  max-height: 86vh;
  overflow: auto;
  padding: 1rem;
  border-radius: 10px;
  background: var(--surface);
}

.selector header,
.selector footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.selector h2 {
  margin: 0;
}

.selector footer {
  margin-top: 1rem;
}

/* Elegida: el borde no basta en una miniatura oscura, así que también baja la opacidad de
   las que no están puestas. */
.elegibles .thumb {
  opacity: 0.55;
  transition: opacity 0.1s;
}

.elegibles .thumb.puesta {
  opacity: 1;
  outline: 3px solid var(--accent);
  outline-offset: 1px;
}

.elegibles .thumb.traida {
  cursor: not-allowed;
}

/* Documentos: lista y no cuadrícula. Un informe se identifica por su nombre, no por su cara. */
.docs {
  margin-top: 0.75rem;
}

.doc {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  padding: 0.5rem;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--surface-alt);
}

.doc + .doc {
  margin-top: 0.375rem;
}

.doc a {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  min-width: 0;
  color: inherit;
  text-decoration: none;
}

.doc .nombre {
  display: flex;
  flex-direction: column;
  min-width: 0;
  overflow-wrap: anywhere;
}

.icono {
  flex: none;
  padding: 0.25rem 0.375rem;
  border-radius: 4px;
  background: var(--accent);
  color: var(--on-accent);
  font-size: 0.6875rem;
  font-weight: 600;
}

.doc-thumb {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.375rem;
  width: 100%;
  aspect-ratio: 4 / 3;
  padding: 0.5rem;
  text-align: center;
  overflow-wrap: anywhere;
}

.marca {
  position: absolute;
  inset-block-end: 4px;
  inset-inline-end: 4px;
  padding: 0 0.35rem;
  border-radius: 4px;
  background: var(--accent);
  color: var(--on-accent);
  font-size: 0.8125rem;
}

.card {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 0.75rem;
}

h2 {
  margin: 0;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

h3 {
  margin: 0.75rem 0 0.375rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(7rem, 1fr));
  gap: 0.5rem;
}

figure {
  margin: 0;
  min-width: 0;
}

.thumb {
  position: relative;
  display: block;
  width: 100%;
  padding: 0;
  border: 1px solid var(--border);
  border-radius: 6px;
  overflow: hidden;
  cursor: zoom-in;
  background: var(--surface-alt);
}

.thumb img {
  display: block;
  width: 100%;
  aspect-ratio: 4 / 3;
  object-fit: cover;
}

.internal {
  position: absolute;
  top: 0.25rem;
  right: 0.25rem;
  padding: 0 0.375rem;
  border-radius: 999px;
  background: rgb(0 0 0 / 0.6);
  color: #fff;
  font-size: 0.6875rem;
}

figcaption {
  margin-top: 0.25rem;
  font-size: 0.75rem;
  color: var(--text-muted);
  overflow-wrap: anywhere;
}

.lightbox {
  position: fixed;
  inset: 0;
  z-index: 50;
  display: grid;
  place-items: center;
  padding: 1rem;
  background: rgb(0 0 0 / 0.8);
}

.viewer {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-width: min(60rem, 100%);
  max-height: 100%;
}

.viewer img {
  min-height: 0;
  max-width: 100%;
  object-fit: contain;
  border-radius: 8px;
}

.viewer footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
  color: #fff;
}

.actions {
  display: flex;
  gap: 0.5rem;
}

</style>

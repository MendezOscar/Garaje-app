<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { mediaApi } from '@/api/garaj'
import { MediaOwnerType, type MediaAttachment } from '@/types/domain'
import { formatDateTime } from '@/utils/format'

const props = defineProps<{
  workOrderId: string
  /** Falso para el Cliente: mira el proceso, no lo documenta. */
  canEdit: boolean
}>()

const photos = ref<MediaAttachment[]>([])
const error = ref('')
const busy = ref(false)
const uploading = ref(0)
const preview = ref<MediaAttachment | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)

/** Las fotos de un paso van agrupadas bajo su título; las de la orden, juntas al principio. */
const groups = computed(() => {
  const byGroup = new Map<string, MediaAttachment[]>()

  for (const photo of photos.value) {
    const key = photo.taskTitle ?? 'General'
    byGroup.set(key, [...(byGroup.get(key) ?? []), photo])
  }

  return [...byGroup.entries()].map(([title, items]) => ({ title, items }))
})

async function load() {
  try {
    photos.value = await mediaApi.listForWorkOrder(props.workOrderId)
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
  uploading.value = files.length

  for (const file of files) {
    try {
      await mediaApi.upload(file, {
        ownerType: MediaOwnerType.WorkOrder,
        ownerId: props.workOrderId,
      })
    } catch (e) {
      // Una foto que falla no cancela las demás: se avisa y se sigue con el resto.
      error.value = errorMessage(e, `No se pudo subir ${file.name}.`)
    } finally {
      uploading.value--
    }
  }

  await load()
}

async function remove(photo: MediaAttachment) {
  if (!confirm('¿Eliminar esta foto? No se puede deshacer.')) return

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
      <h2>Fotos del proceso</h2>
      <button v-if="canEdit" type="button" :disabled="uploading > 0" @click="fileInput?.click()">
        {{ uploading > 0 ? `Subiendo ${uploading}…` : 'Agregar fotos' }}
      </button>
      <input
        ref="fileInput"
        type="file"
        accept="image/jpeg,image/png,image/webp"
        multiple
        hidden
        @change="onFilesPicked"
      />
    </header>

    <p v-if="error" class="error">{{ error }}</p>

    <p v-if="!photos.length && uploading === 0" class="muted">
      Todavía no hay fotos de esta orden.
    </p>

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
  </article>

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

.small {
  font-size: 0.75rem;
}

.muted {
  color: var(--text-muted);
}

.error {
  color: var(--danger);
}
</style>

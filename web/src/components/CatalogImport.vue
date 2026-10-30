<script setup lang="ts">
import { computed, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, laborServicesApi, partsApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import type { Branch, ImportResult } from '@/types/domain'

/**
 * Cargar el catálogo desde la plantilla de Excel.
 *
 * Un taller que arranca tiene su catálogo en un cuaderno o en una hoja de cálculo, y pasarlo
 * a mano son dos días que nadie hace. Se baja la plantilla, se llena y se sube.
 *
 * Primero la vista previa y después guardar, siempre: cargar a ciegas trescientos renglones y
 * descubrir después que la mitad tenía el precio en blanco es peor que no cargar nada.
 */
const props = defineProps<{ kind: 'parts' | 'labor' }>()
const emit = defineEmits<{ imported: [] }>()

const api = computed(() => (props.kind === 'parts' ? partsApi : laborServicesApi))
const que = computed(() => (props.kind === 'parts' ? 'repuestos' : 'servicios'))

const abierto = ref(false)
const archivo = ref<File | null>(null)

/**
 * A qué bodega entran la existencia inicial, el mínimo y la ubicación. El catálogo es del
 * taller entero, pero lo que hay en bodega es de una sucursal: con varias, hay que decir cuál.
 */
const sucursales = ref<Branch[]>([])
const sucursal = ref('')
const entrada = ref<HTMLInputElement | null>(null)
const previa = ref<ImportResult | null>(null)
const guardado = ref<ImportResult | null>(null)
const busy = ref(false)
const error = ref('')

/** Lo que de verdad va a entrar: los renglones sin error. */
const buenos = computed(() => (previa.value?.created ?? 0) + (previa.value?.updated ?? 0))

/**
 * El servidor manda una muestra, no el archivo entero: con tres mil filas, pintar tres mil
 * renglones no le dice nada a nadie. Aquí se cuenta lo que quedó fuera para decirlo.
 */
const sinEnseñar = computed(() => {
  if (!previa.value) return { buenas: 0, errores: 0 }

  const enseñadas = previa.value.rows
  const buenasEnseñadas = enseñadas.filter((f) => f.action !== 'error').length
  const erroresEnseñados = enseñadas.length - buenasEnseñadas

  return {
    buenas: buenos.value - buenasEnseñadas,
    errores: previa.value.failed - erroresEnseñados,
  }
})

async function abrir() {
  abierto.value = true
  limpiar()

  if (props.kind !== 'parts' || sucursales.value.length) return

  try {
    sucursales.value = await branchesApi.list()
    sucursal.value = sucursales.value[0]?.id ?? ''
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar las sucursales.')
  }
}

function limpiar() {
  archivo.value = null
  previa.value = null
  guardado.value = null
  error.value = ''
  if (entrada.value) entrada.value.value = ''
}

async function bajarPlantilla() {
  error.value = ''
  try {
    await api.value.downloadImportTemplate()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo bajar la plantilla.')
  }
}

async function elegir(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file) return

  archivo.value = file
  guardado.value = null
  previa.value = null
  error.value = ''
  busy.value = true

  try {
    previa.value = await importar(file, false)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo leer el archivo.')
  } finally {
    busy.value = false
  }
}

/** La sucursal solo viaja en la carga de repuestos: la mano de obra es del taller entero. */
function importar(file: File, apply: boolean) {
  return props.kind === 'parts'
    ? partsApi.importCatalog(file, apply, sucursal.value || undefined)
    : laborServicesApi.importCatalog(file, apply)
}

async function guardar() {
  if (!archivo.value) return

  busy.value = true
  error.value = ''

  try {
    guardado.value = await importar(archivo.value, true)
    previa.value = null
    emit('imported')
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo guardar la carga.')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <button type="button" class="btn-ghost" @click="abrir">Cargar desde Excel</button>

  <div v-if="abierto" class="drawer-backdrop" @click.self="abierto = false">
    <aside class="drawer">
      <header>
        <h2>Cargar {{ que }} desde Excel</h2>
        <button type="button" class="btn-ghost" @click="abierto = false">Cerrar</button>
      </header>

      <ol class="pasos">
        <li>
          Baje la plantilla y llénela con su catálogo.
          <button type="button" class="link" @click="bajarPlantilla">Bajar la plantilla</button>
        </li>
        <li v-if="kind === 'parts' && sucursales.length > 1">
          Diga a qué bodega entran la existencia inicial, el mínimo y la ubicación.
          <select v-model="sucursal">
            <option v-for="s in sucursales" :key="s.id" :value="s.id">{{ s.name }}</option>
          </select>
        </li>
        <li>
          Súbala aquí. Antes de guardar le enseñamos qué va a entrar.
          <input ref="entrada" type="file" accept=".xlsx" @change="elegir" />
        </li>
      </ol>

      <ErrorNote v-if="error" :message="error" />
      <p v-if="busy" class="muted small">Leyendo el archivo…</p>

      <template v-if="previa">
        <p class="resumen">
          <strong>{{ previa.created }}</strong> {{ que }} nuevos ·
          <strong>{{ previa.updated }}</strong> que se actualizan
          <template v-if="previa.failed">
            · <strong class="danger">{{ previa.failed }}</strong> con error
          </template>
        </p>
        <p v-if="previa.failed" class="muted small">
          Las filas con error no se cargan; las demás sí. Corríjalas en el Excel y vuelva a
          subirlo cuando quiera.
        </p>

        <table class="filas">
          <thead>
            <tr>
              <th>Fila</th>
              <th>Código</th>
              <th>Nombre</th>
              <th>Qué pasa</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="fila in previa.rows" :key="fila.row" :class="{ malo: fila.action === 'error' }">
              <td class="num">{{ fila.row }}</td>
              <td>{{ fila.key || '—' }}</td>
              <td>{{ fila.name || '—' }}</td>
              <td>{{ fila.error ?? (fila.action === 'nuevo' ? 'Se agrega' : 'Se actualiza') }}</td>
            </tr>
          </tbody>
        </table>

        <p v-if="sinEnseñar.buenas > 0 || sinEnseñar.errores > 0" class="muted small">
          Se enseñan los primeros renglones de cada clase.
          <template v-if="sinEnseñar.errores > 0">
            Hay {{ sinEnseñar.errores }} filas con error más;
          </template>
          <template v-if="sinEnseñar.buenas > 0">
            {{ sinEnseñar.buenas }} filas más entran bien.
          </template>
        </p>

        <button type="button" :disabled="busy || !buenos" @click="guardar">
          Guardar {{ buenos }} {{ buenos === 1 ? 'renglón' : 'renglones' }}
        </button>
      </template>

      <template v-if="guardado">
        <p class="resumen">
          Listo: <strong>{{ guardado.created }}</strong> {{ que }} nuevos y
          <strong>{{ guardado.updated }}</strong> actualizados.
          <template v-if="guardado.failed">
            Quedaron {{ guardado.failed }} filas con error sin cargar.
          </template>
        </p>
        <button type="button" class="btn-ghost" @click="limpiar">Cargar otro archivo</button>
      </template>
    </aside>
  </div>
</template>

<style scoped>
.drawer-backdrop {
  position: fixed;
  inset: 0;
  z-index: 40;
  display: flex;
  justify-content: flex-end;
  background: rgb(0 0 0 / 0.4);
}

.drawer {
  width: min(34rem, 100%);
  height: 100%;
  overflow-y: auto;
  padding: 1rem 1.25rem 2rem;
  background: var(--surface);
  border-left: 1px solid var(--border);
}

.drawer header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.drawer h2 {
  margin: 0;
  font-size: 1rem;
}

.pasos {
  display: grid;
  gap: 0.75rem;
  margin: 0 0 1rem;
  padding-left: 1.25rem;
}

.pasos input[type='file'] {
  display: block;
  margin-top: 0.375rem;
}

.pasos .link {
  display: block;
  margin-top: 0.25rem;
}

.resumen {
  margin: 1rem 0 0.25rem;
}

.filas {
  width: 100%;
  margin: 0.75rem 0 1rem;
  border-collapse: collapse;
  font-size: 0.875rem;
}

.filas th,
.filas td {
  padding: 0.3rem 0.4rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
}

.filas .malo td {
  color: var(--danger);
}

.num {
  text-align: right;
  font-variant-numeric: tabular-nums;
}
</style>

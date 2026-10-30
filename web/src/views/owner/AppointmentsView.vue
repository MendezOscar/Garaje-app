<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { errorMessage } from '@/api/client'
import { appointmentsApi, branchesApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import { useTaller } from '@/composables/useTaller'
import SkeletonList from '@/components/SkeletonList.vue'
import {
  APPOINTMENT_SLOT_LABEL,
  APPOINTMENT_SOURCE_LABEL,
  AppointmentSlot,
  AppointmentStatus,
  type Appointment,
  type Branch,
} from '@/types/domain'
import { useAuthStore } from '@/stores/auth'
import { formatDay, whatsappLink } from '@/utils/format'

/**
 * La agenda del taller: lo que el cliente apartó por el enlace público o desde la app.
 *
 * Una cita no es todavía nada —ni orden, ni requerimiento, ni cliente del padrón—: es un día
 * apartado. Por eso la pantalla solo hace tres cosas: confirmar, rechazar y, cuando el carro
 * llega, cerrarla para pasar a recibir el vehículo, que es donde se toman los datos de verdad.
 */
const router = useRouter()

const { vocabulario, cargar: cargarTaller } = useTaller()

const auth = useAuthStore()

const citas = ref<Appointment[]>([])

/** Las sucursales del taller. Aquí siguen haciendo falta: una cita se agenda en una. */
const branches = ref<Branch[]>([])
/** La sucursal sale del menú, no de esta pantalla: es la misma para todo el panel. */
const branchId = computed(() => auth.activeBranchId ?? '')

const loading = ref(false)
const busy = ref(false)
const error = ref('')
const aviso = ref('')

/** Qué se está mirando. Por confirmar es lo primero: es lo único que espera una respuesta. */
const pestana = ref<'porConfirmar' | 'hoy' | 'semana' | 'todas'>('porConfirmar')

/** La ventana abierta: confirmar o rechazar una cita, o anotar una nueva. */
const ventana = ref<'confirmar' | 'rechazar' | 'nueva' | null>(null)
const enMano = ref<Appointment | null>(null)

const hoy = new Date()
const hoyISO = isoDe(hoy)

const confirmacion = ref({ day: '', slot: AppointmentSlot.Morning as AppointmentSlot, note: '' })
const motivo = ref('')
const nueva = ref(citaEnBlanco())

function isoDe(fecha: Date) {
  // Sin `toISOString`: esa pasa por UTC y en Honduras devuelve el día siguiente desde las
  // seis de la tarde, que es justo cuando se agenda para mañana.
  const mes = `${fecha.getMonth() + 1}`.padStart(2, '0')
  const dia = `${fecha.getDate()}`.padStart(2, '0')
  return `${fecha.getFullYear()}-${mes}-${dia}`
}

function citaEnBlanco() {
  return {
    branchId: '',
    contactName: '',
    contactPhone: '',
    vehicleNote: '',
    reason: '',
    day: hoyISO,
    slot: AppointmentSlot.Morning as AppointmentSlot,
  }
}

const finDeSemana = computed(() => {
  const d = new Date(hoy)
  d.setDate(d.getDate() + 7)
  return isoDe(d)
})

const visibles = computed(() => {
  switch (pestana.value) {
    case 'porConfirmar':
      return citas.value.filter((c) => c.status === AppointmentStatus.Requested)
    case 'hoy':
      return citas.value.filter((c) => c.day === hoyISO)
    case 'semana':
      return citas.value.filter((c) => c.day <= finDeSemana.value)
    default:
      return citas.value
  }
})

const pestanas = computed(() => [
  {
    key: 'porConfirmar' as const,
    label: 'Por confirmar',
    cuantos: citas.value.filter((c) => c.status === AppointmentStatus.Requested).length,
  },
  { key: 'hoy' as const, label: 'Hoy', cuantos: citas.value.filter((c) => c.day === hoyISO).length },
  {
    key: 'semana' as const,
    label: 'Esta semana',
    cuantos: citas.value.filter((c) => c.day <= finDeSemana.value).length,
  },
  { key: 'todas' as const, label: 'Todas', cuantos: citas.value.length },
])

async function load() {
  loading.value = true
  error.value = ''
  try {
    const result = await appointmentsApi.list({
      branchId: branchId.value || undefined,
      pageSize: 100,
    })
    citas.value = result.items
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar las citas.')
  } finally {
    loading.value = false
  }
}

async function run(accion: () => Promise<unknown>) {
  busy.value = true
  error.value = ''
  try {
    await accion()
    ventana.value = null
    enMano.value = null
    await load()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

function abrirConfirmar(cita: Appointment) {
  enMano.value = cita
  confirmacion.value = { day: cita.day, slot: cita.slot, note: '' }
  aviso.value = ''
  ventana.value = 'confirmar'
}

function abrirRechazar(cita: Appointment) {
  enMano.value = cita
  motivo.value = ''
  ventana.value = 'rechazar'
}

function abrirNueva() {
  nueva.value = { ...citaEnBlanco(), branchId: branches.value[0]?.id ?? '' }
  ventana.value = 'nueva'
}

function confirmar() {
  const cita = enMano.value
  if (!cita) return

  return run(async () => {
    const guardada = await appointmentsApi.confirm(cita.id, {
      // Solo se manda el día cuando cambió: mandar el mismo no hace daño, pero el servidor
      // revalida el rango y una cita vieja ya confirmada no tiene por qué fallar al tocarla.
      day: confirmacion.value.day !== cita.day ? confirmacion.value.day : undefined,
      slot: confirmacion.value.slot !== cita.slot ? confirmacion.value.slot : undefined,
      note: confirmacion.value.note.trim() || undefined,
    })

    // El cliente del enlace no recibe aviso dentro de la aplicación: se le escribe.
    aviso.value = `Confirmada. Avísele por WhatsApp a ${guardada.contactName}.`
  })
}

function rechazar() {
  const cita = enMano.value
  if (!cita || !motivo.value.trim()) return

  return run(() => appointmentsApi.reject(cita.id, motivo.value.trim()))
}

function anotar() {
  const c = nueva.value
  if (!c.contactName.trim() || !c.contactPhone.trim() || !c.reason.trim()) return

  return run(() =>
    appointmentsApi.create({
      branchId: c.branchId || branches.value[0]?.id || '',
      contactName: c.contactName.trim(),
      contactPhone: c.contactPhone.trim(),
      vehicleNote: c.vehicleNote.trim() || null,
      reason: c.reason.trim(),
      day: c.day,
      slot: c.slot,
    }),
  )
}

/**
 * Llegó el vehículo. La cita se cierra y se pasa a recibirlo: los datos de verdad —placa,
 * marca, año— se toman ahí, con el carro enfrente, y no de lo que la persona escribió.
 */
async function llego(cita: Appointment) {
  busy.value = true
  try {
    await appointmentsApi.attended(cita.id)
    await router.push({ name: 'receive-vehicle' })
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cerrar la cita.')
    busy.value = false
  }
}

/** El mensaje que se le manda: ya escrito, para que confirmar sea un toque y no un dictado. */
function mensaje(cita: Appointment) {
  const cuando = `${formatDay(cita.day)} ${APPOINTMENT_SLOT_LABEL[cita.slot].toLowerCase()}`

  return cita.status === AppointmentStatus.Confirmed
    ? `Hola ${cita.contactName}, su cita quedó confirmada para el ${cuando}. Lo esperamos.`
    : `Hola ${cita.contactName}, recibimos su solicitud de cita para el ${cuando}.`
}

function esHoy(cita: Appointment) {
  return cita.day === hoyISO
}

// Al cambiar de sucursal en el menú, esta pantalla vuelve a pedir lo suyo.

watch(branchId, load)


onMounted(async () => {
  branches.value = await branchesApi.list().catch(() => [])
  await cargarTaller()
  await load()
})
</script>

<template>
  <section>
    <header class="cabecera">
      <div>
        <h1>Citas</h1>
        <p class="muted small">
          <template v-if="auth.user && auth.user.branches.length > 1">
            <strong>{{ auth.activeBranchName }}</strong> ·
          </template>
          Los días que los clientes apartaron, por el enlace o desde la aplicación. Confirme
          para que nadie se presente creyendo que tenía lugar.
        </p>
      </div>
      <div class="filtros">
        <button type="button" @click="abrirNueva">Anotar una cita</button>
      </div>
    </header>

    <ErrorNote v-if="error" :message="error" />
    <p v-if="aviso" class="notice">{{ aviso }}</p>

    <div class="panel">
      <div class="pestanas">
        <button
          v-for="p in pestanas"
          :key="p.key"
          type="button"
          class="pestana"
          :class="{ activa: pestana === p.key }"
          @click="pestana = p.key"
        >
          {{ p.label }}
          <span class="num">{{ p.cuantos }}</span>
        </button>
      </div>

      <SkeletonList v-if="loading" :rows="4" variant="lista" />
      <p v-else-if="!visibles.length" class="vacio muted">
        {{
          pestana === 'porConfirmar'
            ? 'Ninguna cita espera respuesta. Al día.'
            : 'No hay citas en este periodo.'
        }}
      </p>

      <ul v-else class="citas">
        <li v-for="cita in visibles" :key="cita.id" :class="{ hoy: esHoy(cita) }">
          <span class="dia">
            <strong>{{ formatDay(cita.day) }}</strong>
            <span class="muted small">{{ APPOINTMENT_SLOT_LABEL[cita.slot] }}</span>
          </span>

          <span class="quien">
            <strong>{{ cita.contactName }}</strong>
            <span class="muted small">
              {{ cita.vehicleLabel }} · {{ cita.branchName }} ·
              {{ APPOINTMENT_SOURCE_LABEL[cita.source] }}
            </span>
            <span class="motivo small">{{ cita.reason }}</span>
            <span v-if="cita.responseNote" class="muted small">↳ {{ cita.responseNote }}</span>
          </span>

          <span class="estado" :class="`s${cita.status}`">
            {{
              cita.status === AppointmentStatus.Requested
                ? 'Por confirmar'
                : cita.status === AppointmentStatus.Confirmed
                  ? 'Confirmada'
                  : cita.status === AppointmentStatus.Attended
                    ? 'Atendida'
                    : cita.status === AppointmentStatus.Rejected
                      ? 'Rechazada'
                      : 'Cancelada'
            }}
          </span>

          <span class="acciones">
            <a
              class="icono wa"
              :href="whatsappLink(cita.contactPhone, mensaje(cita))"
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

            <template v-if="cita.status === AppointmentStatus.Requested">
              <button type="button" class="btn-sm" :disabled="busy" @click="abrirConfirmar(cita)">
                Confirmar
              </button>
              <button
                type="button"
                class="btn-ghost btn-sm"
                :disabled="busy"
                @click="abrirRechazar(cita)"
              >
                Rechazar
              </button>
            </template>

            <template v-else-if="cita.status === AppointmentStatus.Confirmed">
              <button type="button" class="btn-sm" :disabled="busy" @click="llego(cita)">
                Llegó
              </button>
              <button
                type="button"
                class="btn-ghost btn-sm"
                :disabled="busy"
                @click="abrirConfirmar(cita)"
              >
                Cambiar día
              </button>
            </template>
          </span>
        </li>
      </ul>
    </div>

    <!-- ---------------------------------------------------------------- las ventanas -->

    <div v-if="ventana" class="fondo" @click.self="ventana = null">
      <div class="ventana" role="dialog" aria-modal="true">
        <header class="ventana-top">
          <h2>
            {{
              ventana === 'confirmar'
                ? 'Confirmar la cita'
                : ventana === 'rechazar'
                  ? 'Rechazar la cita'
                  : 'Anotar una cita'
            }}
          </h2>
          <button type="button" class="icono" title="Cerrar" @click="ventana = null">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
              <path d="m6 6 12 12M18 6 6 18" stroke-linecap="round" />
            </svg>
          </button>
        </header>

        <div class="ventana-cuerpo">
          <template v-if="ventana === 'confirmar' && enMano">
            <p class="muted small">
              {{ enMano.contactName }} · {{ enMano.vehicleLabel }}. Puede moverle el día: el
              cliente lo verá al abrir su enlace.
            </p>
            <div class="row">
              <label>
                Día
                <input v-model="confirmacion.day" type="date" :min="hoyISO" />
              </label>
              <label>
                Franja
                <select v-model.number="confirmacion.slot">
                  <option
                    v-for="(label, value) in APPOINTMENT_SLOT_LABEL"
                    :key="value"
                    :value="Number(value)"
                  >
                    {{ label }}
                  </option>
                </select>
              </label>
            </div>
            <label>
              Nota para el cliente
              <input v-model="confirmacion.note" maxlength="500" placeholder="Opcional" />
            </label>
          </template>

          <template v-else-if="ventana === 'rechazar' && enMano">
            <p class="muted small">
              Diga por qué: es lo que va a leer {{ enMano.contactName }} al abrir su enlace.
            </p>
            <label>
              Motivo
              <input
                v-model="motivo"
                maxlength="500"
                placeholder="Ese día está lleno, pruebe el viernes"
              />
            </label>
          </template>

          <template v-else>
            <p class="muted small">
              La que se acuerda por teléfono o en el mostrador. Nace confirmada: usted la está
              acordando con el cliente.
            </p>
            <div class="row">
              <label>
                Nombre
                <input v-model="nueva.contactName" maxlength="200" required />
              </label>
              <label>
                Teléfono
                <input v-model="nueva.contactPhone" maxlength="30" placeholder="99887766" />
              </label>
            </div>
            <label>
              Qué {{ vocabulario.activo }}
              <input v-model="nueva.vehicleNote" maxlength="300" placeholder="Corolla 2014 gris" />
            </label>
            <label>
              Qué necesita
              <input v-model="nueva.reason" maxlength="2000" required />
            </label>
            <div class="row">
              <label>
                Día
                <input v-model="nueva.day" type="date" :min="hoyISO" />
              </label>
              <label>
                Franja
                <select v-model.number="nueva.slot">
                  <option
                    v-for="(label, value) in APPOINTMENT_SLOT_LABEL"
                    :key="value"
                    :value="Number(value)"
                  >
                    {{ label }}
                  </option>
                </select>
              </label>
            </div>
            <label v-if="branches.length > 1">
              Sucursal
              <select v-model="nueva.branchId">
                <option v-for="b in branches" :key="b.id" :value="b.id">{{ b.name }}</option>
              </select>
            </label>
          </template>
        </div>

        <footer class="ventana-pie">
          <button
            v-if="ventana === 'confirmar'"
            type="button"
            :disabled="busy"
            @click="confirmar"
          >
            Confirmar
          </button>
          <button
            v-else-if="ventana === 'rechazar'"
            type="button"
            :disabled="busy || !motivo.trim()"
            @click="rechazar"
          >
            Rechazar
          </button>
          <button
            v-else
            type="button"
            :disabled="busy || !nueva.contactName.trim() || !nueva.contactPhone.trim() || !nueva.reason.trim()"
            @click="anotar"
          >
            Anotar
          </button>
          <button type="button" class="btn-ghost" @click="ventana = null">Cancelar</button>
        </footer>
      </div>
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
  margin: 0;
  font-size: 1.625rem;
}

.cabecera p {
  margin: 0.25rem 0 0;
  max-width: 44rem;
}

.filtros {
  display: flex;
  gap: 0.5rem;
}

.notice {
  color: var(--success-text);
}

.panel {
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  overflow: hidden;
}

.pestanas {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  flex-wrap: wrap;
  padding: 0.625rem 0.875rem;
  border-bottom: 1px solid var(--border);
}

.pestana {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.35rem 0.7rem;
  border: 1px solid transparent;
  border-radius: 999px;
  background: none;
  color: var(--text-muted);
  font-size: 0.875rem;
  cursor: pointer;
}

.pestana .num {
  font-size: 0.75rem;
  opacity: 0.8;
}

.pestana:hover {
  background: var(--surface-alt);
  color: var(--text);
}

.pestana.activa {
  border-color: color-mix(in srgb, var(--brand) 40%, var(--border));
  background: color-mix(in srgb, var(--brand) 10%, transparent);
  color: var(--brand);
  font-weight: 600;
}

.citas {
  list-style: none;
  margin: 0;
  padding: 0;
}

.citas li {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 0.75rem 0.875rem;
  border-top: 1px solid var(--border);
}

.citas li:first-child {
  border-top: 0;
}

.citas li:hover {
  background: var(--surface-alt);
}

/* La de hoy se distingue sin leer la fecha: es la que importa cuando se abre la pantalla. */
.citas li.hoy {
  box-shadow: inset 3px 0 0 var(--brand);
}

.dia {
  display: grid;
  flex: none;
  width: 9rem;
}

.quien {
  display: grid;
  flex: 1;
  min-width: 0;
}

.motivo {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.estado {
  flex: none;
  padding: 0.15rem 0.6rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.estado.s1 {
  border-color: color-mix(in srgb, var(--warning) 45%, var(--border));
  color: var(--warning-text);
}

.estado.s2 {
  border-color: color-mix(in srgb, var(--success) 45%, var(--border));
  color: var(--success-text);
}

.acciones {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  flex: none;
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

.icono svg {
  width: 1.0625rem;
  height: 1.0625rem;
}

.icono.wa:hover {
  color: #1fa971;
  border-color: #1fa971;
}

.vacio {
  margin: 0;
  padding: 2rem 0.875rem;
  text-align: center;
}

/* ------------------------------------------------------------------ la ventana */

.fondo {
  position: fixed;
  inset: 0;
  z-index: 50;
  display: grid;
  place-items: center;
  padding: 1rem;
  background: rgb(0 0 0 / 55%);
}

.ventana {
  display: flex;
  flex-direction: column;
  width: min(32rem, 100%);
  max-height: min(90vh, 44rem);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  box-shadow: 0 1.5rem 3rem rgb(0 0 0 / 35%);
}

.ventana-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.875rem;
  border-bottom: 1px solid var(--border);
}

.ventana-top h2 {
  margin: 0;
  font-size: 1.0625rem;
}

.ventana-cuerpo {
  display: grid;
  gap: 0.6rem;
  align-content: start;
  padding: 0.875rem;
  overflow-y: auto;
}

.ventana-pie {
  display: flex;
  gap: 0.5rem;
  padding: 0.875rem;
  border-top: 1px solid var(--border);
  background: var(--surface-alt);
}

.ventana label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.ventana input,
.ventana select {
  width: 100%;
}

.row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.row label {
  flex: 1 1 10rem;
}
</style>

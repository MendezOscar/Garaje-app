<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { errorMessage } from '@/api/client'
import { publicBookingApi } from '@/api/garaj'
import BrandLogo from '@/components/BrandLogo.vue'
import {
  APPOINTMENT_SLOT_LABEL,
  AppointmentSlot,
  type BookingPage,
  type PublicAppointment,
} from '@/types/domain'
import { formatDay } from '@/utils/format'

/**
 * Pedir cita sin cuenta: el enlace que el taller manda por WhatsApp o pega en un QR.
 *
 * La abre alguien que no tiene —ni quiere— la aplicación, muchas veces desde un teléfono con
 * datos contados. Por eso pide lo mínimo y no valida nada que la persona no pueda saber: los
 * datos de verdad del vehículo se toman en el taller, con el carro enfrente.
 */
const route = useRoute()
const token = computed(() => route.params.token as string)

const taller = ref<BookingPage | null>(null)

/**
 * La cita, una vez pedida. Se enseña **aquí mismo** y no se cambia de pantalla a propósito:
 * cambiar de pantalla obliga al navegador a bajar otro archivo, y si el sitio se desplegó de
 * nuevo mientras la persona llenaba la hoja, ese archivo ya no existe y lo que ve es un error
 * —con la cita ya pedida—. Lo que acaba de mandar no puede depender de eso.
 */
const pedida = ref<PublicAppointment | null>(null)
const cargando = ref(true)
const error = ref('')
const enviando = ref(false)

const logo = computed(() =>
  taller.value?.hasLogo ? publicBookingApi.logoUrl(token.value) : null,
)

const hoy = new Date()
const hoyISO = comoISO(hoy)

function comoISO(fecha: Date): string {
  return `${fecha.getFullYear()}-${`${fecha.getMonth() + 1}`.padStart(2, '0')}-${`${fecha.getDate()}`.padStart(2, '0')}`
}

/**
 * El día que viene puesto: mañana, y si cae domingo, el lunes.
 *
 * Venía puesto hoy, que a las nueve de la noche es pedir cita para un día que ya terminó.
 * No adivina el horario del taller —eso lo confirma él—, pero no propone un imposible.
 */
function proximoDiaHabil(): string {
  const dia = new Date(hoy)
  dia.setDate(dia.getDate() + 1)
  if (dia.getDay() === 0) dia.setDate(dia.getDate() + 1)
  return comoISO(dia)
}

const form = ref({
  branchId: '',
  contactName: '',
  contactPhone: '',
  vehicleNote: '',
  reason: '',
  day: proximoDiaHabil(),
  slot: AppointmentSlot.Morning as AppointmentSlot,
})

/** Lo primero que falte, dicho con palabras. */
const loQueFalta = computed(() => {
  const f = form.value
  if (f.contactName.trim().length <= 1) return 'su nombre'
  if (f.contactPhone.replace(/\D/g, '').length < 8) return 'su teléfono, de ocho números'
  if (f.reason.trim().length <= 2) return 'decir qué necesita'
  if (!f.day) return 'elegir el día'
  return 'completar el formulario'
})

const listo = computed(
  () =>
    form.value.contactName.trim().length > 1 &&
    form.value.contactPhone.replace(/\D/g, '').length >= 8 &&
    form.value.reason.trim().length > 2 &&
    !!form.value.day,
)

async function pedir() {
  if (!listo.value || !taller.value) return

  enviando.value = true
  error.value = ''
  try {
    pedida.value = await publicBookingApi.book(token.value, {
      branchId: form.value.branchId || taller.value.branches[0]?.id || '',
      contactName: form.value.contactName.trim(),
      contactPhone: form.value.contactPhone.trim(),
      vehicleNote: form.value.vehicleNote.trim() || null,
      reason: form.value.reason.trim(),
      day: form.value.day,
      slot: form.value.slot,
    })

    window.scrollTo({ top: 0 })
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo pedir la cita. Intente de nuevo.')
  } finally {
    enviando.value = false
  }
}

onMounted(async () => {
  try {
    taller.value = await publicBookingApi.page(token.value)
    form.value.branchId = taller.value.branches[0]?.id ?? ''
  } catch (e) {
    error.value = errorMessage(e, 'Este enlace no es válido. Pídale otro al taller.')
  } finally {
    cargando.value = false
  }
})
</script>

<template>
  <main class="page">
    <p v-if="cargando" class="muted">Cargando…</p>

    <article v-else-if="taller" class="card">
      <header>
        <img v-if="logo" class="logo-taller" :src="logo" :alt="taller.tenantName" />
        <p class="tenant">{{ taller.tenantName }}</p>
        <h1>Pida su cita</h1>
        <p class="muted small">
          Aparte el día que va a traer su vehículo. El taller le confirma por WhatsApp.
        </p>
      </header>

      <!-- Pedida. Lo primero es que sepa que sí entró, y el enlace para volver a verla. -->
      <template v-if="pedida">
        <p class="hecho">Su cita quedó pedida</p>
        <dl class="datos">
          <dt>Día</dt>
          <dd>
            {{ formatDay(pedida.day) }}
            <span class="muted">· {{ APPOINTMENT_SLOT_LABEL[pedida.slot] }}</span>
          </dd>
          <dt>Dónde</dt>
          <dd>
            {{ pedida.branchName }}
            <span v-if="pedida.branchAddress" class="muted bloque">{{ pedida.branchAddress }}</span>
          </dd>
          <dt>Qué pidió</dt>
          <dd>{{ pedida.reason }}</dd>
        </dl>
        <p class="muted small">
          Queda <strong>por confirmar</strong> hasta que el taller le conteste por WhatsApp.
          Si ese día no les cabe, le proponen otro.
        </p>
        <!-- Enlace de verdad y no navegación interna: así la página se baja entera y no
             depende de lo que esta pestaña tenga cargado. Guárdelo, es el suyo. -->
        <a class="boton" :href="`/mi-cita/${pedida.token}`">Ver mi cita</a>
      </template>

      <p v-else-if="!taller.enabled" class="aviso">
        Este taller no está agendando citas por ahora. Escríbale directamente por WhatsApp.
      </p>

      <form v-else class="form" @submit.prevent="pedir">
        <label>
          Su nombre
          <input v-model="form.contactName" maxlength="200" required />
        </label>

        <label>
          Su teléfono
          <input
            v-model="form.contactPhone"
            type="tel"
            inputmode="tel"
            maxlength="30"
            placeholder="9988-7766"
            required
          />
          <small class="muted">Es por donde le vamos a confirmar.</small>
        </label>

        <label>
          Su vehículo
          <input
            v-model="form.vehicleNote"
            maxlength="300"
            placeholder="Corolla 2014 gris, placa HAB1234"
          />
          <small class="muted">Lo que sepa. Si no está seguro del año, no importa.</small>
        </label>

        <label>
          Qué necesita
          <textarea
            v-model="form.reason"
            rows="3"
            maxlength="2000"
            placeholder="Cambio de aceite y revisar un ruido al frenar"
            required
          />
        </label>

        <div class="row">
          <label>
            Qué día
            <input
              v-model="form.day"
              type="date"
              :min="hoyISO"
              :max="taller.lastDay"
              required
            />
          </label>
          <div class="franja">
            <span class="rotulo">A qué hora</span>
            <!-- Dos opciones no son una lista desplegable: son dos botones. -->
            <div class="opciones">
              <button
                v-for="(label, value) in APPOINTMENT_SLOT_LABEL"
                :key="value"
                type="button"
                :class="{ activa: form.slot === Number(value) }"
                @click="form.slot = Number(value) as AppointmentSlot"
              >
                {{ label }}
              </button>
            </div>
          </div>
        </div>

        <label v-if="taller.branches.length > 1">
          A qué sucursal
          <select v-model="form.branchId">
            <option v-for="b in taller.branches" :key="b.id" :value="b.id">
              {{ b.name }}<template v-if="b.address"> — {{ b.address }}</template>
            </option>
          </select>
        </label>

        <p v-else-if="taller.branches[0]?.address" class="muted small">
          {{ taller.branches[0].name }} · {{ taller.branches[0].address }}
        </p>

        <p v-if="error" class="error">{{ error }}</p>

        <button type="submit" :disabled="!listo || enviando">
          {{ enviando ? 'Enviando…' : 'Pedir la cita' }}
        </button>

        <!-- Qué falta para poder mandarla: el botón apagado sin decir por qué deja a la
             persona tocándolo sin entender. -->
        <p v-if="!listo && !enviando" class="muted small">Falta {{ loQueFalta }}.</p>

        <p class="muted small">
          Queda <strong>por confirmar</strong> hasta que el taller le conteste. Si el día
          {{ formatDay(form.day) }} no les cabe, le proponen otro.
        </p>
      </form>
    </article>

    <p v-else class="error">{{ error }}</p>

    <footer class="pie">
      <BrandLogo class="marca" />
      <span class="muted small">Agenda de taller con GarajApp</span>
    </footer>
  </main>
</template>

<style scoped>
.franja .rotulo {
  display: block;
  margin-bottom: 0.25rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.franja .opciones {
  display: flex;
  gap: 0.5rem;
}

.franja .opciones button {
  flex: 1;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text);
  font: inherit;
}

.franja .opciones button.activa {
  border-color: var(--accent);
  background: color-mix(in srgb, var(--accent) 14%, transparent);
  color: var(--accent);
  font-weight: 500;
}

.page {
  min-height: 100dvh;
  padding: 1rem;
  background: var(--bg);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
}

.card {
  width: min(32rem, 100%);
  padding: 1.5rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

header {
  margin-bottom: 1.25rem;
}

.logo-taller {
  display: block;
  max-width: 12rem;
  max-height: 3.5rem;
  margin-bottom: 0.5rem;
  object-fit: contain;
}

.tenant {
  margin: 0;
  font-weight: 600;
}

h1 {
  margin: 0.125rem 0;
  font-size: 1.375rem;
}

header p.small {
  margin: 0.25rem 0 0;
}

.form {
  display: grid;
  gap: 0.875rem;
}

.form label {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.form label,
.row label {
  /* Sin esto, un campo con contenido largo —la sucursal con su dirección— no deja encoger a
     su columna y la hoja entera se va más ancha que la pantalla del teléfono. */
  min-width: 0;
}

.form input,
.form select,
.form textarea {
  width: 100%;
  min-width: 0;
  max-width: 100%;
  /* 16 px: con menos, el navegador del teléfono hace zoom al tocar el campo y la hoja se
     descuadra justo cuando se está escribiendo. */
  font-size: 1rem;
}

.row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}

.row label {
  flex: 1 1 9rem;
}

.form button {
  padding: 0.75rem;
  font-size: 1rem;
}

.hecho {
  margin: 0 0 1rem;
  padding: 0.5rem 0.9rem;
  border: 1px solid color-mix(in srgb, var(--success) 45%, var(--border));
  border-radius: 999px;
  color: var(--success-text);
  font-weight: 600;
  display: inline-block;
}

.datos {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 0.5rem 1.25rem;
  margin: 0 0 1rem;
}

.datos dt {
  color: var(--text-muted);
  font-size: 0.8125rem;
}

.datos dd {
  margin: 0;
  min-width: 0;
}

.bloque {
  display: block;
}

.boton {
  display: inline-flex;
  align-items: center;
  margin-top: 1rem;
  padding: 0.7rem 1rem;
  border-radius: var(--radius-sm);
  background: var(--accent);
  color: var(--on-accent);
  font-weight: 600;
}

.boton:hover {
  text-decoration: none;
  background: var(--brand-deep);
}

.aviso {
  margin: 0;
  padding: 0.875rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface-alt);
}

.error {
  color: var(--danger);
}

.pie {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  opacity: 0.8;
}

.marca {
  height: 1.25rem;
}
</style>

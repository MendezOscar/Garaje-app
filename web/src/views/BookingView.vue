<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { errorMessage } from '@/api/client'
import { publicBookingApi } from '@/api/garaj'
import BrandLogo from '@/components/BrandLogo.vue'
import {
  APPOINTMENT_SLOT_LABEL,
  AppointmentSlot,
  type BookingPage,
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
const router = useRouter()
const token = computed(() => route.params.token as string)

const taller = ref<BookingPage | null>(null)
const cargando = ref(true)
const error = ref('')
const enviando = ref(false)

const logo = computed(() =>
  taller.value?.hasLogo ? publicBookingApi.logoUrl(token.value) : null,
)

const hoy = new Date()
const hoyISO = `${hoy.getFullYear()}-${`${hoy.getMonth() + 1}`.padStart(2, '0')}-${`${hoy.getDate()}`.padStart(2, '0')}`

const form = ref({
  branchId: '',
  contactName: '',
  contactPhone: '',
  vehicleNote: '',
  reason: '',
  day: hoyISO,
  slot: AppointmentSlot.Morning as AppointmentSlot,
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
    const cita = await publicBookingApi.book(token.value, {
      branchId: form.value.branchId || taller.value.branches[0]?.id || '',
      contactName: form.value.contactName.trim(),
      contactPhone: form.value.contactPhone.trim(),
      vehicleNote: form.value.vehicleNote.trim() || null,
      reason: form.value.reason.trim(),
      day: form.value.day,
      slot: form.value.slot,
    })

    // Se sale de esta página a la de su cita: ese enlace es el que puede volver a abrir para
    // ver si se la confirmaron.
    await router.replace({ name: 'my-appointment', params: { token: cita.token } })
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

      <p v-if="!taller.enabled" class="aviso">
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
          <label>
            A qué hora
            <select v-model.number="form.slot">
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

.form input,
.form select,
.form textarea {
  width: 100%;
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

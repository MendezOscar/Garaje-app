<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { errorMessage } from '@/api/client'
import { publicBookingApi } from '@/api/garaj'
import BrandLogo from '@/components/BrandLogo.vue'
import {
  APPOINTMENT_SLOT_LABEL,
  AppointmentStatus,
  type PublicAppointment,
} from '@/types/domain'
import { formatDay, whatsappLink } from '@/utils/format'

/**
 * La cita del cliente, por su propio token: si se la confirmaron, cuándo, y el botón para
 * cancelarla. Es la página a la que llega después de pedirla y la que puede volver a abrir.
 */
const route = useRoute()
const token = computed(() => route.params.token as string)

const cita = ref<PublicAppointment | null>(null)
const cargando = ref(true)
const error = ref('')
const busy = ref(false)

const estado = computed(() => {
  switch (cita.value?.status) {
    case AppointmentStatus.Confirmed:
      return { texto: 'Confirmada', clase: 'bien' }
    case AppointmentStatus.Attended:
      return { texto: 'Atendida', clase: 'bien' }
    case AppointmentStatus.Rejected:
      return { texto: 'No se pudo agendar', clase: 'mal' }
    case AppointmentStatus.Cancelled:
      return { texto: 'Cancelada', clase: 'mal' }
    default:
      return { texto: 'Por confirmar', clase: 'espera' }
  }
})

const sePuedeCancelar = computed(
  () =>
    cita.value?.status === AppointmentStatus.Requested ||
    cita.value?.status === AppointmentStatus.Confirmed,
)

async function cancelar() {
  if (!confirm('¿Cancelar su cita?')) return

  busy.value = true
  try {
    cita.value = await publicBookingApi.cancel(token.value)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cancelar. Llame al taller.')
  } finally {
    busy.value = false
  }
}

onMounted(async () => {
  try {
    cita.value = await publicBookingApi.mine(token.value)
  } catch (e) {
    error.value = errorMessage(e, 'No encontramos esta cita. Puede que el enlace haya cambiado.')
  } finally {
    cargando.value = false
  }
})
</script>

<template>
  <main class="page">
    <p v-if="cargando" class="muted">Cargando…</p>

    <article v-else-if="cita" class="card">
      <header>
        <p class="tenant">{{ cita.tenantName }}</p>
        <h1>Su cita</h1>
      </header>

      <p class="estado" :class="estado.clase">{{ estado.texto }}</p>

      <dl class="datos">
        <dt>Día</dt>
        <dd>
          {{ formatDay(cita.day) }}
          <span class="muted">· {{ APPOINTMENT_SLOT_LABEL[cita.slot] }}</span>
        </dd>

        <dt>Dónde</dt>
        <dd>
          {{ cita.branchName }}
          <span v-if="cita.branchAddress" class="muted block">{{ cita.branchAddress }}</span>
        </dd>

        <dt>Vehículo</dt>
        <dd>{{ cita.vehicleLabel }}</dd>

        <dt>Qué pidió</dt>
        <dd>{{ cita.reason }}</dd>
      </dl>

      <p v-if="cita.responseNote" class="nota">
        <strong>El taller dice:</strong> {{ cita.responseNote }}
      </p>

      <p v-if="cita.status === AppointmentStatus.Requested" class="muted small">
        Todavía no está apartada. El taller le confirma por WhatsApp; si ese día no les cabe,
        le proponen otro.
      </p>

      <div class="acciones">
        <a
          v-if="cita.branchPhone"
          class="boton"
          :href="whatsappLink(cita.branchPhone, `Hola, le escribo por mi cita del ${formatDay(cita.day)}.`)"
          target="_blank"
          rel="noopener"
        >
          Escribir al taller
        </a>
        <button v-if="sePuedeCancelar" type="button" class="btn-ghost" :disabled="busy" @click="cancelar">
          Cancelar la cita
        </button>
      </div>

      <p v-if="error" class="error">{{ error }}</p>
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

.tenant {
  margin: 0;
  font-weight: 600;
}

h1 {
  margin: 0.125rem 0 1rem;
  font-size: 1.375rem;
}

.estado {
  display: inline-block;
  margin: 0 0 1rem;
  padding: 0.3rem 0.8rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  font-weight: 600;
}

.estado.bien {
  border-color: color-mix(in srgb, var(--success) 45%, var(--border));
  color: var(--success-text);
}

.estado.espera {
  border-color: color-mix(in srgb, var(--warning) 45%, var(--border));
  color: var(--warning-text);
}

.estado.mal {
  border-color: color-mix(in srgb, var(--danger) 45%, var(--border));
  color: var(--danger);
}

.datos {
  display: grid;
  /* `minmax(0, 1fr)` y no `1fr`: con un texto largo —la dirección de la sucursal— la columna
     no encoge y la tarjeta se sale de la pantalla del teléfono. */
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
  overflow-wrap: anywhere;
}

.block {
  display: block;
}

.nota {
  margin: 0 0 1rem;
  padding: 0.75rem;
  border-left: 3px solid var(--border);
  background: var(--surface-alt);
  border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
}

.acciones {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-top: 1rem;
}

.boton {
  display: inline-flex;
  align-items: center;
  padding: 0.55rem 0.9rem;
  border-radius: var(--radius-sm);
  background: var(--accent);
  color: var(--on-accent);
  font-weight: 600;
}

.boton:hover {
  text-decoration: none;
  background: var(--brand-deep);
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

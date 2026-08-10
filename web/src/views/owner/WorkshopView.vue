<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { apiUrl, errorMessage } from '@/api/client'
import { tenantApi } from '@/api/garaj'
import { useAuthStore } from '@/stores/auth'
import type { TenantSettings } from '@/types/domain'

/**
 * La ficha del taller: nombre, razón social, RTN, teléfonos y logo.
 *
 * No es una pantalla de adorno. Estos datos se imprimen en la cotización y en la factura que
 * el cliente del taller recibe por WhatsApp, y hasta ahora eran los que dejó el instalador:
 * el Dueño no tenía forma de corregirlos.
 */
const auth = useAuthStore()

const ficha = ref<TenantSettings | null>(null)
const form = ref({
  name: '',
  legalName: '',
  taxId: '',
  phone: '',
  email: '',
  defaultTaxRate: 15,
  defaultPhoneCountryCode: '504',
})

const loading = ref(false)
const busy = ref(false)
const error = ref('')
const saved = ref(false)

const archivo = ref<HTMLInputElement | null>(null)

const logo = computed(() => apiUrl(ficha.value?.logoUrl))

function fill(data: TenantSettings) {
  ficha.value = data
  form.value = {
    name: data.name,
    legalName: data.legalName ?? '',
    taxId: data.taxId ?? '',
    phone: data.phone ?? '',
    email: data.email ?? '',
    defaultTaxRate: data.defaultTaxRate,
    defaultPhoneCountryCode: data.defaultPhoneCountryCode,
  }
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    fill(await tenantApi.get())
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar la ficha del taller.')
  } finally {
    loading.value = false
  }
}

async function save() {
  busy.value = true
  error.value = ''
  saved.value = false
  try {
    const f = form.value
    fill(
      await tenantApi.update({
        name: f.name.trim(),
        legalName: f.legalName.trim() || null,
        taxId: f.taxId.trim() || null,
        phone: f.phone.trim() || null,
        email: f.email.trim() || null,
        defaultTaxRate: Number(f.defaultTaxRate) || 0,
        defaultPhoneCountryCode: f.defaultPhoneCountryCode.trim() || null,
      }),
    )
    saved.value = true
    // El nombre del taller se pinta en la barra, que lee el usuario de la sesión.
    await auth.loadCurrentUser()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo guardar la ficha.')
  } finally {
    busy.value = false
  }
}

async function subirLogo(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file) return

  busy.value = true
  error.value = ''
  try {
    fill(await tenantApi.setLogo(file))
    await auth.loadCurrentUser()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo subir el logo.')
  } finally {
    busy.value = false
    // Sin esto, elegir el mismo archivo dos veces no vuelve a disparar el evento.
    if (archivo.value) archivo.value.value = ''
  }
}

async function quitarLogo() {
  busy.value = true
  error.value = ''
  try {
    fill(await tenantApi.removeLogo())
    await auth.loadCurrentUser()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo quitar el logo.')
  } finally {
    busy.value = false
  }
}

onMounted(load)
</script>

<template>
  <section>
    <header class="top">
      <h1>Taller</h1>
      <p class="muted small">
        Los datos y el logo de su taller. Es lo que se imprime en las cotizaciones y en las
        facturas que recibe su cliente.
      </p>
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="loading" class="muted">Cargando…</p>

    <div v-else class="layout">
      <form class="card" @submit.prevent="save">
        <h2>Datos del taller</h2>

        <label>
          Nombre comercial
          <input v-model="form.name" required />
        </label>
        <label>
          Razón social
          <input v-model="form.legalName" placeholder="Servicios Automotrices S. de R.L." />
        </label>
        <div class="row">
          <label>
            RTN
            <input v-model="form.taxId" placeholder="08019995123456" />
          </label>
          <label>
            ISV por defecto (%)
            <input v-model.number="form.defaultTaxRate" type="number" min="0" max="100" step="0.01" />
          </label>
        </div>
        <div class="row">
          <label>
            Teléfono
            <input v-model="form.phone" placeholder="50499001111" />
          </label>
          <label>
            Código de país
            <input v-model="form.defaultPhoneCountryCode" placeholder="504" />
          </label>
        </div>
        <label>
          Correo
          <input v-model="form.email" type="email" placeholder="contacto@sutaller.hn" />
        </label>

        <div class="actions">
          <button type="submit" :disabled="busy">Guardar</button>
          <span v-if="saved" class="ok">Guardado</span>
        </div>

        <p class="muted small">
          El código de país arma los enlaces de WhatsApp que se mandan a sus clientes. La
          factura en PDF es comprobante de entrega; no sustituye el talonario del SAR.
        </p>
      </form>

      <div class="card">
        <h2>Logo</h2>

        <div class="marco">
          <img v-if="logo" :src="logo" :alt="ficha?.name" />
          <span v-else class="muted small">Sin logo</span>
        </div>

        <div class="actions">
          <button type="button" :disabled="busy" @click="archivo?.click()">
            {{ logo ? 'Cambiar' : 'Subir logo' }}
          </button>
          <button v-if="logo" type="button" class="link" :disabled="busy" @click="quitarLogo">
            Quitar
          </button>
        </div>

        <input
          ref="archivo"
          class="oculto"
          type="file"
          accept="image/png,image/jpeg"
          @change="subirLogo"
        />

        <p class="muted small">
          PNG o JPEG, hasta 2 MB. Se guarda reducido a 512 px. Un PNG con fondo transparente
          se ve bien tanto en el panel claro como en el oscuro.
        </p>
      </div>
    </div>
  </section>
</template>

<style scoped>
h1 {
  margin: 0;
}

.top {
  margin-bottom: 1rem;
}

.layout {
  display: grid;
  grid-template-columns: 1fr 20rem;
  gap: 1rem;
  align-items: start;
}

@media (max-width: 60rem) {
  .layout {
    grid-template-columns: 1fr;
  }
}

/* Fondo a cuadros para que se vea la transparencia: un logo con fondo blanco pegado sobre
   blanco parece transparente y no lo es, y en el panel oscuro se nota tarde. */
.marco {
  display: grid;
  place-items: center;
  min-height: 8rem;
  margin-bottom: 0.75rem;
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background-color: var(--surface-alt);
  background-image:
    linear-gradient(45deg, var(--border) 25%, transparent 25%),
    linear-gradient(-45deg, var(--border) 25%, transparent 25%),
    linear-gradient(45deg, transparent 75%, var(--border) 75%),
    linear-gradient(-45deg, transparent 75%, var(--border) 75%);
  background-size: 16px 16px;
  background-position: 0 0, 0 8px, 8px -8px, -8px 0;
}

.marco img {
  max-width: 100%;
  max-height: 7rem;
  object-fit: contain;
}

.oculto {
  display: none;
}

.ok {
  align-self: center;
  font-size: 0.875rem;
  color: var(--success);
}

/* Los mismos bloques que el resto de las pantallas del Dueño: el estilo va por vista, no
   hay hoja compartida de componentes. */
.card {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.card h2 {
  margin: 0;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.card label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.card input {
  width: 100%;
}

.row {
  display: flex;
  gap: 0.5rem;
}

.actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.link {
  padding: 0;
  border: none;
  background: none;
  color: var(--accent);
  cursor: pointer;
}

.muted {
  color: var(--text-muted);
}

.small {
  font-size: 0.75rem;
}

.error {
  color: var(--danger);
}
</style>

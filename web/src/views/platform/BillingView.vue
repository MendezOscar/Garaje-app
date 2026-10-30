<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { platformApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import type { PlatformSettings } from '@/types/domain'
import { formatDay } from '@/utils/format'

/**
 * Cómo facturamos nosotros: quiénes somos en el documento y con qué autorización.
 *
 * Es de plataforma, no de ningún taller. Lo que se decide aquí sale impreso en el documento
 * que recibe cada taller al pagar su mensualidad, y el documento nace con el pago: esta
 * pantalla no emite nada, solo decide cómo va a salir lo que se emita.
 */
const settings = ref<PlatformSettings | null>(null)
const loading = ref(false)
const busy = ref(false)
const error = ref('')
const guardado = ref('')

const form = ref({
  name: 'GarajApp',
  legalName: '',
  taxId: '',
  phone: '',
  email: '',
  address: '',
  defaultTaxRate: 15,
  pricesIncludeTax: false,
  invoicePrefix: 'C',
})

const rango = ref({
  cai: '',
  establishmentCode: '000',
  pointOfSaleCode: '001',
  documentType: '01',
  rangeStart: 1,
  rangeEnd: 1000,
  issueDeadline: '',
})

const abrirRango = ref(false)

/** El ejemplo con números: es lo que de verdad explica cuál de las dos formas está puesta. */
const ejemplo = computed(() => {
  const tasa = Number(form.value.defaultTaxRate) || 0
  const neto = 850

  if (tasa <= 0) return 'Sin ISV: la mensualidad de L 850.00 se factura por L 850.00.'

  if (form.value.pricesIncludeTax) {
    const impuesto = Math.round((neto - neto / (1 + tasa / 100)) * 100) / 100
    return `La mensualidad de L 850.00 se factura por L 850.00, desglosando L ${impuesto.toFixed(2)} de ISV.`
  }

  const impuesto = Math.round(neto * (tasa / 100) * 100) / 100
  return `La mensualidad de L 850.00 se factura por L ${(neto + impuesto).toFixed(2)}: L 850.00 más L ${impuesto.toFixed(2)} de ISV.`
})

const estadoDelCai = computed(() => {
  const r = settings.value?.fiscalRange
  if (!r) return null
  if (r.isExpired) return 'Venció: registre el rango nuevo del SAR.'
  if (r.isExhausted) return 'Se agotó: registre el rango nuevo del SAR.'
  if (r.remaining <= 20) return `Quedan ${r.remaining} números: pida el rango nuevo.`
  return null
})

function llenar(data: PlatformSettings) {
  settings.value = data
  form.value = {
    name: data.name,
    legalName: data.legalName ?? '',
    taxId: data.taxId ?? '',
    phone: data.phone ?? '',
    email: data.email ?? '',
    address: data.address ?? '',
    defaultTaxRate: data.defaultTaxRate,
    pricesIncludeTax: data.pricesIncludeTax,
    invoicePrefix: data.invoicePrefix,
  }
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    llenar(await platformApi.billingSettings())
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar los datos de facturación.')
  } finally {
    loading.value = false
  }
}

async function guardar() {
  busy.value = true
  error.value = ''
  guardado.value = ''
  try {
    llenar(
      await platformApi.saveBillingSettings({
        name: form.value.name.trim(),
        legalName: form.value.legalName.trim() || null,
        taxId: form.value.taxId.trim() || null,
        phone: form.value.phone.trim() || null,
        email: form.value.email.trim() || null,
        address: form.value.address.trim() || null,
        defaultTaxRate: Number(form.value.defaultTaxRate) || 0,
        pricesIncludeTax: form.value.pricesIncludeTax,
        invoicePrefix: form.value.invoicePrefix.trim() || 'C',
      }),
    )
    guardado.value = 'Guardado.'
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo guardar.')
  } finally {
    busy.value = false
  }
}

async function guardarRango() {
  if (!rango.value.cai.trim() || !rango.value.issueDeadline) return

  busy.value = true
  error.value = ''
  try {
    llenar(
      await platformApi.saveFiscalRange({
        cai: rango.value.cai.trim(),
        establishmentCode: rango.value.establishmentCode.trim(),
        pointOfSaleCode: rango.value.pointOfSaleCode.trim(),
        documentType: rango.value.documentType.trim(),
        rangeStart: Number(rango.value.rangeStart) || 1,
        rangeEnd: Number(rango.value.rangeEnd) || 1,
        issueDeadline: new Date(rango.value.issueDeadline).toISOString(),
      }),
    )
    abrirRango.value = false
    rango.value.cai = ''
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo registrar el CAI.')
  } finally {
    busy.value = false
  }
}

onMounted(load)
</script>

<template>
  <section>
    <header class="top">
      <h1>Facturación</h1>
      <p class="muted small">
        Lo que sale impreso en el documento que recibe cada taller al pagar. El documento se
        emite al registrar el pago, en la ficha del taller.
      </p>
    </header>

    <ErrorNote v-if="error" :message="error" />

    <div class="grid">
      <form class="card" @submit.prevent="guardar">
        <h2>Quién emite</h2>

        <div class="row">
          <label>
            Nombre
            <input v-model="form.name" required />
          </label>
          <label>
            Razón social
            <input v-model="form.legalName" placeholder="Como está en el RTN" />
          </label>
        </div>

        <div class="row">
          <label>
            RTN
            <input v-model="form.taxId" placeholder="0801…" />
          </label>
          <label>
            Teléfono
            <input v-model="form.phone" placeholder="+504 9824 2108" />
          </label>
        </div>

        <div class="row">
          <label>
            Correo
            <input v-model="form.email" type="email" placeholder="soporte@garajeapp.com" />
          </label>
          <label>
            Dirección
            <input v-model="form.address" />
          </label>
        </div>

        <h2>Cómo se cobra</h2>

        <div class="row">
          <label>
            ISV (%)
            <input v-model.number="form.defaultTaxRate" type="number" min="0" max="100" step="0.01" />
          </label>
          <label>
            Prefijo del comprobante
            <input v-model="form.invoicePrefix" maxlength="8" />
            <small class="muted">El de la numeración propia, la de cuando no hay CAI.</small>
          </label>
        </div>

        <!-- La misma decisión que el taller toma para sus precios, de este lado del negocio:
             si la mensualidad es neta o ya trae el impuesto adentro. -->
        <div v-if="form.defaultTaxRate > 0" class="modelo">
          <label class="radio">
            <input v-model="form.pricesIncludeTax" type="radio" :value="false" />
            <span>
              <strong>La mensualidad es neta: el ISV se suma</strong>
              <small class="muted">El taller deposita la mensualidad más el impuesto.</small>
            </span>
          </label>
          <label class="radio">
            <input v-model="form.pricesIncludeTax" type="radio" :value="true" />
            <span>
              <strong>Ya lleva el ISV adentro</strong>
              <small class="muted">El documento solo desglosa cuánto de ese total es impuesto.</small>
            </span>
          </label>
        </div>

        <p class="ejemplo">{{ ejemplo }}</p>

        <div class="actions">
          <button type="submit" :disabled="busy">Guardar</button>
          <span v-if="guardado" class="muted small">{{ guardado }}</span>
        </div>
      </form>

      <article class="card">
        <h2>Autorización del SAR</h2>

        <template v-if="settings?.fiscalRange">
          <dl>
            <dt>CAI</dt>
            <dd class="mono">{{ settings.fiscalRange.cai }}</dd>
            <dt>Rango</dt>
            <dd class="mono">{{ settings.fiscalRange.rangeText }}</dd>
            <dt>Siguiente</dt>
            <dd class="mono">{{ settings.fiscalRange.nextFiscalNumber }}</dd>
            <dt>Vence</dt>
            <dd>{{ formatDay(settings.fiscalRange.issueDeadline) }}</dd>
            <dt>Quedan</dt>
            <dd>{{ settings.fiscalRange.remaining }}</dd>
          </dl>

          <p v-if="estadoDelCai" class="aviso">{{ estadoDelCai }}</p>
        </template>

        <p v-else class="muted small">
          Todavía no hay CAI registrado. Mientras no lo haya, cada pago deja un
          <strong>comprobante</strong> con numeración propia
          ({{ settings?.invoicePrefix ?? 'C' }}-{{
            String(settings?.nextInvoiceNumber ?? 1).padStart(6, '0')
          }}), que no es una factura fiscal y el documento lo dice.
        </p>

        <button v-if="!abrirRango" type="button" class="link" @click="abrirRango = true">
          {{ settings?.fiscalRange ? 'Registrar el rango nuevo' : 'Registrar el CAI' }}
        </button>

        <form v-else class="rango" @submit.prevent="guardarRango">
          <label>
            CAI
            <input v-model="rango.cai" placeholder="Tal como lo dio el SAR" required />
          </label>

          <div class="row">
            <label>
              Establecimiento
              <input v-model="rango.establishmentCode" maxlength="3" />
            </label>
            <label>
              Punto de emisión
              <input v-model="rango.pointOfSaleCode" maxlength="3" />
            </label>
            <label>
              Tipo
              <input v-model="rango.documentType" maxlength="2" />
            </label>
          </div>

          <div class="row">
            <label>
              Desde
              <input v-model.number="rango.rangeStart" type="number" min="1" />
            </label>
            <label>
              Hasta
              <input v-model.number="rango.rangeEnd" type="number" min="1" />
            </label>
            <label>
              Fecha límite
              <input v-model="rango.issueDeadline" type="date" required />
            </label>
          </div>

          <div class="actions">
            <button type="submit" :disabled="busy">Registrar</button>
            <button type="button" class="link" @click="abrirRango = false">Cancelar</button>
          </div>

          <p class="muted small">
            El rango que estuviera activo queda dado de baja: emitir con dos rangos vivos es
            la forma de saltarse un número sin enterarse.
          </p>
        </form>
      </article>
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

.grid {
  display: grid;
  grid-template-columns: 1fr 22rem;
  gap: 1rem;
  align-items: start;
}

@media (max-width: 60rem) {
  .grid {
    grid-template-columns: 1fr;
  }
}

.card {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.card h2 {
  margin: 0;
  font-size: 1rem;
}

.row {
  display: flex;
  gap: 0.75rem;
}

.row label {
  flex: 1;
  min-width: 0;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.modelo {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.radio {
  flex-direction: row;
  align-items: flex-start;
  gap: 0.5rem;
  color: var(--text);
}

.radio span {
  display: flex;
  flex-direction: column;
}

.ejemplo {
  padding: 0.625rem 0.75rem;
  border-radius: var(--radius-sm);
  background: color-mix(in srgb, var(--accent) 10%, transparent);
  font-size: 0.875rem;
}

.aviso {
  padding: 0.5rem 0.75rem;
  border-radius: var(--radius-sm);
  background: color-mix(in srgb, var(--warning) 14%, transparent);
  color: var(--warning-text);
  font-size: 0.875rem;
}

.actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

dl {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.25rem 0.75rem;
  margin: 0;
  font-size: 0.875rem;
}

dt {
  color: var(--text-muted);
}

dd {
  margin: 0;
  text-align: right;
}

.mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
}

.rango {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
</style>

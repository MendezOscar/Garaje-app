<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { apiUrl, errorMessage } from '@/api/client'
import BrandLogo from '@/components/BrandLogo.vue'
import { publicQuotesApi } from '@/api/garaj'
import { LINE_TYPE_LABEL, QuoteStatus, type PublicQuote } from '@/types/domain'
import { formatDate } from '@/utils/format'

// Página anónima: la abre el cliente desde el link de WhatsApp, sin cuenta ni login. El
// token de la URL es la única credencial, así que aquí no se pide nada más.
const route = useRoute()
const token = computed(() => route.params.token as string)

const quote = ref<PublicQuote | null>(null)
const error = ref('')
const loading = ref(true)
const busy = ref(false)
const note = ref('')
const confirming = ref<'approve' | 'reject' | null>(null)

/** El logo llega bajo el token de la cotización, no bajo el id del taller. */
const logoTaller = computed(() => apiUrl(quote.value?.tenantLogoUrl))

/**
 * El taller decide la moneda; aquí solo se formatea. Se imprime el símbolo y no el código
 * ISO: esta página la lee el cliente del taller, y en Honduras un precio se escribe «L 320.00».
 */
const SIMBOLO: Record<string, string> = { HNL: 'L', USD: '$' }

const money = (value: number) =>
  `${SIMBOLO[quote.value?.currency ?? 'HNL'] ?? quote.value?.currency} ${value.toLocaleString('es-HN', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`

const quantity = (value: number) =>
  value.toLocaleString('es-HN', { maximumFractionDigits: 3 })

async function load() {
  loading.value = true
  try {
    quote.value = await publicQuotesApi.get(token.value)
  } catch (e) {
    error.value = errorMessage(e, 'No encontramos esta cotización. Puede que el enlace haya cambiado.')
  } finally {
    loading.value = false
  }
}

async function respond(approve: boolean) {
  busy.value = true
  error.value = ''
  try {
    quote.value = await publicQuotesApi.respond(token.value, approve, note.value || undefined)
    confirming.value = null
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

const pdfUrl = computed(() => publicQuotesApi.pdfUrl(token.value))

onMounted(load)
</script>

<template>
  <main class="page">
    <p v-if="loading" class="muted center">Cargando…</p>

    <div v-else-if="!quote" class="card center">
      <h1>No encontramos la cotización</h1>
      <p class="muted">{{ error }}</p>
    </div>

    <article v-else class="card">
      <header>
        <div>
          <!-- El logo del taller antes que nada: quien abre esto quiere reconocer a quién le
               está cotizando su carro. -->
          <img
            v-if="logoTaller"
            class="logo-taller"
            :src="logoTaller"
            :alt="quote.tenantName"
          />
          <p class="tenant">{{ quote.tenantName }}</p>
          <h1>Cotización {{ quote.number }}</h1>
          <p class="muted small">
            {{ quote.branchName }}
            <template v-if="quote.tenantPhone"> · {{ quote.tenantPhone }}</template>
          </p>
        </div>
        <span class="badge" :class="`s${quote.status}`">
          {{
            quote.status === QuoteStatus.Approved ? 'Aprobada'
            : quote.status === QuoteStatus.Rejected ? 'Rechazada'
            : quote.isExpired ? 'Vencida'
            : 'Pendiente de su respuesta'
          }}
        </span>
      </header>

      <dl class="meta">
        <div>
          <dt>Cliente</dt>
          <dd>{{ quote.customerName }}</dd>
        </div>
        <div v-if="quote.vehicleLabel">
          <dt>Vehículo</dt>
          <dd>{{ quote.vehicleLabel }} <span v-if="quote.plate">· {{ quote.plate }}</span></dd>
        </div>
        <div v-if="quote.validUntil">
          <dt>Válida hasta</dt>
          <dd>{{ formatDate(quote.validUntil) }}</dd>
        </div>
      </dl>

      <p v-if="quote.notes" class="notes">{{ quote.notes }}</p>

      <table>
        <thead>
          <tr>
            <th>Detalle</th>
            <th class="num">Cant.</th>
            <th class="num unitario">P. unit.</th>
            <th class="num">Total</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(line, i) in quote.lines" :key="i">
            <td>
              {{ line.description }}
              <div class="muted small">{{ LINE_TYPE_LABEL[line.lineType] }}</div>
            </td>
            <td class="num">{{ quantity(line.quantity) }}</td>
            <td class="num unitario">{{ money(line.unitPrice) }}</td>
            <td class="num">{{ money(line.total) }}</td>
          </tr>
        </tbody>
      </table>

      <div class="totals">
        <div><span>Subtotal</span><span>{{ money(quote.subtotal) }}</span></div>
        <div v-if="quote.discountTotal > 0">
          <span>Descuento</span><span>−{{ money(quote.discountTotal) }}</span>
        </div>
        <div v-if="quote.taxRate > 0">
          <span>ISV {{ quote.taxRate }}%</span><span>{{ money(quote.taxTotal) }}</span>
        </div>
        <div class="grand"><span>Total</span><span>{{ money(quote.total) }}</span></div>
      </div>

      <p v-if="error" class="error">{{ error }}</p>

      <!-- Respuesta del cliente -->
      <section v-if="quote.canRespond" class="respond">
        <template v-if="!confirming">
          <p>¿Autoriza que procedamos con este trabajo?</p>
          <div class="actions">
            <button type="button" class="primary" @click="confirming = 'approve'">
              Aprobar cotización
            </button>
            <button type="button" @click="confirming = 'reject'">No por ahora</button>
          </div>
        </template>

        <template v-else>
          <p>
            {{ confirming === 'approve'
              ? '¿Confirma que aprueba esta cotización?'
              : '¿Confirma que la rechaza?' }}
            Esta respuesta no se puede cambiar.
          </p>
          <textarea
            v-model="note"
            rows="2"
            :placeholder="confirming === 'approve' ? 'Comentario (opcional)' : '¿Por qué? (opcional)'"
          />
          <div class="actions">
            <button
              type="button"
              :class="{ primary: confirming === 'approve' }"
              :disabled="busy"
              @click="respond(confirming === 'approve')"
            >
              {{ busy ? 'Enviando…' : 'Sí, confirmar' }}
            </button>
            <button type="button" :disabled="busy" @click="confirming = null">Volver</button>
          </div>
        </template>
      </section>

      <section v-else-if="quote.respondedAt" class="answered">
        <p>
          <strong>
            {{ quote.status === QuoteStatus.Approved ? 'Cotización aprobada' : 'Cotización rechazada' }}
          </strong>
          el {{ formatDate(quote.respondedAt) }}. Gracias por responder.
        </p>
      </section>

      <section v-else-if="quote.isExpired" class="answered">
        <p>Esta cotización venció. Comuníquese con el taller para recibir una actualizada.</p>
      </section>

      <footer>
        <a :href="pdfUrl" target="_blank" rel="noopener">Descargar en PDF</a>
        <a
          v-if="quote.tenantPhone"
          :href="`https://wa.me/${quote.tenantPhone}`"
          target="_blank"
          rel="noopener"
        >
          Escribir al taller
        </a>
      </footer>
    </article>

    <!-- La marca va discreta y al pie: esta página es del taller, no nuestra. Poner el
         logotipo de GarajApp encima del nombre del taller confundiría a quien la abre. -->
    <p v-if="quote" class="madeby">
      <BrandLogo variant="isotipo" :height="16" />
      <span>Cotización enviada con GarajApp</span>
    </p>
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

.madeby {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  margin: 0 0 1rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.card {
  width: min(44rem, 100%);
  padding: 1.5rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
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
  font-size: 0.875rem;
  color: var(--text-muted);
}

h1 {
  margin: 0.125rem 0;
  font-size: 1.375rem;
}

header p:last-child {
  margin: 0;
}

.badge {
  padding: 0.25rem 0.625rem;
  border-radius: 999px;
  font-size: 0.75rem;
  background: var(--surface-alt, #f4f4f5);
  border: 1px solid var(--border);
  white-space: nowrap;
}

.badge.s3 {
  border-color: #15803d;
  color: #15803d;
}

.badge.s4 {
  border-color: var(--danger);
  color: var(--danger);
}

.meta {
  display: flex;
  flex-wrap: wrap;
  gap: 1.5rem;
  margin: 0 0 1rem;
}

dt {
  font-size: 0.75rem;
  color: var(--text-muted);
}

dd {
  margin: 0;
}

.notes {
  margin: 0 0 1rem;
  padding: 0.625rem 0.75rem;
  border-radius: 6px;
  background: var(--surface-alt, #f4f4f5);
  font-size: 0.9375rem;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9375rem;
}

th,
td {
  padding: 0.5rem 0.25rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
  vertical-align: top;
}

th {
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.num {
  text-align: right;
  white-space: nowrap;
}

/*
 * En un teléfono angosto las tres columnas de importes no caben —cada monto lleva su moneda
 * y ninguna parte— y la tabla se sale de la pantalla, que es justo donde esta página se abre
 * casi siempre. Se esconde el precio unitario: lo que el cliente necesita ver es el total de
 * la línea, y el detalle completo lo tiene en el PDF.
 */
@media (max-width: 26rem) {
  .unitario {
    display: none;
  }
}

.totals {
  margin: 1rem 0 0;
  margin-left: auto;
  width: min(18rem, 100%);
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.9375rem;
}

.totals div {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
}

.totals .grand {
  margin-top: 0.375rem;
  padding-top: 0.5rem;
  border-top: 1px solid var(--border);
  font-size: 1.125rem;
  font-weight: 600;
}

.respond {
  margin-top: 1.5rem;
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface-alt, #f4f4f5);
}

.respond p {
  margin: 0 0 0.75rem;
}

.respond textarea {
  width: 100%;
  margin-bottom: 0.75rem;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

/* El botón principal es grande a propósito: esto se abre casi siempre en un teléfono. */
.actions button {
  padding: 0.625rem 1rem;
  font-size: 1rem;
}

.actions .primary {
  background: #15803d;
  border-color: #15803d;
  color: #fff;
}

.answered {
  margin-top: 1.5rem;
  padding: 0.875rem 1rem;
  border-radius: 8px;
  background: var(--surface-alt, #f4f4f5);
}

.answered p {
  margin: 0;
}

footer {
  display: flex;
  gap: 1rem;
  margin-top: 1.5rem;
  padding-top: 1rem;
  border-top: 1px solid var(--border);
  font-size: 0.875rem;
}

.center {
  text-align: center;
}

.small {
  font-size: 0.75rem;
}

.muted {
  color: var(--text-muted);
}

.error {
  margin-top: 1rem;
  color: var(--danger);
}
</style>

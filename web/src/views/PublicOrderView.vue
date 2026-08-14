<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { apiUrl, errorMessage } from '@/api/client'
import BrandLogo from '@/components/BrandLogo.vue'
import { publicOrdersApi } from '@/api/garaj'
import { WorkOrderStatus, type OrderTracking } from '@/types/domain'
import { formatDate, formatDateTime } from '@/utils/format'

// Página anónima, como la de la cotización y la del estado de cuenta: la abre el cliente desde
// el enlace de WhatsApp y el token de la URL es la única credencial. Es la que más se va a
// abrir de las tres: responde «¿ya está listo mi carro?» sin que nadie llame al taller.
const route = useRoute()
const token = computed(() => route.params.token as string)

const order = ref<OrderTracking | null>(null)
const error = ref('')
const loading = ref(true)

const logoTaller = computed(() => apiUrl(order.value?.tenantLogoUrl))
const facturaUrl = computed(() => publicOrdersApi.invoicePdfUrl(token.value))

/** El símbolo, no el código ISO: esta página la lee el cliente del taller. */
const SIMBOLO: Record<string, string> = { HNL: 'L', USD: '$' }

const money = (value: number) =>
  `${SIMBOLO[order.value?.currency ?? 'HNL'] ?? order.value?.currency} ${value.toLocaleString(
    'es-HN',
    { minimumFractionDigits: 2, maximumFractionDigits: 2 },
  )}`

/** Entregada o cancelada: el vehículo ya no está en el taller. */
const cerrada = computed(
  () =>
    order.value?.status === WorkOrderStatus.Delivered ||
    order.value?.status === WorkOrderStatus.Cancelled,
)

const pasosHechos = computed(() => order.value?.steps.filter((s) => s.isDone).length ?? 0)

onMounted(async () => {
  try {
    order.value = await publicOrdersApi.get(token.value)
  } catch (e) {
    error.value = errorMessage(
      e,
      'No encontramos esta orden. Puede que el enlace haya cambiado.',
    )
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <main class="page">
    <p v-if="loading" class="muted">Cargando…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <article v-else-if="order" class="card">
      <header>
        <div>
          <!-- El logo del taller antes que nada: quien abre esto quiere reconocer con quién
               dejó su carro. -->
          <img v-if="logoTaller" class="logo-taller" :src="logoTaller" :alt="order.tenantName" />
          <p class="tenant">{{ order.tenantName }}</p>
          <h1>{{ order.vehicleLabel }}</h1>
          <p class="muted small">
            <span v-if="order.plate" class="placa">{{ order.plate }}</span>
            Orden {{ order.number }} · {{ order.branchName }}
          </p>
        </div>

        <div class="estado">
          <span class="chip">{{ order.statusLabel }}</span>
          <span v-if="!cerrada && order.promisedAt" class="muted small">
            Prometido para {{ formatDate(order.promisedAt) }}
          </span>
          <span v-else-if="order.closedAt" class="muted small">
            Entregada el {{ formatDate(order.closedAt) }}
          </span>
        </div>
      </header>

      <dl class="meta">
        <div>
          <dt>Cliente</dt>
          <dd>{{ order.customerName }}</dd>
        </div>
        <div>
          <dt>Ingresó</dt>
          <dd>{{ formatDate(order.openedAt) }}</dd>
        </div>
      </dl>

      <section>
        <h2>Por qué ingresó</h2>
        <p class="descripcion">{{ order.description }}</p>
      </section>

      <section v-if="order.steps.length">
        <h2>
          Trabajo
          <span class="muted small">{{ pasosHechos }} de {{ order.steps.length }}</span>
        </h2>
        <ul class="pasos">
          <li v-for="(step, i) in order.steps" :key="i" :class="{ hecho: step.isDone }">
            <span class="marca" aria-hidden="true">{{ step.isDone ? '✓' : '○' }}</span>
            <span>
              {{ step.title }}
              <span v-if="step.completedAt" class="muted small">
                · {{ formatDate(step.completedAt) }}
              </span>
            </span>
          </li>
        </ul>
      </section>

      <section v-if="order.photos.length">
        <h2>Fotos</h2>
        <div class="fotos">
          <!-- Al original en pestaña nueva: en el teléfono es la forma de ver el detalle. -->
          <a
            v-for="(photo, i) in order.photos"
            :key="i"
            :href="photo.url"
            target="_blank"
            rel="noopener"
          >
            <img :src="photo.thumbnailUrl" :alt="photo.caption ?? 'Foto del trabajo'" />
            <span v-if="photo.caption || photo.stepTitle" class="muted small">
              {{ photo.caption ?? photo.stepTitle }}
            </span>
          </a>
        </div>
      </section>

      <section v-if="order.timeline.length">
        <h2>Cómo ha avanzado</h2>
        <ol class="linea">
          <li v-for="(entry, i) in order.timeline" :key="i">
            <strong>{{ entry.statusLabel }}</strong>
            <span class="muted small">{{ formatDateTime(entry.changedAt) }}</span>
            <p v-if="entry.note" class="nota">{{ entry.note }}</p>
          </li>
        </ol>
      </section>

      <section v-if="order.invoice" class="factura">
        <h2>Su factura</h2>
        <div class="fila">
          <span>{{ order.invoice.number }}</span>
          <strong class="num">{{ money(order.invoice.total) }}</strong>
        </div>
        <div v-if="order.invoice.paid > 0" class="fila muted small">
          <span>Abonado</span>
          <span class="num">−{{ money(order.invoice.paid) }}</span>
        </div>
        <div v-if="order.invoice.balance > 0" class="fila saldo">
          <span>
            Saldo pendiente
            <template v-if="order.invoice.dueDate">
              · acordado para {{ formatDate(order.invoice.dueDate) }}
            </template>
          </span>
          <strong class="num">{{ money(order.invoice.balance) }}</strong>
        </div>
        <p v-else class="pagado small">Pagada. Gracias.</p>
      </section>

      <footer>
        <a v-if="order.invoice" :href="facturaUrl" target="_blank" rel="noopener">
          Descargar la factura
        </a>
        <a
          v-if="order.tenantPhone"
          :href="`https://wa.me/${order.tenantPhone}`"
          target="_blank"
          rel="noopener"
        >
          Escribir al taller
        </a>
      </footer>
    </article>

    <!-- La marca va discreta y al pie: esta página es del taller, no nuestra. -->
    <p v-if="order" class="madeby">
      <BrandLogo variant="isotipo" :height="16" />
      <span>Seguimiento enviado con GarajApp</span>
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
  font-weight: 600;
}

h1 {
  margin: 0.125rem 0;
  font-size: 1.25rem;
}

h2 {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
  margin: 0 0 0.5rem;
  font-size: 0.9375rem;
}

.placa {
  display: inline-block;
  margin-right: 0.375rem;
  padding: 0.0625rem 0.375rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  font-weight: 600;
  color: var(--text);
}

.estado {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.25rem;
}

.chip {
  padding: 0.25rem 0.625rem;
  border-radius: 999px;
  background: var(--accent);
  color: #fff;
  font-size: 0.8125rem;
  font-weight: 600;
}

.meta {
  display: flex;
  flex-wrap: wrap;
  gap: 1.5rem;
  margin: 0 0 1.25rem;
  padding: 0.75rem;
  border-radius: var(--radius-sm);
  background: var(--bg);
}

dt {
  font-size: 0.75rem;
  color: var(--text-muted);
}

dd {
  margin: 0;
  font-weight: 500;
}

section {
  margin-bottom: 1.25rem;
}

.descripcion {
  margin: 0;
  white-space: pre-wrap;
}

.pasos {
  margin: 0;
  padding: 0;
  list-style: none;
}

.pasos li {
  display: flex;
  gap: 0.5rem;
  padding: 0.25rem 0;
}

.pasos .marca {
  color: var(--text-muted);
}

.pasos .hecho .marca {
  color: var(--success, #16a34a);
}

.fotos {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(7rem, 1fr));
  gap: 0.5rem;
}

.fotos a {
  display: block;
}

.fotos img {
  width: 100%;
  aspect-ratio: 1;
  border-radius: var(--radius-sm);
  object-fit: cover;
}

.linea {
  margin: 0;
  padding-left: 1rem;
}

.linea li {
  margin-bottom: 0.5rem;
}

.linea strong {
  margin-right: 0.375rem;
}

.nota {
  margin: 0.125rem 0 0;
  font-size: 0.875rem;
}

.factura .fila {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.125rem 0;
}

.factura .saldo {
  margin-top: 0.25rem;
  padding-top: 0.375rem;
  border-top: 1px solid var(--border);
}

.pagado {
  margin: 0.375rem 0 0;
  color: var(--success, #16a34a);
}

footer {
  display: flex;
  gap: 1rem;
  padding-top: 1rem;
  border-top: 1px solid var(--border);
  font-size: 0.9375rem;
}

.madeby {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  margin: 0 0 1rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.num {
  white-space: nowrap;
  font-variant-numeric: tabular-nums;
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

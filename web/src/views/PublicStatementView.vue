<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { apiUrl, errorMessage } from '@/api/client'
import BrandLogo from '@/components/BrandLogo.vue'
import { publicStatementsApi } from '@/api/garaj'
import { PAYMENT_METHOD_LABEL, type CustomerStatement } from '@/types/domain'
import { formatDate } from '@/utils/format'

// Página anónima, igual que la de la cotización: la abre el cliente desde el enlace de
// WhatsApp y el token de la URL es la única credencial. No pide nada más ni deja llegar a
// ninguna otra parte de la API.
const route = useRoute()
const token = computed(() => route.params.token as string)

const statement = ref<CustomerStatement | null>(null)
const error = ref('')
const loading = ref(true)

/** El logo llega bajo el token del cliente, no bajo el id del taller. */
const logoTaller = computed(() => apiUrl(statement.value?.tenantLogoUrl))
const pdfUrl = computed(() => publicStatementsApi.pdfUrl(token.value))

/** El símbolo, no el código ISO: esta página la lee el cliente del taller. */
const SIMBOLO: Record<string, string> = { HNL: 'L', USD: '$' }

const money = (value: number) =>
  `${SIMBOLO[statement.value?.currency ?? 'HNL'] ?? statement.value?.currency} ${value.toLocaleString(
    'es-HN',
    { minimumFractionDigits: 2, maximumFractionDigits: 2 },
  )}`

onMounted(async () => {
  try {
    statement.value = await publicStatementsApi.get(token.value)
  } catch (e) {
    error.value = errorMessage(
      e,
      'No encontramos este estado de cuenta. Puede que el enlace haya cambiado.',
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

    <article v-else-if="statement" class="card">
      <header>
        <div>
          <!-- El logo del taller antes que nada: quien abre esto quiere reconocer a quién le
               debe. -->
          <img
            v-if="logoTaller"
            class="logo-taller"
            :src="logoTaller"
            :alt="statement.tenantName"
          />
          <p class="tenant">{{ statement.tenantName }}</p>
          <h1>Estado de cuenta</h1>
          <p class="muted small">
            Al {{ formatDate(statement.asOf) }}
            <template v-if="statement.tenantPhone"> · {{ statement.tenantPhone }}</template>
          </p>
        </div>

        <div class="saldo">
          <span class="muted small">Saldo pendiente</span>
          <strong>{{ money(statement.total) }}</strong>
          <span v-if="statement.overdue > 0" class="danger small">
            {{ money(statement.overdue) }} vencido
          </span>
        </div>
      </header>

      <dl class="meta">
        <div>
          <dt>Cliente</dt>
          <dd>
            {{ statement.billingName ?? statement.customerName }}
            <div v-if="statement.billingName" class="muted small">
              {{ statement.customerName }}
            </div>
          </dd>
        </div>
        <div v-if="statement.taxId">
          <dt>RTN</dt>
          <dd>{{ statement.taxId }}</dd>
        </div>
      </dl>

      <p v-if="!statement.sales.length" class="pagado">
        No tiene saldo pendiente. Todo lo facturado está pagado.
      </p>

      <section v-for="sale in statement.sales" :key="sale.number" class="factura">
        <div class="fila">
          <div>
            <strong>{{ sale.number }}</strong>
            <span v-if="sale.workOrderNumber" class="muted small">
              · Orden {{ sale.workOrderNumber }}
            </span>
            <div class="muted small">
              {{ formatDate(sale.saleDate) }} · {{ sale.branchName }}
              <template v-if="sale.dueDate">
                ·
                <span :class="{ danger: sale.isOverdue }">
                  {{ sale.isOverdue ? 'venció' : 'vence' }} {{ formatDate(sale.dueDate) }}
                </span>
              </template>
              <template v-else> · sin fecha acordada</template>
            </div>
          </div>
          <span class="num">{{ money(sale.total) }}</span>
        </div>

        <div v-for="(payment, i) in sale.payments" :key="i" class="fila abono">
          <span>
            Abono {{ formatDate(payment.paidAt) }}
            <span class="muted">· {{ PAYMENT_METHOD_LABEL[payment.method] }}</span>
            <span v-if="payment.reference" class="muted">· {{ payment.reference }}</span>
          </span>
          <span class="num muted">−{{ money(payment.amount) }}</span>
        </div>

        <p v-if="!sale.payments.length" class="abono muted small">Sin abonos</p>

        <div class="fila saldo-factura">
          <span>Saldo</span>
          <span class="num">{{ money(sale.balance) }}</span>
        </div>
      </section>

      <div v-if="statement.sales.length" class="totales">
        <div>
          <span>Total adeudado</span>
          <strong>{{ money(statement.total) }}</strong>
        </div>
        <div v-if="statement.overdue > 0" class="danger small">
          <span>Vencido</span>
          <span>{{ money(statement.overdue) }}</span>
        </div>
      </div>

      <footer>
        <a :href="pdfUrl" target="_blank" rel="noopener">Descargar en PDF</a>
        <a
          v-if="statement.tenantPhone"
          :href="`https://wa.me/${statement.tenantPhone}`"
          target="_blank"
          rel="noopener"
        >
          Escribir al taller
        </a>
      </footer>

      <p class="aviso muted small">
        Resumen de cuenta. No es documento fiscal: la factura es la de cada venta.
      </p>
    </article>

    <!-- La marca va discreta y al pie: esta página es del taller, no nuestra. -->
    <p v-if="statement" class="madeby">
      <BrandLogo variant="isotipo" :height="16" />
      <span>Estado de cuenta enviado con GarajApp</span>
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

.saldo {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
}

.saldo strong {
  font-size: 1.5rem;
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

.factura {
  padding: 0.75rem 0;
  border-bottom: 1px solid var(--border);
}

.fila {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 1rem;
}

.abono {
  padding-left: 1rem;
  font-size: 0.875rem;
}

.saldo-factura {
  margin-top: 0.25rem;
  padding-left: 1rem;
  padding-top: 0.25rem;
  border-top: 1px solid var(--border);
  font-weight: 600;
  font-size: 0.9375rem;
}

.totales {
  margin-top: 1rem;
  margin-left: auto;
  width: min(18rem, 100%);
}

.totales div {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
}

.totales strong {
  font-size: 1.25rem;
}

.pagado {
  padding: 1rem 0;
  font-size: 1.0625rem;
}

footer {
  display: flex;
  gap: 1rem;
  margin-top: 1.25rem;
  padding-top: 1rem;
  border-top: 1px solid var(--border);
  font-size: 0.9375rem;
}

.aviso {
  margin: 0.75rem 0 0;
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

.danger {
  color: var(--danger);
}

.error {
  color: var(--danger);
}
</style>

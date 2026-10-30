<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { apiUrl, errorMessage } from '@/api/client'
import BrandLogo from '@/components/BrandLogo.vue'
import { publicHistoryApi } from '@/api/garaj'
import { AssetKind, type CustomerHistory } from '@/types/domain'
import { formatDate } from '@/utils/format'

// Página anónima, igual que la del estado de cuenta: la abre el cliente desde el enlace de
// WhatsApp y el token de la URL es la única credencial.
const route = useRoute()
const token = computed(() => route.params.token as string)

const historial = ref<CustomerHistory | null>(null)
const error = ref('')
const loading = ref(true)

/** El logo llega bajo el token del cliente, no bajo el id del taller. */
const logoTaller = computed(() => apiUrl(historial.value?.logoUrl))

const esDeEquipos = computed(
  () => historial.value?.assets[0]?.kind === AssetKind.Equipment,
)

/** El símbolo, no el código ISO: esta página la lee el cliente del taller. */
const SIMBOLO: Record<string, string> = { HNL: 'L', USD: '$' }

const money = (value: number) =>
  `${SIMBOLO[historial.value?.currency ?? 'HNL'] ?? historial.value?.currency} ${value.toLocaleString(
    'es-HN',
    { minimumFractionDigits: 2, maximumFractionDigits: 2 },
  )}`

const visitas = computed(() =>
  (historial.value?.assets ?? []).reduce((total, a) => total + a.visits.length, 0),
)

onMounted(async () => {
  try {
    historial.value = await publicHistoryApi.get(token.value)
  } catch (e) {
    error.value = errorMessage(
      e,
      'No encontramos este historial. Puede que el enlace haya cambiado.',
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

    <article v-else-if="historial" class="card">
      <header>
        <div>
          <img
            v-if="logoTaller"
            class="logo-taller"
            :src="logoTaller"
            :alt="historial.tenantName"
          />
          <p class="tenant">{{ historial.tenantName }}</p>
          <h1>Historial de servicio</h1>
          <p class="muted small">
            {{ historial.customerName }}
            <template v-if="historial.tenantPhone"> · {{ historial.tenantPhone }}</template>
          </p>
        </div>
        <div class="cuenta">
          <span class="muted small">Visitas</span>
          <strong>{{ visitas }}</strong>
        </div>
      </header>

      <p v-if="!historial.assets.length" class="vacio">
        Todavía no tenemos visitas registradas a su nombre.
      </p>

      <section v-for="asset in historial.assets" :key="asset.label + (asset.identifier ?? '')">
        <h2 class="activo">
          {{ asset.label }}
          <span v-if="asset.identifier" class="muted">· {{ asset.identifier }}</span>
        </h2>

        <div v-for="visita in asset.visits" :key="visita.number" class="visita">
          <div class="fila">
            <div>
              <strong>{{ formatDate(visita.openedAt) }}</strong>
              <span class="muted small"> · Orden {{ visita.number }}</span>
              <span v-if="!visita.closedAt" class="abierta">en el taller</span>
              <div class="motivo">{{ visita.description }}</div>
              <div v-if="visita.mileage" class="muted small">
                {{ visita.mileage.toLocaleString('es-HN') }} km al recibirlo
              </div>
            </div>
            <span v-if="visita.total !== null" class="num">{{ money(visita.total) }}</span>
          </div>

          <ul v-if="visita.steps.length" class="pasos">
            <li v-for="(paso, i) in visita.steps" :key="i">{{ paso }}</li>
          </ul>
        </div>
      </section>

      <footer v-if="historial.tenantPhone">
        <a :href="`https://wa.me/${historial.tenantPhone}`" target="_blank" rel="noopener">
          Escribir al taller
        </a>
      </footer>

      <p class="aviso muted small">
        Lo que hemos hecho {{ esDeEquipos ? 'a sus equipos' : 'a sus vehículos' }} en este
        taller. Los montos son los de la factura de cada visita.
      </p>
    </article>

    <!-- La marca va discreta y al pie: esta página es del taller, no nuestra. -->
    <p v-if="historial" class="madeby">
      <BrandLogo variant="isotipo" :height="16" />
      <span>Historial enviado con GarajApp</span>
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

.cuenta {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
}

.cuenta strong {
  font-size: 1.5rem;
}

.activo {
  margin: 1.25rem 0 0.25rem;
  padding-bottom: 0.25rem;
  border-bottom: 1px solid var(--border);
  font-size: 1rem;
}

.visita {
  padding: 0.75rem 0;
  border-bottom: 1px solid var(--border);
}

.fila {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 1rem;
}

.motivo {
  margin-top: 0.125rem;
}

.abierta {
  margin-left: 0.5rem;
  padding: 0.0625rem 0.4rem;
  border-radius: 999px;
  background: var(--bg);
  font-size: 0.75rem;
  color: var(--text-muted);
}

.pasos {
  margin: 0.375rem 0 0 1rem;
  padding: 0;
  font-size: 0.875rem;
  color: var(--text-muted);
}

.vacio {
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

.error {
  padding: 1rem;
  color: var(--danger);
}
</style>

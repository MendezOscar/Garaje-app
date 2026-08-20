<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { reportsApi, workOrdersApi } from '@/api/garaj'
import StatusBadge from '@/components/StatusBadge.vue'
import { useAuthStore } from '@/stores/auth'
import {
  WorkOrderStatus,
  type CashClose,
  type Dashboard,
  type WorkOrderListItem,
} from '@/types/domain'
import { formatMoney, relativeTime } from '@/utils/format'

/**
 * El inicio del Dueño: cómo va el día del taller.
 *
 * Antes entraba directo al tablero, y estos datos —lo cobrado, lo atrasado, lo que espera
 * respuesta, lo vencido— vivían dentro de Reportes, que es una pantalla a la que se entra a
 * mirar y no a trabajar. Aquí cada cifra es un enlace al trabajo que la resuelve.
 *
 * Sin backend nuevo: `/api/reports/dashboard` ya devolvía todo esto, incluido el conteo por
 * estado, y el cierre de caja da lo cobrado, que no es lo mismo que lo facturado.
 */
const auth = useAuthStore()

const dashboard = ref<Dashboard | null>(null)
const caja = ref<CashClose | null>(null)
const recientes = ref<WorkOrderListItem[]>([])
const loading = ref(true)
const error = ref('')

const hoy = new Intl.DateTimeFormat('es-HN', {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
}).format(new Date())

/** Los nueve estados agrupados en cuatro carriles, como en el tablero. */
const CARRILES: { titulo: string; tono: string; estados: WorkOrderStatus[] }[] = [
  {
    titulo: 'Entrando',
    tono: 'activo',
    estados: [WorkOrderStatus.Received, WorkOrderStatus.Diagnosing],
  },
  {
    titulo: 'En trabajo',
    tono: 'activo',
    estados: [WorkOrderStatus.InProgress, WorkOrderStatus.Testing],
  },
  {
    titulo: 'Detenidas',
    tono: 'detenido',
    estados: [WorkOrderStatus.WaitingApproval, WorkOrderStatus.WaitingParts],
  },
  { titulo: 'Listas para entrega', tono: 'listo', estados: [WorkOrderStatus.Ready] },
]

const patio = computed(() =>
  CARRILES.map((carril) => ({
    ...carril,
    cantidad: carril.estados.reduce(
      (suma, estado) =>
        suma + (dashboard.value?.workOrdersByStatus.find((x) => x.status === estado)?.count ?? 0),
      0,
    ),
  })),
)

/** Lo que pide atención. En cero se pinta en gris: si todo grita, nada avisa. */
const atencion = computed(() => {
  const d = dashboard.value
  if (!d) return []

  return [
    {
      rotulo: 'Por atender',
      valor: `${d.pendingRequests}`,
      pie: 'requerimientos',
      tono: d.pendingRequests > 0 ? 'espera' : '',
      to: { name: 'service-requests' },
    },
    {
      rotulo: 'Atrasadas',
      valor: `${d.lateWorkOrders}`,
      pie: 'pasó la fecha prometida',
      tono: d.lateWorkOrders > 0 ? 'alerta' : '',
      to: { name: 'work-orders' },
    },
    {
      rotulo: 'Sin respuesta',
      valor: `${d.quotesAwaitingResponse}`,
      pie: 'cotizaciones enviadas',
      tono: d.quotesAwaitingResponse > 0 ? 'espera' : '',
      to: { name: 'quotes' },
    },
    {
      rotulo: 'Vencido',
      valor: formatMoney(d.overdueReceivables),
      pie: `de ${formatMoney(d.receivables)} por cobrar`,
      tono: d.overdueReceivables > 0 ? 'alerta' : '',
      to: { name: 'receivables' },
    },
    {
      rotulo: 'Bajo mínimo',
      valor: `${d.partsBelowMinimum}`,
      pie: 'repuestos en bodega',
      tono: d.partsBelowMinimum > 0 ? 'espera' : '',
      to: { name: 'inventory' },
    },
  ]
})

async function load() {
  loading.value = true
  error.value = ''

  try {
    const sucursal = auth.activeBranchId || undefined

    const [resumen, cierre, ordenes] = await Promise.all([
      reportsApi.dashboard(sucursal),
      // Lo cobrado del día. Si falla —un taller recién abierto no tiene ni un abono— la
      // pantalla sigue: la cifra se pinta como un guion y no como un error.
      reportsApi.cashClose({ branchId: sucursal }).catch(() => null),
      workOrdersApi.list({ onlyOpen: true, branchId: sucursal, pageSize: 200 }),
    ])

    dashboard.value = resumen
    caja.value = cierre
    recientes.value = [...ordenes.items]
      .sort((a, b) => b.openedAt.localeCompare(a.openedAt))
      .slice(0, 6)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el resumen del día.')
  } finally {
    loading.value = false
  }
}

onMounted(load)

// El selector de sucursal de la barra de arriba manda aquí: hasta hoy no lo consumía ninguna
// pantalla y era un control que no hacía nada.
watch(() => auth.activeBranchId, load)
</script>

<template>
  <section>
    <header class="cabecera">
      <div>
        <h1>Hoy en el taller</h1>
        <p class="muted small">{{ auth.user?.tenantName }} · {{ hoy }}</p>
      </div>
      <!-- La acción más frecuente del mostrador, y hasta hoy estaba a dos pantallas. -->
      <RouterLink class="boton" :to="{ name: 'receive-vehicle' }">Recibir vehículo</RouterLink>
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="muted">Cargando…</p>

    <template v-if="dashboard">
      <div class="dinero">
        <article class="panel principal">
          <p class="rotulo">Cobrado hoy</p>
          <strong class="cifra num">{{ caja ? formatMoney(caja.total) : '—' }}</strong>
          <p class="muted small">
            {{ caja ? `${caja.paymentCount} abono(s)` : 'sin datos de caja' }} ·
            <RouterLink :to="{ name: 'cash-close' }">cerrar la caja</RouterLink>
          </p>
        </article>

        <article class="panel">
          <p class="rotulo">Facturado</p>
          <div class="tres">
            <div>
              <span class="muted small">Hoy</span>
              <strong class="num">{{ formatMoney(dashboard.revenueToday) }}</strong>
            </div>
            <div>
              <span class="muted small">Semana</span>
              <strong class="num">{{ formatMoney(dashboard.revenueWeek) }}</strong>
            </div>
            <div>
              <span class="muted small">Mes</span>
              <strong class="num">{{ formatMoney(dashboard.revenueMonth) }}</strong>
            </div>
          </div>
          <p class="muted small">
            Lo facturado no es lo cobrado: una venta a crédito suma el día que se emite.
            <RouterLink :to="{ name: 'reports' }">Ver reportes</RouterLink>
          </p>
        </article>
      </div>

      <h2 class="titulo">Lo que pide atención</h2>
      <div class="tejas">
        <RouterLink
          v-for="teja in atencion"
          :key="teja.rotulo"
          class="teja"
          :class="teja.tono"
          :to="teja.to"
        >
          <span class="rotulo">{{ teja.rotulo }}</span>
          <strong class="num">{{ teja.valor }}</strong>
          <span class="muted small">{{ teja.pie }}</span>
        </RouterLink>
      </div>

      <div class="abajo">
        <article class="panel">
          <h2 class="titulo">El patio</h2>
          <RouterLink
            v-for="carril in patio"
            :key="carril.titulo"
            class="carril"
            :to="{ name: 'work-orders' }"
          >
            <span class="punto" :class="carril.tono" />
            <span class="nombre">{{ carril.titulo }}</span>
            <strong class="num">{{ carril.cantidad }}</strong>
          </RouterLink>
          <p class="muted small total">
            {{ dashboard.openWorkOrders }}
            {{ dashboard.openWorkOrders === 1 ? 'orden abierta' : 'órdenes abiertas' }} en total
          </p>
        </article>

        <article class="panel">
          <h2 class="titulo">Últimas que entraron</h2>
          <p v-if="!recientes.length" class="muted small">No hay órdenes abiertas.</p>
          <RouterLink
            v-for="order in recientes"
            :key="order.id"
            class="orden"
            :to="{ name: 'work-order', params: { id: order.id } }"
          >
            <span class="num folio">{{ order.number }}</span>
            <span class="vehiculo">
              {{ order.vehicleLabel }}
              <span v-if="order.plate" class="placa num">{{ order.plate }}</span>
            </span>
            <span class="estado"><StatusBadge :status="order.status" /></span>
            <span class="muted small cuando">{{ relativeTime(order.openedAt) }}</span>
          </RouterLink>
        </article>
      </div>
    </template>
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

.cabecera h1 {
  margin: 0;
}

.boton {
  padding: 0.5rem 0.875rem;
  border-radius: var(--radius-sm);
  background: var(--accent);
  color: #fff;
  font-weight: 500;
}

.boton:hover {
  text-decoration: none;
}

.panel {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.rotulo {
  margin: 0 0 0.25rem;
  font-size: 0.6875rem;
  font-weight: 600;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--text-muted);
}

/* ---------------------------------------------------------------- dinero */

.dinero {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 2fr);
  gap: 0.75rem;
  margin-bottom: 1.5rem;
}

/* Lo cobrado va en grande y a la izquierda: es la pregunta de verdad del día. */
.principal .cifra {
  display: block;
  font-family: var(--font-display);
  font-size: 2rem;
  line-height: 1.15;
}

.tres {
  display: flex;
  gap: 1.5rem;
  margin-bottom: 0.5rem;
}

.tres div {
  display: flex;
  flex-direction: column;
}

.tres strong {
  font-size: 1.125rem;
}

/* ---------------------------------------------------------------- tejas */

.titulo {
  margin: 0 0 0.625rem;
  font-size: 0.9375rem;
}

.tejas {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(9.5rem, 1fr));
  gap: 0.75rem;
  margin-bottom: 1.5rem;
}

.teja {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  color: inherit;
}

.teja:hover {
  border-color: var(--accent);
  text-decoration: none;
}

.teja strong {
  font-family: var(--font-display);
  font-size: 1.375rem;
  line-height: 1.1;
}

/* El borde tira hacia el color del estado sin llegar a serlo: marca la teja de lejos y deja
   el color pleno para la cifra. */
.teja.espera {
  border-color: color-mix(in srgb, var(--warning) 45%, var(--border));
}

.teja.espera strong {
  color: var(--warning);
}

.teja.alerta {
  border-color: color-mix(in srgb, var(--danger) 45%, var(--border));
}

.teja.alerta strong {
  color: var(--danger);
}

/* ---------------------------------------------------------------- abajo */

.abajo {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.6fr);
  gap: 0.75rem;
  align-items: start;
}

.carril {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  padding: 0.5rem 0;
  border-bottom: 1px solid var(--border);
  color: inherit;
}

.carril:hover {
  text-decoration: none;
  color: var(--accent);
}

.carril .nombre {
  flex: 1;
}

.punto {
  width: 9px;
  height: 9px;
  border-radius: 999px;
  background: var(--text-muted);
}

.punto.activo {
  background: var(--accent);
}

.punto.detenido {
  background: var(--warning);
}

.punto.listo {
  background: var(--success);
}

.total {
  margin: 0.625rem 0 0;
}

.orden {
  display: grid;
  grid-template-columns: 7.5rem minmax(0, 1fr) auto 5rem;
  align-items: center;
  gap: 0.625rem;
  padding: 0.5rem 0;
  border-bottom: 1px solid var(--border);
  color: inherit;
}

.orden:last-of-type {
  border-bottom: none;
}

.orden:hover {
  text-decoration: none;
  color: var(--accent);
}

.folio {
  font-size: 0.8125rem;
  white-space: nowrap;
}

.vehiculo {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  min-width: 0;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.placa {
  padding: 0 0.25rem;
  border: 1px solid var(--border);
  border-radius: 3px;
  font-size: 0.75rem;
}

.cuando {
  text-align: right;
  white-space: nowrap;
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

/* ---------------------------------------------------------------- pantalla angosta */

@media (max-width: 60rem) {
  .dinero,
  .abajo {
    grid-template-columns: 1fr;
  }

  .tres {
    justify-content: space-between;
    gap: 0.75rem;
  }

  /* En el teléfono el folio y el tiempo se van a su propia línea: cuatro columnas en 390 px
     dejan el vehículo en dos letras. */
  .orden {
    grid-template-columns: minmax(0, 1fr) auto;
    grid-template-areas:
      'folio estado'
      'vehiculo cuando';
    row-gap: 0.125rem;
  }

  .folio {
    grid-area: folio;
  }

  .estado {
    grid-area: estado;
    justify-self: end;
  }

  .vehiculo {
    grid-area: vehiculo;
  }

  .cuando {
    grid-area: cuando;
  }
}
</style>

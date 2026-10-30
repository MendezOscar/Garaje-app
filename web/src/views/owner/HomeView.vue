<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { customersApi, reportsApi, salesApi, workOrdersApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import { useTaller } from '@/composables/useTaller'
import { useAuthStore } from '@/stores/auth'
import {
  WORK_ORDER_STATUS_LABEL,
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
 * Sin backend nuevo: `/api/reports/dashboard` ya devolvía todo esto —incluido el conteo por
 * estado y los últimos catorce días para la gráfica—, y el cierre de caja da lo cobrado, que
 * no es lo mismo que lo facturado.
 */
const auth = useAuthStore()
const { vocabulario, cargar: cargarTaller } = useTaller()

const dashboard = ref<Dashboard | null>(null)
const caja = ref<CashClose | null>(null)
const recientes = ref<WorkOrderListItem[]>([])
const loading = ref(true)
const error = ref('')

/**
 * Los primeros pasos de un taller recién dado de alta, o null cuando ya empezó.
 *
 * Un taller nuevo entra y ve todo en ceros: la pantalla es correcta y no dice nada. Esto le
 * dice por dónde empezar, y desaparece solo cuando los tres están dados —no hay nada que
 * apagar ni que recordar—.
 */
const primerosPasos = ref<{ cliente: boolean; vehiculo: boolean; cobro: boolean } | null>(null)

const pasosHechos = computed(() => {
  const p = primerosPasos.value
  if (!p) return 0
  return Number(p.cliente) + Number(p.vehiculo) + Number(p.cobro)
})

const hoy = new Intl.DateTimeFormat('es-HN', {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
}).format(new Date())

/** Saludar por el nombre cuesta nada y la pantalla deja de parecer un reporte. */
const saludo = computed(() => {
  const hora = new Date().getHours()
  const momento = hora < 12 ? 'Buenos días' : hora < 19 ? 'Buenas tardes' : 'Buenas noches'
  const nombre = auth.user?.fullName?.split(' ')[0]

  return nombre ? `${momento}, ${nombre}` : momento
})

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

/** El patio como una sola barra: de un vistazo se ve si el taller está trabado atrás. */
const barra = computed(() => {
  const total = patio.value.reduce((suma, c) => suma + c.cantidad, 0)
  if (!total) return []

  return patio.value
    .filter((c) => c.cantidad > 0)
    .map((c) => ({ ...c, parte: (c.cantidad / total) * 100 }))
})

/** Cuántas hay en cada estado, para el pie del patio. */
const detallePorEstado = computed(() =>
  (dashboard.value?.workOrdersByStatus ?? [])
    .filter((x) => x.count > 0)
    .map((x) => `${x.count} ${WORK_ORDER_STATUS_LABEL[x.status].toLowerCase()}`)
    .join(' · '),
)

/**
 * Lo facturado en los últimos catorce días, una barra por día.
 *
 * Era un área: con ingresos en dos días de catorce, la línea se iba de un punto al otro e
 * inventaba ingresos en los doce de en medio. Un día sin facturar es una barra que no está,
 * que es exactamente lo que pasó.
 *
 * Sin librería a propósito: son catorce barras, y traer una de gráficas costaría más
 * kilobytes que todo el panel.
 */
const grafica = computed(() => {
  const puntos = dashboard.value?.lastDays ?? []
  if (puntos.length < 2) return null

  const max = Math.max(...puntos.map((p) => p.total), 1)

  return {
    barras: puntos.map((p) => ({
      etiqueta: p.periodLabel,
      total: p.total,
      // Mínimo visible para que un día con poco no desaparezca contra el eje.
      alto: p.total > 0 ? Math.max((p.total / max) * 100, 4) : 0,
    })),
    total: puntos.reduce((suma, p) => suma + p.total, 0),
    dias: puntos.length,
    conVenta: puntos.filter((p) => p.total > 0).length,
  }
})

/**
 * Lo que pide atención, en renglones y no en tejas: cada uno dice qué pasa y lleva a donde se
 * resuelve. Lo que está en cero no se pinta —una lista de ceros no avisa de nada—, y si no
 * queda ninguno la tarjeta lo celebra en una línea.
 */
const atencion = computed(() => {
  const d = dashboard.value
  if (!d) return []

  const filas = [
    {
      clave: 'atrasadas',
      accion: 'Ver cuáles y avisarle al cliente',
      icono: 'reloj',
      tono: 'alerta',
      texto: `${d.lateWorkOrders} ${d.lateWorkOrders === 1 ? 'orden pasó' : 'órdenes pasaron'} su fecha de entrega.`,
      cuantos: d.lateWorkOrders,
      to: { name: 'work-orders' },
    },
    {
      clave: 'requerimientos',
      accion: 'Recibir o rechazar',
      icono: 'bandeja',
      tono: 'espera',
      texto: `${d.pendingRequests} ${d.pendingRequests === 1 ? 'requerimiento' : 'requerimientos'} sin atender.`,
      cuantos: d.pendingRequests,
      to: { name: 'service-requests' },
    },
    {
      clave: 'citas-hoy',
      accion: 'Ver la agenda del día',
      icono: 'calendario',
      tono: 'espera',
      texto: `${d.appointmentsToday} ${d.appointmentsToday === 1 ? 'cita apartada' : 'citas apartadas'} para hoy.`,
      cuantos: d.appointmentsToday,
      to: { name: 'appointments' },
    },
    {
      clave: 'citas-por-confirmar',
      accion: 'Confirmar o mover el día',
      icono: 'calendario',
      tono: 'espera',
      texto: `${d.appointmentsToConfirm} ${d.appointmentsToConfirm === 1 ? 'cita espera' : 'citas esperan'} que el taller confirme.`,
      cuantos: d.appointmentsToConfirm,
      to: { name: 'appointments' },
    },
    {
      clave: 'cotizaciones',
      accion: 'Recordarle por WhatsApp',
      icono: 'papel',
      tono: 'espera',
      texto: `${d.quotesAwaitingResponse} ${d.quotesAwaitingResponse === 1 ? 'cotización' : 'cotizaciones'} sin respuesta del cliente.`,
      cuantos: d.quotesAwaitingResponse,
      to: { name: 'quotes' },
    },
    {
      clave: 'vencido',
      accion: 'Cobrar o mandar el estado de cuenta',
      icono: 'dinero',
      tono: 'alerta',
      texto: `${formatMoney(d.overdueReceivables)} vencidos, de ${formatMoney(d.receivables)} por cobrar.`,
      cuantos: d.overdueReceivables,
      to: { name: 'receivables' },
    },
    {
      clave: 'bodega',
      accion: 'Armar la lista de compras',
      icono: 'caja',
      tono: 'espera',
      texto: `${d.partsBelowMinimum} ${d.partsBelowMinimum === 1 ? 'repuesto' : 'repuestos'} en o por debajo del mínimo.`,
      cuantos: d.partsBelowMinimum,
      to: { name: 'inventory' },
    },
  ]

  return filas.filter((f) => f.cuantos > 0)
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
      // Seis, que son las que se pintan. La API ya las devuelve de la más reciente a la más
      // vieja, así que pedir doscientas para quedarse con seis era mover treinta veces más
      // datos por gusto —y el taller que más órdenes tenga es el que más lo pagaba—.
      workOrdersApi.list({ onlyOpen: true, branchId: sucursal, pageSize: 6 }),
    ])

    dashboard.value = resumen
    caja.value = cierre
    recientes.value = ordenes.items

    await cargarPrimerosPasos(resumen)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el resumen del día.')
  } finally {
    loading.value = false
  }
}

/**
 * Solo se pregunta mientras el mes esté en cero y no haya nada por cobrar: un taller que ya
 * factura no paga ni una consulta de más. Y las tres van en cadena —sin clientes no puede
 * haber órdenes, sin órdenes no puede haber cobro—, así que un taller nuevo hace una sola.
 */
async function cargarPrimerosPasos(resumen: Dashboard) {
  if (resumen.revenueMonth !== 0 || resumen.receivables !== 0) {
    primerosPasos.value = null
    return
  }

  try {
    const clientes = await customersApi.list({ pageSize: 1 })
    if (clientes.total === 0) {
      primerosPasos.value = { cliente: false, vehiculo: false, cobro: false }
      return
    }

    // `onlyOpen: false` a propósito: una orden ya entregada también cuenta como vehículo
    // recibido, y con el filtro por defecto el paso se desmarcaría justo al cerrarla.
    const todas = await workOrdersApi.list({ onlyOpen: false, pageSize: 1 })
    if (todas.total === 0) {
      primerosPasos.value = { cliente: true, vehiculo: false, cobro: false }
      return
    }

    const ventas = await salesApi.list({ pageSize: 1 })
    primerosPasos.value =
      ventas.total > 0 ? null : { cliente: true, vehiculo: true, cobro: false }
  } catch {
    // Si algo de esto falla, no se muestra la guía: es una ayuda, no una pantalla que deba
    // interrumpir el día con un error.
    primerosPasos.value = null
  }
}

onMounted(async () => {
  await cargarTaller()
  await load()
})

// El selector de sucursal de la barra de arriba manda aquí: hasta hoy no lo consumía ninguna
// pantalla y era un control que no hacía nada.
watch(() => auth.activeBranchId, load)
</script>

<template>
  <section>
    <header class="cabecera">
      <div>
        <h1>{{ saludo }}</h1>
        <p class="muted small cabecera-pie">
          <span class="fecha">{{ hoy }}</span> · así va {{ auth.user?.tenantName }} hoy
        </p>
      </div>
      <div class="cabecera-acciones">
        <RouterLink class="boton fantasma" :to="{ name: 'cash-close' }">Ver la caja del día</RouterLink>
        <!-- La acción más frecuente del mostrador, y hasta hoy estaba a dos pantallas. -->
      </div>
    </header>

    <ErrorNote v-if="error" :message="error" />
    <SkeletonList v-else-if="loading" :rows="4" variant="lista" />

    <!--
      Arriba del dinero mientras el taller no ha facturado nada: es lo único accionable de la
      pantalla cuando todo lo demás está en cero. Cada línea lleva a donde se hace.
    -->
    <article v-if="primerosPasos" class="panel pasos">
      <header>
        <h2>Primeros pasos</h2>
        <span class="muted small num">{{ pasosHechos }} de 3</span>
      </header>
      <p class="muted small">Con esto ya puede trabajar el día completo desde el panel.</p>

      <ul>
        <li :class="{ hecho: primerosPasos.cliente }">
          <RouterLink v-if="!primerosPasos.cliente" :to="{ name: 'customers' }">
            Registre su primer cliente
          </RouterLink>
          <span v-else>Registre su primer cliente</span>
        </li>
        <li :class="{ hecho: primerosPasos.vehiculo }">
          <RouterLink v-if="!primerosPasos.vehiculo" :to="{ name: 'receive-vehicle' }">
            Reciba un {{ vocabulario.activo }}
          </RouterLink>
          <span v-else>Reciba un {{ vocabulario.activo }}</span>
        </li>
        <li :class="{ hecho: primerosPasos.cobro }">
          <RouterLink v-if="!primerosPasos.cobro" :to="{ name: 'work-orders' }">
            Cierre una orden y cóbrela
          </RouterLink>
          <span v-else>Cierre una orden y cóbrela</span>
        </li>
      </ul>
    </article>

    <template v-if="dashboard">
      <!-- ------------------------------------------------------------ las cuatro cifras -->
      <div class="tejas">
        <RouterLink class="teja" :to="{ name: 'cash-close' }">
          <span class="teja-icono cobro">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
              <rect x="2.5" y="6.5" width="19" height="12" rx="2" />
              <circle cx="12" cy="12.5" r="2.5" />
            </svg>
          </span>
          <span class="rotulo">Cobrado hoy</span>
          <strong class="cifra num">{{ caja ? formatMoney(caja.total) : '—' }}</strong>
          <span class="muted small">
            {{ caja ? `${caja.paymentCount} ${caja.paymentCount === 1 ? 'abono' : 'abonos'}` : 'sin datos de caja' }}
          </span>
        </RouterLink>

        <RouterLink class="teja" :to="{ name: 'reports' }">
          <span class="teja-icono factura">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
              <path d="M4 20V8m5 12V4m5 16v-7m5 7V10" stroke-linecap="round" />
            </svg>
          </span>
          <span class="rotulo">Facturado del mes</span>
          <strong class="cifra num">{{ formatMoney(dashboard.revenueMonth) }}</strong>
          <span class="muted small">
            hoy {{ formatMoney(dashboard.revenueToday) }} · margen
            {{ formatMoney(dashboard.marginMonth) }}
          </span>
        </RouterLink>

        <RouterLink class="teja" :to="{ name: 'work-orders' }">
          <span class="teja-icono taller">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
              <path d="M3 13.5 5 8h14l2 5.5V18h-3v-1.5H6V18H3z" stroke-linejoin="round" />
              <circle cx="7.5" cy="13.5" r="1.2" />
              <circle cx="16.5" cy="13.5" r="1.2" />
            </svg>
          </span>
          <span class="rotulo">En el taller</span>
          <strong class="cifra num">{{ dashboard.openWorkOrders }}</strong>
          <span class="muted small">{{ detallePorEstado || 'sin órdenes abiertas' }}</span>
        </RouterLink>

        <RouterLink class="teja" :class="{ alerta: dashboard.overdueReceivables > 0 }" :to="{ name: 'receivables' }">
          <span class="teja-icono cobrar">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
              <circle cx="12" cy="12" r="8.5" />
              <path d="M12 7.5v5l3 2" stroke-linecap="round" />
            </svg>
          </span>
          <span class="rotulo">Por cobrar</span>
          <strong class="cifra num">{{ formatMoney(dashboard.receivables) }}</strong>
          <span class="muted small">
            {{ dashboard.overdueReceivables > 0
              ? `${formatMoney(dashboard.overdueReceivables)} ya vencidos`
              : 'nada vencido' }}
          </span>
        </RouterLink>
      </div>

      <!-- ------------------------------------------------------------ gráfica y atención -->
      <div class="medio">
        <article class="panel">
          <header class="panel-cabecera">
            <h2>Pide atención</h2>
          </header>

          <p v-if="!atencion.length" class="al-dia">
            <span class="al-dia-marca">✓</span>
            Nada pendiente: ni órdenes atrasadas, ni cotizaciones sin contestar, ni saldos
            vencidos.
          </p>

          <RouterLink v-for="fila in atencion" :key="fila.clave" class="fila" :to="fila.to">
            <span class="fila-icono" :class="fila.tono">
              <svg v-if="fila.icono === 'reloj'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
                <circle cx="12" cy="12" r="8.5" /><path d="M12 7.5v5l3 2" stroke-linecap="round" />
              </svg>
              <svg v-else-if="fila.icono === 'bandeja'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
                <path d="M3.5 13.5h4l1.5 3h6l1.5-3h4M4 13.5 6.5 5h11L20 13.5V19H4z" stroke-linejoin="round" />
              </svg>
              <svg v-else-if="fila.icono === 'papel'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
                <path d="M6 3.5h8l4 4V20.5H6z" stroke-linejoin="round" /><path d="M9 11h6M9 15h4" stroke-linecap="round" />
              </svg>
              <svg v-else-if="fila.icono === 'calendario'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
                <rect x="3.5" y="5" width="17" height="15" rx="2" /><path d="M3.5 10h17M8 3.5v3M16 3.5v3" stroke-linecap="round" />
              </svg>
              <svg v-else-if="fila.icono === 'dinero'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
                <rect x="2.5" y="6.5" width="19" height="12" rx="2" /><circle cx="12" cy="12.5" r="2.5" />
              </svg>
              <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
                <path d="M4 8.5 12 5l8 3.5v7L12 19l-8-3.5z" stroke-linejoin="round" /><path d="M4 8.5 12 12l8-3.5M12 12v7" />
              </svg>
            </span>
            <span class="fila-texto">
              {{ fila.texto }}
              <!-- Qué hacer con eso: el aviso sin acción obliga a adivinar a dónde lleva. -->
              <span class="fila-accion">{{ fila.accion }}</span>
            </span>
            <svg class="fila-flecha" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
              <path d="m9 6 6 6-6 6" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
          </RouterLink>
        </article>
              <article class="panel">
          <header class="panel-cabecera">
            <div>
              <h2>Ingresos</h2>
              <p class="muted small">Lo facturado en los últimos {{ grafica?.dias ?? 14 }} días</p>
            </div>
            <RouterLink class="ver" :to="{ name: 'reports' }">Ver reportes</RouterLink>
          </header>

          <div v-if="grafica" class="grafica">
            <div class="grafica-cifras">
              <div>
                <span class="muted small">Total del periodo</span>
                <strong class="num">{{ formatMoney(grafica.total) }}</strong>
              </div>
              <div>
                <span class="muted small">Días con ingresos</span>
                <strong class="num">{{ grafica.conVenta }}</strong>
              </div>
            </div>

            <div class="barras">
              <div
                v-for="(barra, i) in grafica.barras"
                :key="i"
                class="columna"
                :title="`${barra.etiqueta}: ${formatMoney(barra.total)}`"
              >
                <div class="barra" :style="{ height: `${barra.alto}%` }" />
              </div>
            </div>

            <div class="grafica-pie muted small">
              <span>{{ dashboard.lastDays[0]?.periodLabel }}</span>
              <span>{{ dashboard.lastDays[dashboard.lastDays.length - 1]?.periodLabel }}</span>
            </div>
          </div>
          <p v-else class="muted small">Todavía no hay movimiento que graficar.</p>
        </article>

</div>

      <!-- ------------------------------------------------------------ patio y entradas -->
      <div class="abajo">
        <article class="panel">
          <header class="panel-cabecera">
            <div>
              <h2>El patio</h2>
              <p class="muted small">
                {{ dashboard.openWorkOrders }}
                {{ dashboard.openWorkOrders === 1 ? 'orden abierta' : 'órdenes abiertas' }}
              </p>
            </div>
            <RouterLink class="ver" :to="{ name: 'work-orders' }">Ver el tablero</RouterLink>
          </header>

          <div v-if="barra.length" class="barra" aria-hidden="true">
            <span
              v-for="tramo in barra"
              :key="tramo.titulo"
              :class="tramo.tono"
              :style="{ width: `${tramo.parte}%` }"
            />
          </div>

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
        </article>

        <article class="panel">
          <header class="panel-cabecera">
            <h2>Últimas que entraron</h2>
            <RouterLink class="ver" :to="{ name: 'work-orders' }">Ver todas</RouterLink>
          </header>

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
.fila-accion {
  display: block;
  margin-top: 0.125rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

/* Una barra por día, con su hueco cuando no se facturó. */
.barras {
  display: flex;
  align-items: flex-end;
  gap: 3px;
  height: 7.5rem;
  margin: var(--space-3) 0 var(--space-2);
}

.columna {
  flex: 1;
  height: 100%;
  display: flex;
  align-items: flex-end;
}

.barra {
  width: 100%;
  min-height: 2px;
  border-radius: 2px 2px 0 0;
  background: var(--accent);
}

/* ------------------------------------------------------------------ cabecera */

.cabecera {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
  margin-bottom: 1.5rem;
}

.cabecera h1 {
  margin: 0;
  font-size: 1.625rem;
}

.cabecera-pie {
  margin: 0.25rem 0 0;
}

/* `es-HN` devuelve el día en minúscula —«jueves, 15 de octubre»— y arriba de todo se lee
   mejor con mayúscula. El nombre del taller se queda como lo escribió su dueño. */
.fecha::first-letter {
  text-transform: uppercase;
}

.cabecera-acciones {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.boton {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.55rem 0.9rem;
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  background: var(--accent);
  color: var(--on-accent);
  font-weight: 600;
}

.boton:hover {
  text-decoration: none;
  background: var(--brand-deep);
}

.boton svg {
  width: 1rem;
  height: 1rem;
}

.boton.fantasma {
  background: var(--surface);
  border-color: var(--border);
  color: var(--text);
  font-weight: 500;
}

.boton.fantasma:hover {
  background: var(--surface-alt);
  border-color: var(--brand);
}

/* ------------------------------------------------------------------ paneles */

.panel {
  padding: 1.125rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.panel-cabecera {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.panel-cabecera h2 {
  margin: 0;
  font-size: 1.0625rem;
}

.panel-cabecera p {
  margin: 0.125rem 0 0;
}

.ver {
  font-size: 0.875rem;
  white-space: nowrap;
}

/* ------------------------------------------------------------------ primeros pasos */

.pasos {
  margin-bottom: 1rem;
}

.pasos header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
}

.pasos h2 {
  margin: 0;
  font-size: 1rem;
}

.pasos ul {
  margin: 0.5rem 0 0;
  padding: 0;
  list-style: none;
}

.pasos li {
  padding: 0.35rem 0;
  border-top: 1px solid var(--border);
}

.pasos li::before {
  content: '○';
  margin-right: 0.5rem;
  color: var(--text-muted);
}

/* Lo ya hecho no se enlaza: llevar a «registre su primer cliente» a quien tiene treinta
   confunde. Queda como constancia de que ese paso está dado. */
.pasos li.hecho::before {
  content: '●';
  color: var(--brand);
}

.pasos li.hecho span {
  color: var(--text-muted);
  text-decoration: line-through;
}

/* ------------------------------------------------------------------ tejas */

.tejas {
  display: grid;
  gap: 0.875rem;
  grid-template-columns: repeat(auto-fit, minmax(13rem, 1fr));
  margin-bottom: 0.875rem;
}

.teja {
  display: grid;
  gap: 0.125rem;
  padding: 1.125rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  color: var(--text);
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.teja:hover {
  text-decoration: none;
  border-color: color-mix(in srgb, var(--brand) 45%, var(--border));
  box-shadow: 0 10px 24px -20px var(--brand);
}

.teja-icono {
  display: grid;
  place-items: center;
  width: 2.25rem;
  height: 2.25rem;
  margin-bottom: 0.625rem;
  border-radius: var(--radius-sm);
  background: color-mix(in srgb, var(--brand) 12%, transparent);
  color: var(--brand);
}

.teja-icono svg {
  width: 1.25rem;
  height: 1.25rem;
}

.teja-icono.cobro {
  background: color-mix(in srgb, var(--success) 14%, transparent);
  color: var(--success-text);
}

.teja-icono.cobrar {
  background: color-mix(in srgb, var(--warning) 16%, transparent);
  color: var(--warning-text);
}

.teja .rotulo {
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.cifra {
  font-size: 1.5rem;
  line-height: 1.2;
  font-family: var(--font-mono);
}

.teja.alerta .cifra {
  color: var(--danger);
}

/* ------------------------------------------------------------------ medio */

.medio,
.abajo {
  display: grid;
  gap: 0.875rem;
  margin-bottom: 0.875rem;
}

@media (min-width: 62rem) {
  .medio {
    grid-template-columns: 1.6fr 1fr;
  }

  .abajo {
    grid-template-columns: 1fr 1.4fr;
  }
}

.grafica-cifras {
  display: flex;
  gap: 2.5rem;
  margin-bottom: 0.75rem;
}

.grafica-cifras span {
  display: block;
}

.grafica-cifras strong {
  font-size: 1.25rem;
}

.trazo {
  display: block;
  width: 100%;
  height: 8.5rem;
}

.grafica-pie {
  display: flex;
  justify-content: space-between;
  margin-top: 0.375rem;
}

/* ------------------------------------------------------------------ pide atención */

.fila {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.7rem 0;
  border-top: 1px solid var(--border);
  color: var(--text);
}

.fila:first-of-type {
  border-top: 0;
}

.fila:hover {
  text-decoration: none;
}

.fila:hover .fila-texto {
  color: var(--brand);
}

.fila-icono {
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2rem;
  flex: none;
  border-radius: 50%;
  background: var(--surface-alt);
  color: var(--text-muted);
}

.fila-icono svg {
  width: 1.125rem;
  height: 1.125rem;
}

.fila-icono.alerta {
  background: color-mix(in srgb, var(--danger) 12%, transparent);
  color: var(--danger);
}

.fila-icono.espera {
  background: color-mix(in srgb, var(--warning) 18%, transparent);
  color: var(--warning-text);
}

.fila-texto {
  flex: 1;
  line-height: 1.4;
}

.fila-flecha {
  width: 1rem;
  height: 1rem;
  flex: none;
  color: var(--text-muted);
}

.al-dia {
  display: flex;
  gap: 0.6rem;
  margin: 0;
  color: var(--text-muted);
  line-height: 1.5;
}

.al-dia-marca {
  color: var(--success-text);
  font-weight: 700;
}

/* ------------------------------------------------------------------ patio */

.barra {
  display: flex;
  gap: 2px;
  height: 0.5rem;
  margin-bottom: 1rem;
  border-radius: 999px;
  overflow: hidden;
}

.barra span {
  display: block;
  background: var(--brand);
}

.barra .detenido {
  background: var(--warning);
}

.barra .listo {
  background: var(--success);
}

.carril {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.6rem 0;
  border-top: 1px solid var(--border);
  color: var(--text);
}

.carril:hover {
  text-decoration: none;
  color: var(--brand);
}

.punto {
  width: 0.625rem;
  height: 0.625rem;
  border-radius: 50%;
  background: var(--brand);
}

.punto.detenido {
  background: var(--warning);
}

.punto.listo {
  background: var(--success);
}

.nombre {
  flex: 1;
}

/* ------------------------------------------------------------------ últimas órdenes */

.orden {
  display: grid;
  grid-template-columns: auto 1fr auto;
  align-items: center;
  gap: 0.25rem 0.75rem;
  padding: 0.6rem 0;
  border-top: 1px solid var(--border);
  color: var(--text);
}

.orden:hover {
  text-decoration: none;
  background: var(--surface-alt);
}

.folio {
  font-size: 0.875rem;
  color: var(--text-muted);
}

.vehiculo {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  min-width: 0;
}

.placa {
  padding: 0.05rem 0.35rem;
  border: 1px solid var(--border);
  border-radius: 4px;
  font-size: 0.75rem;
}

.cuando {
  grid-column: 2 / -1;
}

@media (min-width: 48rem) {
  .orden {
    grid-template-columns: auto 1fr auto auto;
  }

  .cuando {
    grid-column: auto;
  }
}
</style>

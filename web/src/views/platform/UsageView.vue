<script setup lang="ts">
/**
 * Cómo está usando GarajApp cada taller.
 *
 * No es una pantalla de cobro —esa es Talleres— sino de acompañamiento: contesta tres cosas
 * antes de llamar a nadie. ¿Entran? ¿Trabajan adentro? ¿Qué partes no tocan?
 *
 * Un taller que factura pero no cotiza no está en problemas: está usando la mitad, y ahí hay
 * una capacitación. Un taller que no abre una orden en dos semanas sí, aunque esté al día.
 */
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { platformApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import { PLAN_LABEL, type PlatformUsage } from '@/types/domain'
import { formatDate, formatMoney } from '@/utils/format'

const filas = ref<PlatformUsage[]>([])
const loading = ref(false)
const error = ref('')

/** La ventana que se mira. Treinta días es el mes de trabajo del taller. */
const dias = ref(30)

const VENTANAS = [
  { dias: 7, label: '7 días' },
  { dias: 30, label: '30 días' },
  { dias: 90, label: '90 días' },
]

/** Los talleres de prueba no son clientes: ensucian la lista de a quién llamar. */
const sinPruebas = ref(true)

const visibles = computed(() =>
  sinPruebas.value ? filas.value.filter((f) => !/^prueba |^taller demo$/i.test(f.name)) : filas.value,
)

const activos = computed(() => visibles.value.filter((f) => f.isActive))

/**
 * Cuántos de los talleres activos abrieron al menos una orden en la ventana. Es el único
 * número que de verdad dice si el producto se está usando.
 */
const trabajando = computed(() => activos.value.filter((f) => f.workOrders > 0).length)

/** Hace cuántos días fue, en texto corto. Null cuando nunca pasó. */
function hace(cuando: string | null) {
  if (!cuando) return 'nunca'

  const dias = Math.floor((Date.now() - new Date(cuando).getTime()) / 86_400_000)

  if (dias <= 0) return 'hoy'
  if (dias === 1) return 'ayer'
  if (dias < 30) return `hace ${dias} días`

  return formatDate(cuando)
}

/**
 * Qué tan callado está. Se mira por la última orden y no por el último ingreso: entrar a ver
 * es gratis, abrir una orden es trabajar.
 */
function temperatura(f: PlatformUsage) {
  if (!f.isActive) return 'baja'
  if (f.workOrders === 0) return 'frio'
  if (f.workOrders < 5) return 'tibio'
  return 'vivo'
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    filas.value = await platformApi.usage(dias.value)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo leer el uso de los talleres.')
  } finally {
    loading.value = false
  }
}

function cambiarVentana(d: number) {
  dias.value = d
  return load()
}

onMounted(load)
</script>

<template>
  <section>
    <header class="cabecera">
      <div>
        <h1>Uso</h1>
        <p class="muted small">
          Qué tanto usan GarajApp los talleres. Ordenado por el que menos se mueve: esa es la
          lista de a quién llamar.
        </p>
      </div>

      <div class="filtros">
        <button
          v-for="v in VENTANAS"
          :key="v.dias"
          type="button"
          class="pestana"
          :class="{ activa: dias === v.dias }"
          @click="cambiarVentana(v.dias)"
        >
          {{ v.label }}
        </button>
        <label class="check small">
          <input v-model="sinPruebas" type="checkbox" />
          Sin los de prueba
        </label>
      </div>
    </header>

    <ErrorNote v-if="error" :message="error" />

    <SkeletonList v-if="loading" :rows="6" variant="lista" />

    <template v-else>
      <p class="resumen">
        <strong>{{ trabajando }}</strong> de {{ activos.length }} talleres activos abrieron
        órdenes en los últimos {{ dias }} días.
      </p>

      <div class="tabla">
        <table>
          <thead>
            <tr>
              <th>Taller</th>
              <th class="num">Entraron</th>
              <th class="num">Órdenes</th>
              <th class="num">Última orden</th>
              <th class="num">Facturas</th>
              <th class="num">Facturado</th>
              <th class="num">Cotiza</th>
              <th class="num">Fotos</th>
              <th class="num">Escáner</th>
              <th class="num">Asistente</th>
              <th class="num">Citas</th>
              <th class="num">Motos</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="f in visibles" :key="f.id" :class="temperatura(f)">
              <td>
                <RouterLink :to="{ name: 'platform-tenant', params: { id: f.id } }">
                  {{ f.name }}
                </RouterLink>
                <div class="muted small">
                  {{ PLAN_LABEL[f.plan] }} ·
                  {{ f.activeUsers }} usuarios, {{ f.technicianCount }} técnicos
                  <template v-if="f.inactiveUsers">
                    · <span class="baja-usuarios">{{ f.inactiveUsers }} de baja</span>
                  </template>
                  <template v-if="!f.isActive"> · bloqueado</template>
                </div>
              </td>
              <td class="num">
                {{ f.usersSignedIn }}
                <div class="muted small">{{ hace(f.lastLoginAt) }}</div>
              </td>
              <td class="num">
                <strong>{{ f.workOrders }}</strong>
                <div class="muted small">{{ f.workOrdersEver }} en total</div>
              </td>
              <td class="num muted small">{{ hace(f.lastWorkOrderAt) }}</td>
              <td class="num">{{ f.sales }}</td>
              <td class="num">{{ formatMoney(f.billed) }}</td>
              <td class="num">{{ f.quotes }}</td>
              <td class="num">{{ f.photos }}</td>
              <td class="num">{{ f.scans }}</td>
              <td class="num">{{ f.assistantCalls }}</td>
              <td class="num">{{ f.appointments }}</td>
              <td class="num">{{ f.motorcycles }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <p v-if="!visibles.length" class="muted">No hay talleres que mirar.</p>

      <p class="muted small pie">
        «Entraron» cuenta usuarios que iniciaron sesión en la ventana. Las facturas anuladas no
        cuentan. Los talleres bloqueados salen igual, marcados.
      </p>
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

h1 {
  margin: 0;
  font-size: 1.625rem;
}

.cabecera p {
  margin: 0.25rem 0 0;
  max-width: 44rem;
}

.filtros {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  flex-wrap: wrap;
}

.pestana {
  padding: 0.35rem 0.7rem;
  border: 1px solid transparent;
  border-radius: 999px;
  background: none;
  color: var(--text-muted);
  font-size: 0.875rem;
  cursor: pointer;
}

.pestana.activa {
  border-color: color-mix(in srgb, var(--brand) 40%, var(--border));
  background: color-mix(in srgb, var(--brand) 10%, transparent);
  color: var(--brand);
  font-weight: 600;
}

.check {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  color: var(--text-muted);
}

.resumen {
  margin: 0 0 0.75rem;
}

/* La tabla es ancha a propósito: son doce columnas y se lee de lado. */
.tabla {
  overflow-x: auto;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

table {
  width: 100%;
  border-collapse: collapse;
  white-space: nowrap;
}

th,
td {
  padding: 0.55rem 0.75rem;
  border-top: 1px solid var(--border);
  text-align: left;
  vertical-align: top;
}

thead th {
  border-top: 0;
  color: var(--text-muted);
  font-size: var(--text-xs);
  font-weight: 600;
}

.num {
  text-align: right;
}

tbody tr:hover {
  background: var(--surface-alt);
}

/* Una franja al borde dice de un vistazo quién se apagó. */
tbody tr.frio {
  box-shadow: inset 3px 0 0 var(--danger);
}

tbody tr.tibio {
  box-shadow: inset 3px 0 0 var(--warning);
}

tbody tr.vivo {
  box-shadow: inset 3px 0 0 var(--success);
}

tbody tr.baja {
  opacity: 0.55;
}

.baja-usuarios {
  color: var(--warning-text);
}

.pie {
  margin-top: 0.75rem;
}
</style>

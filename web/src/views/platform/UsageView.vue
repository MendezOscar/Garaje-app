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
import type { PlatformUsage } from '@/types/domain'
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

/**
 * Los talleres de prueba no son clientes: ensucian la lista de a quién llamar. Son los que
 * dejan las pruebas del servidor («Prueba 3F2A…») y los de demostración —«Taller Demo» y
 * «Taller de equipos demo»—, que se reconocen por llevar «demo» en el nombre.
 */
const sinPruebas = ref(true)

const DE_PRUEBA = /^prueba |\bdemo\b/i

const visibles = computed(() =>
  sinPruebas.value ? filas.value.filter((f) => !DE_PRUEBA.test(f.name)) : filas.value,
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

/** Las cifras de arriba: cómo va el uso en conjunto, antes de mirar fila por fila. */
const cifras = computed(() => ({
  trabajando: trabajando.value,
  activos: activos.value.length,
  ordenes: visibles.value.reduce((suma, f) => suma + f.workOrders, 0),
  facturado: visibles.value.reduce((suma, f) => suma + f.billed, 0),
  callados: activos.value.filter((f) => f.workOrders === 0).length,
}))

/**
 * En qué anda el taller, dicho en dos palabras y con el porqué debajo. Es lo que decide si
 * hay que llamarlo, y leerlo de doce columnas de números no lo decidía.
 */
function actividad(f: PlatformUsage) {
  if (!f.isActive)
    return { tono: 'cortado', texto: 'Sin uso', razon: 'bloqueado por pago' }

  if (f.workOrders === 0)
    return {
      tono: 'cortado',
      texto: 'Sin uso',
      razon: f.workOrdersEver ? 'trabajó antes, ahora nada' : 'nunca abrió una orden',
    }

  if (f.sales === 0)
    return { tono: 'aviso', texto: 'Poco uso', razon: 'abre órdenes, no factura' }

  if (f.workOrders < 5)
    return { tono: 'aviso', texto: 'Bajando', razon: `solo ${f.workOrders} órdenes en ${dias.value} días` }

  return { tono: 'ok', texto: 'Activo', razon: '' }
}

/** Quién entra y cuándo fue la última vez, dicho como se pregunta. */
function quienEntra(f: PlatformUsage) {
  if (!f.usersSignedIn)
    return `Nadie entra desde ${hace(f.lastLoginAt)} · ${f.activeUsers} ${f.activeUsers === 1 ? 'usuario' : 'usuarios'}`

  return `${f.usersSignedIn} ${f.usersSignedIn === 1 ? 'entró' : 'entraron'} · último ${hace(f.lastLoginAt)}`
}

/**
 * Solo los módulos que el taller usó. Una columna por módulo dejaba una pared de ceros: lo
 * que dice algo es lo que sí tocó.
 */
function queUsa(f: PlatformUsage) {
  return (
    [
      { n: f.quotes, que: f.quotes === 1 ? 'cotización' : 'cotizaciones' },
      { n: f.photos, que: 'fotos' },
      { n: f.scans, que: f.scans === 1 ? 'escaneo' : 'escaneos' },
      { n: f.assistantCalls, que: f.assistantCalls === 1 ? 'consulta al asistente' : 'consultas al asistente' },
      { n: f.appointments, que: f.appointments === 1 ? 'cita' : 'citas' },
      { n: f.motorcycles, que: f.motorcycles === 1 ? 'moto armada' : 'motos armadas' },
    ] as const
  ).filter((x) => x.n > 0)
}

/** Los módulos que nadie tocó en la ventana: se dicen al pie, no con columnas vacías. */
const sinUsar = computed(() => {
  const total = (campo: (f: PlatformUsage) => number) =>
    visibles.value.reduce((suma, f) => suma + campo(f), 0)

  return [
    { n: total((f) => f.scans), nombre: 'Escáner' },
    { n: total((f) => f.assistantCalls), nombre: 'Asistente' },
    { n: total((f) => f.appointments), nombre: 'Citas' },
    { n: total((f) => f.motorcycles), nombre: 'Armado de motos' },
    { n: total((f) => f.quotes), nombre: 'Cotizaciones' },
  ]
    .filter((x) => x.n === 0)
    .map((x) => x.nombre)
})

/** Por qué columna se ordena. Por omisión, el que menos se mueve primero. */
const orden = ref<'ordenes' | 'nombre' | 'facturado' | 'ingreso'>('ordenes')
const descendente = ref(false)

function ordenarPor(columna: typeof orden.value) {
  if (orden.value === columna) descendente.value = !descendente.value
  else {
    orden.value = columna
    descendente.value = false
  }
}

const ordenadas = computed(() => {
  const filas = [...visibles.value].sort((a, b) => {
    switch (orden.value) {
      case 'nombre':
        return a.name.localeCompare(b.name, 'es')
      case 'facturado':
        return a.billed - b.billed
      case 'ingreso':
        return (a.lastLoginAt ?? '').localeCompare(b.lastLoginAt ?? '')
      default:
        return a.workOrders - b.workOrders
    }
  })

  return descendente.value ? filas.reverse() : filas
})

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
          Quién está usando GarajApp. Arriba, los que menos se mueven: a esos conviene llamar.
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
          Ocultar talleres de prueba
        </label>
      </div>
    </header>

    <ErrorNote v-if="error" :message="error" />

    <SkeletonList v-if="loading" :rows="6" variant="lista" />

    <template v-else>
      <div class="cifras">
        <article class="cifra">
          <span class="rotulo">Talleres que abrieron órdenes</span>
          <strong class="num">
            {{ cifras.trabajando }} <span class="de">de {{ cifras.activos }}</span>
          </strong>
        </article>
        <article class="cifra">
          <span class="rotulo">Órdenes en {{ dias }} días</span>
          <strong class="num">{{ cifras.ordenes }}</strong>
        </article>
        <article class="cifra">
          <span class="rotulo">Facturado por los talleres</span>
          <strong class="num">{{ formatMoney(cifras.facturado) }}</strong>
        </article>
        <article class="cifra" :class="{ alerta: cifras.callados > 0 }">
          <span class="rotulo">Sin actividad</span>
          <strong class="num">{{ cifras.callados }}</strong>
        </article>
      </div>

      <div class="tabla">
        <table>
          <thead>
            <tr>
              <th>
                <button type="button" class="ordenar" @click="ordenarPor('nombre')">
                  Taller<span v-if="orden === 'nombre'">{{ descendente ? ' ↓' : ' ↑' }}</span>
                </button>
              </th>
              <th>Actividad</th>
              <th class="num">Última orden</th>
              <th class="num">
                <button type="button" class="ordenar" @click="ordenarPor('ordenes')">
                  Órdenes<span v-if="orden === 'ordenes'">{{ descendente ? ' ↓' : ' ↑' }}</span>
                </button>
              </th>
              <th class="num">
                <button type="button" class="ordenar" @click="ordenarPor('facturado')">
                  Facturado<span v-if="orden === 'facturado'">{{ descendente ? ' ↓' : ' ↑' }}</span>
                </button>
              </th>
              <th>Qué usa</th>
              <th><span class="sr-only">Acciones</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="f in ordenadas" :key="f.id" :class="temperatura(f)">
              <td>
                <RouterLink :to="{ name: 'platform-tenant', params: { id: f.id } }">
                  {{ f.name }}
                </RouterLink>
                <div class="muted small">
                  {{ quienEntra(f) }}
                  <template v-if="f.inactiveUsers">
                    · <span class="baja-usuarios">{{ f.inactiveUsers }} de baja</span>
                  </template>
                </div>
              </td>
              <td>
                <span class="chip" :class="actividad(f).tono">{{ actividad(f).texto }}</span>
                <div v-if="actividad(f).razon" class="muted small">{{ actividad(f).razon }}</div>
              </td>
              <td class="num">
                {{ hace(f.lastWorkOrderAt) }}
              </td>
              <td class="num">
                <strong>{{ f.workOrders }}</strong>
                <div class="muted small">
                  {{ f.sales ? `${f.sales} ${f.sales === 1 ? 'factura' : 'facturas'}` : `${f.workOrdersEver} en total` }}
                </div>
              </td>
              <td class="num" :class="{ muted: !f.billed }">{{ formatMoney(f.billed) }}</td>
              <td>
                <div v-if="queUsa(f).length" class="fichas">
                  <span v-for="u in queUsa(f)" :key="u.que" class="ficha">
                    <b>{{ u.n }}</b> {{ u.que }}
                  </span>
                </div>
                <span v-else class="muted small">Nada este período</span>
              </td>
              <!-- La pantalla termina en una llamada: el número, a un toque. -->
              <td class="acciones-fila">
                <a v-if="f.phone && actividad(f).tono !== 'ok'" class="btn-chico" :href="`tel:${f.phone}`">
                  Llamar
                </a>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <p v-if="!visibles.length" class="muted">No hay talleres que mirar.</p>

      <p class="muted small pie">
        Solo se muestran los módulos que el taller usó en el período.
        <template v-if="sinUsar.length">
          {{ sinUsar.join(', ') }} no {{ sinUsar.length === 1 ? 'tuvo' : 'tuvieron' }} uso en
          ningún taller.
        </template>
        Las facturas anuladas no cuentan, y los talleres bloqueados salen igual, marcados.
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

.ordenar {
  padding: 0;
  border: 0;
  background: none;
  color: inherit;
  font: inherit;
  cursor: pointer;
}

.ordenar:hover {
  color: var(--brand);
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

/* Las cifras de arriba: cómo va el uso en conjunto. */
.cifras {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.cifra {
  display: grid;
  gap: 0.2rem;
  flex: 1 1 12rem;
  padding: 0.75rem 0.875rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.cifra .rotulo {
  color: var(--text-muted);
  font-size: var(--text-xs);
}

.cifra strong {
  font-size: 1.5rem;
}

.cifra .de {
  color: var(--text-muted);
  font-size: 1rem;
  font-weight: 500;
}

.cifra.alerta {
  border-color: color-mix(in srgb, var(--danger) 45%, var(--border));
}

.cifra.alerta strong {
  color: var(--danger);
}

/* En qué anda el taller, en dos palabras. */
.chip {
  display: inline-flex;
  align-items: center;
  padding: 0.15rem 0.55rem;
  border-radius: 999px;
  font-size: var(--text-xs);
  font-weight: 600;
  white-space: nowrap;
}

.chip.ok {
  background: color-mix(in srgb, var(--success) 14%, transparent);
  color: var(--success);
}

.chip.aviso {
  background: color-mix(in srgb, var(--warning) 16%, transparent);
  color: var(--warning);
}

.chip.cortado {
  background: color-mix(in srgb, var(--danger) 14%, transparent);
  color: var(--danger);
}

/* Lo que sí usó, en fichas: una columna por módulo era una pared de ceros. */
.fichas {
  display: flex;
  flex-wrap: wrap;
  gap: 0.3rem;
}

.ficha {
  padding: 0.15rem 0.45rem;
  border-radius: var(--radius-sm, 6px);
  background: color-mix(in srgb, var(--text) 7%, transparent);
  color: var(--text-muted);
  font-size: var(--text-xs);
  white-space: nowrap;
}

.ficha b {
  color: var(--text);
}

.acciones-fila {
  text-align: right;
}

.btn-chico {
  display: inline-flex;
  align-items: center;
  padding: 0.3rem 0.7rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 6px);
  background: var(--surface);
  color: var(--text);
  font-size: var(--text-xs);
  font-weight: 600;
  text-decoration: none;
  white-space: nowrap;
}

.btn-chico:hover {
  border-color: color-mix(in srgb, var(--brand) 45%, var(--border));
  color: var(--brand);
}
</style>

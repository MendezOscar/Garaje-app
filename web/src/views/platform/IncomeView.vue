<script setup lang="ts">
/**
 * Lo que entra por los tres servicios, mes a mes.
 *
 * No es el libro de ventas y no se le parece. El libro es la cuenta de los números
 * autorizados y lo pide el SAR; esto es el control propio, así que entran también los
 * comprobantes —son venta igual, aunque no amparen nada— y se suma el **neto**: el ISV se
 * cobra y se entrega, contarlo como ingreso es contar plata que no es nuestra.
 */
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { platformApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import type { Income } from '@/types/domain'
import { formatMoney } from '@/utils/format'

const datos = ref<Income | null>(null)
const loading = ref(false)
const error = ref('')

const anio = ref(new Date().getFullYear())

/** Los últimos cinco años, que es lo que de verdad se consulta. */
const anios = computed(() => {
  const hoy = new Date().getFullYear()
  return Array.from({ length: 5 }, (_, i) => hoy - i)
})

const MESES = [
  'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
  'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
]

/** Los servicios que facturaron algo este año, en el orden en que más entró. */
const servicios = computed(() => {
  const por = new Map<string, number>()

  for (const r of datos.value?.rows ?? []) {
    por.set(r.productName, (por.get(r.productName) ?? 0) + r.net)
  }

  return [...por.entries()].sort((a, b) => b[1] - a[1]).map(([nombre]) => nombre)
})

/** El neto de un mes y un servicio. Vacío cuando no hubo nada: un cero por mes es ruido. */
function neto(mes: number, servicio: string): number {
  return (datos.value?.rows ?? [])
    .filter((r) => r.month === mes && r.productName === servicio)
    .reduce((suma, r) => suma + r.net, 0)
}

function totalDelMes(mes: number): number {
  return (datos.value?.rows ?? [])
    .filter((r) => r.month === mes)
    .reduce((suma, r) => suma + r.net, 0)
}

function totalDelServicio(servicio: string): number {
  return (datos.value?.rows ?? [])
    .filter((r) => r.productName === servicio)
    .reduce((suma, r) => suma + r.net, 0)
}

/** Los meses que tuvieron algo. Enero a hoy sin nada no dice nada. */
const meses = computed(() =>
  [...Array(12).keys()].map((i) => i + 1).filter((m) => totalDelMes(m) !== 0),
)

const cifras = computed(() => {
  const filas = datos.value?.rows ?? []
  const suma = (lista: typeof filas, campo: 'net' | 'tax') =>
    lista.reduce((total, r) => total + r[campo], 0)

  return {
    neto: suma(filas, 'net'),
    isv: suma(filas, 'tax'),
    conCai: suma(filas.filter((r) => r.fiscal), 'net'),
    comprobante: suma(filas.filter((r) => !r.fiscal), 'net'),
    documentos: filas.reduce((total, r) => total + r.count, 0),
  }
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    datos.value = await platformApi.income(anio.value)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar los ingresos.')
  } finally {
    loading.value = false
  }
}

function cambiarAnio(valor: string) {
  anio.value = Number(valor)
  return load()
}

onMounted(load)
</script>

<template>
  <section>
    <header class="cabecera">
      <div>
        <h1>Ingresos</h1>
        <p class="muted small">
          Lo que entra por los tres servicios, mes a mes. Es el control propio, no el libro de
          ventas: acá entran también los comprobantes, y se suma sin el ISV.
        </p>
      </div>

      <label>
        <span class="sr-only">Año</span>
        <select :value="anio" @change="cambiarAnio(($event.target as HTMLSelectElement).value)">
          <option v-for="a in anios" :key="a" :value="a">{{ a }}</option>
        </select>
      </label>
    </header>

    <ErrorNote v-if="error" :message="error" />

    <SkeletonList v-if="loading" :rows="6" variant="tabla" />

    <template v-else-if="datos">
      <div class="cifras">
        <article class="cifra fuerte">
          <span class="rotulo">Ingresos de {{ anio }}</span>
          <strong class="num">{{ formatMoney(cifras.neto) }}</strong>
          <span class="muted small">sin ISV · {{ cifras.documentos }} documentos</span>
        </article>

        <article class="cifra">
          <span class="rotulo">ISV cobrado</span>
          <strong class="num">{{ formatMoney(cifras.isv) }}</strong>
          <!-- No es ingreso: se cobra y se entrega. Se enseña porque hay que declararlo. -->
          <span class="muted small">no es ingreso: se declara</span>
        </article>

        <article class="cifra">
          <span class="rotulo">Con factura</span>
          <strong class="num">{{ formatMoney(cifras.conCai) }}</strong>
          <span class="muted small">lo que va al libro de ventas</span>
        </article>

        <article class="cifra">
          <span class="rotulo">Con comprobante</span>
          <strong class="num">{{ formatMoney(cifras.comprobante) }}</strong>
          <span class="muted small">sin valor fiscal</span>
        </article>
      </div>

      <p v-if="!meses.length" class="muted">En {{ anio }} todavía no entró nada.</p>

      <div v-else class="tabla">
        <table>
          <thead>
            <tr>
              <th>Mes</th>
              <th v-for="s in servicios" :key="s" class="num">{{ s }}</th>
              <th class="num">Total</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="m in meses" :key="m">
              <td>{{ MESES[m - 1] }}</td>
              <td v-for="s in servicios" :key="s" class="num" :class="{ vacio: !neto(m, s) }">
                {{ neto(m, s) ? formatMoney(neto(m, s)) : '—' }}
              </td>
              <td class="num fuerte">{{ formatMoney(totalDelMes(m)) }}</td>
            </tr>
          </tbody>
          <tfoot>
            <tr>
              <td>Total</td>
              <td v-for="s in servicios" :key="s" class="num">
                {{ formatMoney(totalDelServicio(s)) }}
              </td>
              <td class="num">{{ formatMoney(cifras.neto) }}</td>
            </tr>
          </tfoot>
        </table>
      </div>

      <p class="muted small pie">
        Los documentos anulados no cuentan: el número existió, la venta no. Lo que se suma es
        el neto, porque el ISV se cobra y se entrega. Para lo que pide el SAR, mire el
        <RouterLink :to="{ name: 'platform-sales-book' }">libro de ventas</RouterLink>.
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

.cifras {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-bottom: 1.25rem;
}

.cifra {
  display: grid;
  gap: 0.15rem;
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
  font-size: 1.25rem;
}

.cifra.fuerte {
  border-color: color-mix(in srgb, var(--brand) 40%, var(--border));
}

.cifra.fuerte strong {
  font-size: 1.625rem;
}

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
  padding: 0.5rem 0.75rem;
  border-top: 1px solid var(--border);
  text-align: left;
}

thead th {
  border-top: 0;
  color: var(--text-muted);
  font-size: var(--text-xs);
  font-weight: 600;
}

tfoot td {
  border-top: 2px solid var(--border);
  font-weight: 700;
}

.num {
  text-align: right;
  font-variant-numeric: tabular-nums;
}

td.fuerte {
  font-weight: 600;
}

/* Un mes sin nada de ese servicio: una raya, no un cero. */
td.vacio {
  color: var(--text-muted);
}

.pie {
  margin-top: 0.875rem;
}
</style>

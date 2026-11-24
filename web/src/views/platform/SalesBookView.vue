<script setup lang="ts">
/**
 * El libro de ventas: todos los documentos que emitimos en el mes, de los tres servicios, con
 * sus totales.
 *
 * No es un reporte de ingresos —eso es otra cosa y se mira por producto— sino **la cuenta de
 * los números autorizados**: lo que se le entrega al contador y lo que respalda ante el SAR.
 * Por eso se ordena por número y no por fecha, los anulados salen tachados en vez de
 * desaparecer, y los números que se tomaron y nunca llegaron a documento van dichos aparte.
 */
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { platformApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import type { PlatformProduct, SalesBook } from '@/types/domain'
import { formatDay, formatMoney } from '@/utils/format'

const libro = ref<SalesBook | null>(null)
const servicios = ref<PlatformProduct[]>([])
const loading = ref(false)
const bajando = ref(false)
const error = ref('')

const hoy = new Date()
const anio = ref(hoy.getFullYear())
const mes = ref(hoy.getMonth() + 1)

/** Todos los servicios, o uno. El libro del SAR es de todos: el filtro es para revisar. */
const servicio = ref('')

const MESES = [
  'enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio',
  'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre',
]

const titulo = computed(() => `${MESES[mes.value - 1]} de ${anio.value}`)

/** Los últimos doce meses, que es lo que de verdad se consulta. */
const periodos = computed(() => {
  const lista: { anio: number; mes: number; label: string }[] = []

  for (let i = 0; i < 12; i++) {
    const d = new Date(hoy.getFullYear(), hoy.getMonth() - i, 1)
    lista.push({
      anio: d.getFullYear(),
      mes: d.getMonth() + 1,
      label: `${MESES[d.getMonth()]} ${d.getFullYear()}`,
    })
  }

  return lista
})

function elegirPeriodo(valor: string) {
  const [a, m] = valor.split('-').map(Number)
  anio.value = a
  mes.value = m
  return load()
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    libro.value = await platformApi.salesBook(anio.value, mes.value, servicio.value || undefined)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el libro de ventas.')
  } finally {
    loading.value = false
  }
}

async function bajar() {
  bajando.value = true
  error.value = ''
  try {
    await platformApi.salesBookSheet(anio.value, mes.value, servicio.value || undefined)
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo bajar el libro.')
  } finally {
    bajando.value = false
  }
}

onMounted(async () => {
  servicios.value = await platformApi.products().catch(() => [])
  await load()
})
</script>

<template>
  <section>
    <header class="cabecera">
      <div>
        <h1>Libro de ventas</h1>
        <p class="muted small">
          Todo lo que emitimos en el mes, de todos los servicios, ordenado por número. Es la
          cuenta de los números autorizados, no un reporte de ingresos.
        </p>
      </div>

      <div class="filtros">
        <label class="sr-only" for="periodo">Mes</label>
        <select
          id="periodo"
          :value="`${anio}-${mes}`"
          @change="elegirPeriodo(($event.target as HTMLSelectElement).value)"
        >
          <option v-for="p in periodos" :key="`${p.anio}-${p.mes}`" :value="`${p.anio}-${p.mes}`">
            {{ p.label }}
          </option>
        </select>

        <label class="sr-only" for="servicio">Servicio</label>
        <select id="servicio" v-model="servicio" @change="load">
          <option value="">Todos los servicios</option>
          <option v-for="s in servicios" :key="s.id" :value="s.id">{{ s.name }}</option>
        </select>

        <button type="button" :disabled="bajando || !libro?.rows.length" @click="bajar">
          {{ bajando ? 'Bajando…' : 'Bajar en Excel' }}
        </button>
      </div>
    </header>

    <ErrorNote v-if="error" :message="error" />

    <SkeletonList v-if="loading" :rows="6" variant="tabla" />

    <template v-else-if="libro">
      <div class="cifras">
        <article class="cifra">
          <span class="rotulo">Gravado</span>
          <strong class="num">{{ formatMoney(libro.taxable) }}</strong>
        </article>
        <article class="cifra">
          <span class="rotulo">Exento</span>
          <strong class="num">{{ formatMoney(libro.exempt) }}</strong>
        </article>
        <article class="cifra">
          <span class="rotulo">ISV</span>
          <strong class="num">{{ formatMoney(libro.tax) }}</strong>
        </article>
        <article class="cifra fuerte">
          <span class="rotulo">Total de {{ titulo }}</span>
          <strong class="num">{{ formatMoney(libro.total) }}</strong>
          <span class="muted small">
            {{ libro.count }} {{ libro.count === 1 ? 'documento' : 'documentos' }}
            <template v-if="libro.voidedCount">
              · {{ libro.voidedCount }} anulado{{ libro.voidedCount === 1 ? '' : 's' }}
            </template>
          </span>
        </article>
      </div>

      <!-- Un número que se tomó y no llegó a documento. Se dice, no se esconde: es lo primero
           que pregunta una revisión. -->
      <div v-if="libro.missingNumbers.length" class="huecos">
        <strong>Números tomados que no llegaron a documento:</strong>
        <p class="mono">{{ libro.missingNumbers.join(' · ') }}</p>
        <p class="small">
          Se tomaron del rango y la emisión no llegó a guardarse, así que no ampararon ninguna
          venta. El número no se devuelve a propósito: dos documentos con el mismo número no se
          pueden explicar, un hueco sí.
        </p>
      </div>

      <p v-if="!libro.rows.length" class="muted">
        En {{ titulo }} no se emitió ningún documento.
      </p>

      <div v-else class="tabla">
        <table>
          <thead>
            <tr>
              <th>Fecha</th>
              <th>Número</th>
              <th>CAI</th>
              <th>Cliente</th>
              <th>RTN</th>
              <th class="num">Gravado</th>
              <th class="num">Exento</th>
              <th class="num">ISV</th>
              <th class="num">Total</th>
              <th>Servicio</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="r in libro.rows" :key="r.number" :class="{ anulado: r.isVoided }">
              <td>{{ formatDay(r.issuedOn) }}</td>
              <td class="mono">{{ r.number }}</td>
              <td class="mono small">{{ r.cai ?? '—' }}</td>
              <td>
                {{ r.customerName }}
                <div v-if="r.isVoided" class="muted small">Anulado: {{ r.voidReason }}</div>
              </td>
              <td class="mono small">{{ r.customerTaxId ?? 'Consumidor final' }}</td>
              <td class="num">{{ formatMoney(r.isVoided ? 0 : r.taxable) }}</td>
              <td class="num">{{ formatMoney(r.isVoided ? 0 : r.exempt) }}</td>
              <td class="num">{{ formatMoney(r.isVoided ? 0 : r.tax) }}</td>
              <td class="num">{{ formatMoney(r.isVoided ? 0 : r.total) }}</td>
              <td class="small">{{ r.productName ?? '—' }}</td>
            </tr>
          </tbody>
          <tfoot>
            <tr>
              <td colspan="5">Totales</td>
              <td class="num">{{ formatMoney(libro.taxable) }}</td>
              <td class="num">{{ formatMoney(libro.exempt) }}</td>
              <td class="num">{{ formatMoney(libro.tax) }}</td>
              <td class="num">{{ formatMoney(libro.total) }}</td>
              <td></td>
            </tr>
          </tfoot>
        </table>
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

h1 {
  margin: 0;
  font-size: 1.625rem;
}

.cabecera p {
  margin: 0.25rem 0 0;
  max-width: 42rem;
}

.filtros {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.cifras {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.cifra {
  display: grid;
  gap: 0.15rem;
  flex: 1 1 11rem;
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

.huecos {
  margin-bottom: 1rem;
  padding: 0.75rem 0.875rem;
  border: 1px solid color-mix(in srgb, var(--warning) 45%, var(--border));
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--warning) 8%, transparent);
}

.huecos p {
  margin: 0.35rem 0 0;
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
}

.mono {
  font-family: var(--font-mono);
}

/* El anulado se queda a la vista: el número existió, y un número que desaparece es justo lo
   que levanta la pregunta. */
tbody tr.anulado {
  color: var(--text-muted);
  text-decoration: line-through;
}

tbody tr.anulado .muted {
  text-decoration: none;
}
</style>

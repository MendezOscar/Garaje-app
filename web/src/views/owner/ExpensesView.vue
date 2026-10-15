<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, expensesApi, usersApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import PhotoGallery from '@/components/PhotoGallery.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import {
  EXPENSE_CATEGORY_LABEL,
  ExpenseCategory,
  PAYMENT_METHOD_LABEL,
  PaymentMethod,
  type Branch,
  PAY_MODE_LABEL,
  TechnicianPayMode,
  type Expense,
  type IncomeStatement,
  type SaveExpense,
  type TechnicianPayProposal,
  type User,
} from '@/types/domain'
import { formatDate, formatMoney } from '@/utils/format'
import { PERIODS, periodFrom, type PeriodKey } from '@/utils/period'

/**
 * Gastos y estado de resultados.
 *
 * Van juntos a propósito: el estado de resultados es la razón por la que uno registra gastos,
 * y tenerlo al lado hace que registrarlos valga la pena. Separados, la pantalla de gastos es
 * una tarea sin recompensa y deja de llenarse a la semana.
 */
const period = ref<PeriodKey>('month')

/** Hasta dónde deja elegir el selector de mes: no hay resultados del mes que viene. */
const mesCorriente = new Date().toISOString().slice(0, 7)
const branchId = ref('')
const branches = ref<Branch[]>([])

const expenses = ref<Expense[]>([])
const statement = ref<IncomeStatement | null>(null)

const error = ref('')
const loading = ref(false)
const busy = ref(false)

/**
 * Pagarle a un técnico. El pago es un gasto de salario con su nombre, no un registro aparte:
 * entra a la caja una sola vez y el estado de resultados ya lo cuenta.
 */
const tecnicos = ref<User[]>([])
const tecnicoId = ref('')
const propuesta = ref<TechnicianPayProposal | null>(null)

const editando = ref<string | null>(null)

/** El gasto recién guardado, para adjuntarle el comprobante sin ir a buscarlo. */
const conComprobante = ref<Expense | null>(null)
const form = ref<SaveExpense>(vacio())

function vacio(): SaveExpense {
  return {
    branchId: branchId.value,
    category: ExpenseCategory.Other,
    description: '',
    amount: 0,
    paymentMethod: PaymentMethod.Cash,
    supplierName: '',
    expenseDate: new Date().toISOString().slice(0, 10),
    notes: '',
    employeeUserId: null,
  }
}

/**
 * Un mes cerrado, en formato `YYYY-MM`. Vacío significa «mande el periodo de arriba».
 *
 * Existe porque el estado de resultados de un mes solo se entiende cuando el mes terminó: el
 * día 2, el taller corriente parece vacío, y lo que el dueño quiere ver es cómo cerró el mes
 * pasado.
 */
const mes = ref('')

/** El rango que se consulta: el mes elegido, o el periodo de siempre. */
const rango = computed(() => {
  if (!mes.value) return { from: periodFrom(period.value), to: undefined }

  const [anio, numero] = mes.value.split('-').map(Number)
  return {
    from: new Date(anio, numero - 1, 1).toISOString(),
    // El último instante del mes: el día 1 del siguiente, menos un milisegundo.
    to: new Date(new Date(anio, numero, 1).getTime() - 1).toISOString(),
  }
})

/** Cómo se llama lo que se está mirando, para los rótulos. */
const rotuloDelPeriodo = computed(() => {
  if (!mes.value) return 'este periodo'

  const [anio, numero] = mes.value.split('-').map(Number)
  return new Date(anio, numero - 1, 1).toLocaleDateString('es-HN', {
    month: 'long',
    year: 'numeric',
  })
})

/** Cuánto cambió contra el periodo anterior, en porcentaje. Null si no hay con qué comparar. */
function variacion(actual: number, previo: number | undefined) {
  if (previo === undefined || previo === 0) return null
  return ((actual - previo) / Math.abs(previo)) * 100
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const query = {
      from: rango.value.from,
      to: rango.value.to,
      branchId: branchId.value || undefined,
    }

    const [page, resultado] = await Promise.all([
      expensesApi.list({ ...query, pageSize: 100 }),
      expensesApi.incomeStatement(query),
    ])

    expenses.value = page.items
    statement.value = resultado
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el estado de resultados.')
  } finally {
    loading.value = false
  }
}

async function run(action: () => Promise<unknown>) {
  busy.value = true
  error.value = ''
  try {
    await action()
    await load()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

function guardar() {
  if (!form.value.description.trim() || !form.value.amount) return

  const body: SaveExpense = {
    ...form.value,
    branchId: form.value.branchId || branches.value[0]?.id || '',
    supplierName: form.value.supplierName?.trim() || null,
    employeeUserId: form.value.employeeUserId ?? null,
    notes: form.value.notes?.trim() || null,
    // Mediodía: una fecha suelta es medianoche UTC, que en Honduras es la tarde anterior, y
    // el gasto se iba al mes equivocado los días 1.
    expenseDate: form.value.expenseDate
      ? new Date(`${form.value.expenseDate}T12:00:00`).toISOString()
      : null,
  }

  const id = editando.value
  return run(async () => {
    // Se queda a la vista el recién registrado: es cuando se tiene el comprobante en la mano
    // y el único momento en que de verdad se le va a tomar la foto.
    conComprobante.value = id ? await expensesApi.update(id, body) : await expensesApi.create(body)

    editando.value = null
    form.value = vacio()
  })
}

function editar(expense: Expense) {
  editando.value = expense.id
  conComprobante.value = expense
  form.value = {
    branchId: expense.branchId,
    category: expense.category,
    description: expense.description,
    amount: expense.amount,
    paymentMethod: expense.paymentMethod,
    supplierName: expense.supplierName ?? '',
    expenseDate: expense.expenseDate.slice(0, 10),
    notes: expense.notes ?? '',
    employeeUserId: expense.employeeUserId,
  }
}

function borrar(expense: Expense) {
  if (!confirm(`¿Borrar el gasto de ${formatMoney(expense.amount)} en ${expense.description}?`)) {
    return
  }
  return run(() => expensesApi.remove(expense.id))
}

/** Lo que le tocaría por el periodo que se está mirando. */
async function verPropuesta() {
  propuesta.value = null
  if (!tecnicoId.value) return

  try {
    propuesta.value = await usersApi.payProposal(
      tecnicoId.value,
      rango.value.from ??
        new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString(),
      // Con un mes cerrado, lo que le tocó es hasta el fin de ese mes y no hasta hoy.
      rango.value.to ?? new Date().toISOString(),
    )
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo calcular lo que le toca.')
  }
}

/** Pasa la propuesta al formulario de gasto, ya como salario y con su nombre. */
function pagarle() {
  const p = propuesta.value
  if (!p) return

  form.value = {
    ...vacio(),
    category: ExpenseCategory.Salaries,
    description: `Pago a ${p.technicianName}`,
    amount: p.proposal,
    employeeUserId: p.technicianId,
  }
  editando.value = null
}

/** Cuánto cambió la utilidad neta contra el periodo anterior. Null si no hay con qué. */
const cambioNeto = computed(() =>
  statement.value?.previous
    ? variacion(statement.value.netProfit, statement.value.previous.netProfit)
    : null,
)

const esPerdida = computed(() => (statement.value?.netProfit ?? 0) < 0)

/**
 * En qué se fue la plata: cada categoría de gasto contra la más grande, de mayor a menor.
 *
 * El renglón suelto dentro del estado de resultados dice cuánto, pero no cuál pesa: con la
 * barra se ve de un vistazo que el alquiler se come el mes, que es lo que se hace con esta
 * pantalla.
 */
const composicion = computed(() => {
  const s = statement.value
  if (!s?.expenses.length) return []

  const mayor = Math.max(...s.expenses.map((g) => g.amount))
  return s.expenses
    .map((g) => ({
      etiqueta: EXPENSE_CATEGORY_LABEL[g.category],
      monto: g.amount,
      ancho: mayor > 0 ? (g.amount / mayor) * 100 : 0,
      parte: s.expenseTotal > 0 ? (g.amount / s.expenseTotal) * 100 : 0,
    }))
    .sort((a, b) => b.monto - a.monto)
})

onMounted(async () => {
  branches.value = await branchesApi.list().catch(() => [])
  tecnicos.value = await usersApi.list('Technician').catch(() => [])
  form.value.branchId = branches.value[0]?.id ?? ''
  await load()
})
</script>

<template>
  <section>
    <header class="cabecera">
      <div>
        <h1>Estado de resultados</h1>
        <p class="muted small">
          Qué dejó el taller: lo que entró, lo que costó lo vendido, y lo que se gastó en
          tenerlo abierto.
        </p>
      </div>
      <div class="filtros">
        <select v-if="!mes" v-model="period" @change="load">
          <option v-for="p in PERIODS" :key="p.key" :value="p.key">{{ p.label }}</option>
        </select>
        <!-- Un mes cerrado: es como se mira un resultado, con el mes terminado. -->
        <input v-model="mes" type="month" :max="mesCorriente" @change="load" />
        <button v-if="mes" type="button" class="btn-ghost btn-sm" @click="mes = ''; load()">
          Volver al periodo
        </button>
        <select v-if="branches.length > 1" v-model="branchId" @change="load">
          <option value="">Todas las sucursales</option>
          <option v-for="b in branches" :key="b.id" :value="b.id">{{ b.name }}</option>
        </select>
      </div>
    </header>

    <ErrorNote v-if="error" :message="error" />
    <SkeletonList v-if="loading" :rows="4" variant="lista" />

    <template v-if="statement">
      <!-- Las cuatro cifras que se buscan al abrir: lo demás es el detalle de cómo se llega
           a la última. -->
      <div class="cifras">
        <article class="cifra">
          <span class="rotulo">Ingresos</span>
          <strong class="num">{{ formatMoney(statement.revenue) }}</strong>
          <span class="muted small">
            {{ formatMoney(statement.laborRevenue) }} de mano de obra
          </span>
        </article>
        <article class="cifra">
          <span class="rotulo">Utilidad bruta</span>
          <strong class="num">{{ formatMoney(statement.grossProfit) }}</strong>
          <span class="muted small">{{ statement.grossMarginPercent }}% de margen</span>
        </article>
        <article class="cifra">
          <span class="rotulo">Gastos</span>
          <strong class="num">−{{ formatMoney(statement.expenseTotal) }}</strong>
          <span class="muted small">en tener el taller abierto</span>
        </article>
        <article class="cifra neta" :class="{ perdida: esPerdida }">
          <span class="rotulo">{{ esPerdida ? 'Pérdida' : 'Utilidad neta' }}</span>
          <strong class="num grande">{{ formatMoney(statement.netProfit) }}</strong>
          <span class="muted small">
            {{ statement.netMarginPercent }}% de lo que entró
            <template v-if="cambioNeto !== null">
              ·
              <span class="cambio" :class="cambioNeto >= 0 ? 'sube' : 'baja'">
                {{ cambioNeto >= 0 ? '▲' : '▼' }} {{ Math.abs(cambioNeto).toFixed(0) }}%
              </span>
            </template>
          </span>
        </article>
      </div>

      <article class="card resultado">
        <div class="cuentas-lado">
          <p class="rotulo">Cómo se llega a esa utilidad</p>
          <dl class="cuentas">
            <dt>Ingresos</dt>
            <dd class="num">{{ formatMoney(statement.revenue) }}</dd>

            <dt class="sangria">Repuestos</dt>
            <dd class="num muted">{{ formatMoney(statement.partsRevenue) }}</dd>
            <dt class="sangria">Mano de obra</dt>
            <dd class="num muted">{{ formatMoney(statement.laborRevenue) }}</dd>

            <dt>Costo de lo vendido</dt>
            <dd class="num">−{{ formatMoney(statement.costOfSales) }}</dd>

            <dt class="fuerte">Utilidad bruta</dt>
            <dd class="fuerte num">
              {{ formatMoney(statement.grossProfit) }}
              <span class="muted small">· {{ statement.grossMarginPercent }}%</span>
            </dd>

            <dt>Gastos</dt>
            <dd class="num">−{{ formatMoney(statement.expenseTotal) }}</dd>

            <dt class="fuerte">Utilidad neta</dt>
            <dd class="fuerte num grande" :class="{ perdida: esPerdida }">
              {{ formatMoney(statement.netProfit) }}
              <span class="muted small">· {{ statement.netMarginPercent }}%</span>
            </dd>
          </dl>
        </div>

        <!-- La mitad derecha del estado de resultados estaba vacía y el detalle de gastos
             iba renglón por renglón en la columna de la izquierda, donde la cifra grande se
             pierde. Aquí se ve de un vistazo cuál gasto se está comiendo el mes. -->
        <div class="composicion">
          <p class="rotulo">En qué se fue la plata</p>
          <ul v-if="composicion.length" class="barras">
            <li v-for="g in composicion" :key="g.etiqueta">
              <span class="nombre">{{ g.etiqueta }}</span>
              <span class="monto num">−{{ formatMoney(g.monto) }}</span>
              <span class="barra"><span :style="{ width: g.ancho + '%' }" /></span>
              <span class="parte muted small">{{ g.parte.toFixed(0) }}% de los gastos</span>
            </li>
          </ul>
          <p v-else class="muted small">
            Todavía no hay gastos registrados en {{ rotuloDelPeriodo }}, así que la utilidad
            neta es la bruta. Registre alquiler, salarios y servicios para que el número sea
            real.
          </p>

          <!-- Un número suelto no dice nada: un millón de ingresos puede ser un buen mes o la
               mitad del anterior. -->
          <p v-if="statement.previous" class="comparacion muted small">
            Contra el periodo anterior: ingresos
            <strong>{{ formatMoney(statement.previous.revenue) }}</strong>
            <template v-if="variacion(statement.revenue, statement.previous.revenue) !== null">
              ({{ variacion(statement.revenue, statement.previous.revenue)! > 0 ? '+' : ''
              }}{{ variacion(statement.revenue, statement.previous.revenue)!.toFixed(0) }}%)
            </template>
            · utilidad neta
            <strong>{{ formatMoney(statement.previous.netProfit) }}</strong>.
          </p>
        </div>
      </article>
    </template>

    <div class="layout">
      <article class="panel">
        <header class="panel-top">
          <h2>Gastos de {{ rotuloDelPeriodo }}</h2>
          <span v-if="expenses.length" class="cuantos num">{{ expenses.length }}</span>
        </header>

        <p v-if="!expenses.length" class="vacio muted">Ninguno registrado todavía.</p>
        <ul v-else class="gastos">
          <li v-for="gasto in expenses" :key="gasto.id" :class="{ abierto: conComprobante?.id === gasto.id }">
            <span class="etiqueta">{{ EXPENSE_CATEGORY_LABEL[gasto.category] }}</span>
            <span class="gasto-texto">
              <strong>{{ gasto.description }}</strong>
              <span class="muted small">
                {{ formatDate(gasto.expenseDate) }}
                <template v-if="gasto.employeeName"> · {{ gasto.employeeName }}</template>
                <template v-else-if="gasto.supplierName"> · {{ gasto.supplierName }}</template>
              </span>
            </span>
            <span class="monto num">{{ formatMoney(gasto.amount) }}</span>
            <span class="acciones">
              <button
                type="button"
                class="icono"
                :class="{ tiene: gasto.photoCount > 0 }"
                :disabled="busy"
                :title="gasto.photoCount > 0 ? `Comprobante (${gasto.photoCount})` : 'Adjuntar comprobante'"
                @click="conComprobante = gasto"
              >
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
                  <path d="M3 8h3.5L8 6h8l1.5 2H21v11H3z" stroke-linejoin="round" />
                  <circle cx="12" cy="13" r="3.2" />
                </svg>
              </button>
              <button
                type="button"
                class="icono"
                :disabled="busy"
                title="Corregir"
                @click="editar(gasto)"
              >
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
                  <path d="M4 20h4L19 9l-4-4L4 16z" stroke-linejoin="round" />
                </svg>
              </button>
              <button
                type="button"
                class="icono peligro"
                :disabled="busy"
                title="Borrar"
                @click="borrar(gasto)"
              >
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
                  <path d="M5 7h14M10 7V5h4v2M7 7l1 13h8l1-13" stroke-linecap="round" stroke-linejoin="round" />
                </svg>
              </button>
            </span>
          </li>
        </ul>
      </article>

      <aside class="columna">
        <!-- Se queda a la vista el recién registrado: es cuando se tiene el comprobante en la
             mano y el único momento en que de verdad se le va a tomar la foto. -->
        <article v-if="conComprobante" class="card">
          <h2>Comprobante</h2>
          <p class="muted small">
            {{ conComprobante.description }} · {{ formatMoney(conComprobante.amount) }}.
            Un gasto sin comprobante se puede discutir.
          </p>
          <PhotoGallery :key="conComprobante.id" :expense-id="conComprobante.id" :can-edit="true" />
          <button type="button" class="btn-ghost" @click="conComprobante = null">Listo</button>
        </article>

        <article class="card">
          <h2>{{ editando ? 'Corregir el gasto' : 'Registrar un gasto' }}</h2>
          <form class="gasto" @submit.prevent="guardar">
            <div class="row">
              <label>
                Categoría
                <select v-model.number="form.category">
                  <option
                    v-for="(label, value) in EXPENSE_CATEGORY_LABEL"
                    :key="value"
                    :value="Number(value)"
                  >
                    {{ label }}
                  </option>
                </select>
              </label>
              <label>
                Monto
                <input v-model.number="form.amount" type="number" min="0" step="0.01" required />
              </label>
            </div>

            <label>
              En qué se gastó
              <input v-model="form.description" maxlength="300" required />
            </label>

            <div class="row">
              <label>
                Fecha
                <input v-model="form.expenseDate" type="date" />
              </label>
              <label>
                Forma de pago
                <select v-model.number="form.paymentMethod">
                  <option
                    v-for="(label, value) in PAYMENT_METHOD_LABEL"
                    :key="value"
                    :value="Number(value)"
                  >
                    {{ label }}
                  </option>
                </select>
              </label>
            </div>

            <div class="row">
              <label>
                A quién se le pagó
                <input v-model="form.supplierName" maxlength="200" placeholder="Opcional" />
              </label>
              <label v-if="branches.length > 1">
                Sucursal
                <select v-model="form.branchId">
                  <option v-for="b in branches" :key="b.id" :value="b.id">{{ b.name }}</option>
                </select>
              </label>
            </div>

            <p class="aviso muted small">
              La compra de repuestos para bodega no va aquí: eso es inventario, y se vuelve
              costo cuando se vende. Registrarlo en los dos lados contaría la misma plata dos
              veces.
            </p>

            <div class="acciones-form">
              <button type="submit" :disabled="busy || !form.description.trim() || !form.amount">
                {{ editando ? 'Guardar' : 'Registrar' }}
              </button>
              <button
                v-if="editando"
                type="button"
                class="btn-ghost"
                @click="((editando = null), (form = vacio()))"
              >
                Cancelar
              </button>
            </div>
          </form>
        </article>

        <!-- Se enseña siempre, también sin técnicos: escondida parecía que la pantalla no
             servía, cuando lo que pasaba era que no había a quién pagarle. -->
        <article class="card">
          <h2>Pagarle a un técnico</h2>
          <p v-if="!tecnicos.length" class="muted small">
            Todavía no hay técnicos. Cuando cree uno en Usuarios y le defina cómo se le paga
            —fijo, porcentaje o por hora—, aquí sale cuánto le toca por el periodo.
          </p>
          <label v-else>
            Técnico
            <select v-model="tecnicoId" @change="verPropuesta">
              <option value="">— elija —</option>
              <option v-for="tecnico in tecnicos" :key="tecnico.id" :value="tecnico.id">
                {{ tecnico.fullName }}
              </option>
            </select>
          </label>

          <template v-if="propuesta">
            <p v-if="propuesta.payMode === TechnicianPayMode.Undefined" class="muted small">
              A {{ propuesta.technicianName }} no se le ha definido cómo se le paga. Se
              configura en Usuarios, en su ficha.
            </p>
            <template v-else>
              <dl class="cuentas">
                <dt>Cómo se le paga</dt>
                <dd>
                  {{ PAY_MODE_LABEL[propuesta.payMode] }}
                  <span class="muted small">
                    ·
                    {{
                      propuesta.payMode === TechnicianPayMode.Percentage
                        ? `${propuesta.payAmount}%`
                        : formatMoney(propuesta.payAmount)
                    }}
                  </span>
                </dd>
                <template v-if="propuesta.payMode === TechnicianPayMode.Percentage">
                  <dt>Mano de obra que generó</dt>
                  <dd class="num">{{ formatMoney(propuesta.laborRevenue) }}</dd>
                </template>
                <template v-if="propuesta.payMode === TechnicianPayMode.Hourly">
                  <dt>Horas registradas</dt>
                  <dd class="num">{{ propuesta.hours }}</dd>
                </template>
                <dt class="fuerte">Le toca</dt>
                <dd class="fuerte num">{{ formatMoney(propuesta.proposal) }}</dd>
                <template v-if="propuesta.alreadyPaid > 0">
                  <dt>Ya se le pagó en el periodo</dt>
                  <dd class="num">{{ formatMoney(propuesta.alreadyPaid) }}</dd>
                </template>
              </dl>
              <button type="button" :disabled="busy" @click="pagarle">Registrar el pago</button>
              <p class="muted small">
                Pasa al formulario de gasto como salario. Ahí puede cambiar el monto antes de
                guardarlo: lo que se le paga de verdad lo decide usted.
              </p>
            </template>
          </template>
        </article>
      </aside>
    </div>
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
}

.filtros {
  display: flex;
  gap: 0.5rem;
}

/* ------------------------------------------------------------------ las cuatro cifras */

.cifras {
  display: grid;
  gap: 0.75rem;
  grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
  margin-bottom: 1rem;
}

.cifra {
  display: grid;
  justify-items: start;
  gap: 0.125rem;
  padding: 0.875rem 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.cifra .rotulo {
  margin: 0;
  font-size: 0.8125rem;
  color: var(--text-muted);
  text-transform: none;
  letter-spacing: normal;
}

.cifra strong {
  font-size: 1.375rem;
  font-family: var(--font-mono);
}

/* La utilidad neta es la razón de abrir esta pantalla: tiene que leerse desde la silla de
   atrás, no buscarse entre las otras seis cifras. */
.cifra.neta {
  border-color: color-mix(in srgb, var(--brand) 35%, var(--border));
  background: color-mix(in srgb, var(--brand) 6%, var(--surface));
}

.cifra.neta strong {
  font-size: var(--text-xl);
  line-height: var(--leading-tight);
}

.cifra.neta.perdida strong {
  color: var(--danger);
}

.cambio.sube {
  color: var(--success-text);
}

.cambio.baja {
  color: var(--danger);
}

/* ------------------------------------------------------------------ el estado, en detalle */

.resultado {
  display: grid;
  gap: 2rem;
  margin-bottom: 1rem;
}

@media (min-width: 60rem) {
  .resultado {
    grid-template-columns: minmax(0, 20rem) minmax(0, 1fr);
  }
}

.rotulo {
  margin: 0 0 0.75rem;
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-muted);
}

.cuentas {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: var(--space-1) var(--space-4);
  margin: 0;
}

.cuentas dt,
.cuentas dd {
  margin: 0;
}

.sangria {
  padding-left: 1rem;
  font-size: 0.875rem;
  color: var(--text-muted);
}

.fuerte {
  font-weight: 600;
  padding-top: 0.3rem;
  border-top: 1px solid var(--border);
}

.grande {
  font-size: var(--text-xl);
  line-height: var(--leading-tight);
}

.perdida {
  color: var(--danger);
}

.barras {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 0.75rem;
}

.barras li {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 0.2rem 0.75rem;
}

.barras .barra {
  grid-column: 1 / -1;
  height: 0.5rem;
  border-radius: 999px;
  background: var(--surface-alt);
  overflow: hidden;
}

.barras .barra span {
  display: block;
  height: 100%;
  border-radius: 999px;
  background: var(--brand);
}

.barras .parte {
  grid-column: 1 / -1;
}

.comparacion {
  margin: 1.25rem 0 0;
  padding-top: 0.75rem;
  border-top: 1px solid var(--border);
}

/* ------------------------------------------------------------------ gastos y formularios */

.layout {
  display: grid;
  gap: 1rem;
  grid-template-columns: minmax(0, 1fr) 24rem;
  align-items: start;
}

@media (max-width: 68rem) {
  .layout {
    grid-template-columns: 1fr;
  }
}

.columna {
  display: grid;
  gap: 1rem;
  align-content: start;
}

.panel {
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  overflow: hidden;
}

.panel-top {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.875rem;
  border-bottom: 1px solid var(--border);
  background: var(--surface-alt);
}

.panel-top h2 {
  margin: 0;
  font-size: 1rem;
}

.cuantos {
  padding: 0.1rem 0.5rem;
  border-radius: 999px;
  background: var(--surface);
  border: 1px solid var(--border);
  color: var(--text-muted);
  font-size: 0.8125rem;
}

.gastos {
  list-style: none;
  margin: 0;
  padding: 0;
}

.gastos li {
  display: flex;
  align-items: center;
  gap: 0.875rem;
  padding: 0.625rem 0.875rem;
  border-top: 1px solid var(--border);
}

.gastos li:first-child {
  border-top: 0;
}

.gastos li:hover,
.gastos li.abierto {
  background: var(--surface-alt);
}

.etiqueta {
  flex: none;
  width: 7.5rem;
  padding: 0.15rem 0.5rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  background: var(--surface-alt);
  color: var(--text-muted);
  font-size: 0.75rem;
  text-align: center;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.gasto-texto {
  display: grid;
  flex: 1;
  min-width: 0;
}

.gastos .monto {
  font-weight: 600;
  white-space: nowrap;
}

.acciones {
  display: flex;
  gap: 0.25rem;
  flex: none;
}

.icono {
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2rem;
  padding: 0;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text-muted);
  cursor: pointer;
}

.icono svg {
  width: 1.0625rem;
  height: 1.0625rem;
}

.icono:hover {
  color: var(--brand);
  border-color: var(--brand);
}

/* El que ya tiene foto se distingue sin abrirlo: es la diferencia entre un gasto que se
   puede probar y uno que no. */
.icono.tiene {
  color: var(--success-text);
  border-color: color-mix(in srgb, var(--success) 45%, var(--border));
}

.icono.peligro:hover {
  color: var(--danger);
  border-color: var(--danger);
}

.vacio {
  margin: 0;
  padding: 2rem 0.875rem;
  text-align: center;
}

.card h2 {
  margin: 0 0 0.5rem;
  font-size: 1rem;
}

.card .cuentas {
  margin: 0.75rem 0;
}

.gasto {
  display: grid;
  gap: 0.6rem;
}

.row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.row label {
  flex: 1 1 8rem;
}

.card label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.card input,
.card select {
  width: 100%;
}

.aviso {
  margin: 0;
  padding: 0.5rem 0.625rem;
  border-left: 2px solid var(--border);
  background: var(--surface-alt);
  border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
}

.acciones-form {
  display: flex;
  gap: 0.5rem;
}
</style>

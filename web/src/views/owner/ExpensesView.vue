<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, expensesApi, usersApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import { useAuthStore } from '@/stores/auth'
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
/** La sucursal sale del menú, no de esta pantalla: es la misma para todo el panel. */
const branchId = computed(() => auth.activeBranchId ?? '')
const auth = useAuthStore()

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

/**
 * Qué ventana está abierta encima de la pantalla.
 *
 * Los formularios vivían en una columna al lado, siempre a la vista y siempre lejos de lo que
 * se está mirando. Registrar un gasto es un acto puntual —se hace con el recibo en la mano—,
 * así que se abre cuando se pide y se quita cuando termina.
 */
const modal = ref<'gasto' | 'tecnico' | 'comprobante' | null>(null)

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
    modal.value = 'comprobante'
  })
}

function editar(expense: Expense) {
  editando.value = expense.id
  modal.value = 'gasto'
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
  modal.value = 'gasto'
}

/** Un gasto nuevo, en blanco y en la sucursal de siempre. */
function nuevoGasto() {
  editando.value = null
  form.value = { ...vacio(), branchId: branches.value[0]?.id ?? '' }
  modal.value = 'gasto'
}

function cerrar() {
  if (modal.value === 'gasto') {
    editando.value = null
    form.value = vacio()
  }
  if (modal.value === 'comprobante') conComprobante.value = null
  modal.value = null
}

function verComprobante(expense: Expense) {
  conComprobante.value = expense
  modal.value = 'comprobante'
}

/** Escape cierra, como en cualquier ventana. */
function alTeclear(e: KeyboardEvent) {
  if (e.key === 'Escape' && modal.value) cerrar()
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

// Al cambiar de sucursal en el menú, esta pantalla vuelve a pedir lo suyo.
watch(branchId, load)

onMounted(async () => {
  window.addEventListener('keydown', alTeclear)
  branches.value = await branchesApi.list().catch(() => [])
  tecnicos.value = await usersApi.list('Technician').catch(() => [])
  form.value.branchId = branches.value[0]?.id ?? ''
  await load()
})

onUnmounted(() => window.removeEventListener('keydown', alTeclear))
</script>

<template>
  <section>
    <header class="cabecera">
      <div>
        <h1>Estado de resultados</h1>
        <p class="muted small">
          <template v-if="auth.user && auth.user.branches.length > 1">
            <strong>{{ auth.activeBranchName }}</strong> ·
          </template>
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
        <div>
          <p class="rotulo">Cómo se llega a esa utilidad</p>
          <dl class="cuentas">
            <div class="renglon">
              <dt>Ingresos</dt>
              <dd class="num">{{ formatMoney(statement.revenue) }}</dd>
            </div>
            <div class="renglon sangria">
              <dt>Repuestos</dt>
              <dd class="num">{{ formatMoney(statement.partsRevenue) }}</dd>
            </div>
            <div class="renglon sangria">
              <dt>Mano de obra</dt>
              <dd class="num">{{ formatMoney(statement.laborRevenue) }}</dd>
            </div>
            <div class="renglon">
              <dt>Costo de lo vendido</dt>
              <dd class="num">−{{ formatMoney(statement.costOfSales) }}</dd>
            </div>
            <div class="renglon suma">
              <dt>Utilidad bruta</dt>
              <dd class="num">
                {{ formatMoney(statement.grossProfit) }}
                <span class="pct muted small">{{ statement.grossMarginPercent }}% de margen</span>
              </dd>
            </div>
            <div class="renglon">
              <dt>Gastos</dt>
              <dd class="num">−{{ formatMoney(statement.expenseTotal) }}</dd>
            </div>
            <div class="renglon suma total">
              <dt>{{ esPerdida ? 'Pérdida' : 'Utilidad neta' }}</dt>
              <dd class="num" :class="{ perdida: esPerdida }">
                {{ formatMoney(statement.netProfit) }}
                <span class="pct muted small">
                  {{ statement.netMarginPercent }}% de lo que entró
                </span>
              </dd>
            </div>
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

    <article class="panel">
      <header class="panel-top">
        <h2>Gastos de {{ rotuloDelPeriodo }}</h2>
        <span v-if="expenses.length" class="cuantos num">{{ expenses.length }}</span>
        <div class="panel-acciones">
          <button type="button" class="btn-ghost btn-sm" @click="modal = 'tecnico'">
            Pagarle a un técnico
          </button>
          <button type="button" class="btn-sm" @click="nuevoGasto">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
              <path d="M12 5v14M5 12h14" stroke-linecap="round" />
            </svg>
            Registrar un gasto
          </button>
        </div>
      </header>

      <p v-if="!expenses.length" class="vacio muted">
        Ninguno registrado en {{ rotuloDelPeriodo }}. Sin alquiler, salarios y servicios, la
        utilidad neta de arriba no es la de verdad.
      </p>
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
              @click="verComprobante(gasto)"
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

    <!-- ---------------------------------------------------------------- las ventanas -->

    <div v-if="modal" class="fondo" @click.self="cerrar">
      <div class="ventana" role="dialog" aria-modal="true">
        <header class="ventana-top">
          <h2>
            {{
              modal === 'comprobante'
                ? 'Comprobante'
                : modal === 'tecnico'
                  ? 'Pagarle a un técnico'
                  : editando
                    ? 'Corregir el gasto'
                    : 'Registrar un gasto'
            }}
          </h2>
          <button type="button" class="icono" title="Cerrar" @click="cerrar">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
              <path d="m6 6 12 12M18 6 6 18" stroke-linecap="round" />
            </svg>
          </button>
        </header>

        <div class="ventana-cuerpo">
          <!-- Se abre solo al guardar un gasto: es cuando se tiene el recibo en la mano y el
               único momento en que de verdad se le va a tomar la foto. -->
          <template v-if="modal === 'comprobante' && conComprobante">
            <p class="muted small">
              {{ conComprobante.description }} · {{ formatMoney(conComprobante.amount) }}.
              Un gasto sin comprobante se puede discutir.
            </p>
            <PhotoGallery
              :key="conComprobante.id"
              :expense-id="conComprobante.id"
              :can-edit="true"
            />
          </template>

          <template v-else-if="modal === 'tecnico'">
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
                  <div class="renglon">
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
                  </div>
                  <div v-if="propuesta.payMode === TechnicianPayMode.Percentage" class="renglon">
                    <dt>Mano de obra que generó</dt>
                    <dd class="num">{{ formatMoney(propuesta.laborRevenue) }}</dd>
                  </div>
                  <div v-if="propuesta.payMode === TechnicianPayMode.Hourly" class="renglon">
                    <dt>Horas registradas</dt>
                    <dd class="num">{{ propuesta.hours }}</dd>
                  </div>
                  <div class="renglon suma">
                    <dt>Le toca</dt>
                    <dd class="num">{{ formatMoney(propuesta.proposal) }}</dd>
                  </div>
                  <div v-if="propuesta.alreadyPaid > 0" class="renglon">
                    <dt>Ya se le pagó en el periodo</dt>
                    <dd class="num">{{ formatMoney(propuesta.alreadyPaid) }}</dd>
                  </div>
                </dl>
                <p class="muted small">
                  Pasa al formulario de gasto como salario. Ahí puede cambiar el monto antes de
                  guardarlo: lo que se le paga de verdad lo decide usted.
                </p>
              </template>
            </template>
          </template>

          <form v-else id="form-gasto" class="gasto" @submit.prevent="guardar">
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
          </form>
        </div>

        <footer class="ventana-pie">
          <template v-if="modal === 'gasto'">
            <button
              type="submit"
              form="form-gasto"
              :disabled="busy || !form.description.trim() || !form.amount"
            >
              {{ editando ? 'Guardar' : 'Registrar' }}
            </button>
            <button type="button" class="btn-ghost" @click="cerrar">Cancelar</button>
          </template>
          <template v-else-if="modal === 'tecnico'">
            <button
              v-if="propuesta && propuesta.payMode !== TechnicianPayMode.Undefined"
              type="button"
              :disabled="busy"
              @click="pagarle"
            >
              Registrar el pago
            </button>
            <button type="button" class="btn-ghost" @click="cerrar">Cerrar</button>
          </template>
          <button v-else type="button" @click="cerrar">Listo</button>
        </footer>
      </div>
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
  gap: 2.5rem;
  margin-bottom: 1rem;
}

@media (min-width: 64rem) {
  .resultado {
    grid-template-columns: minmax(24rem, 30rem) minmax(0, 1fr);
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

/* Cada cuenta es su propia fila: con una rejilla de dos columnas, «Costo de lo vendido»
   partía en dos renglones y su cifra quedaba a media altura de la etiqueta. */
.cuentas {
  margin: 0;
}

.renglon {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 2rem;
  padding: 0.4rem 0;
}

.renglon dt,
.renglon dd {
  margin: 0;
}

.renglon dd {
  text-align: right;
  white-space: nowrap;
}

.renglon.sangria {
  padding: 0.15rem 0 0.15rem 1rem;
  font-size: 0.875rem;
  color: var(--text-muted);
}

.renglon.suma {
  margin-top: 0.3rem;
  padding-top: 0.6rem;
  border-top: 1px solid var(--border);
  font-weight: 600;
}

.renglon.total dd {
  font-size: var(--text-xl);
  line-height: var(--leading-tight);
}

.pct {
  display: block;
  font-weight: 400;
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

/* ------------------------------------------------------------------ los gastos */

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
  flex-wrap: wrap;
  padding: 0.75rem 0.875rem;
  border-bottom: 1px solid var(--border);
  background: var(--surface-alt);
}

.panel-top h2 {
  margin: 0;
  font-size: 1rem;
}

.panel-acciones {
  display: flex;
  gap: 0.5rem;
  margin-left: auto;
}

.panel-acciones svg {
  width: 0.9rem;
  height: 0.9rem;
  margin-right: 0.3rem;
  vertical-align: -0.1rem;
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

/* ------------------------------------------------------------------ la ventana */

.fondo {
  position: fixed;
  inset: 0;
  z-index: 50;
  display: grid;
  place-items: center;
  padding: 1rem;
  background: rgb(0 0 0 / 55%);
}

.ventana {
  display: flex;
  flex-direction: column;
  width: min(34rem, 100%);
  max-height: min(90vh, 48rem);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  box-shadow: 0 1.5rem 3rem rgb(0 0 0 / 35%);
}

.ventana-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.875rem;
  border-bottom: 1px solid var(--border);
}

.ventana-top h2 {
  margin: 0;
  font-size: 1.0625rem;
}

.ventana-cuerpo {
  display: grid;
  gap: 0.6rem;
  align-content: start;
  padding: 0.875rem;
  overflow-y: auto;
}

.ventana-pie {
  display: flex;
  gap: 0.5rem;
  padding: 0.875rem;
  border-top: 1px solid var(--border);
  background: var(--surface-alt);
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
  flex: 1 1 10rem;
}

.ventana label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.ventana input,
.ventana select {
  width: 100%;
}

.aviso {
  margin: 0;
  padding: 0.5rem 0.625rem;
  border-left: 2px solid var(--border);
  background: var(--surface-alt);
  border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
}
</style>

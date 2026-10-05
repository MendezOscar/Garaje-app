<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, expensesApi, usersApi } from '@/api/garaj'
import PhotoGallery from '@/components/PhotoGallery.vue'
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

/** Desde cuándo se mira. Sin fecha —«todo»— el servidor toma el mes corriente. */
const desde = computed(() => periodFrom(period.value))

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
      from: desde.value,
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
      desde.value ?? new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString(),
      new Date().toISOString(),
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

onMounted(async () => {
  branches.value = await branchesApi.list().catch(() => [])
  tecnicos.value = await usersApi.list('Technician').catch(() => [])
  form.value.branchId = branches.value[0]?.id ?? ''
  await load()
})
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Estado de resultados</h1>
        <p class="muted small">
          Qué dejó el taller: lo que entró, lo que costó lo vendido, y lo que se gastó en
          tenerlo abierto.
        </p>
      </div>
      <div class="filtros">
        <select v-model="period" @change="load">
          <option v-for="(label, key) in PERIODS" :key="key" :value="key">{{ label }}</option>
        </select>
        <select v-if="branches.length > 1" v-model="branchId" @change="load">
          <option value="">Todas las sucursales</option>
          <option v-for="b in branches" :key="b.id" :value="b.id">{{ b.name }}</option>
        </select>
      </div>
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="loading" class="muted small">Cargando…</p>

    <article v-if="statement" class="card resultado">
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

        <template v-for="grupo in statement.expenses" :key="grupo.category">
          <dt class="sangria">{{ EXPENSE_CATEGORY_LABEL[grupo.category] }}</dt>
          <dd class="num muted">−{{ formatMoney(grupo.amount) }}</dd>
        </template>

        <dt>Gastos</dt>
        <dd class="num">−{{ formatMoney(statement.expenseTotal) }}</dd>

        <dt class="fuerte">Utilidad neta</dt>
        <dd class="fuerte num grande" :class="{ perdida: statement.netProfit < 0 }">
          {{ formatMoney(statement.netProfit) }}
          <span class="muted small">· {{ statement.netMarginPercent }}%</span>
        </dd>
      </dl>

      <!-- Un número suelto no dice nada: un millón de ingresos puede ser un buen mes o la
           mitad del anterior. -->
      <p v-if="statement.previous" class="muted small">
        Contra el periodo anterior: ingresos
        <strong>{{ formatMoney(statement.previous.revenue) }}</strong>
        <template v-if="variacion(statement.revenue, statement.previous.revenue) !== null">
          ({{ variacion(statement.revenue, statement.previous.revenue)! > 0 ? '+' : ''
          }}{{ variacion(statement.revenue, statement.previous.revenue)!.toFixed(0) }}%)
        </template>
        · utilidad neta
        <strong>{{ formatMoney(statement.previous.netProfit) }}</strong>.
      </p>

      <p v-if="!statement.expenses.length" class="muted small">
        Todavía no hay gastos registrados en este periodo, así que la utilidad neta es la
        bruta. Registre alquiler, salarios y servicios para que el número sea real.
      </p>
    </article>

    <div class="dos-columnas">
      <article v-if="tecnicos.length" class="card">
        <h2>Pagarle a un técnico</h2>
        <div class="row">
          <label>
            Técnico
            <select v-model="tecnicoId" @change="verPropuesta">
              <option value="">— elija —</option>
              <option v-for="tecnico in tecnicos" :key="tecnico.id" :value="tecnico.id">
                {{ tecnico.fullName }}
              </option>
            </select>
          </label>
        </div>

        <template v-if="propuesta">
          <p v-if="propuesta.payMode === TechnicianPayMode.Undefined" class="muted small">
            A {{ propuesta.technicianName }} no se le ha definido cómo se le paga. Se configura
            en Usuarios, en su ficha.
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
            <button type="button" :disabled="busy" @click="pagarle">
              Registrar el pago
            </button>
            <p class="muted small">
              Pasa al formulario de gasto como salario. Ahí puede cambiar el monto antes de
              guardarlo: lo que se le paga de verdad lo decide usted.
            </p>
          </template>
        </template>
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

          <p class="muted small">
            La compra de repuestos para bodega no va aquí: eso es inventario, y se vuelve costo
            cuando se vende. Registrarlo en los dos lados contaría la misma plata dos veces.
          </p>

          <div class="acciones">
            <button type="submit" :disabled="busy || !form.description.trim() || !form.amount">
              {{ editando ? 'Guardar' : 'Registrar' }}
            </button>
            <button
              v-if="editando"
              type="button"
              class="suave"
              @click="((editando = null), (form = vacio()))"
            >
              Cancelar
            </button>
          </div>
        </form>
      </article>

      <article class="card">
        <template v-if="conComprobante">
          <h2>Comprobante de {{ conComprobante.description }}</h2>
          <p class="muted small">
            {{ formatMoney(conComprobante.amount) }} ·
            {{ EXPENSE_CATEGORY_LABEL[conComprobante.category] }}. Un gasto sin comprobante se
            puede discutir.
          </p>
          <PhotoGallery :key="conComprobante.id" :expense-id="conComprobante.id" :can-edit="true" />
          <button type="button" class="suave" @click="conComprobante = null">Listo</button>
        </template>
      </article>

      <article class="card">
        <h2>Gastos del periodo</h2>
        <p v-if="!expenses.length" class="muted small">Ninguno registrado todavía.</p>
        <div v-else class="tabla">
          <table>
            <tbody>
              <tr v-for="gasto in expenses" :key="gasto.id">
                <td>
                  <strong>{{ gasto.description }}</strong>
                  <div class="muted small">
                    {{ EXPENSE_CATEGORY_LABEL[gasto.category] }} ·
                    {{ formatDate(gasto.expenseDate) }}
                    <template v-if="gasto.employeeName"> · {{ gasto.employeeName }}</template>
                    <template v-else-if="gasto.supplierName"> · {{ gasto.supplierName }}</template>
                  </div>
                </td>
                <td class="num">{{ formatMoney(gasto.amount) }}</td>
                <td class="num">
                  <button
                    type="button"
                    class="link"
                    :disabled="busy"
                    @click="conComprobante = gasto"
                  >
                    {{ gasto.photoCount > 0 ? `Comprobante (${gasto.photoCount})` : 'Comprobante' }}
                  </button>
                  <button type="button" class="link" :disabled="busy" @click="editar(gasto)">
                    Corregir
                  </button>
                  <button type="button" class="link" :disabled="busy" @click="borrar(gasto)">
                    Borrar
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </article>
    </div>
  </section>
</template>

<style scoped>
.top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

.filtros {
  display: flex;
  gap: 0.5rem;
}

.resultado {
  margin-bottom: 1rem;
}

.cuentas {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 0.3rem 1rem;
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
  border-top: 1px solid var(--border, rgba(127, 127, 127, 0.25));
}

.grande {
  font-size: 1.15rem;
}

.perdida {
  color: var(--danger, #b3261e);
}

.dos-columnas {
  display: grid;
  gap: 1rem;
  grid-template-columns: 1fr 1fr;
  align-items: start;
}

@media (max-width: 860px) {
  .dos-columnas {
    grid-template-columns: 1fr;
  }
}

.gasto {
  display: grid;
  gap: 0.6rem;
}

.row {
  display: flex;
  gap: 0.5rem;
}

.row label {
  flex: 1;
}

.acciones {
  display: flex;
  gap: 0.5rem;
}

.tabla {
  overflow-x: auto;
}
</style>

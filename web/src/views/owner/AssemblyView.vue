<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { assemblyAdminApi, assemblyApi, type AssemblyFilter } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import type {
  AssemblyContractor,
  AssemblyModel,
  AssemblyPage,
  AssemblyPayReport,
  AssemblyRow,
  AssemblySettlement,
  AssemblyWarehouse,
} from '@/types/domain'
import { formatMoney } from '@/utils/format'

/**
 * Las motos armadas: lo que se va a facturar y lo que se va a pagar.
 *
 * La pantalla está ordenada como el mes del dueño: primero el filtro, después lo que suma
 * —que es la cifra con la que se factura—, y al final el detalle. Los dos totales van juntos
 * y arriba porque la diferencia entre ellos es lo que el taller gana, y repartidos en dos
 * pantallas nadie la mira.
 *
 * El corte es lo que contesta «¿esto ya lo cobré?». Al cerrarlo, esas motos se congelan: del
 * otro lado hay una factura emitida o una plata entregada.
 */
const contratistas = ref<AssemblyContractor[]>([])
const modelos = ref<AssemblyModel[]>([])
const bodegas = ref<AssemblyWarehouse[]>([])
const tecnicos = ref<{ id: string; name: string }[]>([])

const pagina = ref<AssemblyPage | null>(null)
const cortes = ref<AssemblySettlement[]>([])
const pago = ref<AssemblyPayReport | null>(null)

const cargando = ref(true)
const busy = ref(false)
const error = ref('')
const aviso = ref('')

const hoy = new Date()
const primeroDelMes = new Date(hoy.getFullYear(), hoy.getMonth(), 1)

const filtro = ref({
  from: iso(primeroDelMes),
  to: iso(hoy),
  contractorId: '',
  technicianUserId: '',
  warehouseId: '',
  modelId: '',
  pendientes: 'todas' as 'todas' | 'por-facturar' | 'por-pagar',
  serial: '',
})

const pestana = ref<'motos' | 'pago' | 'cortes' | 'importar'>('motos')

function iso(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** El filtro tal como lo entiende la API. Lo comparten la lista, el reporte y la descarga. */
const consulta = computed<AssemblyFilter>(() => ({
  from: filtro.value.from || undefined,
  to: filtro.value.to || undefined,
  contractorId: filtro.value.contractorId || undefined,
  technicianUserId: filtro.value.technicianUserId || undefined,
  warehouseId: filtro.value.warehouseId || undefined,
  modelId: filtro.value.modelId || undefined,
  onlyUnbilled: filtro.value.pendientes === 'por-facturar' || undefined,
  onlyUnpaid: filtro.value.pendientes === 'por-pagar' || undefined,
  serial: filtro.value.serial.trim() || undefined,
  pageSize: 100,
}))

const contratistaElegido = computed(() =>
  contratistas.value.find((c) => c.id === filtro.value.contractorId),
)

/** El periodo, para el nombre del archivo que se baja. */
const periodo = computed(() => `${filtro.value.from} a ${filtro.value.to}`)

onMounted(async () => {
  try {
    const [c, m, b, t] = await Promise.all([
      assemblyApi.contractors(),
      assemblyApi.models(),
      assemblyApi.warehouses(),
      assemblyAdminApi.technicians(),
    ])

    contratistas.value = c
    modelos.value = m
    bodegas.value = b
    tecnicos.value = t

    // Con un contratista solo, se elige: es el caso de hoy y evita un reporte vacío.
    if (c.length === 1) filtro.value.contractorId = c[0].id

    await Promise.all([buscar(), cargarCortes()])
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    cargando.value = false
  }
})

watch(consulta, buscar)

async function buscar() {
  busy.value = true
  error.value = ''

  try {
    pagina.value = await assemblyAdminApi.list(consulta.value)
    if (pestana.value === 'pago') pago.value = await assemblyAdminApi.payReport(consulta.value)
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

async function cargarCortes() {
  cortes.value = await assemblyAdminApi.settlements()
}

watch(pestana, async (v) => {
  if (v !== 'pago' || pago.value) return
  await correr(async () => {
    pago.value = await assemblyAdminApi.payReport(consulta.value)
  })
})

async function bajar(que: 'billing' | 'pay', formato: 'xlsx' | 'pdf') {
  // El de facturación se le manda a alguien: mezclar dos contratos en una hoja es el error
  // que nadie detecta hasta que se la rechazan.
  if (que === 'billing' && !filtro.value.contractorId) {
    error.value = 'Elija a qué contratista es el reporte.'
    return
  }

  await correr(async () => {
    if (que === 'billing') {
      await assemblyAdminApi.downloadBilling(
        consulta.value,
        formato,
        `Motos armadas ${contratistaElegido.value?.name ?? ''} ${periodo.value}`.trim(),
      )
    } else {
      await assemblyAdminApi.downloadPay(consulta.value, formato, `Pago a tecnicos ${periodo.value}`)
    }
  })
}

async function cerrarCorte(kind: 1 | 2, technicianUserId?: string) {
  const esFacturacion = kind === 1

  if (esFacturacion && !filtro.value.contractorId) {
    error.value = 'Elija a qué contratista se le facturó.'
    return
  }

  const quien = esFacturacion
    ? contratistaElegido.value?.name
    : tecnicos.value.find((t) => t.id === technicianUserId)?.name

  const referencia = window.prompt(
    esFacturacion
      ? `Número de la factura que le emitió a ${quien} (puede dejarlo vacío):`
      : `Referencia del pago a ${quien} (puede dejarla vacía):`,
    '',
  )

  // Cancelar el diálogo es cancelar el corte; dejarlo vacío es cerrarlo sin referencia.
  if (referencia === null) return

  if (
    !confirm(
      `Se va a cerrar el periodo del ${filtro.value.from} al ${filtro.value.to} para ${quien}. ` +
        'Esas motos quedan congeladas: no se podrán corregir ni borrar mientras el corte exista.',
    )
  )
    return

  await correr(async () => {
    const corte = await assemblyAdminApi.close({
      kind,
      from: filtro.value.from,
      to: filtro.value.to,
      contractorId: esFacturacion ? filtro.value.contractorId : undefined,
      technicianUserId: esFacturacion ? undefined : technicianUserId,
      reference: referencia.trim() || undefined,
    })

    aviso.value = `Corte cerrado: ${corte.count} motos, ${formatMoney(corte.total)}.`
    await Promise.all([buscar(), cargarCortes()])
    pago.value = await assemblyAdminApi.payReport(consulta.value)
  })
}

async function reabrir(corte: AssemblySettlement) {
  if (
    !confirm(
      'Reabrir suelta esas motos y vuelven a quedar pendientes. Hágalo solo si el corte ' +
        'se hizo mal: del otro lado hay una factura o un pago ya entregado. ¿Reabrir?',
    )
  )
    return

  await correr(async () => {
    await assemblyAdminApi.reopen(corte.id)
    aviso.value = 'Corte reabierto. Esas motos vuelven a estar pendientes.'
    await Promise.all([buscar(), cargarCortes()])
    pago.value = await assemblyAdminApi.payReport(consulta.value)
  })
}

async function borrar(moto: AssemblyRow) {
  if (!confirm(`¿Borrar la moto ${moto.serial}? Es para una que se registró por error.`)) return

  await correr(async () => {
    await assemblyAdminApi.remove(moto.id)
    await buscar()
  })
}

/* La importación del Excel de antes. Se enseña primero y se guarda después. */
const archivo = ref<File | null>(null)
const vistaPrevia = ref<Awaited<ReturnType<typeof assemblyAdminApi.import>> | null>(null)

function elegirArchivo(e: Event) {
  archivo.value = (e.target as HTMLInputElement).files?.[0] ?? null
  vistaPrevia.value = null
}

async function revisarImportacion(aplicar: boolean) {
  if (!archivo.value || !filtro.value.contractorId) {
    error.value = 'Elija el contratista y el archivo.'
    return
  }

  if (
    aplicar &&
    !confirm(
      `Se van a guardar ${vistaPrevia.value?.willImport ?? 0} motos. Las filas con problema ` +
        'no entran. ¿Continuar?',
    )
  )
    return

  await correr(async () => {
    vistaPrevia.value = await assemblyAdminApi.import(
      archivo.value!,
      filtro.value.contractorId,
      aplicar,
    )

    if (aplicar) {
      aviso.value = `Se guardaron ${vistaPrevia.value.willImport} motos.`
      archivo.value = null
      await buscar()
    }
  })
}

async function correr(accion: () => Promise<void>) {
  busy.value = true
  error.value = ''
  aviso.value = ''

  try {
    await accion()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

function dia(valor: string) {
  const [a, m, d] = valor.split('-')
  return `${d}/${m}/${a}`
}
</script>

<template>
  <section class="vista">
    <header>
      <div>
        <h1>Motos armadas</h1>
        <p class="muted small">
          Lo que se le factura al contratista y lo que se le paga a cada técnico. El corte es
          lo que deja constancia de qué periodo ya se cobró y cuál ya se pagó.
        </p>
      </div>
    </header>

    <ErrorNote v-if="error" :message="error" />
    <p v-if="aviso" class="aviso">{{ aviso }}</p>

    <!-- El filtro manda sobre todo lo de abajo: la lista, los dos reportes y el corte.
         Un reporte que no cuadra con la pantalla de la que salió es peor que no tenerlo. -->
    <fieldset class="filtro">
      <legend>Periodo y filtros</legend>
      <div class="campos">
        <label>
          Desde
          <input v-model="filtro.from" type="date" />
        </label>
        <label>
          Hasta
          <input v-model="filtro.to" type="date" />
        </label>
        <label>
          Contratista
          <select v-model="filtro.contractorId">
            <option value="">Todos</option>
            <option v-for="c in contratistas" :key="c.id" :value="c.id">{{ c.name }}</option>
          </select>
        </label>
        <label>
          Técnico
          <select v-model="filtro.technicianUserId">
            <option value="">Todos</option>
            <option v-for="t in tecnicos" :key="t.id" :value="t.id">{{ t.name }}</option>
          </select>
        </label>
        <label>
          Bodega
          <select v-model="filtro.warehouseId">
            <option value="">Todas</option>
            <option v-for="b in bodegas" :key="b.id" :value="b.id">{{ b.name }}</option>
          </select>
        </label>
        <label>
          Modelo
          <select v-model="filtro.modelId">
            <option value="">Todos</option>
            <option v-for="m in modelos" :key="m.id" :value="m.id">{{ m.name }}</option>
          </select>
        </label>
        <label>
          Qué enseñar
          <select v-model="filtro.pendientes">
            <option value="todas">Todas</option>
            <option value="por-facturar">Solo lo que falta facturar</option>
            <option value="por-pagar">Solo lo que falta pagar</option>
          </select>
        </label>
        <label>
          Serie
          <input v-model="filtro.serial" placeholder="Parte de la serie" class="serie" />
        </label>
      </div>
    </fieldset>

    <p v-if="cargando" class="muted">Cargando…</p>

    <template v-else>
      <!-- Los dos totales juntos: la diferencia entre ellos es lo que el taller gana. -->
      <div v-if="pagina" class="totales">
        <div class="total">
          <strong>{{ pagina.total }}</strong>
          <span class="muted small">{{ pagina.total === 1 ? 'moto' : 'motos' }}</span>
        </div>
        <div class="total">
          <strong>{{ formatMoney(pagina.totalCharge) }}</strong>
          <span class="muted small">se le cobra al contratista</span>
        </div>
        <div class="total">
          <strong>{{ formatMoney(pagina.totalPay) }}</strong>
          <span class="muted small">se les paga a los técnicos</span>
        </div>
        <div class="total queda">
          <strong>{{ formatMoney(pagina.totalCharge - pagina.totalPay) }}</strong>
          <span class="muted small">queda para el taller</span>
        </div>
      </div>

      <nav class="pestanas">
        <button type="button" :class="{ activa: pestana === 'motos' }" @click="pestana = 'motos'">
          El detalle
        </button>
        <button type="button" :class="{ activa: pestana === 'pago' }" @click="pestana = 'pago'">
          Pago a técnicos
        </button>
        <button type="button" :class="{ activa: pestana === 'cortes' }" @click="pestana = 'cortes'">
          Cortes
        </button>
        <button
          type="button"
          :class="{ activa: pestana === 'importar' }"
          @click="pestana = 'importar'"
        >
          Traer el Excel
        </button>
      </nav>

      <template v-if="pestana === 'motos'">
        <div class="acciones">
          <button type="button" :disabled="busy" @click="bajar('billing', 'xlsx')">
            Bajar en Excel
          </button>
          <button type="button" :disabled="busy" @click="bajar('billing', 'pdf')">
            Bajar en PDF
          </button>
          <button type="button" class="primario" :disabled="busy" @click="cerrarCorte(1)">
            Cerrar la facturación del periodo
          </button>
        </div>

        <p v-if="!pagina?.items.length" class="muted">No hay motos con ese filtro.</p>

        <table v-else class="tabla">
          <thead>
            <tr>
              <th>Fecha</th>
              <th>Serie</th>
              <th>Modelo</th>
              <th>Técnico</th>
              <th>Bodega</th>
              <th class="num">Cobro</th>
              <th class="num">Pago</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="m in pagina.items" :key="m.id" :class="{ congelada: m.billed || m.paid }">
              <td>{{ dia(m.assembledOn) }}</td>
              <td class="mono">{{ m.serial }}</td>
              <td>{{ m.modelName }}</td>
              <td>{{ m.technicianName }}</td>
              <td>{{ m.warehouseName }}</td>
              <td class="num">{{ formatMoney(m.charge) }}</td>
              <td class="num">{{ formatMoney(m.technicianPay) }}</td>
              <td class="estado">
                <span v-if="m.billed" class="sello">facturada</span>
                <span v-if="m.paid" class="sello">pagada</span>
                <button
                  v-if="!m.billed && !m.paid"
                  type="button"
                  class="link"
                  :disabled="busy"
                  @click="borrar(m)"
                >
                  Borrar
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </template>

      <template v-else-if="pestana === 'pago'">
        <div class="acciones">
          <button type="button" :disabled="busy" @click="bajar('pay', 'xlsx')">
            Bajar en Excel
          </button>
          <button type="button" :disabled="busy" @click="bajar('pay', 'pdf')">
            Bajar en PDF
          </button>
        </div>

        <p v-if="!pago?.lines.length" class="muted">Nadie armó motos en ese periodo.</p>

        <table v-else class="tabla">
          <thead>
            <tr>
              <th>Técnico</th>
              <th class="num">Motos</th>
              <th class="num">A pagar</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="l in pago.lines" :key="l.technicianUserId">
              <td>{{ l.technicianName }}</td>
              <td class="num">{{ l.count }}</td>
              <td class="num">{{ formatMoney(l.pay) }}</td>
              <td class="estado">
                <button
                  type="button"
                  class="link"
                  :disabled="busy"
                  @click="cerrarCorte(2, l.technicianUserId)"
                >
                  Cerrar su pago
                </button>
              </td>
            </tr>
            <tr class="suma">
              <td>Total</td>
              <td class="num">{{ pago.count }}</td>
              <td class="num">{{ formatMoney(pago.total) }}</td>
              <td></td>
            </tr>
          </tbody>
        </table>

        <p class="muted small">
          Cerrar el pago de un técnico marca sus motos del periodo como pagadas. Lo que ya
          está pagado no vuelve a aparecer aquí, que es la forma de no pagarlo dos veces.
        </p>
      </template>

      <template v-else-if="pestana === 'cortes'">
        <p v-if="!cortes.length" class="muted">Todavía no se ha cerrado ningún corte.</p>

        <table v-else class="tabla">
          <thead>
            <tr>
              <th>Periodo</th>
              <th>De qué</th>
              <th>A quién</th>
              <th class="num">Motos</th>
              <th class="num">Total</th>
              <th>Referencia</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="c in cortes" :key="c.id">
              <td>{{ dia(c.periodFrom) }} — {{ dia(c.periodTo) }}</td>
              <td>{{ c.kind === 1 ? 'Facturación' : 'Pago' }}</td>
              <td>{{ c.contractorName ?? c.technicianName }}</td>
              <td class="num">{{ c.count }}</td>
              <td class="num">{{ formatMoney(c.total) }}</td>
              <td>{{ c.reference ?? '—' }}</td>
              <td class="estado">
                <button type="button" class="link" :disabled="busy" @click="reabrir(c)">
                  Reabrir
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </template>

      <template v-else>
        <p class="muted">
          El Excel que llevaba antes, para no arrancar en blanco. Las columnas, en este orden:
          <strong>fecha, serie, modelo, técnico, cobro, bodega</strong>. La primera fila se
          toma como encabezado.
        </p>
        <p class="muted small">
          No se crea catálogo: la fila que nombre un modelo, una bodega o un técnico que no
          esté cargado se rechaza con el motivo. Elija primero el contratista en el filtro de
          arriba.
        </p>

        <div class="acciones">
          <input type="file" accept=".xlsx" @change="elegirArchivo" />
          <button type="button" :disabled="busy || !archivo" @click="revisarImportacion(false)">
            Ver qué entraría
          </button>
          <button
            v-if="vistaPrevia && vistaPrevia.willImport > 0"
            type="button"
            class="primario"
            :disabled="busy"
            @click="revisarImportacion(true)"
          >
            Guardar las {{ vistaPrevia.willImport }}
          </button>
        </div>

        <template v-if="vistaPrevia">
          <p>
            Entrarían <strong>{{ vistaPrevia.willImport }}</strong>; quedan fuera
            <strong>{{ vistaPrevia.rejected }}</strong
            >.
          </p>

          <table class="tabla">
            <thead>
              <tr>
                <th>Fila</th>
                <th>Serie</th>
                <th>Modelo</th>
                <th>Técnico</th>
                <th>Bodega</th>
                <th>Qué pasa</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="r in vistaPrevia.rows" :key="r.row" :class="{ mala: r.problem }">
                <td class="num">{{ r.row }}</td>
                <td class="mono">{{ r.serial }}</td>
                <td>{{ r.model }}</td>
                <td>{{ r.technician }}</td>
                <td>{{ r.warehouse }}</td>
                <td>{{ r.problem ?? 'Entra' }}</td>
              </tr>
            </tbody>
          </table>
        </template>
      </template>
    </template>
  </section>
</template>

<style scoped>
.vista {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

header h1 {
  margin: 0 0 0.25rem;
}

.aviso {
  margin: 0;
  padding: 0.625rem 0.875rem;
  border: 1px solid var(--border);
  border-left: 3px solid var(--primary);
  border-radius: 6px;
  background: var(--surface);
}

.filtro {
  margin: 0;
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: 8px;
}

.filtro legend {
  padding: 0 0.375rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

/* Rejilla y no flex: así las etiquetas alinean entre filas. */
.campos {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 0.625rem;
}

.campos label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
  min-width: 0;
}

.campos input,
.campos select {
  width: 100%;
  min-width: 0;
}

.campos .serie {
  font-family: ui-monospace, monospace;
}

/* Mayúsculas a lo tecleado, no al texto de ayuda: en mayúsculas ese texto grita. */
.campos .serie:not(:placeholder-shown) {
  text-transform: uppercase;
}

/* Los cuatro números del periodo, juntos: lo que queda es la diferencia de los dos de enmedio. */
.totales {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 0.625rem;
}

.total {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.total strong {
  font-size: 1.25rem;
  font-variant-numeric: tabular-nums;
}

.total.queda {
  border-left: 3px solid var(--primary);
}

.pestanas {
  display: flex;
  gap: 0.5rem;
  border-bottom: 1px solid var(--border);
  flex-wrap: wrap;
}

.pestanas button {
  padding: 0.5rem 0.75rem;
  border: 0;
  border-bottom: 2px solid transparent;
  border-radius: 0;
  background: none;
  color: var(--text-muted);
  cursor: pointer;
}

.pestanas button.activa {
  border-bottom-color: var(--primary);
  color: var(--text);
  font-weight: 600;
}

.acciones {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
  align-items: center;
}

.tabla {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}

.tabla th,
.tabla td {
  padding: 0.4rem 0.5rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
}

.tabla th {
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.tabla .num {
  text-align: right;
  font-variant-numeric: tabular-nums;
}

.tabla .mono {
  font-family: ui-monospace, monospace;
}

.tabla .estado {
  text-align: right;
  white-space: nowrap;
}

/* Lo congelado se marca sin esconderlo: sigue contando para el total del periodo. */
.tabla tr.congelada {
  opacity: 0.7;
}

.tabla tr.mala td {
  color: var(--danger, #c0392b);
}

.tabla tr.suma td {
  font-weight: 600;
  border-top: 1px solid var(--border);
}

.sello {
  margin-left: 0.375rem;
  padding: 0 0.3rem;
  border: 1px solid var(--border);
  border-radius: 3px;
  font-size: 0.6875rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

@media (max-width: 64rem) {
  .campos,
  .totales {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 36rem) {
  .campos,
  .totales {
    grid-template-columns: 1fr;
  }
}
</style>

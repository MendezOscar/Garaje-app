<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { errorMessage } from '@/api/client'
import { assemblyAdminApi, assemblyApi, usersApi, type AssemblyFilter } from '@/api/garaj'
import { useAuthStore } from '@/stores/auth'
import ErrorNote from '@/components/ErrorNote.vue'
import type {
  AssemblyContractor,
  AssemblyModel,
  AssemblyPage,
  AssemblyPayLine,
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
const auth = useAuthStore()

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

const pestana = ref<'registrar' | 'motos' | 'pago' | 'cortes' | 'importar'>('motos')

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

    // Para el desplegable de «quién la armó» hacen falta todos los técnicos, no solo los
    // que ya armaron alguna: la primera moto de cada uno no tendría a quién apuntarse. Y
    // solo técnicos: el dueño no arma motos, registra lo que le reportan.
    personal.value = (await usersApi.list()).filter(
      (u) => u.isActive && u.role === 'Technician',
    )

    if (b.length === 1) nueva.value.warehouseId = b[0].id
    if (personal.value.length === 1) nueva.value.technicianUserId = personal.value[0].id

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

async function bajar(que: 'billing' | 'pay' | 'shop', formato: 'xlsx' | 'pdf') {
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
    } else if (que === 'shop') {
      await assemblyAdminApi.downloadShop(consulta.value, formato, `Lo del taller ${periodo.value}`)
    } else {
      await assemblyAdminApi.downloadPay(consulta.value, formato, `Pago a tecnicos ${periodo.value}`)
    }
  })
}

/**
 * La hoja de un corte ya cerrado: las motos que entraron en él y nada más.
 *
 * Es la que se le manda al contratista para que pague, y la que se vuelve a sacar el día que
 * discuta la factura. Sale del corte y no del filtro de arriba, que meses después puede dar
 * otra cosa —una moto corregida, otra que entró tarde— sin que nadie se dé cuenta.
 */
async function bajarCorte(c: AssemblySettlement, formato: 'xlsx' | 'pdf') {
  const quien = c.contractorName ?? c.technicianName ?? ''
  const que = c.kind === 1 ? 'Motos armadas' : 'Pago a tecnicos'

  await correr(async () => {
    await assemblyAdminApi.downloadSettlement(
      c.id,
      formato,
      `${que} ${quien} ${dia(c.periodFrom)} a ${dia(c.periodTo)}`.replace(/\//g, '-').trim(),
    )
  })
}

/*
 * Cerrar el pago de un técnico.
 *
 * Abre un formulario con lo que suman sus motos ya escrito, y ahí se cambia si hace falta:
 * hay un bono, una quincena redondeada o un acuerdo que no cabe ni en la tarifa del modelo
 * ni en su fijo por moto. Lo que quede escrito es lo que se cierra y lo que entra como
 * gasto del periodo.
 *
 * El monto no se edita en la casilla de la tabla: una cifra que se puede cambiar sin querer,
 * en la fila de un botón que congela el periodo, es una forma fácil de pagar de más.
 */
const pagando = ref<AssemblyPayLine | null>(null)

const pago_ = ref({ total: 0, reference: '' })

function abrirPago(linea: AssemblyPayLine) {
  pagando.value = linea
  pago_.value = { total: linea.pay, reference: '' }
}

async function cerrarPagoDelTecnico() {
  const linea = pagando.value
  if (!linea) return

  if (
    !confirm(
      `Se le van a pagar ${formatMoney(pago_.value.total)} a ${linea.technicianName} por el ` +
        `periodo del ${filtro.value.from} al ${filtro.value.to}. Sus motos quedan congeladas ` +
        'y el monto entra como gasto de salario del periodo.',
    )
  )
    return

  await correr(async () => {
    const corte = await assemblyAdminApi.close({
      kind: 2,
      from: filtro.value.from,
      to: filtro.value.to,
      technicianUserId: linea.technicianUserId,
      reference: pago_.value.reference.trim() || undefined,
      total: pago_.value.total,
    })

    pagando.value = null
    aviso.value = `Corte cerrado: ${corte.count} motos, ${formatMoney(corte.total)}.`
    await Promise.all([buscar(), cargarCortes()])
    pago.value = await assemblyAdminApi.payReport(consulta.value)
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

/**
 * Lo que está facturado y todavía no ha entrado.
 *
 * Cerrar el corte es facturar: la hoja se le manda al contratista y él se toma su tiempo en
 * pagarla. Sin este número, un corte cerrado parecía plata entrada y no lo era.
 */
const porCobrar = computed(() =>
  cortes.value.filter((c) => c.kind === 1 && !c.collectedOn),
)

const totalPorCobrar = computed(() => porCobrar.value.reduce((t, c) => t + c.total, 0))

/** Cuántos días lleva una factura esperando. Es lo que convierte una lista en una alarma. */
function diasEsperando(c: AssemblySettlement) {
  const desde = new Date(c.closedAt).getTime()
  return Math.max(0, Math.floor((Date.now() - desde) / 86_400_000))
}

async function cobrar(c: AssemblySettlement) {
  const referencia = prompt(
    `¿Con qué pagó ${c.contractorName ?? 'el contratista'} la factura ${c.reference ?? ''}? ` +
      'Escriba el recibo, la transferencia o el cheque (puede dejarlo vacío).',
    '',
  )

  // Cancelar el cuadro devuelve null; dejarlo vacío devuelve cadena y sí es un cobro.
  if (referencia === null) return

  await correr(async () => {
    await assemblyAdminApi.collect(c.id, { reference: referencia.trim() || undefined })
    aviso.value = 'Anotado el cobro de esa factura.'
    await cargarCortes()
  })
}

async function quitarCobro(c: AssemblySettlement) {
  if (!confirm('¿Quitarle el cobro a esa factura? Vuelve a quedar por cobrar.')) return

  await correr(async () => {
    await assemblyAdminApi.uncollect(c.id)
    aviso.value = 'Esa factura vuelve a estar por cobrar.'
    await cargarCortes()
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

/*
 * Corregir una moto ya registrada.
 *
 * La serie y el modelo se teclean con la caja delante y a veces salen mal, y el error se ve
 * días después, cuando el contratista reclama. Mientras la moto no haya entrado en un corte
 * se corrige aquí; si ya se facturó o se pagó, está congelada y el servidor no la deja:
 * del otro lado hay una factura emitida.
 *
 * Los dos importes van en el mismo formulario a propósito: el caso real no es «corregí el
 * modelo», es «corregí el modelo y ahora el pago que le corresponde es otro». Vacíos, el
 * servidor los recalcula con la tarifa de hoy y con el trato del técnico.
 */
const corrigiendo = ref<AssemblyRow | null>(null)

const correccion = ref({
  serial: '',
  modelId: '',
  contractorId: '',
  warehouseId: '',
  technicianUserId: '',
  assembledOn: '',
  notes: '',
  charge: null as number | null,
  technicianPay: null as number | null,
})

function corregir(moto: AssemblyRow) {
  corrigiendo.value = moto
  correccion.value = {
    serial: moto.serial,
    modelId: moto.modelId,
    contractorId: moto.contractorId,
    warehouseId: moto.warehouseId,
    technicianUserId: moto.technicianUserId,
    assembledOn: moto.assembledOn.slice(0, 10),
    notes: moto.notes ?? '',
    charge: moto.charge,
    technicianPay: moto.technicianPay,
  }
}

async function guardarCorreccion() {
  const moto = corrigiendo.value
  if (!moto) return

  const c = correccion.value

  await correr(async () => {
    await assemblyAdminApi.correct(moto.id, {
      serial: c.serial.trim(),
      modelId: c.modelId,
      contractorId: c.contractorId,
      warehouseId: c.warehouseId,
      technicianUserId: c.technicianUserId,
      assembledOn: c.assembledOn,
      notes: c.notes.trim() || null,
      // Vacío no es cero: es «recalcúlemelo con la tarifa».
      charge: c.charge,
      technicianPay: c.technicianPay,
    })

    corrigiendo.value = null
    await buscar()
    aviso.value = 'Moto corregida.'
  })
}

async function borrar(moto: AssemblyRow) {
  if (!confirm(`¿Borrar la moto ${moto.serial}? Es para una que se registró por error.`)) return

  await correr(async () => {
    await assemblyAdminApi.remove(moto.id)
    await buscar()
  })
}

/*
 * Registrar una moto desde el panel.
 *
 * Existe por dos motivos: el dueño carga la que el técnico le reportó por teléfono, y se
 * puede probar el módulo entero sin saltar al celular. Lo que se registra aquí queda a
 * nombre del técnico que se elija, no de quien lo teclea.
 */
const registrando = ref(false)
const personal = ref<{ id: string; fullName: string }[]>([])

const nueva = ref({
  serial: '',
  modelId: '',
  warehouseId: '',
  technicianUserId: '',
  assembledOn: iso(hoy),
  notes: '',
})

const propuesta = ref<{ modelName: string | null; seenBefore: number } | null>(null)

const serieLimpia = computed(() =>
  nueva.value.serial.replace(/[^A-Za-z0-9]/g, '').toUpperCase(),
)

/** El estándar son 17 caracteres y no usa I, O ni Q. Aviso, no bloqueo. */
const avisoDeLaSerie = computed(() => {
  const v = serieLimpia.value
  if (!v) return ''
  if (v.length !== 17) return `Son ${v.length} caracteres; una serie estándar lleva 17.`
  return /[IOQ]/.test(v) ? 'Una serie no lleva I, O ni Q: revise si son 1, 0 o 9.' : ''
})

let reboteDeLaSerie: ReturnType<typeof setTimeout> | undefined

/** Pregunta qué modelo suele llevar una serie así, con rebote: se teclea carácter a carácter. */
watch(serieLimpia, (v) => {
  clearTimeout(reboteDeLaSerie)

  if (v.length < 8) {
    propuesta.value = null
    return
  }

  reboteDeLaSerie = setTimeout(async () => {
    try {
      const r = await assemblyAdminApi.guessModel(v)
      propuesta.value = r.modelId ? { modelName: r.modelName, seenBefore: r.seenBefore } : null
      if (r.modelId && !nueva.value.modelId) nueva.value.modelId = r.modelId
    } catch {
      // Que la propuesta no se pueda calcular no detiene nada.
    }
  }, 400)
})

async function registrar() {
  if (!filtro.value.contractorId) {
    error.value = 'Elija arriba el contratista al que se le arma.'
    return
  }

  // Sin esto la moto se le apuntaba a quien la teclea, y el pago del que de verdad la armó
  // desaparecía del reporte del mes sin que nada avisara.
  if (!nueva.value.technicianUserId) {
    error.value = 'Diga quién armó la moto.'
    return
  }

  registrando.value = true
  error.value = ''
  aviso.value = ''

  try {
    const moto = await assemblyAdminApi.register({
      serial: serieLimpia.value,
      modelId: nueva.value.modelId,
      contractorId: filtro.value.contractorId,
      warehouseId: nueva.value.warehouseId,
      technicianUserId: nueva.value.technicianUserId || undefined,
      assembledOn: nueva.value.assembledOn || undefined,
      branchId: auth.activeBranchId ?? undefined,
      notes: nueva.value.notes.trim() || undefined,
    })

    aviso.value = `Registrada la ${moto.serial}.`

    // Se limpia lo que cambia de una moto a otra; el resto se queda, que es lo mismo toda
    // la tarde.
    nueva.value.serial = ''
    nueva.value.notes = ''
    propuesta.value = null

    await buscar()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    registrando.value = false
  }
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
      <!-- Los números del periodo juntos: lo que queda es el cobro menos el pago y el gasto. -->
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
        <div v-if="pagina.totalExpense" class="total">
          <strong>{{ formatMoney(pagina.totalExpense) }}</strong>
          <span class="muted small">le cuesta al taller armarlas</span>
        </div>
        <div class="total queda">
          <strong>
            {{ formatMoney(pagina.totalCharge - pagina.totalPay - pagina.totalExpense) }}
          </strong>
          <span class="muted small">queda para el taller</span>
        </div>
      </div>

      <nav class="pestanas">
        <button
          type="button"
          :class="{ activa: pestana === 'registrar' }"
          @click="pestana = 'registrar'"
        >
          Registrar una moto
        </button>
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

      <template v-if="pestana === 'registrar'">
        <p class="muted small">
          La moto que el técnico le reportó por teléfono. Se le apunta al técnico que elija,
          no a usted, y se le cobra al contratista que esté puesto arriba en el filtro.
        </p>

        <fieldset class="alta">
          <legend>Moto armada</legend>
          <div class="campos">
            <label>
              Serie del chasis
              <input v-model="nueva.serial" maxlength="25" class="serie" placeholder="17 caracteres" />
            </label>
            <label>
              Modelo
              <select v-model="nueva.modelId">
                <option value="">Elija…</option>
                <option v-for="m in modelos.filter((x) => x.isActive)" :key="m.id" :value="m.id">
                  {{ m.name }}
                </option>
              </select>
            </label>
            <label>
              Bodega
              <select v-model="nueva.warehouseId">
                <option value="">Elija…</option>
                <option v-for="b in bodegas.filter((x) => x.isActive)" :key="b.id" :value="b.id">
                  {{ b.name }}
                </option>
              </select>
            </label>
            <label>
              Quién la armó
              <select v-model="nueva.technicianUserId">
                <option value="" disabled>Elija al técnico</option>
                <option v-for="u in personal" :key="u.id" :value="u.id">{{ u.fullName }}</option>
              </select>
            </label>
            <label>
              Día en que se armó
              <input v-model="nueva.assembledOn" type="date" />
            </label>
            <label class="ancho">
              Nota (opcional)
              <input v-model="nueva.notes" maxlength="500" placeholder="Algo que convenga dejar escrito" />
            </label>
          </div>

          <p v-if="avisoDeLaSerie" class="muted small">{{ avisoDeLaSerie }}</p>
          <!-- Sin veces es porque el prefijo lo dejó escrito el dueño en el catálogo; con
               veces, porque se aprendió de lo ya registrado. Conviene que se note cuál. -->
          <p v-if="propuesta" class="muted small">
            <template v-if="propuesta.seenBefore === 0">
              Por el inicio de la serie, es una <strong>{{ propuesta.modelName }}</strong>.
            </template>
            <template v-else>
              Series como esta han sido <strong>{{ propuesta.modelName }}</strong>
              {{ propuesta.seenBefore === 1 ? 'una vez' : `${propuesta.seenBefore} veces` }}.
            </template>
            Confírmelo con la moto.
          </p>

          <div class="acciones">
            <button
              type="button"
              class="primario"
              :disabled="registrando || !serieLimpia || !nueva.modelId || !nueva.warehouseId"
              @click="registrar"
            >
              {{ registrando ? 'Registrando…' : 'Registrar la moto' }}
            </button>
          </div>
        </fieldset>
      </template>

      <template v-else-if="pestana === 'motos'">
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

        <!-- Aparte, y dicho con todas sus letras: esta hoja lleva el gasto y el margen, así
             que es la única de las tres que no se le manda a nadie. -->
        <div class="acciones del-taller">
          <span class="muted small">Para usted, con el gasto y lo que queda:</span>
          <button type="button" class="chico" :disabled="busy" @click="bajar('shop', 'xlsx')">
            Excel
          </button>
          <button type="button" class="chico" :disabled="busy" @click="bajar('shop', 'pdf')">
            PDF
          </button>
        </div>

        <!-- Corregir una moto. Sale encima de la tabla y no en una ventana aparte porque
             casi siempre se corrige mirando la fila de al lado. -->
        <form v-if="corrigiendo" class="card correccion" @submit.prevent="guardarCorreccion">
          <p class="rotulo">Corregir la moto {{ corrigiendo.serial }}</p>

          <div class="campos">
            <label>
              Serie del chasis
              <input v-model="correccion.serial" maxlength="25" required />
            </label>
            <label>
              Modelo
              <select v-model="correccion.modelId" required>
                <option v-for="m in modelos" :key="m.id" :value="m.id">{{ m.name }}</option>
              </select>
            </label>
            <label>
              Contratista
              <select v-model="correccion.contractorId" required>
                <option v-for="c in contratistas" :key="c.id" :value="c.id">{{ c.name }}</option>
              </select>
            </label>
            <label>
              Bodega
              <select v-model="correccion.warehouseId" required>
                <option v-for="b in bodegas" :key="b.id" :value="b.id">{{ b.name }}</option>
              </select>
            </label>
            <label>
              Quién la armó
              <select v-model="correccion.technicianUserId" required>
                <option v-for="u in personal" :key="u.id" :value="u.id">{{ u.fullName }}</option>
              </select>
            </label>
            <label>
              Día en que se armó
              <input v-model="correccion.assembledOn" type="date" required />
            </label>
            <label>
              Se le cobra al contratista
              <input v-model.number="correccion.charge" type="number" min="0" step="0.01" />
            </label>
            <label>
              Se le paga al técnico
              <input v-model.number="correccion.technicianPay" type="number" min="0" step="0.01" />
              <small class="muted">
                Déjelo vacío y se recalcula con la tarifa del modelo —o con el fijo por moto
                de ese técnico, si lo tiene—.
              </small>
            </label>
            <!-- La nota va dentro de la misma rejilla, ocupando el ancho: fuera de ella
                 quedaba como una etiqueta suelta con un recuadro diminuto al lado. -->
            <label class="ancho">
              Nota
              <input v-model="correccion.notes" maxlength="300" />
            </label>
          </div>

          <div class="acciones">
            <button type="submit" :disabled="busy">Guardar</button>
            <button type="button" class="chico" :disabled="busy" @click="corrigiendo = null">
              Cancelar
            </button>
          </div>
        </form>

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
              <th class="num">Gasto</th>
              <th class="num">Queda</th>
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
              <td class="num">{{ formatMoney(m.expense) }}</td>
              <td class="num queda">
                {{ formatMoney(m.charge - m.technicianPay - m.expense) }}
              </td>
              <td class="estado">
                <span v-if="m.billed" class="sello">facturada</span>
                <span v-if="m.paid" class="sello">pagada</span>
                <template v-if="!m.billed && !m.paid">
                  <button type="button" class="link" :disabled="busy" @click="corregir(m)">
                    Corregir
                  </button>
                  <button type="button" class="link" :disabled="busy" @click="borrar(m)">
                    Borrar
                  </button>
                </template>
              </td>
            </tr>
          </tbody>

          <!-- Los totales son los del filtro entero, no los de esta página: quien va a
               facturar necesita el total del periodo, no el de las primeras veinticinco. -->
          <tfoot>
            <tr>
              <td colspan="5">
                {{ pagina.total }} {{ pagina.total === 1 ? 'moto' : 'motos' }}
                <span v-if="pagina.total > pagina.items.length" class="muted small">
                  (en pantalla, {{ pagina.items.length }})
                </span>
              </td>
              <td class="num">{{ formatMoney(pagina.totalCharge) }}</td>
              <td class="num">{{ formatMoney(pagina.totalPay) }}</td>
              <td class="num">{{ formatMoney(pagina.totalExpense) }}</td>
              <td class="num queda">
                {{ formatMoney(pagina.totalCharge - pagina.totalPay - pagina.totalExpense) }}
              </td>
              <td></td>
            </tr>
          </tfoot>
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

        <!-- El formulario del pago, como el de corregir una moto: encima de la tabla y con
             lo que suman sus motos ya escrito. -->
        <form v-if="pagando" class="card correccion" @submit.prevent="cerrarPagoDelTecnico">
          <p class="rotulo">Pago a {{ pagando.technicianName }}</p>
          <p class="muted small">
            {{ pagando.count }} {{ pagando.count === 1 ? 'moto' : 'motos' }} del
            {{ filtro.from }} al {{ filtro.to }} · suman {{ formatMoney(pagando.pay) }}.
          </p>

          <div class="campos">
            <label>
              Se le paga
              <input v-model.number="pago_.total" type="number" min="0" step="0.01" required />
              <small class="muted">
                Lo que quede escrito es lo que se paga y lo que entra como gasto del periodo.
              </small>
            </label>
            <label>
              Referencia
              <input v-model="pago_.reference" maxlength="60" placeholder="Recibo, transferencia…" />
            </label>
          </div>

          <div class="acciones">
            <button type="submit" :disabled="busy">Cerrar el pago</button>
            <button type="button" class="chico" :disabled="busy" @click="pagando = null">
              Cancelar
            </button>
          </div>
        </form>

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
                <button type="button" class="link" :disabled="busy" @click="abrirPago(l)">
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
          Al cerrarlo se pregunta cuánto se le paga, con lo que suman sus motos ya escrito:
          ahí se cambia si el trato de ese técnico fue otro.
          Y queda anotado como <strong>gasto de salario</strong> a su nombre, así que el
          estado de resultados del periodo ya lo lleva restado — no lo registre otra vez.
        </p>
      </template>

      <template v-else-if="pestana === 'cortes'">
        <!-- Lo primero de esta pestaña, porque es la pregunta que trae a alguien aquí:
             cerrar el corte es facturar, y la plata entra semanas después. -->
        <div v-if="porCobrar.length" class="por-cobrar">
          <strong>{{ formatMoney(totalPorCobrar) }}</strong>
          <span>
            facturados y todavía por cobrar, en
            {{ porCobrar.length }}
            {{ porCobrar.length === 1 ? 'factura' : 'facturas' }}
          </span>
        </div>

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
              <th>Cobro</th>
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
              <td>
                <template v-if="c.kind !== 1">
                  <span class="muted small">pagado al cerrar</span>
                </template>
                <template v-else-if="c.collectedOn">
                  <span class="cobrada">cobrada {{ dia(c.collectedOn) }}</span>
                  <span v-if="c.collectedReference" class="muted small">
                    · {{ c.collectedReference }}
                  </span>
                  <button type="button" class="link" :disabled="busy" @click="quitarCobro(c)">
                    Quitar
                  </button>
                </template>
                <template v-else>
                  <span class="pendiente">por cobrar</span>
                  <span class="muted small"> · {{ diasEsperando(c) }} d</span>
                  <button type="button" class="link" :disabled="busy" @click="cobrar(c)">
                    Ya pagó
                  </button>
                </template>
              </td>
              <td class="estado">
                <button type="button" class="chico" :disabled="busy" @click="bajarCorte(c, 'xlsx')">
                  Excel
                </button>
                <button type="button" class="chico" :disabled="busy" @click="bajarCorte(c, 'pdf')">
                  PDF
                </button>
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
.correccion {
  margin-bottom: 1rem;
}

.correccion .campos {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 0.75rem;
}

.correccion .campos .ancho {
  grid-column: 1 / -1;
}

.correccion .acciones {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

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

/* Los números del periodo, juntos: lo que queda es el cobro menos los dos de enmedio. El
   gasto solo aparece si el taller lo tiene puesto, así que son cuatro o cinco. */
.totales {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
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

/* Separada de la fila de arriba a propósito: lo de arriba se manda, esto no. */
.del-taller {
  margin-top: -0.25rem;
  padding-bottom: 0.25rem;
}

.del-taller .chico {
  padding: 0.25rem 0.6rem;
  font-size: 0.85rem;
}

.alta {
  margin: 0;
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  gap: 0.625rem;
}

.alta legend {
  padding: 0 0.375rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.alta .ancho {
  grid-column: 1 / -1;
}

.alta p {
  margin: 0;
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

/* Lo que queda es la columna que se mira: el resto son los sumandos. */
.tabla td.queda {
  font-weight: 600;
}

.tabla tfoot td {
  border-top: 2px solid var(--border);
  border-bottom: 0;
  font-weight: 600;
}

.tabla .mono {
  font-family: ui-monospace, monospace;
}

.tabla .estado {
  text-align: right;
  white-space: nowrap;
}

/* Lo facturado y no cobrado, arriba de la lista de cortes: es lo que se viene a ver. */
.por-cobrar {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
  flex-wrap: wrap;
  padding: 0.75rem 1rem;
  border: 1px solid var(--border);
  border-left: 3px solid var(--warning);
  border-radius: 8px;
  background: var(--surface);
  margin-bottom: 0.75rem;
}

.por-cobrar strong {
  font-size: 1.25rem;
  font-variant-numeric: tabular-nums;
}

.pendiente {
  font-weight: 600;
  color: var(--warning);
}

.cobrada {
  font-weight: 600;
}

.tabla .estado .chico {
  padding: 0.2rem 0.55rem;
  margin-left: 0.3rem;
  font-size: 0.8rem;
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

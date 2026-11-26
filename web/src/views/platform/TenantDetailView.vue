<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { errorMessage } from '@/api/client'
import { platformApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import {
  PLAN_DEFAULTS,
  PLAN_LABEL,
  SubscriptionPlan,
  WorkshopKind,
  type CreatedTenant,
  type PlatformSettings,
  type PlatformTenantDetail,
  type PlatformUser,
} from '@/types/domain'
import { formatDay, formatMoney } from '@/utils/format'

/**
 * La ficha de cobro de un taller: registrar el pago del mes, darle un acuerdo si quedó
 * debiendo, y —solo como último recurso— suspenderlo.
 */
const route = useRoute()
const id = route.params.id as string

const data = ref<PlatformTenantDetail | null>(null)
const loading = ref(false)
const busy = ref(false)
const error = ref('')

const tenant = computed(() => data.value?.tenant ?? null)

const pago = ref({
  amount: 0,
  paidOn: '',
  method: 'Transferencia',
  reference: '',
  months: 1,
  emitir: true,
  conCai: false,
})

/**
 * Nuestros datos de emisor. De aquí sale el ISV con que se arma el documento y si hay CAI
 * para emitir con él; sin esto el formulario no podría decir por cuánto va a salir la factura
 * antes de registrarla.
 */
const emisor = ref<PlatformSettings | null>(null)


/**
 * Cómo queda el documento. Se enseña antes de registrar: el número que se le dice al taller
 * por teléfono es el total, no la mensualidad pelada.
 *
 * El monto que se escribe es el precio del servicio. El ISV solo entra en la factura con CAI
 * —el comprobante no desglosa impuesto—, y entra como lo cobre el emisor: encima del precio,
 * que es como cobramos, o desglosado hacia atrás si el precio ya lo llevara adentro.
 */
const documento = computed(() => {
  const tasa = emisor.value?.defaultTaxRate ?? 0
  const monto = Number(pago.value.amount) || 0
  const conCai = pago.value.emitir && pago.value.conCai && !sinCai.value

  if (tasa <= 0 || !conCai) return { neto: monto, impuesto: 0, total: monto }

  if (emisor.value?.pricesIncludeTax) {
    const neto = Math.round((monto / (1 + tasa / 100)) * 100) / 100
    return { neto, impuesto: Math.round((monto - neto) * 100) / 100, total: monto }
  }

  const impuesto = Math.round(monto * (tasa / 100) * 100) / 100

  return { neto: monto, impuesto, total: Math.round((monto + impuesto) * 100) / 100 }
})

/**
 * El paso de revisión. Registrar el pago emite el documento y corre el vencimiento —y con CAI
 * quema un número autorizado—, así que primero se enseña exactamente qué va a salir y recién
 * el segundo botón lo hace.
 */
const revisando = ref(false)

/**
 * La pantalla se mira más de lo que se edita, así que arranca en modo ficha: los datos como
 * texto y una sola acción grande. El formulario aparece cuando se pide, y sólo uno a la vez.
 */
const registrando = ref(false)
const editandoFicha = ref(false)
const editandoPlan = ref(false)

/** En qué pestaña está. A esta pantalla se entra a cobrar, así que arranca en Cobros. */
const pestana = ref<'cobros' | 'plan' | 'factura' | 'usuarios' | 'repara'>('cobros')

/** La etiqueta del estado, para el distintivo de al lado del nombre. */
const etiqueta = computed(() => {
  const t = tenant.value
  if (!t) return ''
  if (!t.isActive) return 'Suspendido'
  if (t.unblockedThrough) return 'Acuerdo de pago'
  return (
    {
      Active: 'Al día',
      DueSoon: 'Por vencer',
      Grace: 'En gracia',
      ReadOnly: 'Solo lectura',
      Suspended: 'Suspendido',
    } as const
  )[t.state]
})

/** Teléfono, correo y dirección en un renglón, saltando lo que no tenga. */
const contacto = computed(() => {
  const t = tenant.value
  if (!t) return ''
  return [t.phone, t.email, t.address].filter(Boolean).join(' · ') || 'Sin teléfono ni correo'
})

/**
 * El monto sigue a los meses, salvo que alguien lo escriba. Lo normal es que el taller pague
 * su cuota entera; el que negoció otro número lo escribe y entonces el monto deja de seguir.
 */
const montoAMano = ref(false)

function elegirMeses(meses: number) {
  pago.value.months = meses
  if (!montoAMano.value) pago.value.amount = (tenant.value?.monthlyFee ?? 0) * meses
}

/** Desde cuándo y hasta cuándo queda cubierto con este pago, dicho de corrido. */
const cubre = computed(() => {
  const t = tenant.value
  if (!t) return ''

  const desde = t.paidThrough ? new Date(`${t.paidThrough}T00:00:00`) : new Date()
  if (t.paidThrough) desde.setDate(desde.getDate() + 1)

  return `${formatDay(desde.toISOString().slice(0, 10))} → ${formatDay(cubreHasta.value)}`
})

/** El tramo de gracia, de la fecha de vencimiento al día en que se corta. */
const gracia = computed(() => {
  const t = tenant.value
  if (!t?.paidThrough || !t.readOnlyOn) return '—'

  const desde = new Date(`${t.paidThrough}T00:00:00`)
  desde.setDate(desde.getDate() + 1)

  return `${formatDay(desde.toISOString().slice(0, 10))} – ${formatDay(t.readOnlyOn)}`
})

function abrirElPago() {
  registrando.value = !registrando.value
  revisando.value = false
}

/** El número propio, el del comprobante de cuando no hay CAI. */
const numeroPropio = computed(
  () =>
    `${emisor.value?.invoicePrefix ?? 'C'}-${String(emisor.value?.nextInvoiceNumber ?? 1).padStart(6, '0')}`,
)

/** El número autorizado que le tocaría, si hay rango vigente. */
const numeroFiscal = computed(() => emisor.value?.fiscalRange?.nextFiscalNumber ?? null)

/**
 * Qué papel deja este pago. Al servidor viajan dos campos —emitir y con CAI— porque son dos
 * cosas distintas allá, pero aquí es una sola elección de tres: cruzar dos casillas para
 * saber qué va a salir es justo lo que nadie hace bien a las ocho de la mañana.
 */
const entrega = computed<'comprobante' | 'factura' | 'ninguno'>({
  get() {
    if (!pago.value.emitir) return 'ninguno'
    return pago.value.conCai && !sinCai.value ? 'factura' : 'comprobante'
  },
  set(cual) {
    pago.value.emitir = cual !== 'ninguno'
    pago.value.conCai = cual === 'factura'
  },
})

/** Qué número va a llevar el documento, dicho antes de emitirlo. */
const numeroQueSale = computed(() => {
  if (!pago.value.emitir) return null
  if (pago.value.conCai && !sinCai.value) return emisor.value?.fiscalRange?.nextFiscalNumber ?? null

  const prefijo = emisor.value?.invoicePrefix ?? 'C'
  return `${prefijo}-${String(emisor.value?.nextInvoiceNumber ?? 1).padStart(6, '0')}`
})

/** Hasta cuándo queda cubierto el taller con este pago, que es el otro efecto del botón. */
const cubreHasta = computed(() => {
  const desde = tenant.value?.paidThrough ? new Date(`${tenant.value.paidThrough}T00:00:00`) : new Date()
  const meses = Number(pago.value.months) || 1

  const fecha = new Date(desde)
  fecha.setMonth(fecha.getMonth() + meses)

  return fecha.toISOString().slice(0, 10)
})

const sinCai = computed(() => {
  const rango = emisor.value?.fiscalRange
  return !rango || rango.isExpired || rango.isExhausted
})
const acuerdo = ref({ unblockedThrough: '', note: '' })

/** La ficha del taller como cliente nuestro: es lo que sale impreso en su factura. */
const ficha = ref({ name: '', legalName: '', taxId: '', phone: '', email: '', address: '' })
const plan = ref({
  plan: SubscriptionPlan.Basic as SubscriptionPlan,
  monthlyFee: 0,
  paidThrough: '',
  graceDays: 5,
  /** Vacío es sin tope. Se escribe así y no con una casilla: el número es el acuerdo. */
  technicianLimit: '' as number | '',
  branchLimit: '' as number | '',
  diagnosticsEnabled: false,
  /** El asistente va aparte del diagnóstico: aquel es el del escáner. */
  assistantEnabled: false,
  assemblyEnabled: false,
  assistantCreditsPerMonth: 450,
  assistantCreditsPerDay: 80,
  /** Tope de gasto en lempiras al mes. Este número no lo ve el taller. */
  assistantMonthlyCapHnl: 220,
})

/** El plan con el que el taller está guardado, para saber si el selector lo está moviendo. */
const planGuardado = ref<SubscriptionPlan>(SubscriptionPlan.Basic)

const cambiandoDePlan = computed(() => plan.value.plan !== planGuardado.value)

/**
 * Qué va a quedar al guardar. Cambiar de plan reescribe los dos topes con los del plan nuevo
 * —es lo que se espera al subir a alguien a Pro— y eso hay que decirlo antes, no después.
 */
const avisoDelPlan = computed(() => {
  if (!cambiandoDePlan.value) return ''

  const d = PLAN_DEFAULTS[plan.value.plan]
  const tecnicos = d.technicians === null ? 'técnicos sin tope' : `hasta ${d.technicians} técnicos`
  const diag = d.diagnostics ? 'con módulo de diagnóstico' : 'sin módulo de diagnóstico'

  return `Al guardar, el taller queda en ${PLAN_LABEL[plan.value.plan]}: ${tecnicos}, ${diag}.`
})

function aplicarPlan() {
  const sugerida = PLAN_DEFAULTS[plan.value.plan].fee
  const otras = Object.values(PLAN_DEFAULTS).map((d) => d.fee)

  if (!plan.value.monthlyFee || otras.includes(Number(plan.value.monthlyFee))) {
    plan.value.monthlyFee = sugerida
  }
}
const nuevoDueno = ref({ email: '', fullName: '', password: '' })
const creado = ref<CreatedTenant | null>(null)

/**
 * Los usuarios del taller y el cambio de contraseña.
 *
 * Es soporte, no administración: cuando el que llama diciendo «no puedo entrar» es el Dueño,
 * no hay nadie dentro de ese taller que pueda ayudarlo —el Dueño es quien les cambia la
 * contraseña a los demás—, y antes la única salida era la línea de comandos contra la base
 * de producción. La contraseña nueva se enseña una sola vez y no queda guardada.
 */
const usuarios = ref<PlatformUser[]>([])
const clave = ref<{ email: string; password: string } | null>(null)

async function cargarUsuarios() {
  usuarios.value = await platformApi.users(id).catch(() => [])
}

function restablecer(u: PlatformUser) {
  if (
    !confirm(
      `Se le va a poner una contraseña nueva a ${u.fullName} (${u.email}). ` +
        'Se le cierran las sesiones abiertas y tendrá que entrar con la nueva.',
    )
  )
    return

  return accion(async () => {
    clave.value = await platformApi.resetUserPassword(id, u.id)
    await cargarUsuarios()
  }, 'No se pudo cambiar la contraseña.')
}

/** Qué le está pasando al taller ahora mismo, dicho como se lo diríamos por teléfono. */
const situacion = computed(() => {
  const t = tenant.value
  if (!t) return ''
  if (!t.isActive) return 'Suspendido: no puede ni entrar al sistema.'
  if (t.unblockedThrough)
    return `Trabajando por acuerdo de pago hasta el ${formatDay(t.unblockedThrough)}.`
  if (t.state === 'ReadOnly')
    return 'Solo puede consultar: no registra órdenes ni factura hasta ponerse al día.'
  if (t.state === 'Grace')
    return `Vencido, pero trabajando: se le corta el ${formatDay(t.readOnlyOn)}.`
  if (t.state === 'DueSoon')
    return t.daysLeft === 0 ? 'Le vence hoy.' : `Le vencen ${t.daysLeft} días.`
  return 'Al día.'
})

/**
 * Lo que el taller deposita: la mensualidad pelada. El ISV solo se le suma al que pide
 * factura con CAI, y eso se decide en cada cobro, no en el plan.
 */
const deposita = computed(() => tenant.value?.monthlyFee ?? 0)

/** Cuánto se le suma al que pide factura. Cero cuando no cobramos ISV. */
const isvSiFactura = computed(() => {
  const tasa = emisor.value?.defaultTaxRate ?? 0
  if (tasa <= 0 || emisor.value?.pricesIncludeTax) return 0
  return Math.round((tenant.value?.monthlyFee ?? 0) * (tasa / 100) * 100) / 100
})

/** Lo que falta para que venza, dicho corto para el resumen de arriba. */
const restante = computed(() => {
  const t = tenant.value
  if (!t) return ''
  if (!t.isActive) return 'suspendido'
  if (t.unblockedThrough) return `acuerdo hasta el ${formatDay(t.unblockedThrough)}`
  if (!t.paidThrough) return 'sin fecha: nunca se bloquea'
  if (t.daysLeft === null) return ''
  if (t.daysLeft < 0) return 'vencido'
  return t.daysLeft === 0 ? 'vence hoy' : `faltan ${t.daysLeft} días`
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    data.value = await platformApi.get(id)

    const t = data.value.tenant
    if (!montoAMano.value) pago.value.amount = t.monthlyFee * pago.value.months
    ficha.value = {
      name: t.name,
      legalName: t.legalName ?? '',
      taxId: t.taxId ?? '',
      phone: t.phone ?? '',
      email: t.email ?? '',
      address: t.address ?? '',
    }
    plan.value = {
      plan: t.plan,
      monthlyFee: t.monthlyFee,
      paidThrough: t.paidThrough ?? '',
      graceDays: t.graceDays,
      technicianLimit: t.technicianLimit ?? '',
      branchLimit: t.branchLimit ?? '',
      diagnosticsEnabled: t.diagnosticsEnabled,
      assistantEnabled: t.assistantEnabled,
      assemblyEnabled: t.assemblyEnabled,
      assistantCreditsPerMonth: t.assistantCreditsPerMonth,
      assistantCreditsPerDay: t.assistantCreditsPerDay,
      assistantMonthlyCapHnl: t.assistantMonthlyCapHnl,
    }
    planGuardado.value = t.plan
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el taller.')
  } finally {
    loading.value = false
  }
}

async function accion(fn: () => Promise<unknown>, fallo: string, alSalirBien?: () => void) {
  busy.value = true
  error.value = ''
  try {
    await fn()
    await load()
    // Sólo si salió bien: con un error en pantalla, cerrar el formulario esconde lo que hay
    // que corregir.
    alSalirBien?.()
  } catch (e) {
    error.value = errorMessage(e, fallo)
  } finally {
    busy.value = false
  }
}

function revisar() {
  if (!pago.value.amount) return
  revisando.value = true
}

function registrarPago() {
  if (!pago.value.amount) return

  revisando.value = false

  return accion(
    () =>
      platformApi.registerPayment(id, {
        amount: Number(pago.value.amount),
        paidOn: pago.value.paidOn || null,
        method: pago.value.method || null,
        reference: pago.value.reference.trim() || null,
        months: Number(pago.value.months) || 1,
        invoice: pago.value.emitir,
        fiscal: pago.value.emitir && pago.value.conCai,
      }),
    'No se pudo registrar el pago.',
    () => (registrando.value = false),
  )
}

function anularPago(paymentId: string) {
  const motivo = prompt('¿Por qué se anula el pago? Queda escrito en el historial.')
  if (!motivo?.trim()) return

  return accion(
    () => platformApi.voidPayment(id, paymentId, motivo.trim()),
    'No se pudo anular el pago.',
  )
}

function bajarDocumento(invoiceId: string, numero: string) {
  return platformApi.invoicePdf(invoiceId, numero)
}

/**
 * Le emite el documento a un pago que se registró sin él: el taller no lo pidió entonces y
 * después sí. Consume un número igual que cualquier otra emisión, así que se pregunta antes
 * y se dice cuál va a ser.
 */
function emitirDelPago(paymentId: string) {
  const conCai = !sinCai.value && confirm(
    `¿Con CAI? Consume el número ${emisor.value?.fiscalRange?.nextFiscalNumber ?? ''} del rango `
      + 'autorizado.\n\nCancelar emite un comprobante con numeración propia.',
  )

  return accion(
    () => platformApi.issuePaymentInvoice(id, paymentId, { fiscal: conCai }),
    'No se pudo emitir el documento.',
  )
}

function anularDocumento(invoiceId: string) {
  const motivo = prompt('¿Por qué se anula? Queda escrito en el documento.')
  if (!motivo?.trim()) return

  return accion(
    () => platformApi.voidInvoice(invoiceId, motivo.trim()),
    'No se pudo anular el documento.',
  )
}

function darAcuerdo() {
  if (!acuerdo.value.unblockedThrough) return

  return accion(
    () =>
      platformApi.setAgreement(id, {
        unblockedThrough: acuerdo.value.unblockedThrough,
        note: acuerdo.value.note.trim() || null,
      }),
    'No se pudo registrar el acuerdo.',
  )
}

function quitarAcuerdo() {
  return accion(() => platformApi.clearAgreement(id), 'No se pudo quitar el acuerdo.')
}

/** Sin cambios el botón no hace nada, y decirlo apagado evita el clic de más. */
const fichaCambiada = computed(() => {
  const t = tenant.value
  if (!t) return false

  const f = ficha.value
  return (
    f.name.trim() !== t.name ||
    f.legalName.trim() !== (t.legalName ?? '') ||
    f.taxId.trim() !== (t.taxId ?? '') ||
    f.phone.trim() !== (t.phone ?? '') ||
    f.email.trim() !== (t.email ?? '') ||
    f.address.trim() !== (t.address ?? '')
  )
})

function guardarFicha() {
  if (!ficha.value.name.trim()) return

  return accion(
    () =>
      platformApi.updateTenant(id, {
        name: ficha.value.name.trim(),
        legalName: ficha.value.legalName.trim() || null,
        taxId: ficha.value.taxId.trim() || null,
        phone: ficha.value.phone.trim() || null,
        email: ficha.value.email.trim() || null,
        address: ficha.value.address.trim() || null,
      }),
    'No se pudieron guardar los datos del taller.',
    () => (editandoFicha.value = false),
  )
}

function guardarPlan() {
  return accion(
    () =>
      platformApi.updateSubscription(id, {
        plan: plan.value.plan,
        monthlyFee: Number(plan.value.monthlyFee) || 0,
        paidThrough: plan.value.paidThrough || null,
        graceDays: Number(plan.value.graceDays) || 0,
        // Vacío es sin tope. Si el plan cambió, el servidor los reescribe con los del plan
        // nuevo y estos dos se ignoran: lo dice el aviso de arriba del botón.
        technicianLimit: plan.value.technicianLimit === '' ? null : Number(plan.value.technicianLimit),
        branchLimit: plan.value.branchLimit === '' ? null : Number(plan.value.branchLimit),
        diagnosticsEnabled: plan.value.diagnosticsEnabled,
        assistantEnabled: plan.value.assistantEnabled,
        assemblyEnabled: plan.value.assemblyEnabled,
        assistantCreditsPerMonth: Number(plan.value.assistantCreditsPerMonth) || 0,
        assistantCreditsPerDay: Number(plan.value.assistantCreditsPerDay) || 0,
        assistantMonthlyCapHnl: Number(plan.value.assistantMonthlyCapHnl) || 0,
      }),
    'No se pudo guardar el plan.',
    () => (editandoPlan.value = false),
  )
}

function crearDueno() {
  const f = nuevoDueno.value
  if (!f.email.trim() || !f.fullName.trim()) return

  return accion(async () => {
    creado.value = await platformApi.createOwner(id, {
      email: f.email.trim(),
      fullName: f.fullName.trim(),
      password: f.password.trim() || null,
    })
    nuevoDueno.value = { email: '', fullName: '', password: '' }
  }, 'No se pudo crear el Dueño.')
}

/**
 * Corrige con qué trabaja el taller. Es para el error de dedo del día del alta: cambiarlo con
 * órdenes abiertas deja fichas de vehículo —o de equipo— que ya no se pueden usar.
 */
function cambiarRubro(worksWith: WorkshopKind) {
  if (worksWith === tenant.value?.worksWith) return

  const texto = worksWith === WorkshopKind.Equipment ? 'equipos' : 'vehículos'
  if (!confirm(`El taller pasará a trabajar con ${texto}. ¿Seguro?`)) return

  return accion(() => platformApi.setWorksWith(id, worksWith), 'No se pudo cambiar el rubro.')
}

function suspender() {
  if (!confirm('El taller dejará de poder entrar al sistema. ¿Suspenderlo?')) return

  return accion(() => platformApi.suspend(id), 'No se pudo suspender.')
}

function reactivar() {
  return accion(() => platformApi.reactivate(id), 'No se pudo reactivar.')
}

onMounted(async () => {
  // El emisor primero: de él sale el ISV con que se propone el monto del pago.
  emisor.value = await platformApi.billingSettings().catch(() => null)
  await load()
  await cargarUsuarios()

  // Se entró desde «Registrar pago» de la lista: el formulario ya abierto, que es a lo que
  // venía. Un clic menos en lo único que se hace todos los meses.
  if (route.query.cobrar) {
    pestana.value = 'cobros'
    registrando.value = true
  }
})
</script>

<template>
  <section v-if="tenant">
    <header class="top">
      <div>
        <RouterLink :to="{ name: 'platform-tenants' }" class="volver">← Talleres</RouterLink>
        <div class="titulo">
          <h1>{{ tenant.name }}</h1>
          <span class="chip" :class="tenant.state.toLowerCase()">{{ etiqueta }}</span>
        </div>

        <!-- Quién es como cliente y de qué tamaño es, en dos renglones: es lo que hay que
             tener delante antes de hablarle de plan o de emitirle la factura. -->
        <p class="muted small">
          {{ PLAN_LABEL[tenant.plan] ?? tenant.planName ?? 'Sin plan' }} ·
          {{ formatMoney(tenant.monthlyFee) }} al mes ·
          {{ tenant.branchCount }} {{ tenant.branchCount === 1 ? 'sucursal' : 'sucursales' }} ·
          {{ tenant.technicianCount ?? '—' }}
          {{ tenant.technicianCount === 1 ? 'técnico' : 'técnicos' }} ·
          cliente desde {{ formatDay(tenant.createdAt) }}
        </p>
        <p class="muted small">{{ contacto }}</p>
      </div>
      <button v-if="!tenant.isActive" type="button" @click="reactivar">Reactivar</button>
    </header>

    <ErrorNote v-if="error" :message="error" />

    <!-- Dónde está parado el taller en su ciclo de cobro, dibujado. «Vencido hace 19 días» no
         dice si todavía puede facturar; los tres tramos sí, y de un vistazo. -->
    <section v-if="tenant.paidThrough" class="linea" :class="tenant.state.toLowerCase()">
      <div v-if="tenant.state !== 'Active' && tenant.state !== 'DueSoon'" class="linea-dice">
        <strong>{{ situacion }}</strong>
        <p v-if="tenant.state === 'ReadOnly'" class="muted small">
          No registra órdenes ni factura hasta que pague {{ formatMoney(deposita) }}. Se
          desbloquea al registrar el pago o al darle un acuerdo.
        </p>
      </div>

      <ol class="tramos">
        <li class="tramo ok">
          <i></i>
          <span class="muted small">Pagado hasta</span>
          <strong>{{ formatDay(tenant.paidThrough) }}</strong>
        </li>
        <li class="tramo aviso">
          <i></i>
          <span class="muted small">{{ tenant.graceDays }} días de gracia</span>
          <strong>{{ gracia }}</strong>
        </li>
        <li class="tramo cortado">
          <i></i>
          <span class="muted small">Solo lectura</span>
          <strong>{{ tenant.readOnlyOn ? `desde ${formatDay(tenant.readOnlyOn)}` : '—' }}</strong>
        </li>
      </ol>
    </section>

    <!-- La cuenta de un vistazo. Antes había que leer los dos formularios de abajo para saber
         qué plan tiene, cuánto deposita y hasta cuándo está cubierto. -->
    <div class="cifras">
      <article class="cifra">
        <span class="rotulo">Plan</span>
        <strong>{{ PLAN_LABEL[tenant.plan] ?? tenant.planName ?? 'Sin plan' }}</strong>
        <span class="muted small">
          {{ tenant.branchCount }} {{ tenant.branchCount === 1 ? 'sucursal' : 'sucursales' }} ·
          {{ tenant.technicianCount }}{{ tenant.technicianLimit === null ? '' : ` de ${tenant.technicianLimit}` }}
          {{ tenant.technicianCount === 1 ? 'técnico' : 'técnicos' }}
        </span>
      </article>

      <article class="cifra">
        <span class="rotulo">Deposita al mes</span>
        <strong class="num">{{ formatMoney(deposita) }}</strong>
        <span class="muted small">
          {{ isvSiFactura > 0 ? `+ ISV ${formatMoney(isvSiFactura)} si pide factura` : 'Sin ISV' }}
        </span>
      </article>

      <article class="cifra" :class="{ alerta: tenant.state !== 'Active' }">
        <span class="rotulo">Cubierto hasta</span>
        <strong>{{ formatDay(tenant.paidThrough) }}</strong>
        <span class="muted small">{{ restante }}</span>
      </article>

      <article class="cifra">
        <span class="rotulo">Último pago</span>
        <strong>{{ formatDay(tenant.lastPaymentOn) }}</strong>
        <span class="muted small">
          {{ data?.payments.length ?? 0 }}
          {{ (data?.payments.length ?? 0) === 1 ? 'mensualidad registrada' : 'mensualidades registradas' }}
        </span>
      </article>
    </div>

    <!-- Cada cosa en su pestaña: la ficha tenía cinco bloques compitiendo por la misma
         pantalla, y a la que se entra es a cobrar. -->
    <nav class="tabs">
      <button
        v-for="t in [
          { clave: 'cobros' as const, label: 'Cobros' },
          { clave: 'plan' as const, label: 'Plan y módulos' },
          { clave: 'factura' as const, label: 'Datos para la factura' },
          { clave: 'usuarios' as const, label: 'Usuarios' },
          { clave: 'repara' as const, label: 'Qué repara' },
        ]"
        :key="t.clave"
        type="button"
        class="tab"
        :class="{ activa: pestana === t.clave }"
        @click="pestana = t.clave"
      >
        {{ t.label }}
        <span v-if="t.clave === 'usuarios' && usuarios.length" class="muted">
          {{ usuarios.length }}
        </span>
      </button>
    </nav>

    <div v-show="pestana === 'cobros'">
      <!-- Una sola acción grande, y la ficha como ficha. Un formulario siempre abierto le dice a
           quien entra «llená esto», cuando lo que viene a hacer casi siempre es mirar cómo va el
           taller o anotarle el pago del mes. -->
      <div class="accion">
        <button type="button" class="grande" @click="abrirElPago">
          {{ registrando ? 'Cancelar' : 'Registrar pago' }}
        </button>
      </div>


      <form v-if="registrando" class="card" @submit.prevent="revisar">

        <p class="muted small">Elija cuántos meses paga; el monto se calcula solo.</p>

        <!-- Meses, no monto: el monto es la cuota por los meses, y escribirlo a mano era la
             forma de equivocarse en el ISV o en un cero. -->
        <fieldset class="meses">
          <legend>Meses que paga</legend>
          <button
            v-for="m in [1, 2, 3, 6, 12]"
            :key="m"
            type="button"
            class="mes"
            :class="{ activo: pago.months === m }"
            @click="elegirMeses(m)"
          >
            {{ m }}
          </button>
        </fieldset>

        <div class="cuenta">
          <div>
            <span class="muted small">Debe depositar</span>
            <strong class="num grande">{{ formatMoney(documento.total) }}</strong>
            <span v-if="documento.impuesto > 0" class="muted small num">
              {{ formatMoney(documento.neto) }} + ISV {{ emisor?.defaultTaxRate }}%
              {{ formatMoney(documento.impuesto) }}
            </span>
            <!-- Sin factura con CAI no hay impuesto que cobrar: lo que entra es el precio. -->
            <span v-else class="muted small">Sin ISV: no lleva factura con CAI</span>
          </div>

          <div class="cubre">
            <span class="muted small">Queda cubierto</span>
            <strong class="num">{{ cubre }}</strong>
            <span class="muted small">Cuenta desde que venció, no desde hoy</span>
          </div>
        </div>

        <!-- La escapatoria, no la regla: el que negoció otro número lo escribe, y entonces el
             monto deja de seguir a los meses. -->
        <button v-if="!montoAMano" type="button" class="link" @click="montoAMano = true">
          Recibió otro monto
        </button>
        <label v-else class="corta">
          Monto recibido
          <input v-model.number="pago.amount" type="number" min="0" step="0.01" required />
        </label>

        <div class="row">
          <label>
            Fecha del depósito
            <input v-model="pago.paidOn" type="date" />
          </label>
          <label>
            Forma de pago
            <select v-model="pago.method">
              <option>Transferencia</option>
              <option>Depósito</option>
              <option>Efectivo</option>
            </select>
          </label>
          <label>
            N.º de referencia
            <input v-model="pago.reference" placeholder="Ej. 4471203" />
          </label>
        </div>

        <!-- El documento nace con el pago, así que lo que va a decir se ve aquí, antes de
             registrarlo. Después solo se puede anular. -->
        <!-- Una pregunta con tres respuestas: son excluyentes, y cada una trae el número
             que va a salir, que es lo que de verdad se quiere ver antes de confirmar. -->
        <fieldset class="entrega">
          <legend>Qué se le entrega</legend>

          <label>
            <input v-model="entrega" type="radio" value="comprobante" />
            <span>
              <strong>Comprobante <span class="mono">{{ numeroPropio }}</span></strong>
              <small class="muted">Numeración propia, sin valor fiscal.</small>
            </span>
          </label>

          <label :class="{ apagado: sinCai }">
            <input v-model="entrega" type="radio" value="factura" :disabled="sinCai" />
            <span>
              <strong>
                Factura con CAI
                <span v-if="!sinCai" class="mono">{{ numeroFiscal }}</span>
              </strong>
              <small v-if="sinCai" class="muted">
                No hay CAI vigente.
                <RouterLink :to="{ name: 'platform-billing' }">Registrarlo</RouterLink>
              </small>
              <small v-else class="muted">
                Quema un número del rango autorizado, que no se devuelve. Solo si la pide.
              </small>
            </span>
          </label>

          <label>
            <input v-model="entrega" type="radio" value="ninguno" />
            <span>
              <strong>Nada, solo registrar el cobro</strong>
              <small class="muted">Se le puede emitir después desde el historial.</small>
            </span>
          </label>
        </fieldset>

        <div class="actions">
          <button type="submit" :disabled="busy">
            Registrar {{ formatMoney(documento.total) }}
          </button>
        </div>
      </form>


      <article v-if="revisando" class="card revision">
        <h2>Revise antes de registrar</h2>

        <dl>
          <dt>Taller</dt>
          <dd>{{ tenant.name }}<span v-if="tenant.taxId"> · RTN {{ tenant.taxId }}</span></dd>

          <dt>Monto recibido</dt>
          <dd class="num">{{ formatMoney(documento.total) }}</dd>

          <template v-if="pago.emitir">
            <dt>Documento</dt>
            <dd>
              {{ pago.conCai && !sinCai ? 'Factura con CAI' : 'Comprobante sin CAI' }}
              <strong class="mono">{{ numeroQueSale }}</strong>
            </dd>

            <template v-if="documento.impuesto > 0">
              <dt>Neto</dt>
              <dd class="num">{{ formatMoney(documento.neto) }}</dd>
              <dt>ISV {{ emisor?.defaultTaxRate }}%</dt>
              <dd class="num">{{ formatMoney(documento.impuesto) }}</dd>
            </template>
          </template>
          <template v-else>
            <dt>Documento</dt>
            <dd>No se emite ninguno.</dd>
          </template>

          <dt>Queda cubierto hasta</dt>
          <dd>{{ formatDay(cubreHasta) }}</dd>
        </dl>

        <!-- Sin RTN la factura sale a consumidor final, y entonces al taller no le sirve para
             deducirla. Mejor enterarse ahora que cuando la lleve a su contador. -->
        <p v-if="pago.emitir && pago.conCai && !sinCai && !tenant.taxId" class="aviso">
          Este taller no tiene RTN registrado: la factura va a salir a consumidor final.
          Agrégueselo en el plan antes de emitir si lo va a deducir.
        </p>

        <p v-if="pago.conCai && !sinCai" class="aviso">
          Consume el número {{ numeroQueSale }} del rango autorizado. Un número emitido no se
          devuelve: si se registra por error, el documento se anula pero el número ya se usó.
        </p>

        <div class="actions">
          <button type="button" :disabled="busy" @click="registrarPago">
            Confirmar y registrar
          </button>
          <button type="button" class="link" @click="revisando = false">Corregir</button>
        </div>
      </article>



      <h2 class="historial">Pagos</h2>

      <p v-if="!data?.payments.length" class="muted">Todavía no ha pagado ninguna mensualidad.</p>

      <table v-else>
        <thead>
          <tr>
            <th scope="col">Pagó el</th>
            <th scope="col" class="num">Monto</th>
            <th scope="col">Forma</th>
            <th scope="col">Referencia</th>
            <th scope="col">Cubrió hasta</th>
            <th scope="col">Documento</th>
            <th scope="col"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="p in data?.payments" :key="p.id" :class="{ anulada: p.isVoided }">
            <td>
              {{ formatDay(p.paidOn) }}
              <div v-if="p.isVoided" class="muted small">Anulado: {{ p.voidReason }}</div>
            </td>
            <td class="num">{{ formatMoney(p.amount) }}</td>
            <td>{{ p.method ?? '—' }}</td>
            <td class="mono">{{ p.reference ?? '—' }}</td>
            <td>{{ formatDay(p.coversThrough) }}</td>
            <td>
              <template v-if="p.invoiceId && p.invoiceNumber">
                <button
                  type="button"
                  class="link"
                  @click="bajarDocumento(p.invoiceId, p.invoiceNumber)"
                >
                  {{ p.invoiceNumber }}
                </button>
                <span v-if="!p.invoiceIsFiscal" class="muted small"> · sin CAI</span>
                <span v-if="p.invoiceIsVoided" class="anulado"> · anulado</span>
                <button
                  v-else
                  type="button"
                  class="link danger"
                  :disabled="busy"
                  @click="anularDocumento(p.invoiceId)"
                >
                  Anular documento
                </button>
              </template>
              <!-- El pago que se registró sin documento: el taller no lo pidió entonces y
                   después sí. Se puede emitir desde acá, con su número. -->
              <button
                v-else-if="!p.isVoided"
                type="button"
                class="link"
                :disabled="busy"
                @click="emitirDelPago(p.id)"
              >
                Emitir
              </button>
              <span v-else class="muted">—</span>
            </td>
            <td>
              <!-- El pago registrado por error: se anula, el vencimiento vuelve a donde estaba
                   y el documento se va con él. No se borra, para que el historial siga
                   contestando «¿yo ya te pagué junio?». -->
              <button
                v-if="!p.isVoided"
                type="button"
                class="link danger"
                :disabled="busy"
                @click="anularPago(p.id)"
              >
                Anular pago
              </button>
            </td>
          </tr>
        </tbody>
      </table>

      <!-- Abierto solo cuando hace falta: con el taller al día, un formulario de acuerdo de pago
           es una tarjeta pidiendo que se le haga caso sin motivo. -->
      <details class="ajuste" :open="tenant.state !== 'Active' || !!tenant.unblockedThrough">
        <summary>
          Acuerdo de pago
          <span v-if="tenant.unblockedThrough" class="muted small">
            · vigente hasta el {{ formatDay(tenant.unblockedThrough) }}
          </span>
        </summary>
          <form class="card" @submit.prevent="darAcuerdo">
            <h2>Acuerdo de pago</h2>

            <p v-if="tenant.unblockedThrough" class="vigente">
              Vigente hasta el {{ formatDay(tenant.unblockedThrough) }}.
              <span v-if="tenant.unblockNote" class="muted">{{ tenant.unblockNote }}</span>
            </p>

            <label>
              Desbloqueado hasta
              <input v-model="acuerdo.unblockedThrough" type="date" required />
            </label>
            <label>
              Motivo
              <input v-model="acuerdo.note" placeholder="Paga el 30, habló con él el 15" />
            </label>

            <div class="actions">
              <button type="submit" :disabled="busy">Dar acuerdo</button>
              <button
                v-if="tenant.unblockedThrough"
                type="button"
                class="link"
                :disabled="busy"
                @click="quitarAcuerdo"
              >
                Quitarlo
              </button>
            </div>

            <p class="muted small">
              El taller sigue trabajando aunque deba. Se cancela solo al registrar el pago.
            </p>
          </form>
      </details>
    </div>

    <div v-show="pestana === 'plan'">

      <section class="bloque">
        <header class="bloque-top">
          <h2>Plan y vencimiento</h2>
          <button type="button" class="link" @click="editandoPlan = !editandoPlan">
            {{ editandoPlan ? 'Cancelar' : 'Editar' }}
          </button>
        </header>

        <dl v-if="!editandoPlan" class="lectura">
          <dt>Plan</dt>
          <dd>{{ PLAN_LABEL[tenant.plan] ?? tenant.planName ?? 'Sin plan' }}</dd>

          <dt>Cuota</dt>
          <dd>{{ formatMoney(tenant.monthlyFee) }} al mes</dd>

          <dt>Pagado hasta</dt>
          <dd>
            {{ formatDay(tenant.paidThrough) }}
            <span class="muted">· {{ tenant.graceDays }} días de gracia</span>
          </dd>

          <dt>Topes</dt>
          <dd>
            {{ tenant.technicianLimit === null ? 'Técnicos sin tope' : `Hasta ${tenant.technicianLimit} técnicos` }}
            ·
            {{ tenant.branchLimit === null ? 'sucursales sin tope' : `hasta ${tenant.branchLimit} sucursales` }}
          </dd>

          <dt>Módulos</dt>
          <dd>
            {{ tenant.diagnosticsEnabled ? 'Diagnóstico con escáner' : 'Sin diagnóstico' }}
            ·
            {{ tenant.assistantEnabled ? `Asistente (${tenant.assistantCreditsPerMonth} al mes)` : 'Sin asistente' }}
          </dd>

          <dt>Qué repara</dt>
          <dd>{{ tenant.worksWith === WorkshopKind.Equipment ? 'Equipos' : 'Vehículos y motos' }}</dd>
        </dl>
        <form v-else class="card" @submit.prevent="guardarPlan">

          <div class="row">
            <label>
              Plan
              <!-- Seleccionable: de aquí salen el tope de técnicos y el diagnóstico. -->
              <select v-model.number="plan.plan" @change="aplicarPlan">
                <option :value="SubscriptionPlan.Basic">Básico</option>
                <option :value="SubscriptionPlan.Pro">Pro — con diagnóstico</option>
                <option :value="SubscriptionPlan.Legacy">Heredado — sin topes</option>
              </select>
            </label>
            <label>
              Cuota mensual
              <input v-model.number="plan.monthlyFee" type="number" min="0" step="0.01" />
            </label>
          </div>

          <!-- Lo que de verdad rige para este taller, que no tiene por qué ser lo del plan:
               el que paga por un técnico de más sube a cuatro sin dejar de ser Básico. -->
          <label class="corta">
            Tope de técnicos
            <input
              v-model="plan.technicianLimit"
              type="number"
              min="1"
              max="500"
              placeholder="Sin tope"
              :disabled="cambiandoDePlan"
            />
          </label>

          <!-- La ayuda va pegada a su campo. Suelta al final de la tarjeta parecía explicar
               lo último que se leyó, que era otra cosa. -->
          <p class="muted small">
            <template v-if="tenant">
              Hoy tiene <strong>{{ tenant.technicianCount }}</strong>
              {{ tenant.technicianCount === 1 ? 'técnico activo' : 'técnicos activos' }}.
            </template>
            Déjelo en blanco para que no tenga tope. Del cuarto técnico en adelante se cobra
            aparte: súbale el número aquí sin moverlo de plan.
          </p>

          <label class="corta">
            Tope de sucursales
            <input
              v-model="plan.branchLimit"
              type="number"
              min="1"
              max="500"
              placeholder="Sin tope"
              :disabled="cambiandoDePlan"
            />
          </label>

          <p class="muted small">
            <template v-if="tenant">
              Hoy tiene <strong>{{ tenant.branchCount }}</strong>
              {{ tenant.branchCount === 1 ? 'sucursal' : 'sucursales' }}.
            </template>
            La cuota del Básico es por una. El taller que abre la segunda paga por ella: súbale
            el número aquí. Cuenta solo las activas, así que cerrar un local y abrir otro no
            pide permiso.
          </p>

          <!-- Los dos módulos que se venden aparte, juntos y con su casilla a la izquierda del
               texto. El asistente lleva interruptor propio: con uno solo, apagárselo a un
               taller le apagaría también el escáner que ya está usando dentro de las órdenes. -->
          <fieldset class="modulos" :disabled="cambiandoDePlan">
            <legend>Módulos</legend>

            <label class="checkbox">
              <input v-model="plan.diagnosticsEnabled" type="checkbox" />
              <span>
                Diagnóstico con escáner
                <em>El informe en la orden y la lectura de códigos con el adaptador.</em>
              </span>
            </label>

            <label class="checkbox">
              <input v-model="plan.assistantEnabled" type="checkbox" />
              <span>
                Asistente
                <em>Propone los pasos y los repuestos a partir del diagnóstico.</em>
              </span>
            </label>

            <div v-if="plan.assistantEnabled" class="creditos">
              <div class="row">
                <label>
                  Créditos al mes
                  <input v-model="plan.assistantCreditsPerMonth" type="number" min="0" max="20000" />
                </label>
                <label>
                  Créditos al día
                  <input v-model="plan.assistantCreditsPerDay" type="number" min="0" max="2000" />
                </label>
                <label>
                  Tope de gasto, L al mes
                  <input v-model="plan.assistantMonthlyCapHnl" type="number" min="0" max="50000" />
                </label>
              </div>

              <p class="muted small">
                Una propuesta de pasos vale 1 crédito y una consulta del técnico 5; la lectura
                del negocio no gasta.
                <template v-if="tenant">
                  Este mes lleva gastados
                  <strong>L {{ tenant.assistantSpentHnl.toFixed(2) }}</strong>
                  de los {{ plan.assistantMonthlyCapHnl }} del tope.
                </template>
                El tope de gasto corta aunque le queden créditos, y el taller no lo ve.
              </p>
            </div>
          </fieldset>

          <!-- Fuera del fieldset de arriba a propósito: ese se deshabilita al cambiar de plan
               porque los módulos se reescriben desde el plan. Este no sale de ningún plan, así
               que ni se enciende al subir a Pro ni se apaga al bajar a Básico. -->
          <fieldset class="modulos">
            <legend>Por contrato</legend>

            <label class="checkbox">
              <input v-model="plan.assemblyEnabled" type="checkbox" />
              <span>
                Armado de motos
                <em>
                  Registrar cada moto armada en la bodega, y los reportes de facturación al
                  contratista y de pago a los técnicos. No va con ningún plan.
                </em>
              </span>
            </label>
          </fieldset>

          <p v-if="avisoDelPlan" class="aviso-plan">{{ avisoDelPlan }}</p>

          <div class="row">
            <label>
              Pagado hasta
              <input v-model="plan.paidThrough" type="date" />
            </label>
            <label>
              Días de gracia
              <input v-model.number="plan.graceDays" type="number" min="0" max="60" />
            </label>
          </div>

          <div class="actions">
            <button type="submit" :disabled="busy">Guardar</button>
          </div>

          <p class="muted small">
            Sin fecha de «pagado hasta», el taller nunca se bloquea.
          </p>
        </form>
      </section>
    </div>

    <div v-show="pestana === 'factura'">

      <section class="bloque">
        <header class="bloque-top">
          <h2>Datos del taller</h2>
          <button type="button" class="link" @click="editandoFicha = !editandoFicha">
            {{ editandoFicha ? 'Cancelar' : 'Editar' }}
          </button>
        </header>

        <dl v-if="!editandoFicha" class="lectura">
          <dt>Razón social</dt>
          <dd>{{ tenant.legalName ?? '—' }}</dd>

          <dt>RTN</dt>
          <dd>
            <template v-if="tenant.taxId"><span class="mono">{{ tenant.taxId }}</span></template>
            <span v-else class="falta">Sin RTN: su factura sale a consumidor final</span>
          </dd>

          <dt>Teléfono</dt>
          <dd>{{ tenant.phone ?? '—' }}</dd>

          <dt>Correo</dt>
          <dd>{{ tenant.email ?? '—' }}</dd>

          <dt>Dirección</dt>
          <dd>{{ tenant.address ?? '—' }}</dd>
        </dl>
        <form v-else class="card" @submit.prevent="guardarFicha">
          <p class="muted small">
            Es lo que sale impreso en la factura de su mensualidad. El Dueño edita la ficha de
            su taller desde su panel; esto es para corregirla de este lado.
          </p>

          <label>
            Nombre
            <input v-model="ficha.name" required />
          </label>
          <label>
            Razón social
            <input v-model="ficha.legalName" placeholder="Como está en el RTN" />
          </label>
          <label>
            RTN
            <input v-model="ficha.taxId" placeholder="0801…" />
            <small v-if="!ficha.taxId.trim()" class="muted">
              Sin RTN, su factura sale a consumidor final.
            </small>
          </label>

          <div class="row">
            <label>
              Teléfono
              <input v-model="ficha.phone" placeholder="9888-1111" />
            </label>
            <label>
              Correo
              <input v-model="ficha.email" type="email" placeholder="taller@correo.hn" />
            </label>
          </div>

          <label>
            Dirección
            <input v-model="ficha.address" />
          </label>

          <div class="actions">
            <button type="submit" :disabled="busy || !fichaCambiada">Guardar</button>
          </div>
        </form>
      </section>
    </div>

    <div v-show="pestana === 'usuarios'">

      <details class="ajuste" open>
        <summary>Usuarios del taller</summary>
        <!-- Se enseña una sola vez, y acá: la contraseña nueva sale donde se pidió, no al final
             de la pantalla, donde se podía cerrar sin verla. -->
        <article v-if="clave" class="card credenciales">
          <h2>Contraseña nueva</h2>
          <p class="muted small">Dictésela y pídale que la cambie. No vuelve a verse.</p>
          <p class="mono">{{ clave.email }}</p>
          <p class="mono">{{ clave.password }}</p>
          <button type="button" class="link" @click="clave = null">Ya la anoté</button>
        </article>

        <div class="card">
          <p class="muted small">
            Con quién se está hablando cuando el taller llama. Cambiarle la contraseña a alguien
            es el último recurso: lo normal es que el Dueño se la cambie a su gente desde su
            propio panel. Hace falta cuando el que no puede entrar es el Dueño, porque entonces
            no hay nadie dentro del taller que pueda ayudarlo.
          </p>

          <p v-if="!usuarios.length" class="muted">Este taller no tiene usuarios.</p>

          <table v-else>
            <thead>
              <tr>
                <th scope="col">Nombre</th>
                <th scope="col">Correo</th>
                <th scope="col">Perfil</th>
                <th scope="col">Última entrada</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="u in usuarios" :key="u.id">
                <td>
                  {{ u.fullName }}
                  <span v-if="!u.isActive" class="muted small">· dado de baja</span>
                  <span v-if="u.lockedOut" class="muted small">· bloqueado por intentos</span>
                </td>
                <td class="mono">{{ u.email }}</td>
                <td>{{ u.role }}</td>
                <td class="muted small">
                  {{ u.lastLoginAt ? formatDay(u.lastLoginAt) : 'nunca' }}
                </td>
                <td>
                  <button type="button" class="link" :disabled="busy" @click="restablecer(u)">
                    Contraseña nueva
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </details>


      <details class="ajuste" open>
        <summary>Crear otro acceso de Dueño</summary>
        <article v-if="creado" class="card credenciales">
          <h2>Acceso creado</h2>
          <p>Entrégueselas ahora. <strong>No se pueden volver a consultar.</strong></p>
          <p class="mono">{{ creado.ownerEmail }}</p>
          <p class="mono">{{ creado.password }}</p>
          <button type="button" class="link" @click="creado = null">Ya las anoté</button>
        </article>

        <form class="card" @submit.prevent="crearDueno">
          <p class="muted small">
            Para cuando el taller se quedó sin nadie que pueda entrar: perdió la contraseña, borró
            su cuenta desde la app o se fue del negocio. Sus datos siguen intactos; lo que falta es
            la puerta. El Dueño que ya exista no se toca.
          </p>

          <div class="row">
            <label>
              Correo
              <input v-model="nuevoDueno.email" type="email" placeholder="dueno@taller.hn" required />
            </label>
            <label>
              Nombre
              <input v-model="nuevoDueno.fullName" placeholder="Juan Martínez" required />
            </label>
            <label>
              Contraseña
              <input v-model="nuevoDueno.password" type="text" placeholder="Se genera si va vacía" />
            </label>
          </div>

          <div class="actions">
            <button type="submit" :disabled="busy">Crear acceso</button>
          </div>
        </form>
      </details>
    </div>

    <div v-show="pestana === 'repara'">

      <details class="ajuste" open>
        <summary>
          Qué repara
          <span class="muted small">
            · {{ tenant.worksWith === WorkshopKind.Equipment ? 'Equipos' : 'Vehículos y motos' }}
          </span>
        </summary>
          <article class="card">
            <h2>Qué repara</h2>
            <p class="muted small">
              Decide qué se le puede dar de alta y cómo se llama en sus pantallas. Se fija al
              crear el taller; esto es para corregirlo, no para cambiarle el rubro andando.
            </p>

            <div class="row">
              <label>
                <select
                  :value="tenant.worksWith"
                  :disabled="busy"
                  @change="cambiarRubro(Number(($event.target as HTMLSelectElement).value) as WorkshopKind)"
                >
                  <option :value="WorkshopKind.Vehicles">Vehículos y motos</option>
                  <option :value="WorkshopKind.Equipment">Equipos (cómputo, celulares, aires)</option>
                </select>
              </label>
            </div>
          </article>
      </details>
    </div>

    <!-- Suspender no es el castigo por no pagar: para eso está el bloqueo automático, que se
         levanta solo al cobrar. Esto es para el que dejó de ser cliente. -->
    <section v-if="tenant.isActive" class="peligro">
      <div>
        <strong>Suspender taller</strong>
        <p class="muted small">
          Nadie del taller podrá entrar y se cierran sus sesiones. Sus datos quedan intactos.
          Úselo solo si dejó de ser cliente; para un atraso basta el bloqueo automático.
        </p>
      </div>
      <button type="button" class="btn-danger" :disabled="busy" @click="suspender">
        Suspender…
      </button>
    </section>


  </section>

  <SkeletonList v-else-if="loading" :rows="4" variant="tabla" />
  <p v-else class="error">{{ error }}</p>
</template>

<style scoped>
/* El aviso de que el plan va a reescribir los topes: se lee justo antes de tocar Guardar. */
.aviso-plan {
  margin: 0;
  padding: 0.5rem 0.75rem;
  border-left: 3px solid var(--accent);
  background: var(--surface-alt);
  border-radius: var(--radius-sm);
  font-size: 0.875rem;
}

h1 {
  margin: 0.25rem 0 0;
}

.volver {
  font-size: 0.875rem;
}

.top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.situacion {
  margin: 0 0 1rem;
  padding: 0.625rem 0.875rem;
  border-left: 3px solid currentcolor;
  border-radius: var(--radius-sm);
  background: var(--surface-alt);
  font-size: 0.9375rem;
}

.situacion.grace,
.situacion.duesoon {
  color: var(--warning-text);
  background: color-mix(in srgb, var(--warning) 12%, transparent);
}

.situacion.readonly,
.situacion.suspended {
  color: var(--danger);
  background: color-mix(in srgb, var(--danger) 12%, transparent);
}

.card {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.card h2 {
  margin: 0 0 0.75rem;
  font-size: 1rem;
}

.row {
  display: flex;
  gap: 0.75rem;
}

/* Sin `min-width: 0` el ancho mínimo del input desborda la tarjeta en la columna angosta. */
.row label {
  flex: 1;
  min-width: 0;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  margin-bottom: 0.5rem;
  font-size: 0.875rem;
}

/* Una casilla no es un campo con etiqueta encima: va al lado del texto. Sin esto heredaba
   el `column` de arriba y salía flotando sobre su propia etiqueta. */
label.checkbox {
  flex-direction: row;
  align-items: flex-start;
  gap: 0.5rem;
}

label.checkbox input {
  margin-top: 0.15rem;
  width: auto;
  flex: none;
}

label.checkbox span {
  display: flex;
  flex-direction: column;
}

label.checkbox em {
  font-style: normal;
  font-size: 0.8rem;
  color: var(--text-muted);
}

/* El campo de un solo dato no se estira a lo ancho de la tarjeta: se lee como si esperara
   una frase. */
.corta {
  max-width: 14rem;
}

.modulos {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 0.75rem;
  margin: 0.25rem 0 0.75rem;
}

.modulos legend {
  padding: 0 0.4rem;
  font-size: 0.8rem;
  color: var(--text-muted);
}

.modulos[disabled] {
  opacity: 0.55;
}

/* Sangrados bajo su casilla: son ajustes del asistente, no otra cosa de la tarjeta. */
.creditos {
  margin: 0.25rem 0 0 1.75rem;
}

.actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-top: 0.25rem;
}

.vigente {
  margin: 0 0 0.75rem;
  padding: 0.5rem 0.625rem;
  border-radius: var(--radius-sm);
  background: var(--surface-alt);
  font-size: 0.875rem;
}

.credenciales {
  border-color: var(--success);
  margin-top: 1rem;
}

.mono {
  font-family: var(--font-mono);
  margin: 0.25rem 0;
}

.historial {
  margin: 1.5rem 0 0.75rem;
  font-size: 1rem;
}

table {
  width: 100%;
  border-collapse: collapse;
}

th,
td {
  padding: 0.5rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
}

.num {
  text-align: right;
}

.mono {
  font-family: var(--font-mono);
}


.documento {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  padding-top: 0.5rem;
  border-top: 1px solid var(--border);
}

.documento p {
  flex-basis: 100%;
  margin: 0;
}

.documento .checkbox {
  display: flex;
  align-items: center;
  gap: 0.375rem;
}

/* Apagado y con el motivo a la vista: que se sepa que el sistema puede emitir con CAI
   aunque hoy falte registrarlo. */
.documento .apagado {
  opacity: 0.6;
}

.anulado {
  color: var(--danger);
}

.revision {
  margin-bottom: 1rem;
  border-color: var(--accent);
}

.revision dl {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.25rem 1rem;
  margin: 0;
}

.revision dt {
  color: var(--text-muted);
}

.revision dd {
  margin: 0;
}

.revision .num {
  font-variant-numeric: tabular-nums;
}

.revision .aviso {
  padding: 0.5rem 0.75rem;
  border-radius: var(--radius-sm);
  background: color-mix(in srgb, var(--warning) 14%, transparent);
  color: var(--warning-text);
  font-size: 0.875rem;
}

/* Lo anulado se lee, pero no se confunde con lo que cuenta. */
tr.anulada td {
  opacity: 0.55;
  text-decoration: line-through;
}

tr.anulada td:first-child,
tr.anulada td:last-child {
  text-decoration: none;
}

/* Mitad y mitad: en una pantalla ancha, la columna fija dejaba un hueco a la mitad de la
   página y el formulario del pago estirado a lo bobo. */
.ajuste {
  margin-top: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.ajuste > summary {
  padding: 0.75rem 1rem;
  cursor: pointer;
  font-weight: 600;
}

.ajuste[open] > summary {
  border-bottom: 1px solid var(--border);
}

.ajuste .card {
  border: 0;
  background: none;
}

/* El resumen de arriba: la cuenta del taller sin tener que leer los formularios. */
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

.cifra.alerta {
  border-color: color-mix(in srgb, var(--warning) 45%, var(--border));
  background: color-mix(in srgb, var(--warning) 8%, transparent);
}

.aparte {
  margin: 2rem 0 0.75rem;
  color: var(--text-muted);
  font-size: 1rem;
  font-weight: 600;
}

/* La acción del mes, sola y grande: es a lo que se entra. */
.accion {
  margin: 0 0 1.25rem;
}

.accion .grande {
  width: 100%;
  padding: 0.75rem 1rem;
  font-size: 1.0625rem;
  font-weight: 600;
}

/* Un bloque de la ficha: su título con el «Editar» al lado, y debajo los datos o el
   formulario, nunca los dos. */
.bloque {
  margin-top: 2rem;
}

.bloque-top {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 0.5rem;
}

.bloque-top h2 {
  margin: 0;
  font-size: 1rem;
}

/* Los datos como datos: texto que se lee, no campos que piden que los llenen. */
.lectura {
  display: grid;
  grid-template-columns: minmax(8rem, max-content) 1fr;
  gap: 0.4rem 1.25rem;
  margin: 0;
  padding: 0.875rem 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.lectura dt {
  color: var(--text-muted);
  font-size: var(--text-xs);
}

.lectura dd {
  margin: 0;
}

/* Lo que falta y hace falta: el RTN que deja la factura a consumidor final. */
.lectura .falta {
  color: var(--warning);
}

@media (max-width: 34rem) {
  .lectura {
    grid-template-columns: 1fr;
    gap: 0.15rem;
  }

  .lectura dd {
    margin-bottom: 0.5rem;
  }
}

/* El nombre y su estado, juntos: el distintivo contesta «¿puede facturar hoy?». */
.titulo {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  flex-wrap: wrap;
}

.chip {
  display: inline-flex;
  align-items: center;
  padding: 0.15rem 0.55rem;
  border-radius: 999px;
  font-size: var(--text-xs);
  font-weight: 600;
}

.chip.active {
  background: color-mix(in srgb, var(--success) 14%, transparent);
  color: var(--success);
}

.chip.duesoon,
.chip.grace {
  background: color-mix(in srgb, var(--warning) 16%, transparent);
  color: var(--warning);
}

.chip.readonly,
.chip.suspended {
  background: color-mix(in srgb, var(--danger) 14%, transparent);
  color: var(--danger);
}

/* El ciclo de cobro dibujado: pagado, gracia, cortado. */
.linea {
  display: grid;
  gap: 1rem;
  margin-bottom: 1.25rem;
  padding: 1rem 1.125rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.linea.grace {
  border-color: color-mix(in srgb, var(--warning) 45%, var(--border));
}

.linea.readonly,
.linea.suspended {
  border-color: color-mix(in srgb, var(--danger) 45%, var(--border));
  background: color-mix(in srgb, var(--danger) 6%, var(--surface));
}

.linea-dice p {
  margin: 0.3rem 0 0;
  line-height: 1.5;
}

.tramos {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 0.5rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.tramo {
  display: grid;
  gap: 0.3rem;
}

/* La barrita de color es el tramo; el texto, su fecha. */
.tramo i {
  height: 4px;
  border-radius: 4px;
}

.tramo.ok i {
  background: var(--success);
}

.tramo.aviso i {
  background: var(--warning);
}

.tramo.cortado i {
  background: var(--danger);
}

/* Las pestañas de la ficha. */
.tabs {
  display: flex;
  gap: 1.5rem;
  margin-bottom: 1.25rem;
  border-bottom: 1px solid var(--border);
  overflow-x: auto;
}

.tab {
  padding: 0.65rem 0.15rem;
  border: 0;
  border-bottom: 2px solid transparent;
  background: none;
  color: var(--text-muted);
  font: inherit;
  font-weight: 500;
  white-space: nowrap;
  cursor: pointer;
}

.tab.activa {
  border-bottom-color: var(--brand);
  color: var(--text);
  font-weight: 600;
}

/* Los meses que paga, en botones: es una elección de cinco, no un número que se escribe. */
.meses {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  margin: 0;
  padding: 0;
  border: 0;
}

.meses legend {
  padding: 0;
  color: var(--text-muted);
  font-size: var(--text-xs);
}

.mes {
  min-width: 3.25rem;
  padding: 0.4rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 6px);
  background: var(--surface);
  color: var(--text);
  font: inherit;
  font-weight: 600;
  cursor: pointer;
}

.mes.activo {
  border-color: var(--brand);
  background: color-mix(in srgb, var(--brand) 14%, transparent);
  color: var(--brand);
}

/* Lo que tiene que depositar y hasta cuándo lo cubre: las dos cifras de la llamada. */
.cuenta {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  justify-content: space-between;
  padding: 0.875rem 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--bg, var(--surface));
}

.cuenta > div {
  display: grid;
  gap: 0.15rem;
}

.cuenta .cubre {
  text-align: right;
}

.cuenta .grande {
  font-size: 1.75rem;
  line-height: 1.1;
}

/* Qué se le entrega: tres salidas excluyentes, cada una con el número que va a salir. */
.entrega {
  display: grid;
  gap: 0.25rem;
  margin: 0;
  padding: 0.75rem 0.875rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

.entrega legend {
  padding: 0 0.3rem;
  color: var(--text-muted);
  font-size: var(--text-xs);
}

.entrega label {
  display: flex;
  gap: 0.6rem;
  align-items: flex-start;
  padding: 0.4rem 0.2rem;
  cursor: pointer;
}

.entrega label > span {
  display: grid;
  gap: 0.1rem;
}

.entrega small {
  line-height: 1.5;
}

/* Sin CAI la factura no es una opción: se enseña apagada, con el porqué y dónde arreglarlo. */
.entrega label.apagado {
  cursor: default;
}

.entrega label.apagado strong {
  color: var(--text-muted);
}

/* Suspender vive abajo y aparte: no es el castigo por no pagar. */
.peligro {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-top: 2.5rem;
  padding: 1rem 1.125rem;
  border: 1px solid color-mix(in srgb, var(--danger) 35%, var(--border));
  border-radius: var(--radius-md);
}

.peligro p {
  margin: 0.25rem 0 0;
  max-width: 44rem;
}

@media (max-width: 40rem) {
  .tramos {
    grid-template-columns: 1fr;
  }

  .cuenta .cubre {
    text-align: left;
  }
}
</style>

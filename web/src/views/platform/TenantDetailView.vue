<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { errorMessage } from '@/api/client'
import { platformApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import {
  WorkshopKind,
  type CreatedTenant,
  type PlatformSettings,
  type PlatformTenantDetail,
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

/** Lo que el taller tiene que depositar: la mensualidad más el ISV, si va por encima. */
function conImpuesto(neto: number): number {
  const tasa = emisor.value?.defaultTaxRate ?? 0
  if (tasa <= 0 || emisor.value?.pricesIncludeTax !== false) return neto
  return Math.round(neto * (1 + tasa / 100) * 100) / 100
}

/**
 * Cómo queda repartido el monto recibido en el documento. Se enseña antes de registrar: el
 * número que se le dice al taller por teléfono es este, no el de la mensualidad pelada.
 */
const documento = computed(() => {
  const tasa = emisor.value?.defaultTaxRate ?? 0
  const total = Number(pago.value.amount) || 0

  if (tasa <= 0) return { neto: total, impuesto: 0, total }

  // Con el ISV por encima, el neto sale de dividir lo recibido; con el ISV adentro, el total
  // ya es lo cobrado y el impuesto se desglosa hacia atrás. En los dos casos cuadra con el
  // depósito, que es lo que no puede fallar.
  const neto = Math.round((total / (1 + tasa / 100)) * 100) / 100

  return { neto, impuesto: Math.round((total - neto) * 100) / 100, total }
})

/**
 * El paso de revisión. Registrar el pago emite el documento y corre el vencimiento —y con CAI
 * quema un número autorizado—, así que primero se enseña exactamente qué va a salir y recién
 * el segundo botón lo hace.
 */
const revisando = ref(false)

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
const plan = ref({ planName: '', monthlyFee: 0, paidThrough: '', graceDays: 5 })
const nuevoDueno = ref({ email: '', fullName: '', password: '' })
const creado = ref<CreatedTenant | null>(null)

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

async function load() {
  loading.value = true
  error.value = ''
  try {
    data.value = await platformApi.get(id)

    const t = data.value.tenant
    pago.value.amount = conImpuesto(t.monthlyFee)
    ficha.value = {
      name: t.name,
      legalName: t.legalName ?? '',
      taxId: t.taxId ?? '',
      phone: t.phone ?? '',
      email: t.email ?? '',
      address: t.address ?? '',
    }
    plan.value = {
      planName: t.planName ?? '',
      monthlyFee: t.monthlyFee,
      paidThrough: t.paidThrough ?? '',
      graceDays: t.graceDays,
    }
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar el taller.')
  } finally {
    loading.value = false
  }
}

async function accion(fn: () => Promise<unknown>, fallo: string) {
  busy.value = true
  error.value = ''
  try {
    await fn()
    await load()
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
  )
}

function guardarPlan() {
  return accion(
    () =>
      platformApi.updateSubscription(id, {
        planName: plan.value.planName.trim() || null,
        monthlyFee: Number(plan.value.monthlyFee) || 0,
        paidThrough: plan.value.paidThrough || null,
        graceDays: Number(plan.value.graceDays) || 0,
      }),
    'No se pudo guardar el plan.',
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
})
</script>

<template>
  <section v-if="tenant">
    <header class="top">
      <div>
        <RouterLink :to="{ name: 'platform-tenants' }" class="volver">← Talleres</RouterLink>
        <h1>{{ tenant.name }}</h1>
        <p class="muted small">
          {{ tenant.planName ?? 'Sin plan' }} · {{ formatMoney(tenant.monthlyFee) }} al mes ·
          cliente desde {{ formatDay(tenant.createdAt) }}
        </p>
      </div>
      <button v-if="tenant.isActive" type="button" class="btn-danger" @click="suspender">
        Suspender
      </button>
      <button v-else type="button" @click="reactivar">Reactivar</button>
    </header>

    <ErrorNote v-if="error" :message="error" />

    <p class="situacion" :class="tenant.state.toLowerCase()">{{ situacion }}</p>

    <!-- Lo de todos los meses a la izquierda y grande; a la derecha, los datos del taller,
         que es lo que hay que mirar al emitirle la factura. Lo que se toca una vez al año
         queda plegado abajo: cuatro tarjetas al mismo nivel no decían cuál era el trabajo. -->
    <div class="columnas">
      <div class="principal">
        <form class="card" @submit.prevent="revisar">
          <h2>Registrar pago</h2>

          <div class="row">
            <label>
              Monto
              <input v-model.number="pago.amount" type="number" min="0" step="0.01" required />
            </label>
            <label>
              Meses
              <input v-model.number="pago.months" type="number" min="1" max="24" />
            </label>
          </div>

          <div class="row">
            <label>
              Fecha del pago
              <input v-model="pago.paidOn" type="date" />
            </label>
            <label>
              Forma
              <select v-model="pago.method">
                <option>Transferencia</option>
                <option>Depósito</option>
                <option>Efectivo</option>
              </select>
            </label>
          </div>

          <label>
            Número de referencia
            <input v-model="pago.reference" placeholder="Número de depósito" />
          </label>

          <!-- El documento nace con el pago, así que lo que va a decir se ve aquí, antes de
               registrarlo. Después solo se puede anular. -->
          <div class="documento">
            <label class="checkbox">
              <input v-model="pago.emitir" type="checkbox" />
              Emitir documento
            </label>

            <template v-if="pago.emitir">
              <label class="checkbox" :class="{ apagado: sinCai }">
                <input v-model="pago.conCai" type="checkbox" :disabled="sinCai" />
                Con CAI
              </label>

              <p v-if="sinCai" class="muted small">
                Sin CAI vigente sale un comprobante con numeración propia
                ({{ emisor?.invoicePrefix ?? 'C' }}-{{
                  String(emisor?.nextInvoiceNumber ?? 1).padStart(6, '0')
                }}). El CAI se registra en Facturación.
              </p>
              <p v-else-if="pago.conCai" class="muted small">
                Consume el número {{ emisor?.fiscalRange?.nextFiscalNumber }} del rango
                autorizado.
              </p>

              <p v-if="documento.impuesto > 0" class="muted small">
                Sale por {{ formatMoney(documento.total) }}: neto
                {{ formatMoney(documento.neto) }} + ISV
                {{ emisor?.defaultTaxRate }}% {{ formatMoney(documento.impuesto) }}.
              </p>
            </template>
          </div>

          <div class="actions">
            <button type="submit" :disabled="busy">Revisar</button>
          </div>

          <p class="muted small">
            La fecha se corre desde el vencimiento anterior, no desde hoy: quien paga con atraso
            no pierde los días que ya había pagado.
          </p>
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
      </div>

      <div class="lateral">
        <form class="card" @submit.prevent="guardarFicha">
          <h2>Datos del taller</h2>
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

        <form class="card" @submit.prevent="guardarPlan">
          <h2>Plan y vencimiento</h2>

          <div class="row">
            <label>
              Plan
              <input v-model="plan.planName" placeholder="Básico" />
            </label>
            <label>
              Cuota mensual
              <input v-model.number="plan.monthlyFee" type="number" min="0" step="0.01" />
            </label>
          </div>

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
      </div>
    </div>

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

    <details class="ajuste">
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

    <!-- Se enseña una sola vez: la contraseña no queda guardada en claro en ninguna parte. -->
    <article v-if="creado" class="card credenciales">
      <h2>Acceso creado</h2>
      <p>Entrégueselas ahora. <strong>No se pueden volver a consultar.</strong></p>
      <p class="mono">{{ creado.ownerEmail }}</p>
      <p class="mono">{{ creado.password }}</p>
      <button type="button" class="link" @click="creado = null">Ya las anoté</button>
    </article>

    <details class="rescate">
      <summary>Crear otro acceso de Dueño</summary>
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

    <!-- Lo que va a pasar al confirmar, con todos sus efectos a la vista. Es el paso que
         faltaba: el botón emitía el documento y corría el vencimiento de un solo clic. -->

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
  </section>

  <SkeletonList v-else-if="loading" :rows="4" variant="tabla" />
  <p v-else class="error">{{ error }}</p>
</template>

<style scoped>
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

.rescate {
  margin-top: 1.5rem;
}

.rescate summary {
  cursor: pointer;
  font-size: 0.875rem;
  color: var(--text-muted);
  margin-bottom: 0.5rem;
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
.columnas {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
  align-items: start;
}

@media (max-width: 64rem) {
  .columnas {
    grid-template-columns: 1fr;
  }
}

.principal,
.lateral {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  min-width: 0;
}

/* Lo que se toca una vez al año: plegado, y con lo que vale ahora dicho en el título. */
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
</style>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { platformApi } from '@/api/garaj'
import EmptyState from '@/components/EmptyState.vue'
import ErrorNote from '@/components/ErrorNote.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import {
  PLAN_DEFAULTS,
  PLAN_LABEL,
  SubscriptionPlan,
  WorkshopKind,
  type Collected,
  type CreatedTenant,
  type PlatformSettings,
  type PlatformTenant,
} from '@/types/domain'
import { formatDay, formatMoney } from '@/utils/format'

/**
 * Los talleres como clientes nuestros: quién está al día, a quién hay que cobrarle y quién
 * dejó de pagar. Es la única pantalla del perfil Plataforma junto con el detalle.
 *
 * No hay una sola orden ni un solo cliente del taller aquí, y no por olvido: este perfil no
 * tiene taller, así que el filtro del backend le devuelve vacío todo lo que sea trabajo ajeno.
 */
const tenants = ref<PlatformTenant[]>([])
const loading = ref(false)
const busy = ref(false)
const error = ref('')

const creando = ref(false)
const creado = ref<CreatedTenant | null>(null)

const repaso = ref('')

/**
 * Los avisos del calendario —servicio que toca, orden parada, cita de mañana, saldo
 * vencido— corren solos a las siete de la mañana. Esto los corre ahora, para verlos sin
 * esperar al día siguiente. Lo que ya se avisó no se repite, así que no molesta a nadie.
 */
async function correrElRepaso() {
  busy.value = true
  repaso.value = ''
  error.value = ''
  try {
    await platformApi.repasoDeAvisos()
    repaso.value = 'Repaso hecho. Los avisos nuevos ya están en la campana de cada taller.'
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo correr el repaso de avisos.')
  } finally {
    busy.value = false
  }
}

const vacio = {
  name: '',
  legalName: '',
  taxId: '',
  email: '',
  ownerEmail: '',
  ownerName: '',
  branchName: '',
  branchCode: '',
  city: '',
  address: '',
  phone: '',
  plan: SubscriptionPlan.Basic as SubscriptionPlan,
  monthlyFee: PLAN_DEFAULTS[SubscriptionPlan.Basic].fee,
  paidThrough: '',
  password: '',
  worksWith: WorkshopKind.Vehicles as WorkshopKind,
}

const form = ref({ ...vacio })

/**
 * Al cambiar de plan se propone su cuota. Se propone y no se impone: la cuota se negocia, y
 * pisarle al que ya escribió un número sería peor que no ayudar.
 */
function aplicarPlan() {
  const sugerida = PLAN_DEFAULTS[form.value.plan].fee
  const otras = Object.values(PLAN_DEFAULTS).map((d) => d.fee)

  if (!form.value.monthlyFee || otras.includes(Number(form.value.monthlyFee))) {
    form.value.monthlyFee = sugerida
  }
}

/** Qué le toca al taller con el plan elegido, dicho antes de darlo de alta. */
const queTraeElPlan = computed(() => {
  const d = PLAN_DEFAULTS[form.value.plan]
  const tecnicos = d.technicians === null ? 'técnicos sin tope' : `hasta ${d.technicians} técnicos`
  const diag = d.diagnostics ? 'con módulo de diagnóstico' : 'sin diagnóstico'

  return `${PLAN_LABEL[form.value.plan]}: ${tecnicos}, ${diag}. Los dos se ajustan después en la ficha del taller.`
})

/**
 * Un taller sin cuota no es un cliente que deba: es una cuenta de prueba o de cortesía. Se
 * separa porque mezclarlo con los que pagan arruina las dos cuentas —la de ingresos y la de
 * mora—, y porque lo que hay que hacer con él es otra cosa: ponerle plan.
 */
function esDePrueba(t: PlatformTenant) {
  return t.isActive && t.monthlyFee <= 0
}

/** Nuestros datos de emisor: de aquí sale el ISV con que se dice lo que debe depositar. */
const emisor = ref<PlatformSettings | null>(null)

/** Lo cobrado en el mes corriente, que es la otra mitad de «cómo vamos». */
const cobrado = ref<Collected | null>(null)

/** Lo que el taller deposita: la cuota más el ISV, si el ISV va por encima. */
function conIsv(neto: number) {
  const tasa = emisor.value?.defaultTaxRate ?? 0
  if (tasa <= 0 || emisor.value?.pricesIncludeTax !== false) return neto
  return Math.round(neto * (1 + tasa / 100) * 100) / 100
}

const MESES = [
  'enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio',
  'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre',
]

const mesCorriente = computed(() =>
  cobrado.value ? MESES[cobrado.value.month - 1] : MESES[new Date().getMonth()],
)

/** De una ojeada: cuántos son, cuántos pagan y cuántos están de prueba. */
const resumen = computed(() => {
  const activos = tenants.value.filter((t) => t.isActive)
  const prueba = activos.filter(esDePrueba).length
  const pagan = activos.length - prueba

  return [
    `${tenants.value.length} ${tenants.value.length === 1 ? 'taller' : 'talleres'}`,
    `${pagan} ${pagan === 1 ? 'paga mensualidad' : 'pagan mensualidad'}`,
    prueba ? `${prueba} de prueba` : null,
  ]
    .filter(Boolean)
    .join(' · ')
})

/**
 * A quién hay que atender hoy, dicho en palabras y con el botón al lado. Es lo primero de la
 * pantalla a propósito: una tabla ordenada por vencimiento obliga a leer fechas y restar; esto
 * ya trae la resta hecha y lo que hay que hacer.
 */
const atencion = computed(() => {
  const dias = (t: PlatformTenant) => t.daysLeft ?? 0

  return tenants.value
    .filter((t) => t.isActive)
    .map((t) => {
      if (esDePrueba(t)) {
        if (!t.paidThrough || dias(t) > 7) return null
        return {
          t,
          tono: 'aviso',
          etiqueta: 'Prueba termina',
          razon: `La prueba termina el ${formatDay(t.paidThrough)}, ${enCuanto(t)}. Todavía no tiene cuota.`,
          monto: null,
          accion: 'Ponerle plan',
        }
      }

      if (t.state === 'ReadOnly')
        return {
          t,
          tono: 'cortado',
          etiqueta: 'Solo lectura',
          razon: `Venció el ${formatDay(t.paidThrough)}, ${enCuanto(t)}. No puede registrar órdenes ni facturar.`,
          monto: conIsv(t.monthlyFee),
          accion: 'Registrar pago',
        }

      if (t.state === 'Grace')
        return {
          t,
          tono: 'urgente',
          etiqueta: 'En gracia',
          razon: `Venció el ${formatDay(t.paidThrough)}, ${enCuanto(t)}. Sigue trabajando hasta el ${formatDay(t.readOnlyOn)}.`,
          monto: conIsv(t.monthlyFee),
          accion: 'Registrar pago',
        }

      if (t.state === 'DueSoon')
        return {
          t,
          tono: 'aviso',
          etiqueta: 'Vence pronto',
          razon: `Vence el ${formatDay(t.paidThrough)}, ${enCuanto(t)}.${
            t.lastPaymentOn ? ` Último pago el ${formatDay(t.lastPaymentOn)}.` : ' Nunca ha pagado.'
          }`,
          monto: conIsv(t.monthlyFee),
          accion: 'Registrar pago',
        }

      return null
    })
    .filter((x): x is NonNullable<typeof x> => x !== null)
    // Lo más urgente primero: el cortado antes que el que vence la semana que viene.
    .sort((a, b) => (a.t.daysLeft ?? 0) - (b.t.daysLeft ?? 0))
})

/** «hace 19 días», «en 6 días», «hoy»: la resta hecha, que es lo que se pregunta. */
function enCuanto(t: PlatformTenant) {
  const d = t.daysLeft
  if (d === null) return 'sin fecha'
  if (d === 0) return 'hoy'
  return d > 0 ? `en ${d} ${d === 1 ? 'día' : 'días'}` : `hace ${-d} ${-d === 1 ? 'día' : 'días'}`
}

const busqueda = ref('')

/** Qué se está mirando. «Por cobrar» es todo lo que no está al día. */
const filtro = ref<'todos' | 'porCobrar' | 'acuerdo' | 'prueba' | 'suspendidos'>('todos')

/** El número de cada pestaña: dice dónde mirar antes de tocarla. */
const cuentas = computed(() => ({
  todos: tenants.value.length,
  porCobrar: tenants.value.filter((t) => t.isActive && !esDePrueba(t) && t.state !== 'Active').length,
  acuerdo: tenants.value.filter((t) => !!t.unblockedThrough).length,
  prueba: tenants.value.filter(esDePrueba).length,
  suspendidos: tenants.value.filter((t) => t.state === 'Suspended' || !t.isActive).length,
}))

/** Por qué columna se ordena, y hacia dónde. */
const orden = ref<'vence' | 'nombre' | 'cuota' | 'pago'>('vence')
const descendente = ref(false)

function ordenarPor(columna: typeof orden.value) {
  if (orden.value === columna) descendente.value = !descendente.value
  else {
    orden.value = columna
    descendente.value = false
  }
}

const visibles = computed(() => {
  const texto = busqueda.value.trim().toLowerCase()

  const filtrados = tenants.value.filter((t) => {
    if (texto && !`${t.name} ${t.legalName ?? ''} ${t.phone ?? ''}`.toLowerCase().includes(texto))
      return false

    switch (filtro.value) {
      case 'porCobrar':
        return t.isActive && !esDePrueba(t) && t.state !== 'Active'
      case 'acuerdo':
        return !!t.unblockedThrough
      case 'prueba':
        return esDePrueba(t)
      case 'suspendidos':
        return t.state === 'Suspended' || !t.isActive
      default:
        return true
    }
  })

  // Sin fecha de pago, al final: es el que nunca ha pagado, no el que vence primero.
  const alFinal = '9999-12-31'

  const ordenados = [...filtrados].sort((a, b) => {
    switch (orden.value) {
      case 'nombre':
        return a.name.localeCompare(b.name, 'es')
      case 'cuota':
        return a.monthlyFee - b.monthlyFee
      case 'pago':
        return (a.lastPaymentOn ?? '').localeCompare(b.lastPaymentOn ?? '')
      default:
        return (a.paidThrough ?? alFinal).localeCompare(b.paidThrough ?? alFinal)
    }
  })

  return descendente.value ? ordenados.reverse() : ordenados
})

/**
 * Lo que contesta «¿cómo vamos?» sin abrir una ficha: lo que entra al mes si todos pagan, y
 * lo que está parado. Se cuenta sobre los talleres activos: uno suspendido no es ingreso.
 */
const cifras = computed(() => {
  const pagan = tenants.value.filter((t) => t.isActive && !esDePrueba(t))
  const atrasados = pagan.filter((t) => t.state === 'Grace' || t.state === 'ReadOnly')
  const porVencer = pagan.filter((t) => t.state === 'DueSoon')
  const suma = (lista: PlatformTenant[]) => lista.reduce((total, t) => total + t.monthlyFee, 0)

  return {
    talleres: pagan.length,
    mensual: suma(pagan),
    atrasados: atrasados.length,
    detenido: suma(atrasados),
    porVencer: porVencer.length,
    porVencerMonto: suma(porVencer),
    // Un solo nombre cuando es uno solo: «Lubri Smart» dice más que «1 taller».
    quienVence: porVencer.length === 1 ? porVencer[0].name : null,
  }
})

const etiquetas: Record<PlatformTenant['state'], string> = {
  Active: 'Al día',
  DueSoon: 'Por vencer',
  Grace: 'En gracia',
  ReadOnly: 'Solo lectura',
  Suspended: 'Suspendido',
}

function tono(t: PlatformTenant) {
  if (t.unblockedThrough) return 'acuerdo'
  return t.state === 'Active'
    ? 'ok'
    : t.state === 'DueSoon'
      ? 'aviso'
      : t.state === 'Grace'
        ? 'urgente'
        : 'cortado'
}

function etiqueta(t: PlatformTenant) {
  return t.unblockedThrough ? 'Acuerdo de pago' : etiquetas[t.state]
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    // El emisor y lo cobrado no tumban la pantalla si fallan: son dos tarjetas, no la lista.
    const [lista, ajustes, mes] = await Promise.all([
      platformApi.list(),
      platformApi.billingSettings().catch(() => null),
      platformApi.collected().catch(() => null),
    ])

    tenants.value = lista
    emisor.value = ajustes
    cobrado.value = mes
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cargar la lista de talleres.')
  } finally {
    loading.value = false
  }
}

async function crear() {
  const f = form.value
  if (!f.name.trim() || !f.ownerEmail.trim() || !f.ownerName.trim() || !f.branchName.trim()) return

  busy.value = true
  error.value = ''
  try {
    creado.value = await platformApi.create({
      name: f.name.trim(),
      legalName: f.legalName.trim() || null,
      // El RTN de aquí es el del taller como cliente nuestro: es lo que lleva impreso la
      // factura de su mensualidad. Sin él sale a consumidor final y no la puede deducir.
      taxId: f.taxId.trim() || null,
      email: f.email.trim() || null,
      ownerEmail: f.ownerEmail.trim(),
      ownerName: f.ownerName.trim(),
      branchName: f.branchName.trim(),
      branchCode: f.branchCode.trim() || null,
      city: f.city.trim() || null,
      address: f.address.trim() || null,
      phone: f.phone.trim() || null,
      plan: f.plan,
      monthlyFee: Number(f.monthlyFee) || 0,
      paidThrough: f.paidThrough || null,
      // Vacía, la genera el servidor. Sale igual en la tarjeta de abajo en los dos casos.
      password: f.password.trim() || null,
      worksWith: f.worksWith,
    })

    form.value = { ...vacio }
    creando.value = false
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo dar de alta el taller.')
  } finally {
    busy.value = false
  }
}

onMounted(load)
</script>

<template>
  <section>
    <header class="top">
      <div>
        <h1>Talleres</h1>
        <p class="muted small">{{ resumen }}</p>
      </div>
      <div class="acciones">
        <button type="button" class="btn-ghost" :disabled="busy" @click="correrElRepaso">
          Enviar recordatorios de pago
        </button>
        <button type="button" @click="creando = !creando">
          {{ creando ? 'Cancelar' : 'Nuevo taller' }}
        </button>
      </div>
    </header>

    <ErrorNote v-if="error" :message="error" />
    <p v-if="repaso" class="muted small">{{ repaso }}</p>

    <!-- Se enseña una sola vez: la contraseña no se guarda en claro en ninguna parte. -->
    <article v-if="creado" class="card credenciales">
      <h2>Taller creado</h2>
      <p>
        Entrégueselas al Dueño ahora. <strong>No se pueden volver a consultar</strong>: no
        quedan guardadas en ningún lado.
      </p>
      <dl>
        <dt>Correo</dt>
        <dd class="mono">{{ creado.ownerEmail }}</dd>
        <dt>Contraseña</dt>
        <dd class="mono">{{ creado.password }}</dd>
      </dl>
      <button type="button" class="link" @click="creado = null">Ya las anoté</button>
    </article>

    <form v-if="creando" class="card alta" @submit.prevent="crear">
      <h2>Nuevo taller</h2>

      <div class="row">
        <label>
          Nombre del taller
          <input v-model="form.name" placeholder="Taller Martínez" required />
        </label>
        <label>
          Razón social
          <input v-model="form.legalName" placeholder="Como está en el RTN" />
        </label>
      </div>

      <div class="row">
        <label>
          RTN del taller
          <input v-model="form.taxId" placeholder="0801…" />
          <span class="ayuda">
            Lo que lleva impreso su factura. Sin él sale a consumidor final.
          </span>
        </label>
        <label>
          Teléfono
          <input v-model="form.phone" placeholder="9888-1111" />
        </label>
        <label>
          Correo del taller
          <input v-model="form.email" type="email" placeholder="taller@correo.hn" />
        </label>
      </div>

      <div class="row">
        <label>
          Nombre del Dueño
          <input v-model="form.ownerName" placeholder="Juan Martínez" required />
        </label>
        <label>
          Correo del Dueño
          <input v-model="form.ownerEmail" type="email" placeholder="juan@taller.hn" required />
        </label>
        <label>
          Contraseña
          <input
            v-model="form.password"
            type="text"
            placeholder="La escribe él, o se genera"
            minlength="8"
          />
          <span class="ayuda">
            Con el Dueño al lado, que la escriba él: se la lleva sabida. En blanco, se genera.
          </span>
        </label>
      </div>

      <div class="row">
        <label>
          Primera sucursal
          <input v-model="form.branchName" placeholder="Casa matriz" required />
        </label>
        <label>
          Código
          <input v-model="form.branchCode" placeholder="MTZ" />
        </label>
        <label>
          Ciudad
          <input v-model="form.city" placeholder="Tegucigalpa" />
        </label>
      </div>

      <!-- Qué repara se decide aquí y no en el panel del taller: cambiarlo con órdenes
           abiertas deja fichas que ya no se pueden usar. -->
      <div class="row">
        <label>
          Qué repara
          <select v-model.number="form.worksWith">
            <option :value="WorkshopKind.Vehicles">Vehículos y motos</option>
            <option :value="WorkshopKind.Equipment">Equipos (cómputo, celulares, aires)</option>
          </select>
        </label>
      </div>

      <div class="row">
        <label>
          Plan
          <!-- Seleccionable y no texto libre: el plan decide el tope de técnicos y si lleva
               diagnóstico, así que escribirlo a mano dejaba al taller sin ninguno de los dos. -->
          <select v-model.number="form.plan" @change="aplicarPlan">
            <option :value="SubscriptionPlan.Basic">Básico</option>
            <option :value="SubscriptionPlan.Pro">Pro — con diagnóstico</option>
            <option :value="SubscriptionPlan.Legacy">Heredado — sin topes</option>
          </select>
        </label>
        <label>
          Cuota mensual
          <input v-model.number="form.monthlyFee" type="number" min="0" step="0.01" />
        </label>
        <label>
          Pagado hasta
          <input v-model="form.paidThrough" type="date" />
        </label>
      </div>

      <p class="muted small">{{ queTraeElPlan }}</p>

      <div class="actions">
        <button type="submit" :disabled="busy">Dar de alta</button>
      </div>

      <p class="muted small">
        Si no se pone fecha, queda pagado hasta dentro de un mes: lo normal es que el taller
        pague la instalación el día que se instala. La contraseña —la escriba él o se genere—
        se enseña una sola vez al guardar, y el Dueño puede cambiarla en Usuarios.
      </p>
    </form>

    <!-- A quién atender hoy, con la resta hecha y el botón al lado. Va primero porque es a lo
         que se entra: una tabla ordenada por vencimiento obliga a leer fechas y restar. -->
    <section v-if="!loading && atencion.length" class="atencion">
      <h2>
        Necesitan atención
        <span class="muted">· {{ atencion.length }}</span>
      </h2>

      <div class="tarjetas">
        <article v-for="a in atencion" :key="a.t.id" class="aviso" :class="a.tono">
          <div class="aviso-top">
            <RouterLink :to="{ name: 'platform-tenant', params: { id: a.t.id } }" class="nombre">
              {{ a.t.name }}
            </RouterLink>
            <span class="chip" :class="a.tono">{{ a.etiqueta }}</span>
          </div>

          <p class="razon">{{ a.razon }}</p>

          <div class="aviso-pie">
            <span v-if="a.monto !== null" class="num">
              {{ formatMoney(a.monto) }}
              <span class="muted small">{{ a.monto === a.t.monthlyFee ? '' : 'con ISV' }}</span>
            </span>
            <span v-else class="muted small">Cuenta de prueba</span>

            <RouterLink
              class="btn-chico"
              :to="{ name: 'platform-tenant', params: { id: a.t.id }, query: { cobrar: '1' } }"
            >
              {{ a.accion }}
            </RouterLink>
          </div>
        </article>
      </div>
    </section>

    <!-- Cómo vamos, sin abrir una ficha. -->
    <div v-if="!loading && tenants.length" class="cifras">
      <article class="cifra">
        <span class="rotulo">Mensualidades al mes</span>
        <strong class="num">{{ formatMoney(cifras.mensual) }}</strong>
        <span class="muted small">
          {{ cifras.talleres }} {{ cifras.talleres === 1 ? 'taller que paga' : 'talleres que pagan' }}
          · sin ISV
        </span>
      </article>

      <article class="cifra">
        <span class="rotulo">Cobrado en {{ mesCorriente }}</span>
        <strong class="num">{{ cobrado ? formatMoney(cobrado.total) : '—' }}</strong>
        <span class="muted small">
          <template v-if="cobrado">
            {{ cobrado.count }} {{ cobrado.count === 1 ? 'pago registrado' : 'pagos registrados' }}
          </template>
          <template v-else>No se pudo leer</template>
        </span>
      </article>

      <article class="cifra" :class="{ aviso: cifras.porVencer > 0 }">
        <span class="rotulo">Vence en los próximos 7 días</span>
        <strong class="num">{{ formatMoney(cifras.porVencerMonto) }}</strong>
        <span class="muted small">
          {{ cifras.quienVence ?? `${cifras.porVencer} ${cifras.porVencer === 1 ? 'taller' : 'talleres'}` }}
        </span>
      </article>

      <article class="cifra" :class="{ alerta: cifras.atrasados > 0 }">
        <span class="rotulo">Vencido sin pagar</span>
        <strong class="num">{{ formatMoney(cifras.detenido) }}</strong>
        <span class="muted small">
          {{ cifras.atrasados }} {{ cifras.atrasados === 1 ? 'taller' : 'talleres' }} en gracia o
          en solo lectura
        </span>
      </article>
    </div>

    <div v-if="!loading && tenants.length" class="barra">
      <input
        v-model="busqueda"
        type="search"
        class="buscar"
        placeholder="Buscar por nombre, razón social o teléfono"
        aria-label="Buscar un taller"
      />

      <div class="pestanas">
        <button
          v-for="f in [
            { clave: 'todos' as const, label: 'Todos', urgente: false },
            { clave: 'porCobrar' as const, label: 'Por cobrar', urgente: true },
            { clave: 'acuerdo' as const, label: 'Con acuerdo', urgente: false },
            { clave: 'prueba' as const, label: 'Prueba', urgente: false },
            { clave: 'suspendidos' as const, label: 'Suspendidos', urgente: false },
          ]"
          :key="f.clave"
          type="button"
          class="pestana"
          :class="{ activa: filtro === f.clave }"
          @click="filtro = f.clave"
        >
          {{ f.label }}
          <span class="cuenta" :class="{ urgente: f.urgente && cuentas[f.clave] > 0 }">
            {{ cuentas[f.clave] }}
          </span>
        </button>
      </div>
    </div>

    <SkeletonList v-if="loading" :rows="4" variant="tabla" />
    <EmptyState v-else-if="!tenants.length" title="Todavía no hay talleres" />
    <EmptyState
      v-else-if="!visibles.length"
      title="Ningún taller con ese filtro"
      :hint="busqueda ? 'Pruebe con otro nombre.' : 'Cambie la pestaña para verlos todos.'"
    />

    <table v-else>
      <thead>
        <tr>
          <!-- Ordenable: la lista llega por vencimiento, pero a veces se busca por nombre o
               se quiere ver quién paga más. -->
          <th scope="col">
            <button type="button" class="ordenar" @click="ordenarPor('nombre')">
              Taller<span v-if="orden === 'nombre'">{{ descendente ? ' ↓' : ' ↑' }}</span>
            </button>
          </th>
          <th scope="col">Estado</th>
          <th scope="col">
            <button type="button" class="ordenar" @click="ordenarPor('vence')">
              Pagado hasta<span v-if="orden === 'vence'">{{ descendente ? ' ↓' : ' ↑' }}</span>
            </button>
          </th>
          <th scope="col" class="num">
            <button type="button" class="ordenar" @click="ordenarPor('cuota')">
              Cuota<span v-if="orden === 'cuota'">{{ descendente ? ' ↓' : ' ↑' }}</span>
            </button>
          </th>
          <th scope="col">
            <button type="button" class="ordenar" @click="ordenarPor('pago')">
              Último pago<span v-if="orden === 'pago'">{{ descendente ? ' ↓' : ' ↑' }}</span>
            </button>
          </th>
          <th scope="col"><span class="sr-only">Acciones</span></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="t in visibles" :key="t.id">
          <td>
            <RouterLink class="nombre" :to="{ name: 'platform-tenant', params: { id: t.id } }">
              {{ t.name }}
            </RouterLink>
            <div class="muted small">
              {{ PLAN_LABEL[t.plan] ?? t.planName ?? 'Sin plan' }} ·
              {{ t.branchCount }} {{ t.branchCount === 1 ? 'sucursal' : 'sucursales' }}
              <!-- Los técnicos son lo que el plan Taller limita. Sin el número a la vista, el
                   tope no se puede ni revisar ni cobrar. Con `v-if` porque el panel se
                   despliega antes que la API: sin el dato, mejor nada que «· técnicos». -->
              <template v-if="t.technicianCount !== undefined">
                ·
                <span :class="{ tope: t.technicianLimit !== null && t.technicianCount >= t.technicianLimit }">
                  {{ t.technicianCount }}{{ t.technicianLimit === null ? '' : ` de ${t.technicianLimit}` }}
                  {{ t.technicianCount === 1 ? 'técnico' : 'técnicos' }}
                </span>
              </template>
            </div>
          </td>
          <td>
            <span class="chip" :class="tono(t)">{{ etiqueta(t) }}</span>
          </td>
          <td>
            {{ formatDay(t.paidThrough) }}
            <div v-if="t.state === 'DueSoon' && t.daysLeft !== null" class="muted small">
              {{ t.daysLeft === 0 ? 'vence hoy' : `faltan ${t.daysLeft} días` }}
            </div>
          </td>
          <td class="num">
            <template v-if="esDePrueba(t)"><span class="muted">Sin cuota</span></template>
            <template v-else>{{ formatMoney(t.monthlyFee) }}</template>
          </td>
          <td>
            <template v-if="t.lastPaymentOn">{{ formatDay(t.lastPaymentOn) }}</template>
            <span v-else class="muted">Nunca ha pagado</span>
          </td>
          <!-- La acción en la fila: el que abre esta pantalla viene a cobrar, no a navegar. -->
          <td class="acciones-fila">
            <RouterLink
              class="btn-chico"
              :to="{ name: 'platform-tenant', params: { id: t.id }, query: { cobrar: '1' } }"
            >
              {{ esDePrueba(t) ? 'Ponerle plan' : 'Registrar pago' }}
            </RouterLink>
          </td>
        </tr>
      </tbody>
    </table>

    <!-- Qué significa cada estado. Son cuatro palabras que deciden si un taller puede
         facturar hoy: dejarlas a la interpretación de quien mira sale caro. -->
    <dl v-if="!loading && tenants.length" class="leyenda">
      <dt><span class="chip ok">Al día</span></dt>
      <dd>pagado</dd>
      <dt><span class="chip aviso">Por vencer</span></dt>
      <dd>faltan 7 días o menos</dd>
      <dt><span class="chip urgente">En gracia</span></dt>
      <dd>vencido, pero sigue trabajando los días de gracia</dd>
      <dt><span class="chip cortado">Solo lectura</span></dt>
      <dd>pasó la gracia: consulta, pero no registra órdenes ni factura</dd>
    </dl>
  </section>
</template>

<style scoped>
/* ------------------------------------------------------- cifras, filtros y orden */

.cifras {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.cifra {
  display: grid;
  gap: 0.15rem;
  flex: 1 1 14rem;
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

/* Dinero parado: no es una cifra más, es la que hay que mirar. */
.cifra.alerta {
  border-color: color-mix(in srgb, var(--danger) 45%, var(--border));
}

.cifra.alerta strong {
  color: var(--danger);
}

.barra {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  flex-wrap: wrap;
  margin-bottom: 0.75rem;
}

.buscar {
  flex: 1 1 18rem;
  max-width: 26rem;
}

.pestanas {
  display: flex;
  gap: 0.25rem;
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

/* El nombre es el enlace: en una tabla ancha, un «Ver» al final queda a un palmo del
   taller que se quería abrir. */
.nombre {
  font-weight: 600;
}

/* El taller que ya llegó a su tope: es con quien hay que hablar antes de que pida otro
   técnico y se encuentre con el bloqueo. */
.tope {
  color: var(--warning);
  font-weight: 600;
}

.acciones {
  display: flex;
  gap: var(--space-2);
}

h1 {
  margin: 0;
}

.top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.card {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  margin-bottom: 1rem;
}

.card h2 {
  margin: 0 0 0.5rem;
  font-size: 1rem;
}

.credenciales {
  border-color: var(--success);
}

.credenciales dl {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.25rem 0.75rem;
  margin: 0.75rem 0;
}

.credenciales dt {
  color: var(--text-muted);
  font-size: 0.875rem;
}

.mono {
  font-family: var(--font-mono);
  margin: 0;
}

.alta .row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.alta .row label {
  flex: 1 1 12rem;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.875rem;
}

.ayuda {
  color: var(--text-muted);
  font-size: 0.75rem;
}

.actions {
  margin-top: 0.5rem;
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
  vertical-align: top;
}

.num {
  text-align: right;
}

.chip {
  display: inline-block;
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 600;
}

.chip.ok {
  background: color-mix(in srgb, var(--success) 15%, transparent);
  color: var(--success-text);
}

.chip.aviso {
  background: color-mix(in srgb, var(--warning) 18%, transparent);
  color: var(--warning-text);
}

.chip.urgente {
  background: color-mix(in srgb, var(--warning) 30%, transparent);
  color: var(--warning-text);
}

.chip.cortado {
  background: color-mix(in srgb, var(--danger) 15%, transparent);
  color: var(--danger);
}

.chip.acuerdo {
  background: var(--surface-alt);
  color: var(--text-muted);
}


/* A quién atender hoy: lo primero que se ve, con la razón en palabras y el botón al lado. */
.atencion {
  margin-bottom: 1.25rem;
}

.atencion h2 {
  margin: 0 0 0.6rem;
  font-size: 1rem;
  font-weight: 600;
}

.tarjetas {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(17.5rem, 1fr));
  gap: 0.75rem;
}

.aviso {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
  padding: 0.875rem 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

/* El borde dice de qué urgencia es sin tener que leer la etiqueta. */
.aviso.cortado {
  border-color: color-mix(in srgb, var(--danger) 45%, var(--border));
}

.aviso.urgente,
.aviso.aviso {
  border-color: color-mix(in srgb, var(--warning) 45%, var(--border));
}

.aviso-top,
.aviso-pie {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.aviso .razon {
  margin: 0;
  color: var(--text);
  line-height: 1.5;
}

.aviso-pie .num {
  font-weight: 600;
}

/* El botón de una fila o de una tarjeta: el mismo en los dos lados. */
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

.acciones-fila {
  text-align: right;
}

/* El número de cada pestaña: dice dónde mirar antes de tocarla. */
.pestana .cuenta {
  margin-left: 0.35rem;
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

.pestana .cuenta.urgente {
  color: var(--danger);
  font-weight: 700;
}

.cifra.aviso {
  border-color: color-mix(in srgb, var(--warning) 45%, var(--border));
}

.cifra.aviso strong {
  color: var(--warning);
}

/* Qué quiere decir cada estado. Cuatro palabras que deciden si un taller factura hoy. */
.leyenda {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.35rem 1.25rem;
  margin: 0.875rem 0 0;
  color: var(--text-muted);
  font-size: var(--text-xs);
}

.leyenda dt {
  display: inline-flex;
}

.leyenda dd {
  margin: 0 0 0 -0.9rem;
}
</style>

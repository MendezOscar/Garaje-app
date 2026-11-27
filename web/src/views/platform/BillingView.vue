<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { errorMessage } from '@/api/client'
import { platformApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import type { InvoiceListItem, PlatformProduct, PlatformSettings } from '@/types/domain'
import { formatDay, formatMoney } from '@/utils/format'

/**
 * Cómo facturamos nosotros: quiénes somos en el documento y con qué autorización.
 *
 * Es de plataforma, no de ningún taller. Lo que se decide aquí sale impreso en el documento
 * que recibe cada taller al pagar su mensualidad, y el documento nace con el pago: esta
 * pantalla no emite nada por sí sola, salvo cuando se emite a mano.
 *
 * Y es de los tres servicios, no solo de GarajApp: el CAI es uno —mismo emisor, mismo RTN—,
 * así que aquí viven también las llaves con que StockRuta y Profeapp piden sus números, y los
 * documentos de todos.
 */
const settings = ref<PlatformSettings | null>(null)
const loading = ref(false)
const busy = ref(false)
const error = ref('')
const guardado = ref('')

const form = ref({
  name: 'NAOS Apps',
  legalName: '',
  taxId: '',
  phone: '',
  email: '',
  address: '',
  defaultTaxRate: 15,
  pricesIncludeTax: false,
  invoicePrefix: 'C',
})

const rango = ref({
  cai: '',
  establishmentCode: '000',
  pointOfSaleCode: '001',
  documentType: '01',
  rangeStart: 1,
  rangeEnd: 1000,
  issueDeadline: '',
})

const abrirRango = ref(false)

/** Qué se está mirando. Lo del emisor se toca una vez al año; los documentos, a diario. */
const pestana = ref<'emisor' | 'servicios' | 'documentos'>('emisor')

// ---------- Los servicios que cobramos ----------

const servicios = ref<PlatformProduct[]>([])

/** La llave recién generada. Se enseña una sola vez: después solo queda su hash. */
const llaveNueva = ref<{ name: string; apiKey: string } | null>(null)
const servicioNuevo = ref('')

async function cargarServicios() {
  servicios.value = await platformApi.products().catch(() => [])
}

async function crearServicio() {
  const nombre = servicioNuevo.value.trim()
  if (!nombre) return

  busy.value = true
  error.value = ''
  try {
    await platformApi.saveProduct(null, { name: nombre })
    servicioNuevo.value = ''
    await cargarServicios()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo dar de alta el servicio.')
  } finally {
    busy.value = false
  }
}

async function alternarServicio(s: PlatformProduct) {
  busy.value = true
  error.value = ''
  try {
    await platformApi.saveProduct(s.id, { name: s.name, isActive: !s.isActive })
    await cargarServicios()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

/**
 * Llave nueva. Deja la anterior sin valor en el mismo acto, así que se avisa antes: el portal
 * que la esté usando deja de poder emitir hasta que se le ponga la nueva.
 */
async function rotarLlave(s: PlatformProduct) {
  const aviso = s.apiKeyHint
    ? `La llave de ${s.name} que termina en ${s.apiKeyHint} dejará de funcionar. `
        + 'Su portal no podrá emitir hasta que le ponga la nueva. ¿Seguir?'
    : `Se generará la llave de ${s.name}. Solo se enseña una vez. ¿Seguir?`

  if (!confirm(aviso)) return

  busy.value = true
  error.value = ''
  llaveNueva.value = null
  try {
    const nueva = await platformApi.rotateProductKey(s.id)
    llaveNueva.value = { name: nueva.name, apiKey: nueva.apiKey }
    await cargarServicios()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo generar la llave.')
  } finally {
    busy.value = false
  }
}

// ---------- Los documentos ----------

const documentos = ref<InvoiceListItem[]>([])
const totalDocumentos = ref(0)
const buscado = ref('')
const servicioFiltrado = ref('')
const pagina = ref(1)
const buscando = ref(false)

async function buscarDocumentos() {
  buscando.value = true
  error.value = ''
  try {
    const page = await platformApi.invoices({
      search: buscado.value.trim() || undefined,
      productId: servicioFiltrado.value || undefined,
      page: pagina.value,
      pageSize: 25,
    })
    documentos.value = page.items
    totalDocumentos.value = page.total
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar los documentos.')
  } finally {
    buscando.value = false
  }
}

function buscarDesdeElPrincipio() {
  pagina.value = 1
  return buscarDocumentos()
}

const paginas = computed(() => Math.max(1, Math.ceil(totalDocumentos.value / 25)))

function irA(p: number) {
  pagina.value = Math.min(Math.max(1, p), paginas.value)
  return buscarDocumentos()
}

// ---------- Emitir a mano ----------

const emitiendo = ref(false)

const aMano = ref({
  productId: '',
  name: '',
  legalName: '',
  taxId: '',
  fiscal: true,
  paymentReference: '',
  note: '',
  lines: [{ description: '', quantity: 1, unitPrice: 0 }],
})

const totalAMano = computed(() =>
  aMano.value.lines.reduce(
    (suma, l) => suma + (Number(l.quantity) || 0) * (Number(l.unitPrice) || 0),
    0,
  ),
)

function agregarLinea() {
  aMano.value.lines.push({ description: '', quantity: 1, unitPrice: 0 })
}

function quitarLinea(i: number) {
  if (aMano.value.lines.length > 1) aMano.value.lines.splice(i, 1)
}

async function emitirAMano() {
  const f = aMano.value

  if (!f.productId || !f.name.trim()) return
  if (!f.lines.some((l) => l.description.trim())) return

  busy.value = true
  error.value = ''
  guardado.value = ''
  try {
    const factura = await platformApi.issueByHand({
      productId: f.productId,
      customer: {
        name: f.name.trim(),
        legalName: f.legalName.trim() || null,
        taxId: f.taxId.trim() || null,
      },
      lines: f.lines
        .filter((l) => l.description.trim())
        .map((l) => ({
          description: l.description.trim(),
          quantity: Number(l.quantity) || 1,
          unitPrice: Number(l.unitPrice) || 0,
        })),
      fiscal: f.fiscal,
      paymentReference: f.paymentReference.trim() || null,
      note: f.note.trim() || null,
    })

    guardado.value = `Emitido ${factura.number}.`
    emitiendo.value = false
    aMano.value = {
      productId: f.productId,
      name: '',
      legalName: '',
      taxId: '',
      fiscal: true,
      paymentReference: '',
      note: '',
      lines: [{ description: '', quantity: 1, unitPrice: 0 }],
    }

    await Promise.all([load(), buscarDesdeElPrincipio()])
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo emitir el documento.')
  } finally {
    busy.value = false
  }
}

function bajarPdf(d: InvoiceListItem) {
  return platformApi.invoicePdf(d.id, d.number)
}

async function anular(d: InvoiceListItem) {
  const motivo = prompt(`¿Por qué se anula ${d.number}? Es lo que explica el número.`)
  if (!motivo?.trim()) return

  busy.value = true
  error.value = ''
  try {
    await platformApi.voidInvoice(d.id, motivo.trim())
    await buscarDocumentos()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

/**
 * El número de documentos de un servicio no es un rótulo: se toca y deja la pestaña de
 * Documentos con los suyos, que es lo que se quiere mirar después de verlo.
 */
async function verLosDe(servicio: PlatformProduct) {
  servicioFiltrado.value = servicio.id
  await abrirPestana('documentos')
  await buscarDesdeElPrincipio()
}

/** Del aviso de arriba al paso 3, que es donde se arregla. */
async function irAlCai() {
  await abrirPestana('emisor')
  if (!settings.value?.fiscalRange) abrirRango.value = true
  document.getElementById('cai')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

async function abrirPestana(cual: typeof pestana.value) {
  pestana.value = cual
  llaveNueva.value = null

  if (cual === 'servicios' && !servicios.value.length) await cargarServicios()
  if (cual === 'documentos') {
    if (!servicios.value.length) await cargarServicios()
    if (!documentos.value.length) await buscarDesdeElPrincipio()
  }
}

/** El ejemplo con números: es lo que de verdad explica cuál de las dos formas está puesta. */
/**
 * Qué falta para poder emitir una factura con CAI. Es una lista, no un párrafo, porque lo que
 * se pregunta es «¿ya puedo?» y la respuesta son cinco casillas.
 *
 * El nombre, el teléfono y el correo van juntos: son los que casi siempre están.
 */
const requisitos = computed(() => {
  const r = settings.value?.fiscalRange

  return [
    {
      que: 'Nombre, teléfono y correo',
      ok: !!form.value.name.trim() && !!form.value.phone.trim() && !!form.value.email.trim(),
    },
    { que: 'Razón social', ok: !!form.value.legalName.trim() },
    { que: 'RTN', ok: !!form.value.taxId.trim() },
    { que: 'Dirección', ok: !!form.value.address.trim() },
    { que: 'CAI y fecha límite', ok: !!r && !r.isExpired && !r.isExhausted },
  ]
})

const faltan = computed(() => requisitos.value.filter((x) => !x.ok).length)

/** Hay rango, pero no sirve para emitir: venció o se agotó. */
const sinCai = computed(() => {
  const r = settings.value?.fiscalRange
  return !r || r.isExpired || r.isExhausted
})

/**
 * El rango que de verdad va a salir impreso, o null.
 *
 * La vista previa se dibuja con él y no con `settings.fiscalRange` a secas: un rango vencido
 * o agotado está guardado pero no imprime nada, y el papel tiene que enseñar eso.
 */
const rangoImpreso = computed(() => {
  const r = settings.value?.fiscalRange
  return !r || sinCai.value ? null : r
})

/** El número que sale mientras no haya CAI: el del comprobante propio. */
const numeroPropio = computed(
  () =>
    `${settings.value?.invoicePrefix ?? (form.value.invoicePrefix || 'C')}-${String(
      settings.value?.nextInvoiceNumber ?? 1,
    ).padStart(6, '0')}`,
)

/**
 * El aviso de arriba. Lo que no puede pasar es que alguien crea que está facturando cuando
 * está emitiendo comprobantes: eso se descubre cuando el taller lleva el papel a su contador.
 */
const alarma = computed(() => {
  const r = settings.value?.fiscalRange

  if (!r)
    return {
      titulo: 'Todavía no emite facturas fiscales',
      texto: `Sin CAI, cada pago sale como comprobante ${numeroPropio.value} con numeración propia.`,
      faltan: faltan.value,
    }

  if (r.isExpired)
    return {
      titulo: 'El CAI venció',
      texto: 'Hasta registrar el rango nuevo del SAR, cada pago saldrá como comprobante.',
      faltan: 0,
    }

  if (r.isExhausted)
    return {
      titulo: 'Se agotó el rango autorizado',
      texto: 'Hasta registrar el rango nuevo del SAR, cada pago saldrá como comprobante.',
      faltan: 0,
    }

  if (r.remaining <= 20)
    return {
      titulo: `Quedan ${r.remaining} números del rango`,
      texto: 'Pida el rango nuevo al SAR antes de que se acabe: sin números no se factura.',
      faltan: 0,
    }

  return null
})

/** Si el rango que se está escribiendo alcanza, y qué números va a cubrir. */
const elRango = computed(() => {
  const desde = Number(rango.value.rangeStart) || 0
  const hasta = Number(rango.value.rangeEnd) || 0
  const pieza = (n: number) =>
    `${rango.value.establishmentCode}-${rango.value.pointOfSaleCode}-${rango.value.documentType}-${String(n).padStart(8, '0')}`

  return {
    primera: pieza(desde),
    ultima: pieza(hasta),
    cuantas: hasta >= desde ? hasta - desde + 1 : 0,
  }
})

/** Si hay algo escrito que todavía no se guardó. Sin esto, «Guardar» es un botón a ciegas. */
const cambiado = computed(() => {
  const d = settings.value
  if (!d) return false

  return (
    form.value.name !== d.name ||
    form.value.legalName !== (d.legalName ?? '') ||
    form.value.taxId !== (d.taxId ?? '') ||
    form.value.phone !== (d.phone ?? '') ||
    form.value.email !== (d.email ?? '') ||
    form.value.address !== (d.address ?? '') ||
    Number(form.value.defaultTaxRate) !== d.defaultTaxRate ||
    form.value.pricesIncludeTax !== d.pricesIncludeTax ||
    form.value.invoicePrefix !== d.invoicePrefix
  )
})

function descartar() {
  if (settings.value) llenar(settings.value)
  guardado.value = ''
}

/**
 * El documento tal como le llega al taller, con los números del formulario. Es la única forma
 * de ver qué cambia una casilla antes de que lo vea un cliente.
 */
const muestra = computed(() => {
  const tasa = Number(form.value.defaultTaxRate) || 0
  const cuota = 850

  // El ISV solo entra en la factura con CAI: el comprobante no desglosa impuesto. Por eso la
  // previa deja de enseñarlo en cuanto el documento que saldría es un comprobante.
  if (tasa <= 0 || !rangoImpreso.value) return { base: cuota, isv: 0, total: cuota }

  if (form.value.pricesIncludeTax) {
    const base = Math.round((cuota / (1 + tasa / 100)) * 100) / 100
    return { base, isv: Math.round((cuota - base) * 100) / 100, total: cuota }
  }

  const isv = Math.round(cuota * (tasa / 100) * 100) / 100
  return { base: cuota, isv, total: Math.round((cuota + isv) * 100) / 100 }
})



function llenar(data: PlatformSettings) {
  settings.value = data
  form.value = {
    name: data.name,
    legalName: data.legalName ?? '',
    taxId: data.taxId ?? '',
    phone: data.phone ?? '',
    email: data.email ?? '',
    address: data.address ?? '',
    defaultTaxRate: data.defaultTaxRate,
    pricesIncludeTax: data.pricesIncludeTax,
    invoicePrefix: data.invoicePrefix,
  }
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    llenar(await platformApi.billingSettings())
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar los datos de facturación.')
  } finally {
    loading.value = false
  }
}

async function guardar() {
  busy.value = true
  error.value = ''
  guardado.value = ''
  try {
    llenar(
      await platformApi.saveBillingSettings({
        name: form.value.name.trim(),
        legalName: form.value.legalName.trim() || null,
        taxId: form.value.taxId.trim() || null,
        phone: form.value.phone.trim() || null,
        email: form.value.email.trim() || null,
        address: form.value.address.trim() || null,
        defaultTaxRate: Number(form.value.defaultTaxRate) || 0,
        pricesIncludeTax: form.value.pricesIncludeTax,
        invoicePrefix: form.value.invoicePrefix.trim() || 'C',
      }),
    )
    guardado.value = 'Guardado.'
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo guardar.')
  } finally {
    busy.value = false
  }
}

async function guardarRango() {
  if (!rango.value.cai.trim() || !rango.value.issueDeadline) return

  busy.value = true
  error.value = ''
  try {
    llenar(
      await platformApi.saveFiscalRange({
        cai: rango.value.cai.trim(),
        establishmentCode: rango.value.establishmentCode.trim(),
        pointOfSaleCode: rango.value.pointOfSaleCode.trim(),
        documentType: rango.value.documentType.trim(),
        rangeStart: Number(rango.value.rangeStart) || 1,
        rangeEnd: Number(rango.value.rangeEnd) || 1,
        issueDeadline: new Date(rango.value.issueDeadline).toISOString(),
      }),
    )
    abrirRango.value = false
    rango.value.cai = ''
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo registrar el CAI.')
  } finally {
    busy.value = false
  }
}

onMounted(load)
</script>

<template>
  <section>
    <header class="top">
      <h1>Facturación</h1>
      <p class="muted small">
        Lo que sale impreso en el documento que recibe cada taller al pagar. El documento se
        emite al registrar el pago, en la ficha del taller.
      </p>
    </header>

    <ErrorNote v-if="error" :message="error" />
    <p v-if="guardado" class="ok">{{ guardado }}</p>

    <!-- El CAI por acabarse no es un detalle de una tarjeta: si se agota, no se puede emitir
         nada, de ningún servicio. Va arriba y en todas las pestañas. -->
    <section v-if="alarma" class="alarma">
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"
           stroke-width="2" stroke-linecap="round" aria-hidden="true">
        <path d="M12 3l9.5 17h-19z" />
        <path d="M12 10v4M12 17v.5" />
      </svg>

      <div>
        <strong>{{ alarma.titulo }}</strong>
        <p>
          {{ alarma.texto }}
          <template v-if="alarma.faltan">
            Faltan {{ alarma.faltan }} {{ alarma.faltan === 1 ? 'dato' : 'datos' }} para facturar.
          </template>
        </p>
      </div>

      <button type="button" @click="irAlCai">
        {{ alarma.faltan ? 'Completar datos' : 'Ver la autorización' }}
      </button>
    </section>

    <div class="pestanas">
      <button
        v-for="p in [
          { clave: 'emisor' as const, label: 'Emisor y CAI' },
          { clave: 'servicios' as const, label: 'Servicios' },
          { clave: 'documentos' as const, label: 'Documentos emitidos' },
        ]"
        :key="p.clave"
        type="button"
        class="pestana"
        :class="{ activa: pestana === p.clave }"
        @click="abrirPestana(p.clave)"
      >
        {{ p.label }}
      </button>
    </div>

    <div v-show="pestana === 'emisor'" class="emisor">
      <div class="pasos">
        <!-- Numerados porque es una secuencia: sin quién emite no hay CAI que valga, y sin
             CAI lo de abajo no sale como factura. -->
        <section class="card">
          <header class="paso">
            <span class="numero">1</span>
            <div>
              <h2>Quién emite</h2>
              <p class="muted small">Tal como aparece en su RTN.</p>
            </div>
          </header>

          <div class="campos">
            <label>
              <span class="rotulo">Nombre comercial</span>
              <input v-model="form.name" required />
            </label>

            <label :class="{ falta: !form.legalName.trim() }">
              <span class="rotulo">
                Razón social
                <em v-if="!form.legalName.trim()">Falta</em>
              </span>
              <input v-model="form.legalName" placeholder="Como está en el RTN" />
            </label>

            <label :class="{ falta: !form.taxId.trim() }">
              <span class="rotulo">
                RTN
                <em v-if="!form.taxId.trim()">Falta</em>
              </span>
              <input v-model="form.taxId" class="mono" placeholder="14 dígitos, ej. 08011999123456" />
            </label>

            <label>
              <span class="rotulo">Teléfono</span>
              <input v-model="form.phone" placeholder="+504 9824 2108" />
            </label>

            <label>
              <span class="rotulo">Correo</span>
              <input v-model="form.email" type="email" placeholder="soporte@garajeapp.com" />
            </label>

            <label :class="{ falta: !form.address.trim() }">
              <span class="rotulo">
                Dirección
                <em v-if="!form.address.trim()">Falta</em>
              </span>
              <input v-model="form.address" placeholder="Colonia, ciudad, departamento" />
            </label>
          </div>
        </section>

        <section class="card">
          <header class="paso">
            <span class="numero">2</span>
            <div>
              <h2>Cómo se cobra el ISV</h2>
              <p class="muted small">
                Solo entra en la factura con CAI: el comprobante no lleva impuesto.
              </p>
            </div>
          </header>

          <!-- La misma decisión que el taller toma para sus precios, de este lado del
               negocio. Con el número hecho en cada opción: «neto» y «bruto» no le dicen a
               nadie cuánto va a depositar el taller. -->
          <div v-if="form.defaultTaxRate > 0" class="modelo">
            <label class="radio" :class="{ elegido: !form.pricesIncludeTax }">
              <input v-model="form.pricesIncludeTax" type="radio" :value="false" />
              <span>
                <strong>Se suma a la cuota</strong>
                <small class="muted">
                  Cuota {{ formatMoney(850) }} → con factura deposita
                  {{ formatMoney(850 * (1 + Number(form.defaultTaxRate) / 100)) }}; con
                  comprobante, {{ formatMoney(850) }}
                </small>
              </span>
            </label>

            <label class="radio" :class="{ elegido: form.pricesIncludeTax }">
              <input v-model="form.pricesIncludeTax" type="radio" :value="true" />
              <span>
                <strong>Ya viene incluido</strong>
                <small class="muted">
                  Cuota {{ formatMoney(850) }} → deposita {{ formatMoney(850) }}, y con
                  factura el ISV se desglosa de adentro
                </small>
              </span>
            </label>
          </div>

          <div class="campos dos">
            <label>
              <span class="rotulo">ISV</span>
              <input
                v-model.number="form.defaultTaxRate"
                type="number"
                min="0"
                max="100"
                step="0.01"
              />
            </label>

            <label>
              <span class="rotulo">Prefijo del comprobante</span>
              <input v-model="form.invoicePrefix" maxlength="8" />
              <small class="muted">
                Solo se usa mientras no haya CAI: {{ form.invoicePrefix || 'C' }}-000001,
                {{ form.invoicePrefix || 'C' }}-000002…
              </small>
            </label>
          </div>
        </section>

        <section id="cai" class="card">
          <header class="paso">
            <span class="numero">3</span>
            <div>
              <h2>Autorización del SAR (CAI)</h2>
              <p class="muted small">Cópielo de la resolución que le dio el SAR.</p>
            </div>
            <span class="chip" :class="settings?.fiscalRange && !sinCai ? 'ok' : 'aviso'">
              {{ settings?.fiscalRange ? (sinCai ? 'No sirve' : 'Vigente') : 'Sin CAI' }}
            </span>
          </header>

          <dl v-if="settings?.fiscalRange && !abrirRango" class="lectura">
            <dt>CAI</dt>
            <dd class="mono">{{ settings.fiscalRange.cai }}</dd>
            <dt>Rango</dt>
            <dd class="mono">{{ settings.fiscalRange.rangeText }}</dd>
            <dt>Siguiente</dt>
            <dd class="mono">{{ settings.fiscalRange.nextFiscalNumber }}</dd>
            <dt>Fecha límite</dt>
            <dd>{{ formatDay(settings.fiscalRange.issueDeadline) }}</dd>
            <dt>Quedan</dt>
            <dd>{{ settings.fiscalRange.remaining }} números</dd>
          </dl>

          <p v-else-if="!abrirRango" class="muted small">
            Todavía no hay CAI registrado. Mientras no lo haya, cada pago deja un
            <strong>comprobante</strong> con numeración propia ({{ numeroPropio }}), que no es
            una factura fiscal y el documento lo dice.
          </p>

          <button v-if="!abrirRango" type="button" class="link" @click="abrirRango = true">
            {{ settings?.fiscalRange ? 'Registrar el rango nuevo' : 'Registrar el CAI' }}
          </button>

          <form v-else class="rango" @submit.prevent="guardarRango">
            <label :class="{ falta: !rango.cai.trim() }">
              <span class="rotulo">
                CAI
                <em v-if="!rango.cai.trim()">Falta</em>
              </span>
              <input
                v-model="rango.cai"
                class="mono"
                placeholder="XXXXXX-XXXXXX-XXXXXX-XXXXXX-XXXXXX-XX"
                required
              />
            </label>

            <fieldset>
              <legend>Número de factura</legend>
              <div class="campos tres">
                <label>
                  <span class="rotulo">Establecimiento</span>
                  <input v-model="rango.establishmentCode" class="mono" maxlength="3" />
                </label>
                <label>
                  <span class="rotulo">Punto de emisión</span>
                  <input v-model="rango.pointOfSaleCode" class="mono" maxlength="3" />
                </label>
                <label>
                  <span class="rotulo">Tipo de documento</span>
                  <input v-model="rango.documentType" class="mono" maxlength="2" />
                </label>
              </div>
            </fieldset>

            <fieldset>
              <legend>Rango autorizado</legend>
              <div class="campos tres">
                <label>
                  <span class="rotulo">Desde</span>
                  <input v-model.number="rango.rangeStart" type="number" min="1" />
                </label>
                <label>
                  <span class="rotulo">Hasta</span>
                  <input v-model.number="rango.rangeEnd" type="number" min="1" />
                </label>
                <label>
                  <span class="rotulo">Fecha límite de emisión</span>
                  <input v-model="rango.issueDeadline" type="date" required />
                </label>
              </div>
            </fieldset>

            <!-- Los números armados, antes de guardar: así se ve si se copió mal un código
                 del SAR, que después no se puede corregir sin dar de baja el rango. -->
            <div class="resumen-rango">
              <div>
                <span class="muted small">Primera factura</span>
                <strong class="mono">{{ elRango.primera }}</strong>
              </div>
              <div>
                <span class="muted small">Última factura</span>
                <strong class="mono">{{ elRango.ultima }}</strong>
              </div>
              <div>
                <span class="muted small">Alcanza para</span>
                <strong>{{ elRango.cuantas.toLocaleString('es-HN') }} facturas</strong>
              </div>
            </div>

            <div class="actions">
              <button type="submit" :disabled="busy || elRango.cuantas < 1">Registrar</button>
              <button type="button" class="link" @click="abrirRango = false">Cancelar</button>
            </div>

            <p class="muted small">
              El rango que estuviera activo queda dado de baja: emitir con dos rangos vivos es
              la forma de saltarse un número sin enterarse.
            </p>
          </form>
        </section>

        <!-- Guardar vive al pie de los tres pasos, no dentro del primero: lo que se guarda es
             el emisor entero. -->
        <div class="guardar">
          <span v-if="cambiado" class="muted small">Hay cambios sin guardar</span>
          <span v-else-if="guardado" class="muted small">{{ guardado }}</span>
          <button type="button" class="btn-ghost" :disabled="!cambiado || busy" @click="descartar">
            Descartar
          </button>
          <button type="button" :disabled="!cambiado || busy" @click="guardar">
            Guardar cambios
          </button>
        </div>
      </div>

      <aside class="lado">
        <section class="card">
          <h2>Para facturar con CAI falta</h2>
          <ul class="requisitos">
            <li v-for="r in requisitos" :key="r.que" :class="{ listo: r.ok }">
              <span class="marca" aria-hidden="true">{{ r.ok ? '✓' : '!' }}</span>
              {{ r.que }}
            </li>
          </ul>
        </section>

        <section>
          <header class="lado-top">
            <h2>Así lo recibe el taller</h2>
            <span class="muted small">Vista previa</span>
          </header>

          <!-- El papel, con los números del formulario. Es la única forma de ver qué cambia
               una casilla antes de que lo vea un cliente. -->
          <article class="papel">
            <div class="papel-top">
              <div>
                <!-- El mismo logotipo que lleva el PDF, y por eso tampoco se repite el nombre
                     debajo: lo que se ve aquí tiene que ser lo que recibe el cliente. -->
                <img class="papel-marca" src="/brand/naos-apps-horizontal.png" alt="NAOS Apps" />
                <div class="fuerte" :class="{ hueco: !form.legalName.trim() }">
                  {{ form.legalName.trim() || '[Razón social]' }}
                </div>
                <div :class="{ hueco: !form.taxId.trim() }">
                  RTN {{ form.taxId.trim() || '[falta]' }}
                </div>
                <div v-if="form.address.trim()" class="tenue">{{ form.address.trim() }}</div>
                <div class="tenue">{{ [form.phone, form.email].filter(Boolean).join(' · ') }}</div>
              </div>

              <div class="papel-numero">
                <span class="papel-tipo">
                  {{ rangoImpreso ? 'FACTURA' : 'COMPROBANTE DE PAGO' }}
                </span>
                <div class="papel-folio mono">
                  {{ rangoImpreso ? rangoImpreso.nextFiscalNumber : numeroPropio }}
                </div>
                <div class="tenue">
                  Emitida el {{ formatDay(new Date().toISOString().slice(0, 10)) }}
                </div>

                <!-- Lo que el régimen pide impreso, en el mismo recuadro que lleva el PDF. -->
                <dl v-if="rangoImpreso" class="papel-cai">
                  <dt>CAI</dt>
                  <dd class="mono">{{ rangoImpreso.cai }}</dd>
                  <dt>Rango autorizado</dt>
                  <dd class="mono">{{ rangoImpreso.rangeText }}</dd>
                  <dt>Fecha límite de emisión</dt>
                  <dd>{{ formatDay(rangoImpreso.issueDeadline.slice(0, 10)) }}</dd>
                </dl>
              </div>
            </div>

            <div class="papel-regla"></div>

            <div class="papel-partes">
              <div>
                <span class="papel-rotulo">Cliente</span>
                <strong>Taller de ejemplo</strong>
              </div>
              <div class="derecha">
                <span class="papel-rotulo">Servicio</span>
                <strong>GarajApp</strong>
              </div>
            </div>

            <table class="papel-tabla">
              <thead>
                <tr>
                  <th>Concepto</th>
                  <th class="num">Cant.</th>
                  <th class="num">Total</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td>Mensualidad GarajApp · 1 mes</td>
                  <td class="num">1</td>
                  <td class="num mono">{{ formatMoney(muestra.base) }}</td>
                </tr>
              </tbody>
            </table>

            <div class="papel-totales">
              <div class="papel-linea">
                <span class="tenue">{{ muestra.isv > 0 ? 'Neto' : 'Subtotal' }}</span>
                <span class="mono">{{ formatMoney(muestra.base) }}</span>
              </div>
              <div v-if="muestra.isv > 0" class="papel-linea">
                <span class="tenue">ISV {{ form.defaultTaxRate }}%</span>
                <span class="mono">{{ formatMoney(muestra.isv) }}</span>
              </div>
              <div class="papel-linea total">
                <span>TOTAL</span>
                <span class="mono">{{ formatMoney(muestra.total) }}</span>
              </div>
            </div>

            <p class="papel-pie">
              {{ rangoImpreso
                ? 'Documento fiscal autorizado por el SAR.'
                : 'Este documento es un comprobante de pago y no sustituye una factura fiscal.' }}
            </p>
          </article>
        </section>
      </aside>
    </div>

    <!-- ------------------------------------------------------------- los servicios -->

    <div v-show="pestana === 'servicios'">
      <p class="muted small intro">
        Lo que cobramos con este mismo RTN. Cada uno lleva una llave con la que su portal pide
        el número de sus facturas: el padrón de clientes vive allá, el CAI vive aquí.
      </p>

      <!-- Se enseña una sola vez: no queda guardada en ningún lado. -->
      <article v-if="llaveNueva" class="card credenciales">
        <h2>Llave de {{ llaveNueva.name }}</h2>
        <p>
          Cópiela ahora y póngala en su portal.
          <strong>No se puede volver a consultar</strong>: solo queda su huella.
        </p>
        <p class="mono llave">{{ llaveNueva.apiKey }}</p>
        <p class="muted small">
          Va en la cabecera <code>X-Billing-Key</code>, nunca en la dirección.
        </p>
        <button type="button" class="link" @click="llaveNueva = null">Ya la copié</button>
      </article>

      <div class="tabla">
        <table>
          <thead>
            <tr>
              <th>Servicio</th>
              <th>Llave</th>
              <th class="num">Documentos</th>
              <th>Estado</th>
              <th><span class="sr-only">Acciones</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="s in servicios" :key="s.id" :class="{ apagado: !s.isActive }">
              <td><strong>{{ s.name }}</strong></td>
              <td class="small">
                <!-- Nosotros no pedimos número por la API: lo tomamos aquí mismo, con el pago
                     del taller. Ofrecerle una llave sería repartir una credencial que nadie
                     va a usar. -->
                <span v-if="s.billsFromConsole" class="muted">
                  No la necesita: factura desde esta consola
                </span>
                <template v-else-if="s.apiKeyHint">
                  <span class="mono">…{{ s.apiKeyHint }}</span>
                  <div class="muted">desde {{ formatDay(s.apiKeyIssuedOn) }}</div>
                </template>
                <span v-else class="muted">sin llave</span>
              </td>
              <td class="num">
                <button
                  v-if="s.invoiceCount"
                  type="button"
                  class="link"
                  @click="verLosDe(s)"
                >
                  {{ s.invoiceCount }}
                </button>
                <span v-else class="muted">0</span>
              </td>
              <td>{{ s.isActive ? 'Activo' : 'Apagado' }}</td>
              <td class="acciones-fila">
                <template v-if="!s.billsFromConsole">
                  <button type="button" class="link" :disabled="busy" @click="rotarLlave(s)">
                    {{ s.apiKeyHint ? 'Llave nueva' : 'Generar llave' }}
                  </button>
                  <button type="button" class="link" :disabled="busy" @click="alternarServicio(s)">
                    {{ s.isActive ? 'Apagar' : 'Encender' }}
                  </button>
                </template>
                <span v-else class="muted small">—</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <form class="alta" @submit.prevent="crearServicio">
        <label>
          Otro servicio
          <input v-model="servicioNuevo" placeholder="Cómo se llama" maxlength="80" />
        </label>
        <button type="submit" :disabled="busy || !servicioNuevo.trim()">Dar de alta</button>
      </form>

      <p class="muted small">
        Apagar un servicio le corta la emisión sin borrarle el historial. Sus documentos siguen
        en el libro: el número existió.
      </p>
      <p class="muted small">
        <strong>GarajApp no lleva llave</strong> porque no pide el número por la API: lo toma
        aquí mismo al registrar el pago del taller. La llave es para los sistemas que están
        afuera.
      </p>
    </div>

    <!-- ------------------------------------------------------------ los documentos -->

    <div v-show="pestana === 'documentos'">
      <div class="barra">
        <input
          v-model="buscado"
          type="search"
          class="buscar"
          placeholder="Número, cliente o RTN"
          aria-label="Buscar un documento"
          @keyup.enter="buscarDesdeElPrincipio"
        />
        <select v-model="servicioFiltrado" @change="buscarDesdeElPrincipio">
          <option value="">Todos los servicios</option>
          <option v-for="s in servicios" :key="s.id" :value="s.id">{{ s.name }}</option>
        </select>
        <button type="button" :disabled="buscando" @click="buscarDesdeElPrincipio">Buscar</button>
        <span class="separa" />
        <RouterLink :to="{ name: 'platform-sales-book' }" class="btn-ghost alLibro">
          Libro de ventas
        </RouterLink>
        <button type="button" class="btn-ghost" @click="emitiendo = !emitiendo">
          {{ emitiendo ? 'Cancelar' : 'Emitir a mano' }}
        </button>
      </div>

      <form v-if="emitiendo" class="card aMano" @submit.prevent="emitirAMano">
        <h2>Emitir a mano</h2>
        <p class="muted small">
          Para el servicio que todavía no está integrado y para la factura suelta.
          <strong>Consume un número igual que cualquier otra emisión.</strong>
        </p>

        <div class="row">
          <label>
            Servicio
            <select v-model="aMano.productId" required>
              <option value="" disabled>Elija</option>
              <option v-for="s in servicios.filter((x) => x.isActive)" :key="s.id" :value="s.id">
                {{ s.name }}
              </option>
            </select>
          </label>
          <label>
            A nombre de
            <input v-model="aMano.name" maxlength="200" required />
          </label>
          <label>
            RTN
            <input v-model="aMano.taxId" maxlength="20" placeholder="Sin él, consumidor final" />
          </label>
        </div>

        <div class="row">
          <label>
            Razón social
            <input v-model="aMano.legalName" maxlength="200" />
          </label>
          <label>
            Referencia del pago
            <input v-model="aMano.paymentReference" maxlength="80" placeholder="Depósito, transferencia" />
          </label>
          <label class="check">
            <input v-model="aMano.fiscal" type="checkbox" />
            Con CAI
          </label>
        </div>

        <div v-for="(l, i) in aMano.lines" :key="i" class="row linea">
          <label>
            Concepto
            <input v-model="l.description" maxlength="200" placeholder="Mensualidad de noviembre" />
          </label>
          <label class="corto">
            Cantidad
            <input v-model.number="l.quantity" type="number" min="0.01" step="0.01" />
          </label>
          <label class="corto">
            Precio
            <input v-model.number="l.unitPrice" type="number" min="0" step="0.01" />
          </label>
          <button
            type="button"
            class="link"
            :disabled="aMano.lines.length === 1"
            @click="quitarLinea(i)"
          >
            Quitar
          </button>
        </div>

        <div class="actions">
          <button type="button" class="link" @click="agregarLinea">Otro concepto</button>
          <span class="separa" />
          <strong>{{ formatMoney(totalAMano) }}</strong>
          <button type="submit" :disabled="busy || !aMano.productId || !aMano.name.trim()">
            Emitir
          </button>
        </div>

        <p class="muted small">
          El ISV sale como diga «Cómo se cobra», arriba en Emisor. Lo que se escriba aquí se
          congela en el documento.
        </p>
      </form>

      <SkeletonList v-if="buscando" :rows="5" variant="tabla" />

      <p v-else-if="!documentos.length" class="muted">
        {{ buscado ? 'Ningún documento con esa búsqueda.' : 'Todavía no se ha emitido ninguno.' }}
      </p>

      <template v-else>
        <div class="tabla">
          <table>
            <thead>
              <tr>
                <th>Fecha</th>
                <th>Número</th>
                <th>Cliente</th>
                <th>RTN</th>
                <th>Servicio</th>
                <th class="num">Total</th>
                <th><span class="sr-only">Acciones</span></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="d in documentos" :key="d.id" :class="{ anulado: d.isVoided }">
                <td>{{ formatDay(d.issuedOn) }}</td>
                <td class="mono">
                  {{ d.number }}
                  <div v-if="!d.isFiscal" class="muted small">comprobante</div>
                </td>
                <td>{{ d.customerName }}</td>
                <td class="mono small">{{ d.customerTaxId ?? 'Consumidor final' }}</td>
                <td class="small">{{ d.productName ?? '—' }}</td>
                <td class="num">{{ formatMoney(d.total) }}</td>
                <td class="acciones-fila">
                  <button type="button" class="link" @click="bajarPdf(d)">PDF</button>
                  <button
                    v-if="!d.isVoided"
                    type="button"
                    class="link danger"
                    :disabled="busy"
                    @click="anular(d)"
                  >
                    Anular
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div v-if="paginas > 1" class="paginas">
          <button type="button" class="link" :disabled="pagina === 1" @click="irA(pagina - 1)">
            Anteriores
          </button>
          <span class="muted small">Página {{ pagina }} de {{ paginas }} · {{ totalDocumentos }} documentos</span>
          <button
            type="button"
            class="link"
            :disabled="pagina === paginas"
            @click="irA(pagina + 1)"
          >
            Siguientes
          </button>
        </div>
      </template>
    </div>
  </section>
</template>

<style scoped>
h1 {
  margin: 0;
}

.top {
  margin-bottom: 1rem;
}

.ok {
  color: var(--success-text);
}

/* El CAI por acabarse para toda la plataforma: no cabe en una esquina. */
/* Emitir comprobantes creyendo que son facturas se descubre cuando el taller lleva el papel
   a su contador. Por eso el aviso es un panel con su acción, no un renglón más. */
.alarma {
  display: flex;
  align-items: center;
  gap: 0.875rem;
  flex-wrap: wrap;
  margin: 0 0 1.25rem;
  padding: 0.875rem 1.125rem;
  border: 1px solid color-mix(in srgb, var(--warning) 45%, var(--border));
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--warning) 8%, transparent);
}

.alarma svg {
  flex: none;
  color: var(--warning);
}

.alarma > div {
  flex: 1 1 18rem;
}

.alarma p {
  margin: 0.2rem 0 0;
  color: var(--text-muted);
  line-height: 1.5;
}

.pestanas {
  display: flex;
  gap: 0.25rem;
  flex-wrap: wrap;
  margin-bottom: 1rem;
}

.pestana {
  padding: 0.35rem 0.75rem;
  border: 1px solid transparent;
  border-radius: 999px;
  background: none;
  color: var(--text-muted);
  font-size: 0.9375rem;
  cursor: pointer;
}

.pestana.activa {
  border-color: color-mix(in srgb, var(--brand) 40%, var(--border));
  background: color-mix(in srgb, var(--brand) 10%, transparent);
  color: var(--brand);
  font-weight: 600;
}

.intro {
  margin: 0 0 0.75rem;
  max-width: 44rem;
}

.tabla {
  overflow-x: auto;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  margin-bottom: 0.75rem;
}

.tabla table {
  width: 100%;
  border-collapse: collapse;
  white-space: nowrap;
}

.tabla th,
.tabla td {
  padding: 0.5rem 0.75rem;
  border-top: 1px solid var(--border);
  text-align: left;
  vertical-align: top;
}

.tabla thead th {
  border-top: 0;
  color: var(--text-muted);
  font-size: var(--text-xs);
  font-weight: 600;
}

.num {
  text-align: right;
}

.acciones-fila {
  display: flex;
  gap: 0.75rem;
}

tbody tr.apagado,
tbody tr.anulado {
  color: var(--text-muted);
}

tbody tr.anulado .mono {
  text-decoration: line-through;
}

.llave {
  padding: 0.5rem 0.625rem;
  border-radius: var(--radius-sm);
  background: var(--surface-alt);
  word-break: break-all;
}

.alta {
  display: flex;
  align-items: flex-end;
  gap: 0.75rem;
  margin: 0.75rem 0;
}

.barra {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-bottom: 0.75rem;
}

.buscar {
  flex: 1 1 16rem;
  max-width: 22rem;
}

.separa {
  flex: 1;
}

.aMano .row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.aMano .row label {
  flex: 1 1 12rem;
}

.aMano .row label.corto {
  flex: 0 1 7rem;
}

.aMano .check {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  flex: 0 1 auto;
}

.aMano .linea {
  align-items: flex-end;
}

.aMano .actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.paginas {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.grid {
  display: grid;
  grid-template-columns: 1fr 22rem;
  gap: 1rem;
  align-items: start;
}

@media (max-width: 60rem) {
  .grid {
    grid-template-columns: 1fr;
  }
}

.card {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.card h2 {
  margin: 0;
  font-size: 1rem;
}

.row {
  display: flex;
  gap: 0.75rem;
}

.row label {
  flex: 1;
  min-width: 0;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.modelo {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.radio {
  flex-direction: row;
  align-items: flex-start;
  gap: 0.5rem;
  color: var(--text);
}

.radio span {
  display: flex;
  flex-direction: column;
}

.ejemplo {
  padding: 0.625rem 0.75rem;
  border-radius: var(--radius-sm);
  background: color-mix(in srgb, var(--accent) 10%, transparent);
  font-size: 0.875rem;
}

.aviso {
  padding: 0.5rem 0.75rem;
  border-radius: var(--radius-sm);
  background: color-mix(in srgb, var(--warning) 14%, transparent);
  color: var(--warning-text);
  font-size: 0.875rem;
}

.actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

dl {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.25rem 0.75rem;
  margin: 0;
  font-size: 0.875rem;
}

dt {
  color: var(--text-muted);
}

dd {
  margin: 0;
  text-align: right;
}

.mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
}

.rango {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

/* El enlace al libro va con los botones de la barra, así que se viste como ellos. */
.alLibro {
  display: inline-flex;
  align-items: center;
  text-decoration: none;
}

/* Los tres pasos a la izquierda, lo que falta y el papel a la derecha. */
.emisor {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 22rem;
  gap: 1.5rem;
  align-items: start;
}

.pasos {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  min-width: 0;
}

.lado {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  position: sticky;
  top: 1rem;
}

.lado-top {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.lado-top h2 {
  margin: 0;
  font-size: 1rem;
}

/* La cabecera de cada paso: el número, el título y, en el CAI, su estado. */
.paso {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.paso h2 {
  margin: 0;
  font-size: 1.0625rem;
}

.paso p {
  margin: 0.1rem 0 0;
}

.paso > div:first-of-type {
  flex: 1 1 12rem;
}

.numero {
  display: flex;
  align-items: center;
  justify-content: center;
  flex: none;
  width: 1.625rem;
  height: 1.625rem;
  border-radius: 50%;
  background: color-mix(in srgb, var(--brand) 16%, transparent);
  color: var(--brand);
  font-size: 0.8125rem;
  font-weight: 700;
}

.chip {
  display: inline-flex;
  align-items: center;
  padding: 0.15rem 0.55rem;
  border-radius: 999px;
  font-size: var(--text-xs);
  font-weight: 600;
}

.chip.ok {
  background: color-mix(in srgb, var(--success) 14%, transparent);
  color: var(--success);
}

.chip.aviso {
  background: color-mix(in srgb, var(--warning) 16%, transparent);
  color: var(--warning);
}

.campos {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
  gap: 0.875rem;
}

.campos.dos {
  grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
}

.campos.tres {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.campos label {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
  min-width: 0;
}

.rotulo {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 0.5rem;
  font-size: 0.8125rem;
  font-weight: 500;
}

/* Lo que falta se dice en el campo, no solo en la lista de al lado: ahí es donde se escribe. */
.rotulo em {
  color: var(--warning);
  font-size: var(--text-xs);
  font-style: normal;
  font-weight: 500;
}

.campos label.falta input {
  border-color: color-mix(in srgb, var(--warning) 50%, var(--border));
}

.radio {
  padding: 0.625rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
}

/* La opción elegida se nota: dos radios iguales no dicen cuál está puesta. */
.radio.elegido {
  border-color: color-mix(in srgb, var(--brand) 50%, var(--border));
  background: color-mix(in srgb, var(--brand) 8%, transparent);
}

.rango fieldset {
  margin: 0;
  padding: 0;
  border: 0;
}

.rango legend {
  padding: 0 0 0.4rem;
  font-size: 0.8125rem;
  font-weight: 500;
}

/* Los números armados antes de guardar: copiar mal un código del SAR no se corrige después
   sin dar de baja el rango entero. */
.resumen-rango {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem 1.5rem;
  justify-content: space-between;
  padding: 0.75rem 0.875rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--bg, var(--surface));
}

.resumen-rango > div {
  display: grid;
  gap: 0.1rem;
}

.guardar {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.625rem;
  flex-wrap: wrap;
}

/* Lo que falta para facturar: la pregunta es «¿ya puedo?» y la respuesta son cinco casillas. */
.requisitos {
  display: grid;
  gap: 0.4rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.requisitos li {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.875rem;
}

.marca {
  display: flex;
  align-items: center;
  justify-content: center;
  flex: none;
  width: 1.125rem;
  height: 1.125rem;
  border-radius: 50%;
  background: color-mix(in srgb, var(--warning) 20%, transparent);
  color: var(--warning);
  font-size: 0.6875rem;
  font-weight: 700;
}

.requisitos li.listo .marca {
  background: color-mix(in srgb, var(--success) 18%, transparent);
  color: var(--success);
}

/* El papel. Claro siempre, en los dos temas: es papel, y así se imprime. Los colores son
   los del PDF —el azul del logotipo— porque esto tiene que ser lo mismo que se recibe. */
.papel {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 1.25rem;
  border: 1px solid #e3e5ea;
  border-radius: var(--radius-md);
  background: #fff;
  color: #201e1d;
  font-size: 0.8125rem;
}

.papel-top {
  display: flex;
  justify-content: space-between;
  gap: 0.75rem;
}

.papel-marca {
  height: 1.75rem;
  width: auto;
  margin-bottom: 0.4rem;
}

.papel .fuerte {
  font-weight: 600;
}

.papel-numero {
  text-align: right;
}

.papel-tipo {
  color: #0d2a7a;
  font-size: 0.6875rem;
  font-weight: 700;
  letter-spacing: 0.1em;
}

.papel-folio {
  font-size: 1.0625rem;
  font-weight: 600;
}

/* El bloque del CAI, en su recuadro: es lo que busca quien revisa el papel. */
.papel-cai {
  display: grid;
  gap: 0.05rem;
  margin: 0.5rem 0 0;
  padding: 0.5rem 0.6rem;
  background: #f3f5fb;
  text-align: left;
}

.papel-cai dt {
  color: #605d5d;
  font-size: 0.625rem;
  letter-spacing: 0.06em;
}

.papel-cai dd {
  margin: 0 0 0.25rem;
  font-size: 0.6875rem;
  word-break: break-all;
}

.papel-cai dd:last-child {
  margin-bottom: 0;
}

.papel-regla {
  border-top: 2px solid #0d2a7a;
}

.papel .tenue {
  color: #605d5d;
}

/* Lo que falta, en el papel, con el color del aviso: ahí es donde se ve lo que recibe. */
.papel .hueco {
  color: #8a5a00;
}

.papel-partes {
  display: flex;
  justify-content: space-between;
  gap: 0.75rem;
}

.papel-partes .derecha {
  text-align: right;
}

.papel-rotulo {
  display: block;
  color: #605d5d;
  font-size: 0.6875rem;
  letter-spacing: 0.06em;
}

.papel-tabla {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.75rem;
}

.papel-tabla th {
  padding: 0.3rem 0.4rem;
  background: #0d2a7a;
  color: #fff;
  font-weight: 600;
  text-align: left;
}

.papel-tabla td {
  padding: 0.4rem;
  border-bottom: 1px solid #dfe3ee;
}

.papel-tabla .num {
  text-align: right;
}

.papel-totales {
  display: grid;
  gap: 0.05rem;
}

.papel-linea {
  display: flex;
  justify-content: space-between;
  gap: 0.75rem;
  padding: 0.1rem 0.5rem;
}

.papel-linea.total {
  margin-top: 0.3rem;
  padding: 0.45rem 0.5rem;
  background: #f3f5fb;
  color: #0d2a7a;
  font-size: 0.9375rem;
  font-weight: 700;
}

.papel-pie {
  margin: 0;
  padding-top: 0.5rem;
  border-top: 1px solid #dfe3ee;
  color: #605d5d;
  font-size: 0.6875rem;
  line-height: 1.5;
}

@media (max-width: 64rem) {
  .emisor {
    grid-template-columns: 1fr;
  }

  .lado {
    position: static;
  }
}

@media (max-width: 34rem) {
  .campos.tres {
    grid-template-columns: 1fr;
  }
}
</style>

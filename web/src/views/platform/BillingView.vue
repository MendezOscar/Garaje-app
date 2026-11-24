<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
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
  name: 'GarajApp',
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
const ejemplo = computed(() => {
  const tasa = Number(form.value.defaultTaxRate) || 0
  const neto = 850

  if (tasa <= 0) return 'Sin ISV: la mensualidad de L 850.00 se factura por L 850.00.'

  if (form.value.pricesIncludeTax) {
    const impuesto = Math.round((neto - neto / (1 + tasa / 100)) * 100) / 100
    return `La mensualidad de L 850.00 se factura por L 850.00, desglosando L ${impuesto.toFixed(2)} de ISV.`
  }

  const impuesto = Math.round(neto * (tasa / 100) * 100) / 100
  return `La mensualidad de L 850.00 se factura por L ${(neto + impuesto).toFixed(2)}: L 850.00 más L ${impuesto.toFixed(2)} de ISV.`
})

const estadoDelCai = computed(() => {
  const r = settings.value?.fiscalRange
  if (!r) return null
  if (r.isExpired) return 'Venció: registre el rango nuevo del SAR.'
  if (r.isExhausted) return 'Se agotó: registre el rango nuevo del SAR.'
  if (r.remaining <= 20) return `Quedan ${r.remaining} números: pida el rango nuevo.`
  return null
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
    <p v-if="estadoDelCai" class="alarma">
      <strong>CAI:</strong> {{ estadoDelCai }}
      <button type="button" class="link" @click="abrirPestana('emisor')">Ver la autorización</button>
    </p>

    <div class="pestanas">
      <button
        v-for="p in [
          { clave: 'emisor' as const, label: 'Emisor y CAI' },
          { clave: 'servicios' as const, label: 'Servicios' },
          { clave: 'documentos' as const, label: 'Documentos' },
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

    <div v-show="pestana === 'emisor'" class="grid">
      <form class="card" @submit.prevent="guardar">
        <h2>Quién emite</h2>

        <div class="row">
          <label>
            Nombre
            <input v-model="form.name" required />
          </label>
          <label>
            Razón social
            <input v-model="form.legalName" placeholder="Como está en el RTN" />
          </label>
        </div>

        <div class="row">
          <label>
            RTN
            <input v-model="form.taxId" placeholder="0801…" />
          </label>
          <label>
            Teléfono
            <input v-model="form.phone" placeholder="+504 9824 2108" />
          </label>
        </div>

        <div class="row">
          <label>
            Correo
            <input v-model="form.email" type="email" placeholder="soporte@garajeapp.com" />
          </label>
          <label>
            Dirección
            <input v-model="form.address" />
          </label>
        </div>

        <h2>Cómo se cobra</h2>

        <div class="row">
          <label>
            ISV (%)
            <input v-model.number="form.defaultTaxRate" type="number" min="0" max="100" step="0.01" />
          </label>
          <label>
            Prefijo del comprobante
            <input v-model="form.invoicePrefix" maxlength="8" />
            <small class="muted">El de la numeración propia, la de cuando no hay CAI.</small>
          </label>
        </div>

        <!-- La misma decisión que el taller toma para sus precios, de este lado del negocio:
             si la mensualidad es neta o ya trae el impuesto adentro. -->
        <div v-if="form.defaultTaxRate > 0" class="modelo">
          <label class="radio">
            <input v-model="form.pricesIncludeTax" type="radio" :value="false" />
            <span>
              <strong>La mensualidad es neta: el ISV se suma</strong>
              <small class="muted">El taller deposita la mensualidad más el impuesto.</small>
            </span>
          </label>
          <label class="radio">
            <input v-model="form.pricesIncludeTax" type="radio" :value="true" />
            <span>
              <strong>Ya lleva el ISV adentro</strong>
              <small class="muted">El documento solo desglosa cuánto de ese total es impuesto.</small>
            </span>
          </label>
        </div>

        <p class="ejemplo">{{ ejemplo }}</p>

        <div class="actions">
          <button type="submit" :disabled="busy">Guardar</button>
          <span v-if="guardado" class="muted small">{{ guardado }}</span>
        </div>
      </form>

      <article class="card">
        <h2>Autorización del SAR</h2>

        <template v-if="settings?.fiscalRange">
          <dl>
            <dt>CAI</dt>
            <dd class="mono">{{ settings.fiscalRange.cai }}</dd>
            <dt>Rango</dt>
            <dd class="mono">{{ settings.fiscalRange.rangeText }}</dd>
            <dt>Siguiente</dt>
            <dd class="mono">{{ settings.fiscalRange.nextFiscalNumber }}</dd>
            <dt>Vence</dt>
            <dd>{{ formatDay(settings.fiscalRange.issueDeadline) }}</dd>
            <dt>Quedan</dt>
            <dd>{{ settings.fiscalRange.remaining }}</dd>
          </dl>

          <p v-if="estadoDelCai" class="aviso">{{ estadoDelCai }}</p>
        </template>

        <p v-else class="muted small">
          Todavía no hay CAI registrado. Mientras no lo haya, cada pago deja un
          <strong>comprobante</strong> con numeración propia
          ({{ settings?.invoicePrefix ?? 'C' }}-{{
            String(settings?.nextInvoiceNumber ?? 1).padStart(6, '0')
          }}), que no es una factura fiscal y el documento lo dice.
        </p>

        <button v-if="!abrirRango" type="button" class="link" @click="abrirRango = true">
          {{ settings?.fiscalRange ? 'Registrar el rango nuevo' : 'Registrar el CAI' }}
        </button>

        <form v-else class="rango" @submit.prevent="guardarRango">
          <label>
            CAI
            <input v-model="rango.cai" placeholder="Tal como lo dio el SAR" required />
          </label>

          <div class="row">
            <label>
              Establecimiento
              <input v-model="rango.establishmentCode" maxlength="3" />
            </label>
            <label>
              Punto de emisión
              <input v-model="rango.pointOfSaleCode" maxlength="3" />
            </label>
            <label>
              Tipo
              <input v-model="rango.documentType" maxlength="2" />
            </label>
          </div>

          <div class="row">
            <label>
              Desde
              <input v-model.number="rango.rangeStart" type="number" min="1" />
            </label>
            <label>
              Hasta
              <input v-model.number="rango.rangeEnd" type="number" min="1" />
            </label>
            <label>
              Fecha límite
              <input v-model="rango.issueDeadline" type="date" required />
            </label>
          </div>

          <div class="actions">
            <button type="submit" :disabled="busy">Registrar</button>
            <button type="button" class="link" @click="abrirRango = false">Cancelar</button>
          </div>

          <p class="muted small">
            El rango que estuviera activo queda dado de baja: emitir con dos rangos vivos es
            la forma de saltarse un número sin enterarse.
          </p>
        </form>
      </article>
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
              <td class="num">{{ s.invoiceCount }}</td>
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
.alarma {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin: 0 0 1rem;
  padding: 0.625rem 0.875rem;
  border: 1px solid color-mix(in srgb, var(--danger) 45%, var(--border));
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--danger) 8%, transparent);
  color: var(--danger);
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
</style>

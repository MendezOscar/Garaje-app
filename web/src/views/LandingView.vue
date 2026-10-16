<script setup lang="ts">
/**
 * Página de venta. La abre un dueño de taller que recibió el enlace por WhatsApp: no tiene
 * cuenta, no sabe qué es un tenant y no le interesa. Por eso habla de su negocio —lo que se
 * le pierde hoy y qué gana— y deja el panel detrás de «Entrar».
 *
 * No hace ni una petición a la API a propósito: el plan gratuito de Render duerme a los 15
 * minutos y el primer visitante pagaría 40 segundos de página en blanco.
 *
 * El precio no va en la página: se manda en la propuesta (propuesta-garajapp.html), que se
 * adapta a las sucursales y a la forma de pago de cada taller.
 */
import BrandLogo from '@/components/BrandLogo.vue'

const WHATSAPP = '50498242108'
const CORREO = 'soporte@garajeapp.com'

const MENSAJE = encodeURIComponent(
  'Buenas, vi GarajApp y quiero saber más para mi taller.',
)

const whatsapp = `https://wa.me/${WHATSAPP}?text=${MENSAJE}`
const correo = `mailto:${CORREO}?subject=${encodeURIComponent('GarajApp para mi taller')}`

/**
 * Talleres que ya trabajan con GarajApp. Es la parte de la página que no se puede escribir:
 * un dueño de taller no le cree a la lista de funciones, le cree al taller de al lado.
 *
 * `fondo` va por logo y no por diseño: el de RVM es negro sobre blanco y el de El Ártico
 * viene con su fondo oscuro incrustado, así que cada uno se pinta sobre el suyo o uno de los
 * dos desaparece.
 */
const talleres = [
  {
    nombre: 'Motorepuestos y Taller RVM',
    logo: '/talleres/rvm.jpg',
    fondo: 'claro',
  },
  {
    nombre: 'Frío Automotriz El Ártico',
    logo: '/talleres/artico.jpg',
    fondo: 'oscuro',
  },
  {
    nombre: 'Ariel Garage',
    logo: '/talleres/ariel.jpg',
    fondo: 'oscuro',
  },
]

/**
 * Lo que el dueño ya vive, y al lado lo que pasa a ser. Nombrar el problema es la mitad de
 * la venta; ponerlo enfrente de la respuesta es la otra mitad.
 */
const comparacion = [
  {
    hoy: 'El cuaderno',
    textoHoy:
      'Lo que se le hizo a cada vehículo vive en una hoja que se moja, se pierde, o la tiene ' +
      'el técnico que hoy no llegó.',
    con: 'El historial por placa',
    textoCon:
      'Teclea la placa y sale todo lo que ha entrado ese vehículo, con sus fotos, lo que se ' +
      'le cambió y lo que se cobró.',
  },
  {
    hoy: '«¿Ya está mi carro?»',
    textoHoy:
      'Dos o tres llamadas por vehículo, y quien contesta el teléfono no siempre sabe. El ' +
      'cliente desconfía de lo que no ve.',
    con: 'El cliente lo ve solo',
    textoCon:
      'Le llega un enlace por WhatsApp y abre en qué va su vehículo, con fotos. Sin instalar ' +
      'nada y sin crear cuenta.',
  },
  {
    hoy: 'Los repuestos',
    textoHoy:
      'Nadie sabe cuánto hay hasta que falta. Sale mercadería y no queda claro para qué ' +
      'trabajo, así que la diferencia aparece hasta el conteo.',
    con: 'Cada salida con su dueño',
    textoCon:
      'El repuesto sale de la bodega pegado a la orden en la que se usó, con su responsable ' +
      'y su hora. El conteo deja de ser una sorpresa.',
  },
  {
    hoy: 'El fin de mes',
    textoHoy:
      'Se factura de memoria y en el cuaderno. Al cerrar, nadie puede decir qué dejó el mes ' +
      'ni qué sucursal lo dejó.',
    con: 'El número, todos los días',
    textoCon:
      'Lo cobrado de hoy, lo facturado del mes y el margen, separado en repuestos y mano de ' +
      'obra, por sucursal y por técnico.',
  },
]

/** Lo que trae, en una línea cada cosa. Es el barrido rápido del que viene comparando. */
const modulos = [
  'Órdenes de trabajo',
  'Recepción con firma',
  'Cotizaciones por WhatsApp',
  'Inventario por sucursal',
  'Caja del día',
  'Factura con CAI',
  'Cuentas por cobrar',
  'Reclamos y garantías',
  'Historial por placa',
  'Recordatorios de servicio',
  'Citas por enlace',
  'Reportes y Excel',
  'Encargados por sucursal',
]

/** El flujo real del taller, en el orden en que pasa. */
const pasos = [
  {
    titulo: 'Recibe el vehículo',
    texto:
      'Cliente, placa y motivo de ingreso en un minuto, en el mostrador. Con la hoja de ' +
      'recepción firmada en la pantalla: combustible, golpes que ya traía y lo que dejó adentro.',
  },
  {
    titulo: 'Reparte y documenta',
    texto:
      'Asigna al técnico, arma los pasos de la reparación y sube fotos de lo que encuentra y ' +
      'de lo que cambia. Sin señal también: las fotos suben al recuperar la red.',
  },
  {
    titulo: 'Cotiza por WhatsApp',
    texto:
      'Arma la cotización con repuestos y mano de obra y sale por WhatsApp con un enlace. El ' +
      'cliente la abre sin instalar nada y aprueba con un toque.',
  },
  {
    titulo: 'Factura y cobra',
    texto:
      'Al entregar, la orden se factura con lo que de verdad se consumió, con CAI del SAR si ' +
      'lo tiene. Si el cliente queda debiendo, el saldo queda en cuentas por cobrar.',
  },
]

/**
 * Los tres bloques grandes, cada uno con su captura. Son las tres cosas que un dueño quiere
 * ver antes de creer: la orden, lo que ve su cliente y el cobro.
 */
const bloques = [
  {
    etiqueta: 'La orden',
    titulo: 'Todo lo del vehículo, en una sola pantalla',
    texto:
      'El motivo de ingreso, el diagnóstico, los pasos de la reparación, los repuestos que se ' +
      'consumieron y las fotos. La línea de tiempo dice quién movió qué y a qué hora, así que ' +
      'el trabajo no depende de quién se acuerde.',
    puntos: [
      'Los pasos se marcan desde el teléfono, donde está el vehículo',
      'El repuesto que se usa descuenta de la bodega de esa sucursal',
      'Las fotos quedan con la orden, no en el teléfono de alguien',
    ],
    imagen: '/capturas/telefono-orden.png?v=2026-08',
    alt: 'El detalle de una orden en el teléfono, con el diagnóstico y los pasos de la reparación',
    ancho: 390,
    alto: 848,
  },
  {
    etiqueta: 'Su cliente',
    titulo: 'La cotización la aprueba él, desde su teléfono',
    texto:
      'Sale por WhatsApp como un enlace. El cliente ve el detalle de lo que se le va a cobrar ' +
      '—repuestos y mano de obra, con el ISV aparte— y aprueba o dice que no. Usted lo ve en ' +
      'la orden y en sus avisos, sin tener que llamarlo a preguntar.',
    puntos: [
      'Sin instalar nada y sin crear cuenta',
      'Si quiere, le abre acceso para seguir la reparación con fotos',
      'La ficha de recepción firmada se le puede mandar igual',
    ],
    imagen: '/capturas/cotizacion-cliente.png?v=2026-08',
    alt: 'La cotización tal como la abre el cliente desde WhatsApp, con el detalle y el total',
    ancho: 390,
    alto: 820,
    invertido: true,
  },
  {
    etiqueta: 'El cobro',
    titulo: 'Se factura y se cobra con el cliente enfrente',
    texto:
      'Al entregar, la orden se cierra y se factura con lo que de verdad se consumió. Con CAI ' +
      'del SAR sale con número autorizado, rango, fecha límite y valor en letras; sin CAI sirve ' +
      'como comprobante de entrega. Y al que solo llega por un repuesto, se le vende de mostrador.',
    puntos: [
      'Abonos y crédito, con lo vencido aparte en cuentas por cobrar',
      'La caja del día cuadra por forma de pago y por quien cobró',
      'Le avisa cuando el rango de CAI se está acabando',
    ],
    imagen: '/capturas/telefono-facturar.png?v=2026-08',
    alt: 'Cerrar y facturar en el teléfono: el total, la forma de pago y el aviso del próximo servicio',
    ancho: 390,
    alto: 848,
  },
]

const incluye = [
  {
    titulo: 'Órdenes con evidencia',
    texto:
      'Cada vehículo con su motivo, su diagnóstico, sus pasos y sus fotos. La línea de tiempo ' +
      'dice quién movió qué y a qué hora.',
  },
  {
    titulo: 'Inventario por sucursal',
    texto:
      'Existencias, entradas de compra, ajustes por conteo, traslados y kardex. El stock no se ' +
      'edita a mano: cada movimiento queda con su responsable.',
  },
  {
    titulo: 'Cotizaciones por WhatsApp',
    texto:
      'En PDF y como página que el cliente abre sin cuenta. Cuando aprueba, el taller lo ve en ' +
      'la orden y en sus avisos.',
  },
  {
    titulo: 'Facturación y abonos',
    texto:
      'Cierre de la orden, venta de mostrador, pagos a crédito con abonos y la lista de lo que ' +
      'falta por cobrar, con lo vencido aparte.',
  },
  {
    titulo: 'Venta de mostrador',
    texto:
      'Alguien entra por un filtro y se va. Sale de la bodega de esa sucursal y entra a la caja ' +
      'del día, con cliente o sin él.',
  },
  {
    titulo: 'Factura con CAI',
    texto:
      'El CAI de cada sucursal, con su rango y su fecha límite. El ISV lo lleva solo la factura ' +
      'con CAI, como manda el régimen.',
  },
  {
    titulo: 'Reclamos y garantía',
    texto:
      'Si el vehículo vuelve, el reclamo queda pegado a su factura y la reparación se abre como ' +
      'orden nueva. Así se sabe qué costó la garantía.',
  },
  {
    titulo: 'Su gente y sus sucursales',
    texto:
      'Técnicos, encargados de sucursal y usted. Cada uno ve solo lo suyo: el técnico su trabajo, ' +
      'el encargado su sucursal, el cliente sus vehículos.',
  },
]

/** Lo que de verdad preguntan antes de decir que sí. */
const preguntas = [
  {
    p: '¿Hay que instalar algo o comprar equipo?',
    r:
      'No. Se abre en el navegador de cualquier computadora y la aplicación se baja del App ' +
      'Store o de Play Store en el teléfono que ya tiene. No hace falta servidor ni nada ' +
      'conectado en el taller.',
  },
  {
    p: '¿Sirve para motos, pick-ups y camiones?',
    r:
      'Sí. Cada vehículo lleva su tipo, su placa y su kilometraje, y el historial es por placa ' +
      'sin importar qué sea.',
  },
  {
    p: '¿Y si mi taller todavía no tiene CAI?',
    r:
      'Trabaja igual: el documento sale como comprobante de entrega. El día que tenga el CAI se ' +
      'registra con su rango y las facturas empiezan a salir autorizadas, sin cambiar nada más.',
  },
  {
    p: '¿Qué pasa con lo que tengo en el cuaderno o en Excel?',
    r:
      'Lo paso yo al instalar, sin costo: sus clientes, sus vehículos y sus repuestos con ' +
      'existencia. El taller arranca con sus datos, no vacío.',
  },
  {
    p: '¿Funciona si en el taller no hay buena señal?',
    r:
      'Las fotos que tome el técnico se guardan y suben solas cuando el teléfono recupera la ' +
      'red. Lo demás pide señal, como cualquier sistema en línea.',
  },
  {
    p: '¿Mis datos quedan separados de otros talleres?',
    r:
      'Sí. Cada taller tiene los suyos aparte, con respaldos. Las fotos y los documentos se ' +
      'sirven con enlaces que caducan: no quedan en una dirección pública.',
  },
  {
    p: '¿El técnico ve lo que le cobro al cliente?',
    r:
      'Usted decide. Hay un interruptor en los ajustes del taller para que el técnico trabaje ' +
      'sus órdenes sin ver precios.',
  },
  {
    p: '¿Cuánto cuesta?',
    r:
      'Depende de cuántas sucursales tenga y de la forma de pago, y lleva la instalación, el ' +
      'traslado de sus datos y la capacitación incluidas. Escríbame por WhatsApp y le paso la ' +
      'propuesta para su taller.',
  },
]
</script>

<template>
  <div class="landing">
    <!-- ------------------------------------------------------------------ barra -->
    <header class="bar">
      <div class="bar-dentro">
        <BrandLogo variant="horizontal" :height="28" />
        <nav class="anclas">
          <a href="#como">Cómo funciona</a>
          <a href="#incluye">Qué incluye</a>
          <a href="#preguntas">Preguntas</a>
        </nav>
        <div class="bar-acciones">
          <RouterLink class="entrar" :to="{ name: 'login' }">Entrar</RouterLink>
          <a class="boton chico" :href="whatsapp" target="_blank" rel="noopener">
            Escribir por WhatsApp
          </a>
        </div>
      </div>
    </header>

    <!-- ------------------------------------------------------------------ portada -->
    <section class="hero">
      <div class="hero-dentro">
        <div class="hero-texto">
          <p class="pastilla">
            <span class="punto" aria-hidden="true"></span>
            Hecho en Honduras · Lempiras, ISV 15% y factura con CAI
          </p>
          <h1>Sepa en qué va cada vehículo sin moverse del mostrador</h1>
          <p class="claim">
            GarajApp lleva las órdenes de trabajo, el inventario, las cotizaciones y el cobro de
            su taller de autos y motos. En el teléfono, que es donde usted trabaja.
          </p>
          <div class="acciones">
            <a class="boton grande" :href="whatsapp" target="_blank" rel="noopener">
              <svg class="icono-wa" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                <path
                  d="M17.47 14.38c-.3-.15-1.76-.87-2.03-.97-.27-.1-.47-.15-.67.15-.2.3-.77.97-.94 1.16-.17.2-.35.22-.64.08-.3-.15-1.26-.46-2.39-1.48-.88-.79-1.48-1.76-1.65-2.06-.18-.3-.02-.46.13-.6.13-.14.35-.35.52-.52.17-.18.23-.3.35-.5.11-.2.05-.37-.03-.52-.09-.15-.66-1.61-.91-2.21-.23-.58-.47-.5-.65-.51h-.57c-.2 0-.52.07-.79.37-.27.3-1.04 1.02-1.04 2.48s1.07 2.87 1.21 3.07c.15.2 2.1 3.2 5.08 4.49.71.3 1.26.49 1.69.62.71.23 1.36.2 1.87.12.57-.09 1.76-.72 2.01-1.41.25-.7.25-1.29.17-1.42-.07-.12-.27-.2-.57-.35M12.05 21.8a9.87 9.87 0 0 1-5.03-1.38l-.36-.22-3.74.99 1-3.65-.24-.38a9.86 9.86 0 0 1-1.51-5.26c0-5.45 4.44-9.89 9.89-9.89 2.64 0 5.12 1.03 6.99 2.9a9.83 9.83 0 0 1 2.89 6.99c0 5.45-4.43 9.89-9.89 9.89m8.41-18.3A11.82 11.82 0 0 0 12.05 0C5.5 0 .16 5.34.16 11.89c0 2.1.55 4.15 1.59 5.95L.06 24l6.3-1.65a11.88 11.88 0 0 0 5.69 1.45c6.55 0 11.89-5.34 11.89-11.9 0-3.17-1.24-6.16-3.48-8.41Z"
                />
              </svg>
              Escribir por WhatsApp
            </a>
            <a class="boton fantasma grande" href="#como">Ver cómo funciona</a>
          </div>
          <ul class="sellos">
            <li>Instalado con los datos de su taller</li>
            <li>Sin equipo nuevo: sirve el teléfono que ya tiene</li>
            <li>Capacitación y soporte incluidos</li>
          </ul>
        </div>

        <div class="telefono">
          <img
            src="/capturas/telefono-taller.png?v=2026-08"
            width="390"
            height="848"
            alt="La pantalla de Hoy en el teléfono: lo cobrado del día, lo que urge atender y el patio por estado"
          />
          <!-- Dos tarjetas encima de la captura: dicen de un vistazo qué contesta la
               pantalla. Se esconden en pantalla angosta, donde solo estorbarían. -->
          <div class="flotante arriba" aria-hidden="true">
            <svg class="flotante-paloma" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
              <path d="m5 13 4 4 10-10" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
            <div>
              <strong>Cotización aprobada</strong>
              <span class="flotante-pie">el cliente dijo que sí</span>
            </div>
          </div>
          <div class="flotante abajo" aria-hidden="true">
            <span class="flotante-punto"></span>
            <div>
              <strong>3 listas para entrega</strong>
              <span class="flotante-pie">avisadas por WhatsApp</span>
            </div>
          </div>
        </div>
      </div>
    </section>

    <!-- ------------------------------------------------------------------ quiénes la usan -->
    <!--
      Va pegado a la portada: es lo primero que pregunta un dueño de taller —«¿quién más la
      usa?»— y ninguna lista de funciones le contesta eso.
    -->
    <section class="prueba">
      <p class="prueba-rotulo">Talleres que ya trabajan con GarajApp</p>
      <div class="talleres">
        <figure v-for="taller in talleres" :key="taller.nombre" class="taller">
          <div class="marco" :class="taller.fondo">
            <img
              :src="taller.logo"
              :alt="`Logo de ${taller.nombre}`"
              loading="lazy"
              @error="($event.target as HTMLImageElement).style.display = 'none'"
            />
          </div>
          <figcaption>{{ taller.nombre }}</figcaption>
        </figure>
      </div>
    </section>

    <!-- ------------------------------------------------------------------ comparación -->
    <section class="seccion">
      <header class="titulo">
        <p class="rotulo">El día en el taller</p>
        <h2>Lo que hoy se pierde, y dónde queda a partir de mañana</h2>
      </header>

      <div class="comparacion">
        <article v-for="fila in comparacion" :key="fila.hoy" class="fila">
          <div class="lado hoy">
            <span class="marca-lado">Hoy</span>
            <h3>{{ fila.hoy }}</h3>
            <p>{{ fila.textoHoy }}</p>
          </div>
          <div class="flecha" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M4 12h15m0 0-5-5m5 5-5 5" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
          </div>
          <div class="lado con">
            <span class="marca-lado">Con GarajApp</span>
            <h3>{{ fila.con }}</h3>
            <p>{{ fila.textoCon }}</p>
          </div>
        </article>
      </div>
    </section>

    <!-- ------------------------------------------------------------------ módulos -->
    <section class="banda modulos-banda">
      <h2>Todo el taller, en un solo sitio</h2>
      <ul class="modulos">
        <li v-for="modulo in modulos" :key="modulo">{{ modulo }}</li>
      </ul>
    </section>

    <!-- ------------------------------------------------------------------ flujo -->
    <section id="como" class="seccion">
      <header class="titulo">
        <p class="rotulo">Cómo funciona</p>
        <h2>Así pasa un vehículo por GarajApp</h2>
      </header>
      <ol class="pasos">
        <li v-for="(paso, i) in pasos" :key="paso.titulo">
          <span class="numero num">{{ i + 1 }}</span>
          <h3>{{ paso.titulo }}</h3>
          <p>{{ paso.texto }}</p>
        </li>
      </ol>
    </section>

    <!-- ------------------------------------------------------------------ bloques -->
    <!--
      Las capturas llevan `?v=` con el mes en que se tomaron. Los archivos de `public/` no
      llevan hash en el nombre, así que al rehacer una captura el navegador de quien ya entró
      sigue enseñando la vieja; la marca cambia la dirección y lo obliga a bajarla. Al
      rehacerlas hay que subir la marca, aquí y en `public/manual.html`.
    -->
    <section
      v-for="bloque in bloques"
      :key="bloque.titulo"
      class="bloque"
      :class="{ invertido: bloque.invertido }"
    >
      <div class="bloque-texto">
        <p class="rotulo">{{ bloque.etiqueta }}</p>
        <h2>{{ bloque.titulo }}</h2>
        <p class="bloque-cuerpo">{{ bloque.texto }}</p>
        <ul class="palomas">
          <li v-for="punto in bloque.puntos" :key="punto">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" aria-hidden="true">
              <path d="m5 13 4 4 10-10" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
            {{ punto }}
          </li>
        </ul>
      </div>
      <div class="bloque-imagen">
        <img
          :src="bloque.imagen"
          :alt="bloque.alt"
          :width="bloque.ancho"
          :height="bloque.alto"
          loading="lazy"
        />
      </div>
    </section>

    <!-- ------------------------------------------------------------------ panel -->
    <section class="banda">
      <header class="titulo">
        <p class="rotulo">En la computadora</p>
        <h2>Lo mismo, con más sitio para sentarse a ver el mes</h2>
      </header>
      <div class="navegador">
        <div class="navegador-barra" aria-hidden="true">
          <span></span><span></span><span></span>
          <p>panel.garajeapp.com</p>
        </div>
        <img
          src="/capturas/panel-indicadores.png?v=2026-08"
          width="1440"
          height="400"
          alt="El reporte de ingresos: repuestos, lo vendido en mostrador, mano de obra, total y margen, con la gráfica de cada día"
          loading="lazy"
        />
      </div>
      <div class="rejilla-panel">
        <img
          src="/capturas/panel-ordenes.png?v=2026-08"
          width="1440"
          height="900"
          alt="El tablero de órdenes de trabajo en el navegador, con las órdenes agrupadas por estado"
          loading="lazy"
        />
        <img
          src="/capturas/panel-ventas.png?v=2026-08"
          width="1440"
          height="1000"
          alt="El registro de ventas en el navegador: lo facturado del mes, cuántas fueron de mostrador y el comprobante de cada una"
          loading="lazy"
        />
        <img
          src="/capturas/panel-inventario.png?v=2026-08"
          width="1440"
          height="900"
          alt="El inventario en el navegador, con la existencia de cada repuesto por sucursal y su ubicación"
          loading="lazy"
        />
      </div>
    </section>

    <!-- ------------------------------------------------------------------ alcance -->
    <section id="incluye" class="seccion">
      <header class="titulo">
        <p class="rotulo">Qué incluye</p>
        <h2>Lo que trae desde el primer día</h2>
      </header>
      <div class="rejilla">
        <article v-for="item in incluye" :key="item.titulo" class="tarjeta">
          <h3>{{ item.titulo }}</h3>
          <p>{{ item.texto }}</p>
        </article>
      </div>
    </section>

    <!-- ------------------------------------------------------------------ preguntas -->
    <section id="preguntas" class="banda">
      <header class="titulo">
        <p class="rotulo">Preguntas</p>
        <h2>Lo que se pregunta antes de decidir</h2>
      </header>
      <div class="preguntas">
        <details v-for="pregunta in preguntas" :key="pregunta.p">
          <summary>
            {{ pregunta.p }}
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
              <path d="m6 9 6 6 6-6" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
          </summary>
          <p>{{ pregunta.r }}</p>
        </details>
      </div>
    </section>

    <!-- ------------------------------------------------------------------ cierre -->
    <section class="cierre">
      <div class="cierre-dentro">
        <h2>Hablemos de su taller</h2>
        <p>
          Cuénteme cuántas sucursales tiene y cómo trabaja hoy. Se lo instalo con sus datos
          —clientes, vehículos y repuestos— y lo prueba con trabajos de verdad. La instalación,
          el traslado de sus datos y la capacitación van sin costo.
        </p>
        <div class="acciones">
          <a class="boton claro grande" :href="whatsapp" target="_blank" rel="noopener">
            <svg class="icono-wa" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
              <path
                d="M17.47 14.38c-.3-.15-1.76-.87-2.03-.97-.27-.1-.47-.15-.67.15-.2.3-.77.97-.94 1.16-.17.2-.35.22-.64.08-.3-.15-1.26-.46-2.39-1.48-.88-.79-1.48-1.76-1.65-2.06-.18-.3-.02-.46.13-.6.13-.14.35-.35.52-.52.17-.18.23-.3.35-.5.11-.2.05-.37-.03-.52-.09-.15-.66-1.61-.91-2.21-.23-.58-.47-.5-.65-.51h-.57c-.2 0-.52.07-.79.37-.27.3-1.04 1.02-1.04 2.48s1.07 2.87 1.21 3.07c.15.2 2.1 3.2 5.08 4.49.71.3 1.26.49 1.69.62.71.23 1.36.2 1.87.12.57-.09 1.76-.72 2.01-1.41.25-.7.25-1.29.17-1.42-.07-.12-.27-.2-.57-.35M12.05 21.8a9.87 9.87 0 0 1-5.03-1.38l-.36-.22-3.74.99 1-3.65-.24-.38a9.86 9.86 0 0 1-1.51-5.26c0-5.45 4.44-9.89 9.89-9.89 2.64 0 5.12 1.03 6.99 2.9a9.83 9.83 0 0 1 2.89 6.99c0 5.45-4.43 9.89-9.89 9.89m8.41-18.3A11.82 11.82 0 0 0 12.05 0C5.5 0 .16 5.34.16 11.89c0 2.1.55 4.15 1.59 5.95L.06 24l6.3-1.65a11.88 11.88 0 0 0 5.69 1.45c6.55 0 11.89-5.34 11.89-11.9 0-3.17-1.24-6.16-3.48-8.41Z"
              />
            </svg>
            Pedir la propuesta
          </a>
          <a class="boton fantasma claro grande" :href="correo">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true">
              <rect x="2.5" y="4.5" width="19" height="15" rx="2" />
              <path d="m3 7 9 5.5L21 7" />
            </svg>
            Correo
          </a>
        </div>
        <p class="fino">
          El precio depende de cuántas sucursales tenga y de la forma de pago. Usuarios,
          órdenes, fotos y cotizaciones, sin límite.
        </p>
      </div>
    </section>

    <footer class="pie">
      <BrandLogo variant="isotipo" :height="18" />
      <span>Hecho en Honduras · Lempiras e ISV 15%</span>
      <!-- Archivos estáticos de `public`, así que van con <a> y no con RouterLink: el router
           de Vue no los conoce y los mandaría al 404. El manual no se enlaza a propósito: se
           abre por su ruta (/manual) cuando hace falta mandárselo a alguien. -->
      <a href="/privacidad">Privacidad</a>
      <a href="/soporte">Soporte</a>
      <RouterLink :to="{ name: 'login' }">Entrar al panel</RouterLink>
    </footer>

    <!-- El botón de siempre, flotando en el teléfono: la página es larga y el que se decide
         a la mitad no debería tener que subir hasta arriba. -->
    <a class="flota-wa" :href="whatsapp" target="_blank" rel="noopener" aria-label="Escribir por WhatsApp">
      <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
        <path
          d="M17.47 14.38c-.3-.15-1.76-.87-2.03-.97-.27-.1-.47-.15-.67.15-.2.3-.77.97-.94 1.16-.17.2-.35.22-.64.08-.3-.15-1.26-.46-2.39-1.48-.88-.79-1.48-1.76-1.65-2.06-.18-.3-.02-.46.13-.6.13-.14.35-.35.52-.52.17-.18.23-.3.35-.5.11-.2.05-.37-.03-.52-.09-.15-.66-1.61-.91-2.21-.23-.58-.47-.5-.65-.51h-.57c-.2 0-.52.07-.79.37-.27.3-1.04 1.02-1.04 2.48s1.07 2.87 1.21 3.07c.15.2 2.1 3.2 5.08 4.49.71.3 1.26.49 1.69.62.71.23 1.36.2 1.87.12.57-.09 1.76-.72 2.01-1.41.25-.7.25-1.29.17-1.42-.07-.12-.27-.2-.57-.35M12.05 21.8a9.87 9.87 0 0 1-5.03-1.38l-.36-.22-3.74.99 1-3.65-.24-.38a9.86 9.86 0 0 1-1.51-5.26c0-5.45 4.44-9.89 9.89-9.89 2.64 0 5.12 1.03 6.99 2.9a9.83 9.83 0 0 1 2.89 6.99c0 5.45-4.43 9.89-9.89 9.89m8.41-18.3A11.82 11.82 0 0 0 12.05 0C5.5 0 .16 5.34.16 11.89c0 2.1.55 4.15 1.59 5.95L.06 24l6.3-1.65a11.88 11.88 0 0 0 5.69 1.45c6.55 0 11.89-5.34 11.89-11.9 0-3.17-1.24-6.16-3.48-8.41Z"
        />
      </svg>
    </a>
  </div>
</template>

<style scoped>
/* Ancho de lectura común a todas las secciones. */
.landing {
  --ancho: min(70rem, 100%);
  --tinta: #0d1420;
  --tinta-suave: #16203260;
  background: var(--bg);
  overflow-x: clip;
}

/* ------------------------------------------------------------------ barra */

.bar {
  position: sticky;
  top: 0;
  z-index: 20;
  background: color-mix(in srgb, var(--bg) 88%, transparent);
  backdrop-filter: blur(10px);
  border-bottom: 1px solid color-mix(in srgb, var(--border) 60%, transparent);
}

.bar-dentro {
  width: var(--ancho);
  margin: 0 auto;
  padding: 0.75rem 1rem;
  display: flex;
  align-items: center;
  gap: 1rem;
}

.anclas {
  display: none;
  margin-left: auto;
  gap: 1.5rem;
}

.anclas a {
  color: var(--text-muted);
  font-size: 0.9375rem;
}

.anclas a:hover {
  color: var(--text);
  text-decoration: none;
}

.bar-acciones {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.anclas + .bar-acciones {
  margin-left: 0;
}

.entrar {
  font-weight: 500;
  padding: 0.4rem 0.75rem;
  border-radius: var(--radius-sm);
  color: var(--text);
}

.entrar:hover {
  text-decoration: none;
  background: var(--surface-alt);
}

@media (min-width: 60rem) {
  .anclas {
    display: flex;
  }
}

/* ------------------------------------------------------------------ botones */

.boton {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  padding: 0.7rem 1.1rem;
  border-radius: 999px;
  background: var(--brand);
  color: #fff;
  font-weight: 600;
  line-height: 1;
  border: 1px solid transparent;
  transition: transform 0.15s ease, box-shadow 0.15s ease, background 0.15s ease;
  box-shadow: 0 8px 20px -12px #1f6feb;
}

.boton:hover {
  text-decoration: none;
  transform: translateY(-1px);
  box-shadow: 0 14px 28px -14px #1f6feb;
}

.boton.chico {
  padding: 0.45rem 0.9rem;
  font-size: 0.9375rem;
}

.boton.grande {
  padding: 0.9rem 1.5rem;
  font-size: 1.0625rem;
}

.boton.fantasma {
  background: transparent;
  color: var(--text);
  border-color: var(--border);
  box-shadow: none;
}

.boton.fantasma:hover {
  border-color: var(--brand);
  color: var(--brand);
}

.boton.claro {
  background: #fff;
  color: var(--tinta);
  box-shadow: none;
}

.boton.fantasma.claro {
  background: transparent;
  color: #fff;
  border-color: #ffffff55;
}

.boton.fantasma.claro:hover {
  color: #fff;
  border-color: #fff;
}

.boton svg {
  width: 1.125rem;
  height: 1.125rem;
  flex: none;
}

/* ------------------------------------------------------------------ portada */

.hero {
  position: relative;
  color: #f2f5fa;
  background:
    radial-gradient(60rem 30rem at 15% -10%, #1f6feb55, transparent 60%),
    radial-gradient(45rem 28rem at 95% 10%, #124293aa, transparent 65%),
    linear-gradient(180deg, #0d1420, #101b2c);
  overflow: hidden;
}

/* La malla de fondo: insinúa una cuadrícula de taller sin competir con la captura. */
.hero::before {
  content: '';
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(#ffffff0a 1px, transparent 1px),
    linear-gradient(90deg, #ffffff0a 1px, transparent 1px);
  background-size: 48px 48px;
  mask-image: radial-gradient(70% 60% at 50% 20%, #000, transparent);
}

.hero-dentro {
  position: relative;
  width: var(--ancho);
  margin: 0 auto;
  padding: 3rem 1rem 0;
  display: grid;
  gap: 2.5rem;
}

@media (min-width: 60rem) {
  .hero-dentro {
    grid-template-columns: 1.1fr 0.9fr;
    align-items: center;
    padding-top: 4.5rem;
  }
}

.pastilla {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  margin: 0 0 1.25rem;
  padding: 0.35rem 0.8rem;
  border: 1px solid #ffffff2e;
  border-radius: 999px;
  background: #ffffff0f;
  font-size: 0.8125rem;
  color: #cfdcf0;
}

.punto {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
  background: var(--success);
  box-shadow: 0 0 0 3px #1fa97133;
}

h1 {
  margin: 0;
  font-size: clamp(2rem, 6vw, 3.25rem);
  line-height: 1.05;
  letter-spacing: -0.03em;
  color: #fff;
}

.claim {
  margin: 1rem 0 0;
  max-width: 34rem;
  font-size: clamp(1rem, 2.4vw, 1.1875rem);
  line-height: 1.55;
  color: #c3d0e2;
}

.acciones {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-top: 1.75rem;
}

.hero .boton.fantasma {
  color: #fff;
  border-color: #ffffff40;
}

.hero .boton.fantasma:hover {
  border-color: #fff;
  color: #fff;
}

.sellos {
  list-style: none;
  margin: 1.75rem 0 0;
  padding: 0;
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem 1.25rem;
  font-size: 0.875rem;
  color: #9fb2cc;
}

.sellos li {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.sellos li::before {
  content: '';
  width: 0.375rem;
  height: 0.375rem;
  border-radius: 50%;
  background: #5c9dff;
}

.telefono {
  position: relative;
  justify-self: center;
  align-self: end;
}

.telefono img {
  display: block;
  width: min(17rem, 72vw);
  height: auto;
  border-radius: 1.75rem;
  border: 1px solid #ffffff1f;
  box-shadow: 0 40px 80px -30px #000000cc;
}

.flotante {
  position: absolute;
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.6rem 0.85rem;
  border-radius: var(--radius-md);
  background: #fff;
  color: var(--tinta);
  box-shadow: 0 18px 36px -18px #000000a8;
  font-size: 0.8125rem;
}

.flotante.arriba {
  top: 12%;
  left: -2.5rem;
}

.flotante-paloma {
  width: 1.125rem;
  height: 1.125rem;
  flex: none;
  color: var(--success);
}

.flotante.abajo {
  bottom: 18%;
  right: -2rem;
}

.flotante-pie {
  display: block;
  color: #5a6475;
  font-size: 0.75rem;
}

.flotante-punto {
  width: 0.625rem;
  height: 0.625rem;
  border-radius: 50%;
  background: var(--success);
  flex: none;
}

@media (max-width: 60rem) {
  .flotante {
    display: none;
  }
}

/* ------------------------------------------------------------------ prueba social */

.prueba {
  width: var(--ancho);
  margin: 0 auto;
  padding: 2.5rem 1rem;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: center;
  gap: 1rem 2.5rem;
  text-align: center;
}

.prueba-rotulo {
  margin: 0;
  color: var(--text-muted);
  font-size: 0.875rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

.talleres {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 1.5rem;
}

.taller {
  margin: 0;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  font-size: 0.875rem;
  color: var(--text-muted);
}

.marco {
  width: 4rem;
  height: 4rem;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  display: grid;
  place-items: center;
  overflow: hidden;
}

.marco.claro {
  background: #fff;
}

.marco.oscuro {
  background: var(--tinta);
}

.marco img {
  width: 100%;
  height: 100%;
  object-fit: contain;
}

/* ------------------------------------------------------------------ secciones */

.seccion,
.banda {
  width: var(--ancho);
  margin: 0 auto;
  padding: 3.5rem 1rem;
}

.banda {
  width: 100%;
  background: var(--surface-alt);
}

.banda > * {
  width: var(--ancho);
  margin-inline: auto;
}

.titulo {
  max-width: 44rem;
  margin: 0 auto 2.5rem;
  text-align: center;
}

.rotulo {
  margin: 0 0 0.5rem;
  color: var(--brand);
  font-size: 0.8125rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.1em;
}

h2 {
  margin: 0;
  font-size: clamp(1.5rem, 3.6vw, 2.125rem);
  line-height: 1.15;
}

h3 {
  margin: 0 0 0.35rem;
  font-size: 1.0625rem;
}

/* ------------------------------------------------------------------ comparación */

.comparacion {
  display: grid;
  gap: 1rem;
}

.fila {
  display: grid;
  gap: 0.75rem;
  align-items: stretch;
}

@media (min-width: 48rem) {
  .fila {
    grid-template-columns: 1fr auto 1fr;
    gap: 1.25rem;
  }
}

.lado {
  padding: 1.25rem;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--surface);
}

.lado.hoy {
  background: var(--surface-alt);
  border-style: dashed;
}

.lado.hoy h3,
.lado.hoy p {
  color: var(--text-muted);
}

.lado.con {
  border-color: color-mix(in srgb, var(--brand) 35%, var(--border));
  box-shadow: 0 12px 30px -24px var(--brand);
}

.marca-lado {
  display: inline-block;
  margin-bottom: 0.6rem;
  font-size: 0.6875rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--text-muted);
}

.lado.con .marca-lado {
  color: var(--brand);
}

.lado p {
  margin: 0;
  line-height: 1.55;
}

.flecha {
  display: grid;
  place-items: center;
  color: var(--brand);
}

.flecha svg {
  width: 1.5rem;
  height: 1.5rem;
  rotate: 90deg;
}

@media (min-width: 48rem) {
  .flecha svg {
    rotate: none;
  }
}

/* ------------------------------------------------------------------ módulos */

.modulos-banda h2 {
  text-align: center;
  margin-bottom: 1.75rem;
}

.modulos {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 0.6rem;
}

.modulos li {
  padding: 0.5rem 0.9rem;
  border-radius: 999px;
  border: 1px solid var(--border);
  background: var(--surface);
  font-size: 0.9375rem;
}

/* ------------------------------------------------------------------ pasos */

.pasos {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 1rem;
}

@media (min-width: 48rem) {
  .pasos {
    grid-template-columns: repeat(4, 1fr);
  }
}

.pasos li {
  position: relative;
  padding: 1.5rem 1.25rem 1.25rem;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--surface);
}

.numero {
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2rem;
  margin-bottom: 0.85rem;
  border-radius: 50%;
  background: var(--brand);
  color: #fff;
  font-weight: 700;
  font-size: 0.9375rem;
}

.pasos p {
  margin: 0;
  color: var(--text-muted);
  line-height: 1.55;
}

/* ------------------------------------------------------------------ bloques -->*/

.bloque {
  width: var(--ancho);
  margin: 0 auto;
  padding: 3.5rem 1rem;
  display: grid;
  gap: 2rem;
  align-items: center;
}

@media (min-width: 60rem) {
  .bloque {
    grid-template-columns: 1fr 1fr;
    gap: 3.5rem;
  }

  .bloque.invertido .bloque-texto {
    order: 2;
  }
}

.bloque-cuerpo {
  margin: 0.75rem 0 0;
  font-size: 1.0625rem;
  line-height: 1.6;
  color: var(--text-muted);
}

.palomas {
  list-style: none;
  margin: 1.25rem 0 0;
  padding: 0;
  display: grid;
  gap: 0.6rem;
}

.palomas li {
  display: flex;
  align-items: flex-start;
  gap: 0.6rem;
  line-height: 1.5;
}

.palomas svg {
  width: 1.125rem;
  height: 1.125rem;
  flex: none;
  margin-top: 0.15rem;
  color: var(--success);
}

.bloque-imagen {
  display: grid;
  place-items: center;
}

.bloque-imagen img {
  width: min(17rem, 76vw);
  height: auto;
  border-radius: 1.5rem;
  border: 1px solid var(--border);
  box-shadow: 0 30px 60px -35px #0d142099;
}

/* ------------------------------------------------------------------ panel */

.navegador {
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--surface);
  overflow: hidden;
  box-shadow: 0 30px 60px -40px #0d142099;
}

.navegador-barra {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.6rem 0.9rem;
  border-bottom: 1px solid var(--border);
  background: var(--surface-alt);
}

.navegador-barra span {
  width: 0.625rem;
  height: 0.625rem;
  border-radius: 50%;
  background: var(--border);
}

.navegador-barra p {
  margin: 0 auto;
  font-size: 0.75rem;
  color: var(--text-muted);
  font-family: var(--font-mono);
}

.navegador img,
.rejilla-panel img {
  display: block;
  width: 100%;
  height: auto;
}

.rejilla-panel {
  margin-top: 1rem;
  display: grid;
  gap: 1rem;
}

@media (min-width: 48rem) {
  .rejilla-panel {
    grid-template-columns: repeat(3, 1fr);
  }
}

.rejilla-panel img {
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  aspect-ratio: 4 / 3;
  object-fit: cover;
  object-position: top left;
}

/* ------------------------------------------------------------------ tarjetas */

.rejilla {
  display: grid;
  gap: 1rem;
  grid-template-columns: repeat(auto-fit, minmax(15rem, 1fr));
}

.tarjeta {
  padding: 1.25rem;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--surface);
  transition: border-color 0.15s ease, transform 0.15s ease;
}

.tarjeta:hover {
  border-color: color-mix(in srgb, var(--brand) 40%, var(--border));
  transform: translateY(-2px);
}

.tarjeta p {
  margin: 0;
  color: var(--text-muted);
  line-height: 1.55;
}

/* ------------------------------------------------------------------ preguntas */

.preguntas {
  max-width: 48rem;
  margin: 0 auto;
  display: grid;
  gap: 0.6rem;
}

.preguntas details {
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  overflow: hidden;
}

.preguntas summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem 1.25rem;
  cursor: pointer;
  font-weight: 600;
  list-style: none;
}

.preguntas summary::-webkit-details-marker {
  display: none;
}

.preguntas summary svg {
  width: 1.125rem;
  height: 1.125rem;
  flex: none;
  color: var(--text-muted);
  transition: rotate 0.2s ease;
}

.preguntas details[open] summary svg {
  rotate: 180deg;
}

.preguntas p {
  margin: 0;
  padding: 0 1.25rem 1.25rem;
  color: var(--text-muted);
  line-height: 1.6;
}

/* ------------------------------------------------------------------ cierre */

.cierre {
  color: #f2f5fa;
  background:
    radial-gradient(40rem 24rem at 50% -20%, #1f6feb66, transparent 65%),
    linear-gradient(180deg, #101b2c, #0d1420);
  text-align: center;
}

.cierre-dentro {
  width: var(--ancho);
  margin: 0 auto;
  padding: 4rem 1rem;
}

.cierre h2 {
  color: #fff;
}

.cierre p {
  max-width: 38rem;
  margin: 1rem auto 0;
  color: #c3d0e2;
  line-height: 1.6;
}

.cierre .acciones {
  justify-content: center;
}

.fino {
  margin-top: 1.5rem;
  font-size: 0.875rem;
  color: #9fb2cc;
}

/* ------------------------------------------------------------------ pie */

.pie {
  width: var(--ancho);
  margin: 0 auto;
  padding: 1.5rem 1rem 5rem;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem 1.25rem;
  font-size: 0.875rem;
  color: var(--text-muted);
}

.pie a {
  color: var(--text-muted);
}

/* ------------------------------------------------------------------ WhatsApp flotante */

.flota-wa {
  position: fixed;
  right: 1rem;
  bottom: 1rem;
  z-index: 30;
  display: grid;
  place-items: center;
  width: 3.25rem;
  height: 3.25rem;
  border-radius: 50%;
  background: #25d366;
  color: #fff;
  box-shadow: 0 14px 30px -12px #00000080;
}

.flota-wa svg {
  width: 1.75rem;
  height: 1.75rem;
}

.flota-wa:hover {
  text-decoration: none;
}

@media (min-width: 60rem) {
  .flota-wa {
    display: none;
  }
}

/* Quien pidió menos movimiento no lo quiere ni en los botones. */
@media (prefers-reduced-motion: reduce) {
  .boton,
  .tarjeta,
  .preguntas summary svg {
    transition: none;
  }

  .boton:hover,
  .tarjeta:hover {
    transform: none;
  }
}
</style>

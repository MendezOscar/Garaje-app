<script setup lang="ts">
/**
 * Página de venta. La abre un dueño de taller que recibió el enlace por WhatsApp: no tiene
 * cuenta, no sabe qué es un tenant y no le interesa. Por eso habla de su negocio —lo que se
 * le pierde hoy y qué gana— y deja el panel detrás de «Entrar».
 *
 * No hace ni una petición a la API a propósito: el plan gratuito de Render duerme a los 15
 * minutos y el primer visitante pagaría 40 segundos de página en blanco.
 */
import BrandLogo from '@/components/BrandLogo.vue'

const WHATSAPP = '50498242108'
const CORREO = 'mendez01developer@gmail.com'

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
]

/** Lo que el dueño ya vive. Nombrarlo es la mitad de la venta. */
const problemas = [
  {
    titulo: 'El cuaderno',
    texto:
      'Lo que se le hizo a cada vehículo vive en una hoja que se moja, se pierde, o la tiene ' +
      'el técnico que hoy no llegó. Al mes nadie puede decir qué se cobró ni por qué.',
  },
  {
    titulo: '«¿Ya está mi carro?»',
    texto:
      'Dos o tres llamadas por vehículo, y quien contesta el teléfono no siempre sabe. ' +
      'El cliente desconfía de lo que no ve.',
  },
  {
    titulo: 'Los repuestos',
    texto:
      'Nadie sabe cuánto hay hasta que falta. Sale mercadería y no queda claro para qué ' +
      'trabajo, así que la diferencia aparece hasta el conteo.',
  },
]

/** El flujo real del taller, en el orden en que pasa. */
const pasos = [
  {
    titulo: 'Recibe el vehículo',
    texto:
      'Cliente, placa y motivo de ingreso en un minuto, en el mostrador. Si el cliente es ' +
      'nuevo se registra ahí mismo, con su vehículo.',
  },
  {
    titulo: 'Reparte y documenta',
    texto:
      'Asigna al técnico, arma los pasos de la reparación y sube fotos de lo que encuentra ' +
      'y de lo que cambia. Sin señal también: las fotos suben al recuperar la red.',
  },
  {
    titulo: 'Cotiza por WhatsApp',
    texto:
      'Arma la cotización con repuestos y mano de obra y sale por WhatsApp con un enlace. ' +
      'El cliente la abre sin instalar nada y aprueba con un toque.',
  },
  {
    titulo: 'Factura y cobra',
    texto:
      'Al entregar, la orden se factura con lo que de verdad se consumió, con CAI del SAR si ' +
      'lo tiene. Si el cliente queda debiendo, el saldo queda en cuentas por cobrar.',
  },
]

const incluye = [
  {
    titulo: 'Órdenes con evidencia',
    texto:
      'Cada vehículo con su motivo, su diagnóstico, sus pasos y sus fotos. La línea de ' +
      'tiempo dice quién movió qué y a qué hora.',
  },
  {
    titulo: 'Inventario por sucursal',
    texto:
      'Existencias, entradas de compra, ajustes por conteo, traslados y kardex. El stock no ' +
      'se edita a mano: cada movimiento queda con su responsable.',
  },
  {
    titulo: 'Cotizaciones por WhatsApp',
    texto:
      'En PDF y como página que el cliente abre sin cuenta. Cuando aprueba, el taller lo ve ' +
      'en la orden y en sus avisos.',
  },
  {
    titulo: 'Facturación y abonos',
    texto:
      'Cierre de la orden, venta de mostrador, pagos a crédito con abonos y la lista de lo ' +
      'que falta por cobrar, con lo vencido aparte.',
  },
  {
    titulo: 'Vende repuestos sin recibir el vehículo',
    texto:
      'La venta de mostrador: alguien entra por un filtro y se va. Sale de la bodega de esa ' +
      'sucursal y entra a la caja del día, con cliente o sin él.',
  },
  {
    titulo: 'El registro de ventas',
    texto:
      'Todo lo facturado, factura por factura, diciendo si salió de una orden o del ' +
      'mostrador. Con su comprobante para volver a mandarlo, y anulación con motivo cuando ' +
      'algo se digitó mal —el número no se reutiliza y los repuestos regresan—.',
  },
  {
    titulo: 'Factura con CAI',
    texto:
      'Registra el CAI de cada sucursal y la factura sale con número autorizado, rango, fecha ' +
      'límite, RTN del cliente y valor en letras. El ISV lo lleva solo la factura con CAI, ' +
      'como manda el régimen. Le avisa cuando el rango se está acabando.',
  },
  {
    titulo: 'Reportes del día',
    texto:
      'Lo facturado hoy, esta semana y este mes, separado en repuestos y mano de obra, con ' +
      'el reparto por sucursal y por técnico. Se exporta a Excel.',
  },
  {
    titulo: 'Su gente y sus sucursales',
    texto:
      'Da de alta técnicos y les asigna sucursales. Cada uno ve solo lo que le toca: el ' +
      'técnico su trabajo, el cliente sus vehículos, usted todo.',
  },
]
</script>

<template>
  <div class="landing">
    <header class="bar">
      <BrandLogo variant="horizontal" :height="30" />
      <RouterLink class="entrar" :to="{ name: 'login' }">Entrar</RouterLink>
    </header>

    <!-- ------------------------------------------------------------------ portada -->
    <section class="hero">
      <div class="hero-texto">
        <h1>Sepa en qué va cada vehículo sin moverse del mostrador</h1>
        <p class="claim">
          GarajApp lleva las órdenes de trabajo, el inventario, las cotizaciones y el cobro de
          su taller de autos y motos. En el teléfono, que es donde usted trabaja.
        </p>
        <div class="acciones">
          <a class="boton" :href="whatsapp" target="_blank" rel="noopener">
            Escribir por WhatsApp
          </a>
          <a class="boton fantasma" href="#propuesta">Ver qué incluye</a>
        </div>
        <p class="fino">
          Instalado con los datos de su taller. Sin equipo nuevo: sirve el teléfono que ya
          tiene.
        </p>
      </div>

      <div class="telefono">
        <img
          src="/capturas/telefono-taller.png"
          width="390"
          height="848"
          alt="La pantalla de Hoy en el teléfono: lo cobrado del día, lo que urge atender y el patio por estado"
        />
      </div>
    </section>

    <!-- ------------------------------------------------------------------ quiénes la usan -->
    <!--
      Va pegado a la portada: es lo primero que pregunta un dueño de taller —«¿quién más la
      usa?»— y ninguna lista de funciones le contesta eso.
    -->
    <section class="seccion talleres-seccion">
      <h2>Talleres que ya trabajan con GarajApp</h2>
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

    <!-- ------------------------------------------------------------------ problema -->
    <section class="banda">
      <h2>El día en el taller, hoy</h2>
      <div class="rejilla tres">
        <article v-for="p in problemas" :key="p.titulo" class="tarjeta">
          <h3>{{ p.titulo }}</h3>
          <p>{{ p.texto }}</p>
        </article>
      </div>
    </section>

    <!-- ------------------------------------------------------------------ flujo -->
    <section class="seccion">
      <h2>Cómo funciona</h2>
      <ol class="pasos">
        <li v-for="(paso, i) in pasos" :key="paso.titulo">
          <span class="numero num">{{ i + 1 }}</span>
          <div>
            <h3>{{ paso.titulo }}</h3>
            <p>{{ paso.texto }}</p>
          </div>
        </li>
      </ol>
    </section>

    <!-- ------------------------------------------------------------------ el cliente -->
    <section class="banda partida">
      <div>
        <h2>Su cliente también lo ve</h2>
        <p>
          La cotización sale por WhatsApp como un enlace. El cliente la abre en su teléfono,
          ve lo que se le va a cobrar y aprueba —o dice que no— sin instalar nada ni crear
          ninguna cuenta.
        </p>
        <p>
          Si usted quiere, además le abre acceso para que siga la reparación de su vehículo con
          las fotos y el avance. Es opcional, cliente por cliente.
        </p>
      </div>
      <img
        class="captura-telefono"
        src="/capturas/cotizacion-cliente.png"
        width="390"
        height="820"
        alt="La cotización tal como la abre el cliente desde WhatsApp, con el detalle y el total"
        loading="lazy"
      />
    </section>

    <!-- ------------------------------------------------------------------ alcance -->
    <section class="seccion">
      <h2>Qué incluye</h2>
      <div class="rejilla dos">
        <article v-for="item in incluye" :key="item.titulo" class="tarjeta">
          <h3>{{ item.titulo }}</h3>
          <p>{{ item.texto }}</p>
        </article>
      </div>
    </section>

    <!-- ------------------------------------------------------------------ teléfono -->
    <section class="banda">
      <h2>Todo desde el teléfono, incluso cobrar</h2>
      <p class="centrado">
        El técnico marca los pasos y toma las fotos donde está el vehículo. Usted arma la
        cotización y factura con el cliente enfrente, sin subir a la oficina.
      </p>
      <div class="galeria">
        <img
          src="/capturas/telefono-orden.png"
          width="390"
          height="848"
          alt="El detalle de una orden en el teléfono, con el diagnóstico y los pasos de la reparación"
          loading="lazy"
        />
        <img
          src="/capturas/telefono-facturar.png"
          width="390"
          height="848"
          alt="Cerrar y facturar en el teléfono: el total sin ISV, la forma de pago y el aviso del próximo servicio"
          loading="lazy"
        />
      </div>
    </section>

    <!-- ------------------------------------------------------------------ panel -->
    <section class="seccion">
      <h2>Y en la computadora, lo mismo con más sitio</h2>
      <p class="centrado">
        El tablero por estados y la bodega en pantalla grande, para cuando toca sentarse a ver
        el mes.
      </p>
      <img
        class="captura-panel"
        src="/capturas/panel-indicadores.png"
        width="1440"
        height="400"
        alt="El reporte de ingresos: repuestos, lo vendido en mostrador, mano de obra, total y margen, con la gráfica de cada día"
        loading="lazy"
      />
      <img
        class="captura-panel"
        src="/capturas/panel-ordenes.png"
        width="1440"
        height="900"
        alt="El tablero de órdenes de trabajo en el navegador, con las órdenes agrupadas por estado"
        loading="lazy"
      />
      <img
        class="captura-panel"
        src="/capturas/panel-inventario.png"
        width="1440"
        height="900"
        alt="El inventario en el navegador, con la existencia de cada repuesto por sucursal y su ubicación"
        loading="lazy"
      />
    </section>

    <!-- ------------------------------------------------------------------ propuesta -->
    <!-- El precio no va en la página: se manda en la propuesta (propuesta-garajapp.html),
         que se adapta a las sucursales y a la forma de pago de cada taller. -->
    <section id="propuesta" class="seccion">
      <h2>Qué recibe al contratar</h2>
      <div class="propuesta">
        <ul>
          <li>Usuarios, órdenes, fotos y cotizaciones <strong>sin límite</strong></li>
          <li>Instalación, traslado de sus datos y capacitación <strong>sin costo</strong></li>
          <li>Alojamiento, respaldos y soporte incluidos</li>
          <li>Sus datos aparte de los de cualquier otro taller</li>
        </ul>
        <p class="cifra-pie">
          El precio depende de cuántas sucursales tenga y de la forma de pago. Escríbame y le
          paso la propuesta.
        </p>
        <a class="boton" :href="whatsapp" target="_blank" rel="noopener">
          Pedir la propuesta por WhatsApp
        </a>
        <p class="fino">
          Si su taller tiene CAI, la factura sale con él: número autorizado, rango, fecha
          límite y valor en letras. Sin CAI sirve como comprobante de entrega.
        </p>
      </div>
    </section>

    <!-- ------------------------------------------------------------------ cierre -->
    <section class="cierre">
      <h2>Hablemos de su taller</h2>
      <p>
        Cuénteme cuántas sucursales tiene y cómo trabaja hoy. Se lo instalo con sus datos y lo
        prueba con trabajos de verdad.
      </p>
      <div class="acciones">
        <a class="boton claro" :href="whatsapp" target="_blank" rel="noopener">
          <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path
              d="M17.47 14.38c-.3-.15-1.76-.87-2.03-.97-.27-.1-.47-.15-.67.15-.2.3-.77.97-.94 1.16-.17.2-.35.22-.64.08-.3-.15-1.26-.46-2.39-1.48-.88-.79-1.48-1.76-1.65-2.06-.18-.3-.02-.46.13-.6.13-.14.35-.35.52-.52.17-.18.23-.3.35-.5.11-.2.05-.37-.03-.52-.09-.15-.66-1.61-.91-2.21-.23-.58-.47-.5-.65-.51h-.57c-.2 0-.52.07-.79.37-.27.3-1.04 1.02-1.04 2.48s1.07 2.87 1.21 3.07c.15.2 2.1 3.2 5.08 4.49.71.3 1.26.49 1.69.62.71.23 1.36.2 1.87.12.57-.09 1.76-.72 2.01-1.41.25-.7.25-1.29.17-1.42-.07-.12-.27-.2-.57-.35M12.05 21.8a9.87 9.87 0 0 1-5.03-1.38l-.36-.22-3.74.99 1-3.65-.24-.38a9.86 9.86 0 0 1-1.51-5.26c0-5.45 4.44-9.89 9.89-9.89 2.64 0 5.12 1.03 6.99 2.9a9.83 9.83 0 0 1 2.89 6.99c0 5.45-4.43 9.89-9.89 9.89m8.41-18.3A11.82 11.82 0 0 0 12.05 0C5.5 0 .16 5.34.16 11.89c0 2.1.55 4.15 1.59 5.95L.06 24l6.3-1.65a11.88 11.88 0 0 0 5.69 1.45c6.55 0 11.89-5.34 11.89-11.9 0-3.17-1.24-6.16-3.48-8.41Z"
            />
          </svg>
          WhatsApp
        </a>
        <a class="boton fantasma claro" :href="correo">
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            aria-hidden="true"
          >
            <rect x="2.5" y="4.5" width="19" height="15" rx="2" />
            <path d="m3 7 9 5.5L21 7" />
          </svg>
          Correo
        </a>
      </div>
    </section>

    <footer class="pie">
      <BrandLogo variant="isotipo" :height="18" />
      <span>Hecho en Honduras · Lempiras e ISV 15%</span>
      <!-- Archivo estático en `public`, así que va con <a> y no con RouterLink: el router de
           Vue no lo conoce y lo mandaría al 404. El manual no se enlaza a propósito: se abre
           por su ruta (/manual) cuando hace falta mandárselo a alguien. -->
      <a href="/privacidad">Privacidad</a>
      <a href="/soporte">Soporte</a>
      <RouterLink :to="{ name: 'login' }">Entrar al panel</RouterLink>
    </footer>
  </div>
</template>

<style scoped>
/* Ancho de lectura común a todas las secciones. */
.landing {
  --ancho: min(64rem, 100%);
  background: var(--bg);
}

.bar {
  width: var(--ancho);
  margin: 0 auto;
  padding: 1rem;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.entrar {
  font-weight: 500;
  padding: 0.375rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
}

.entrar:hover {
  text-decoration: none;
  border-color: var(--accent);
}

/* ------------------------------------------------------------------ portada */

.hero {
  width: var(--ancho);
  margin: 0 auto;
  padding: 1rem 1rem 3rem;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 2rem;
}

.hero-texto {
  flex: 1 1 20rem;
}

h1 {
  font-size: clamp(1.875rem, 6vw, 2.75rem);
  line-height: 1.1;
  margin: 0 0 1rem;
}

.claim {
  font-size: 1.125rem;
  line-height: 1.55;
  color: var(--text-muted);
  margin: 0 0 1.5rem;
}

.acciones {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}

/* Los botones de esta página son enlaces, no <button>, así que no heredan el estilo global. */
.boton {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1.25rem;
  border: 1px solid var(--accent);
  border-radius: var(--radius-sm);
  background: var(--accent);
  color: var(--surface);
  font-weight: 500;
  font-size: 1rem;
}

.boton:hover {
  text-decoration: none;
  background: var(--brand-deep);
  border-color: var(--brand-deep);
}

.boton svg {
  width: 1.125rem;
  height: 1.125rem;
  flex: none;
}

.boton.fantasma {
  background: transparent;
  color: var(--accent);
}

.boton.fantasma:hover {
  background: var(--surface-alt);
  border-color: var(--accent);
  color: var(--accent);
}

.fino {
  margin: 1rem 0 0;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

/* Marco de teléfono dibujado con CSS: no hace falta una imagen de aparato. */
.telefono {
  flex: 0 1 17rem;
  margin: 0 auto;
  padding: 0.5rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  background: var(--surface);
}

.telefono img,
.captura-telefono {
  display: block;
  width: 100%;
  height: auto;
  border-radius: var(--radius-md);
}

/* ------------------------------------------------------------------ secciones */

.seccion,
.banda {
  padding: 3rem 1rem;
}

.banda {
  background: var(--surface-alt);
}

.seccion > h2,
.banda > h2 {
  width: var(--ancho);
  margin: 0 auto 1.5rem;
  font-size: clamp(1.375rem, 4vw, 1.875rem);
}

.centrado {
  width: var(--ancho);
  margin: -0.75rem auto 1.5rem;
  color: var(--text-muted);
}

.rejilla {
  width: var(--ancho);
  margin: 0 auto;
  display: grid;
  gap: 1rem;
}

.tarjeta {
  padding: 1.25rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.tarjeta h3 {
  margin: 0 0 0.5rem;
  font-size: 1.0625rem;
}

.tarjeta p {
  margin: 0;
  color: var(--text-muted);
  line-height: 1.55;
}

/* ------------------------------------------------------- talleres que ya la usan */

/* Menos aire que una sección normal: es una franja de confianza, no un capítulo. */
.talleres-seccion {
  padding-top: 2rem;
  padding-bottom: 2rem;
}

.talleres {
  width: var(--ancho);
  margin: 0 auto;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(13rem, 1fr));
  gap: 1rem;
  justify-items: center;
}

.taller {
  margin: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.625rem;
  width: 100%;
  max-width: 16rem;
}

/* Cada logo sobre el fondo que le corresponde: uno es negro sobre blanco y el otro trae el
   suyo oscuro incrustado, así que un fondo común borraría a uno de los dos. */
.marco {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  min-height: 8rem;
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

.marco.claro {
  background: #ffffff;
}

.marco.oscuro {
  background: #0d1117;
}

.marco img {
  max-width: 100%;
  max-height: 6rem;
  object-fit: contain;
}

.taller figcaption {
  color: var(--text-muted);
  font-size: 0.9375rem;
  text-align: center;
}

/* ------------------------------------------------------------------ pasos */

.pasos {
  width: var(--ancho);
  margin: 0 auto;
  padding: 0;
  list-style: none;
  display: grid;
  gap: 1.25rem;
}

.pasos li {
  display: flex;
  gap: 1rem;
  align-items: flex-start;
}

.numero {
  flex: 0 0 2rem;
  height: 2rem;
  display: grid;
  place-items: center;
  border-radius: 999px;
  background: var(--accent);
  color: var(--surface);
  font-weight: 500;
}

.pasos h3 {
  margin: 0.25rem 0 0.25rem;
  font-size: 1.0625rem;
}

.pasos p {
  margin: 0;
  color: var(--text-muted);
  line-height: 1.55;
}

/* ------------------------------------------------------------------ el cliente */

.partida {
  display: flex;
  flex-wrap: wrap;
  gap: 2rem;
  align-items: center;
  justify-content: center;
}

.partida > div {
  flex: 1 1 20rem;
  max-width: 34rem;
}

.partida h2 {
  margin: 0 0 1rem;
  font-size: clamp(1.375rem, 4vw, 1.875rem);
}

.partida p {
  margin: 0 0 1rem;
  color: var(--text-muted);
  line-height: 1.55;
}

.captura-telefono {
  flex: 0 1 15rem;
  border: 1px solid var(--border);
}

/* ------------------------------------------------------------------ galería */

.galeria {
  width: var(--ancho);
  margin: 0 auto;
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 1.5rem;
}

.galeria img {
  width: min(15rem, 100%);
  height: auto;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

/* ------------------------------------------------------------------ panel */

.captura-panel {
  display: block;
  width: var(--ancho);
  height: auto;
  margin: 0 auto 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

/* ------------------------------------------------------------------ propuesta */

.propuesta {
  width: min(28rem, 100%);
  margin: 0 auto;
  padding: 1.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  text-align: center;
}

.cifra-pie {
  margin: 0 0 1.25rem;
  color: var(--text-muted);
  line-height: 1.5;
}

.propuesta ul {
  margin: 0 0 1.25rem;
  padding: 0;
  list-style: none;
  text-align: left;
  display: grid;
  gap: 0.5rem;
}

.propuesta li {
  padding-left: 1.25rem;
  position: relative;
  line-height: 1.5;
}

.propuesta li::before {
  content: '·';
  position: absolute;
  left: 0.375rem;
  color: var(--accent);
  font-weight: 700;
}

/* ------------------------------------------------------------------ cierre */

.cierre {
  /* La banda es azul de marca en los dos temas, así que aquí --surface no puede seguir al
     tema: en oscuro el botón «claro» salía negro con texto azul marino, ilegible sobre el
     azul. Se fija a los valores del tema claro solo dentro de esta sección. */
  --surface: #ffffff;
  --surface-alt: #eef0f4;

  padding: 3.5rem 1rem;
  background: var(--brand-deep);
  color: var(--surface);
  text-align: center;
}

.cierre h2 {
  margin: 0 0 0.75rem;
  font-size: clamp(1.375rem, 4vw, 1.875rem);
}

.cierre p {
  width: min(34rem, 100%);
  margin: 0 auto 1.5rem;
  line-height: 1.55;
}

.cierre .acciones {
  justify-content: center;
}

/* Sobre el azul profundo, el botón se invierte: relleno claro y texto azul. */
.boton.claro {
  background: var(--surface);
  border-color: var(--surface);
  color: var(--brand-deep);
}

.boton.claro:hover {
  background: var(--surface-alt);
  border-color: var(--surface-alt);
  color: var(--brand-deep);
}

.boton.fantasma.claro {
  background: transparent;
  border-color: var(--surface);
  color: var(--surface);
}

.boton.fantasma.claro:hover {
  background: transparent;
  color: var(--surface);
  text-decoration: underline;
}

/* ------------------------------------------------------------------ pie */

.pie {
  width: var(--ancho);
  margin: 0 auto;
  padding: 1.5rem 1rem 2.5rem;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.pie span {
  flex: 1 1 auto;
}

/* Dos columnas desde tableta, tres solo para las tarjetas cortas del problema. */
@media (min-width: 40rem) {
  .rejilla.dos {
    grid-template-columns: 1fr 1fr;
  }
}

@media (min-width: 56rem) {
  .rejilla.tres {
    grid-template-columns: repeat(3, 1fr);
  }

  .pasos {
    grid-template-columns: 1fr 1fr;
    gap: 2rem;
  }
}
</style>

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
      'Al entregar, la orden se factura con lo que de verdad se consumió. Si el cliente ' +
      'queda debiendo, el saldo queda en cuentas por cobrar con su fecha de pago.',
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
          <a class="boton fantasma" href="#precio">Ver el precio</a>
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
          height="808"
          alt="La bandeja del taller en el teléfono, con los ingresos del día y las órdenes abiertas"
        />
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
        height="614"
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
          height="808"
          alt="El detalle de una orden en el teléfono, con el diagnóstico y los pasos de la reparación"
          loading="lazy"
        />
        <img
          src="/capturas/telefono-facturar.png"
          width="390"
          height="808"
          alt="La sección de cerrar y facturar en el teléfono, con el total y la forma de pago"
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
        height="330"
        alt="Los indicadores del día: facturado de hoy, de la semana y del mes, órdenes abiertas y cuentas por cobrar"
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

    <!-- ------------------------------------------------------------------ precio -->
    <section id="precio" class="seccion">
      <h2>Precio</h2>
      <div class="precio">
        <p class="cifra num">L 1,200<span>/ mes</span></p>
        <p class="cifra-pie">por taller, con una sucursal incluida</p>
        <ul>
          <li><strong class="num">+ L 400 / mes</strong> por cada sucursal adicional</li>
          <li>Usuarios, órdenes, fotos y cotizaciones <strong>sin límite</strong></li>
          <li>Instalación, traslado de sus datos y capacitación <strong>sin costo</strong></li>
          <li>Alojamiento, respaldos y soporte incluidos</li>
        </ul>
        <a class="boton" :href="whatsapp" target="_blank" rel="noopener">
          Escribir por WhatsApp
        </a>
        <p class="fino">
          La factura en PDF sirve como comprobante de entrega para su cliente; no sustituye el
          talonario autorizado por el SAR.
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
          WhatsApp 9824-2108
        </a>
        <a class="boton fantasma claro" :href="correo">{{ CORREO }}</a>
      </div>
    </section>

    <footer class="pie">
      <BrandLogo variant="isotipo" :height="18" />
      <span>Hecho en Honduras · Lempiras e ISV 15%</span>
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
  display: inline-block;
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

/* ------------------------------------------------------------------ precio */

.precio {
  width: min(28rem, 100%);
  margin: 0 auto;
  padding: 1.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  text-align: center;
}

.cifra {
  margin: 0;
  font-size: 2.5rem;
  font-weight: 500;
  line-height: 1.1;
}

.cifra span {
  font-size: 1rem;
  color: var(--text-muted);
}

.cifra-pie {
  margin: 0.25rem 0 1.25rem;
  color: var(--text-muted);
}

.precio ul {
  margin: 0 0 1.5rem;
  padding: 0;
  list-style: none;
  text-align: left;
  display: grid;
  gap: 0.5rem;
}

.precio li {
  padding-left: 1.25rem;
  position: relative;
  line-height: 1.5;
}

.precio li::before {
  content: '·';
  position: absolute;
  left: 0.375rem;
  color: var(--accent);
  font-weight: 700;
}

/* ------------------------------------------------------------------ cierre */

.cierre {
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

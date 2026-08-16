<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { apiUrl } from '@/api/client'
import BrandLogo from '@/components/BrandLogo.vue'
import NotificationBell from '@/components/NotificationBell.vue'
import { useAuthStore } from '@/stores/auth'
import { Roles } from '@/types/api'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

/**
 * El logo del taller, si lo subió. La ruta es pública porque un `<img>` no manda la cabecera
 * Authorization; ver el comentario de TenantLogoController en el backend.
 */
const logoTaller = computed(() => apiUrl(auth.user?.tenantLogoUrl))

/**
 * El menú se arma por perfil: cada rol solo ve lo suyo.
 *
 * Va agrupado y a la izquierda porque en fila ya no cabía: el Dueño tiene trece entradas y
 * cada función nueva le añade otra. Los grupos siguen el día del taller —primero el trabajo,
 * después el dinero, luego lo que se configura una vez— y no el orden en que se construyeron.
 */
const navGroups = computed<{ title: string | null; items: { to: object; label: string }[] }[]>(
  () => {
    switch (auth.role) {
      case Roles.Owner:
        return [
          {
            title: 'Trabajo',
            items: [
              { to: { name: 'work-orders' }, label: 'Órdenes' },
              { to: { name: 'service-requests' }, label: 'Requerimientos' },
              { to: { name: 'service-reminders' }, label: 'Recordatorios' },
            ],
          },
          {
            title: 'Dinero',
            items: [
              { to: { name: 'quotes' }, label: 'Cotizaciones' },
              { to: { name: 'receivables' }, label: 'Por cobrar' },
              // Se abre todos los días al cerrar, así que va en el menú y no dentro de Reportes.
              { to: { name: 'cash-close' }, label: 'Caja' },
              { to: { name: 'reports' }, label: 'Reportes' },
            ],
          },
          {
            title: 'Catálogos',
            items: [
              { to: { name: 'customers' }, label: 'Clientes' },
              { to: { name: 'inventory' }, label: 'Inventario' },
              { to: { name: 'labor-services' }, label: 'Mano de obra' },
              { to: { name: 'job-templates' }, label: 'Trabajos frecuentes' },
            ],
          },
          {
            title: 'Administración',
            items: [
              { to: { name: 'users' }, label: 'Usuarios' },
              { to: { name: 'workshop' }, label: 'Taller' },
            ],
          },
        ]
      case Roles.Technician:
        // Tres entradas no necesitan títulos de grupo: nombrarlos sería más ruido que ayuda.
        return [
          {
            title: null,
            items: [
              { to: { name: 'my-assignments' }, label: 'Mis asignaciones' },
              { to: { name: 'service-requests' }, label: 'Requerimientos' },
              { to: { name: 'inventory' }, label: 'Inventario' },
            ],
          },
        ]
      case Roles.Customer:
        return [{ title: null, items: [{ to: { name: 'my-vehicles' }, label: 'Mis vehículos' }] }]
      case Roles.Platform:
        // Nosotros. No hay órdenes ni clientes que ver: este perfil administra el cobro.
        return [{ title: null, items: [{ to: { name: 'platform-tenants' }, label: 'Talleres' }] }]
      default:
        return []
    }
  },
)

/**
 * En pantalla angosta el menú se retira fuera de vista y se saca con el botón. En escritorio
 * está siempre, y este estado no le afecta.
 */
const menuAbierto = ref(false)

/**
 * El aviso de la mensualidad. Viene resuelto del backend —incluido el texto— y solo llega al
 * Dueño; al Técnico y al Cliente les llega null, así que aquí no hay nada que decidir sobre
 * quién lo ve. Estando al día tampoco se pinta: un aviso permanente deja de leerse.
 */
const suscripcion = computed(() => {
  const info = auth.user?.subscription
  return info && info.state !== 'Active' ? info : (info?.agreementThrough ? info : null)
})

/** Cuánto apremia. Solo cambia el color y el ícono; el texto lo escribe el backend. */
const tonoSuscripcion = computed(() => {
  const estado = suscripcion.value?.state
  if (!estado) return ''
  if (suscripcion.value?.agreementThrough) return 'acuerdo'
  return estado === 'DueSoon' ? 'aviso' : estado === 'Grace' ? 'urgente' : 'cortado'
})

// Al navegar se cierra solo: en un teléfono, quedarse con el menú encima de la pantalla que
// se acaba de abrir es la forma más rápida de que parezca que no pasó nada.
watch(() => route.fullPath, () => (menuAbierto.value = false))

async function logout() {
  await auth.logout()
  await router.replace({ name: 'login' })
}
</script>

<template>
  <div class="shell">
    <aside :class="{ abierto: menuAbierto }">
      <div class="brand">
        <BrandLogo variant="horizontal" :height="24" />
        <!-- El perfil Plataforma no está «para» ningún taller: es el nuestro. -->
        <div v-if="auth.role !== Roles.Platform" class="taller">
          <span class="para">para</span>
          <img
            v-if="logoTaller"
            class="logo-taller"
            :src="logoTaller"
            :alt="auth.user?.tenantName"
          />
          <span v-else class="tenant">{{ auth.user?.tenantName }}</span>
        </div>
      </div>

      <nav>
        <template v-for="(group, i) in navGroups" :key="group.title ?? i">
          <p v-if="group.title" class="grupo">{{ group.title }}</p>
          <RouterLink v-for="item in group.items" :key="item.label" :to="item.to">
            {{ item.label }}
          </RouterLink>
        </template>
      </nav>

      <div class="cuenta">
        <div class="quien">
          <span class="nombre">{{ auth.user?.fullName }}</span>
          <span class="role">{{ auth.role }}</span>
        </div>
        <button type="button" @click="logout">Salir</button>
      </div>
    </aside>

    <!-- Cierra el menú al tocar fuera. Solo existe con el menú desplegado en pantalla angosta. -->
    <div v-if="menuAbierto" class="velo" @click="menuAbierto = false" />

    <div class="col">
      <header>
        <button
          type="button"
          class="hamburguesa"
          aria-label="Menú"
          @click="menuAbierto = !menuAbierto"
        >
          ☰
        </button>

        <select
          v-if="auth.user && auth.user.branches.length > 1"
          :value="auth.activeBranchId"
          @change="auth.setActiveBranch(($event.target as HTMLSelectElement).value)"
        >
          <option v-for="branch in auth.user.branches" :key="branch.id" :value="branch.id">
            {{ branch.name }}
          </option>
        </select>

        <!-- Los avisos son del trabajo del taller, y la plataforma no tiene taller. -->
        <NotificationBell v-if="auth.role !== Roles.Platform" />
      </header>

      <main>
        <p v-if="suscripcion" class="suscripcion" :class="tonoSuscripcion">
          {{ suscripcion.message }}
        </p>
        <RouterView />
      </main>
    </div>
  </div>
</template>

<style scoped>
.shell {
  min-height: 100dvh;
  display: flex;
}

/* ---------------------------------------------------------------- menú lateral */

aside {
  position: sticky;
  top: 0;
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  width: 15rem;
  height: 100dvh;
  border-right: 1px solid var(--border);
  background: var(--surface);
}

/* «GarajApp para [el taller]»: el sistema es nuestro, el taller es suyo. */
.brand {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.375rem;
  padding: 1rem 1rem 0.875rem;
  border-bottom: 1px solid var(--border);
}

.taller {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  min-height: 1.5rem;
}

.para {
  font-size: 0.75rem;
  color: var(--text-muted);
}

.tenant {
  font-size: 0.875rem;
  font-weight: 500;
}

/* Tope de ancho además de alto: un logo muy horizontal se saldría del menú. */
.logo-taller {
  height: 24px;
  width: auto;
  max-width: 8.5rem;
  object-fit: contain;
}

nav {
  display: flex;
  flex-direction: column;
  gap: 0.0625rem;
  flex: 1;
  padding: 0.75rem 0.5rem;
  overflow-y: auto;
}

.grupo {
  margin: 0.875rem 0 0.25rem;
  padding: 0 0.625rem;
  font-size: 0.6875rem;
  font-weight: 600;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--text-muted);
}

.grupo:first-child {
  margin-top: 0;
}

nav a {
  padding: 0.4375rem 0.625rem;
  border-radius: var(--radius-sm);
  color: var(--text);
  font-size: 0.875rem;
  text-decoration: none;
}

nav a:hover {
  background: var(--surface-alt);
}

/* La entrada actual se marca con una barra al costado además del color: en la pantalla de un
   taller, con sol de frente, el color solo no siempre se distingue. */
nav a.router-link-active {
  position: relative;
  background: var(--surface-alt);
  color: var(--accent);
  font-weight: 600;
}

nav a.router-link-active::before {
  content: '';
  position: absolute;
  left: 0;
  top: 0.375rem;
  bottom: 0.375rem;
  width: 3px;
  border-radius: 0 3px 3px 0;
  background: var(--accent);
}

.cuenta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  border-top: 1px solid var(--border);
}

.quien {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  min-width: 0;
}

.nombre {
  font-size: 0.875rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.role {
  align-self: flex-start;
  padding: 0.0625rem 0.4375rem;
  border-radius: 999px;
  background: var(--surface-alt);
  color: var(--text-muted);
  font-size: 0.6875rem;
}

/* ---------------------------------------------------------------- contenido */

.col {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
}

header {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.75rem;
  padding: 0.625rem 1.5rem;
  border-bottom: 1px solid var(--border);
  background: var(--surface);
}

.hamburguesa {
  display: none;
  margin-right: auto;
  padding: 0.25rem 0.625rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: none;
  color: var(--text);
  font-size: 1.125rem;
  line-height: 1.2;
  cursor: pointer;
}

main {
  flex: 1;
  padding: 1.5rem;
}

/* El aviso de la mensualidad. Va arriba del contenido y no en una esquina: el Dueño tiene que
   toparse con él, pero se puede seguir trabajando con la franja puesta. */
.suscripcion {
  margin: 0 0 1rem;
  padding: 0.625rem 0.875rem;
  border-left: 3px solid currentcolor;
  border-radius: var(--radius-sm);
  font-size: 0.875rem;
}

.suscripcion.acuerdo {
  color: var(--text-muted);
  background: var(--surface-alt);
}

.suscripcion.aviso {
  color: var(--warning);
  background: color-mix(in srgb, var(--warning) 12%, transparent);
}

.suscripcion.urgente {
  color: var(--warning);
  background: color-mix(in srgb, var(--warning) 22%, transparent);
  font-weight: 600;
}

.suscripcion.cortado {
  color: var(--danger);
  background: color-mix(in srgb, var(--danger) 14%, transparent);
  font-weight: 600;
}

.velo {
  position: fixed;
  inset: 0;
  z-index: 5;
  background: rgb(0 0 0 / 45%);
}

/* ---------------------------------------------------------------- pantalla angosta */

@media (max-width: 60rem) {
  aside {
    position: fixed;
    z-index: 10;
    transform: translateX(-100%);
    transition: transform 0.15s ease-out;
  }

  aside.abierto {
    transform: none;
    box-shadow: 0 0 2rem rgb(0 0 0 / 25%);
  }

  .hamburguesa {
    display: block;
  }

  main {
    padding: 1rem;
  }
}
</style>

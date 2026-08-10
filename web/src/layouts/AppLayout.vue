<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { apiUrl } from '@/api/client'
import BrandLogo from '@/components/BrandLogo.vue'
import NotificationBell from '@/components/NotificationBell.vue'
import { useAuthStore } from '@/stores/auth'
import { Roles } from '@/types/api'

const auth = useAuthStore()
const router = useRouter()

/**
 * El logo del taller, si lo subió. La ruta es pública porque un `<img>` no manda la cabecera
 * Authorization; ver el comentario de TenantLogoController en el backend.
 */
const logoTaller = computed(() => apiUrl(auth.user?.tenantLogoUrl))

/** El menú se arma por perfil: cada rol solo ve lo suyo. */
const navItems = computed(() => {
  switch (auth.role) {
    case Roles.Owner:
      return [
        { to: { name: 'work-orders' }, label: 'Órdenes' },
        { to: { name: 'service-requests' }, label: 'Requerimientos' },
        { to: { name: 'customers' }, label: 'Clientes' },
        { to: { name: 'inventory' }, label: 'Inventario' },
        { to: { name: 'labor-services' }, label: 'Mano de obra' },
        { to: { name: 'quotes' }, label: 'Cotizaciones' },
        { to: { name: 'reports' }, label: 'Reportes' },
        { to: { name: 'users' }, label: 'Usuarios' },
        { to: { name: 'workshop' }, label: 'Taller' },
      ]
    case Roles.Technician:
      return [
        { to: { name: 'my-assignments' }, label: 'Mis asignaciones' },
        { to: { name: 'service-requests' }, label: 'Requerimientos' },
        { to: { name: 'inventory' }, label: 'Inventario' },
      ]
    case Roles.Customer:
      return [{ to: { name: 'my-vehicles' }, label: 'Mis vehículos' }]
    default:
      return []
  }
})

async function logout() {
  await auth.logout()
  await router.replace({ name: 'login' })
}
</script>

<template>
  <div class="shell">
    <header>
      <div class="brand">
        <BrandLogo variant="horizontal" :height="26" />
        <span class="para">para</span>
        <img
          v-if="logoTaller"
          class="logo-taller"
          :src="logoTaller"
          :alt="auth.user?.tenantName"
        />
        <span v-else class="tenant">{{ auth.user?.tenantName }}</span>
      </div>

      <nav>
        <RouterLink v-for="item in navItems" :key="item.label" :to="item.to">
          {{ item.label }}
        </RouterLink>
      </nav>

      <div class="user">
        <NotificationBell />

        <select
          v-if="auth.user && auth.user.branches.length > 1"
          :value="auth.activeBranchId"
          @change="auth.setActiveBranch(($event.target as HTMLSelectElement).value)"
        >
          <option v-for="branch in auth.user.branches" :key="branch.id" :value="branch.id">
            {{ branch.name }}
          </option>
        </select>

        <span>{{ auth.user?.fullName }}</span>
        <span class="role">{{ auth.role }}</span>
        <button type="button" @click="logout">Salir</button>
      </div>
    </header>

    <main>
      <RouterView />
    </main>
  </div>
</template>

<style scoped>
.shell {
  min-height: 100dvh;
  display: flex;
  flex-direction: column;
}

header {
  display: flex;
  align-items: center;
  gap: 1.5rem;
  flex-wrap: wrap;
  padding: 0.75rem 1.25rem;
  border-bottom: 1px solid var(--border);
  background: var(--surface);
}

/* «GarajApp para [el taller]», en una línea: el sistema es nuestro, el taller es suyo. */
.brand {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.para {
  font-size: 0.75rem;
  color: var(--text-muted);
}

.tenant {
  font-size: 0.875rem;
  font-weight: 500;
}

/* Tope de ancho además de alto: un logo muy horizontal se comería la navegación. */
.logo-taller {
  height: 26px;
  width: auto;
  max-width: 9rem;
  object-fit: contain;
}

nav {
  display: flex;
  gap: 1rem;
  margin-right: auto;
}

nav a.router-link-active {
  font-weight: 600;
  color: var(--accent);
}

.user {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  font-size: 0.875rem;
}

.role {
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  background: var(--surface-alt);
  color: var(--text-muted);
  font-size: 0.75rem;
}

main {
  flex: 1;
  padding: 1.5rem;
}
</style>

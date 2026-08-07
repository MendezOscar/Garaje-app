import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { loadSession } from '@/api/session'
import { useAuthStore } from '@/stores/auth'
import { Roles, type Role } from '@/types/api'

declare module 'vue-router' {
  interface RouteMeta {
    /** Perfiles con acceso. Ausente = cualquier usuario autenticado. */
    roles?: Role[]
    public?: boolean
  }
}

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/LoginView.vue'),
    meta: { public: true },
  },
  {
    path: '/',
    component: () => import('@/layouts/AppLayout.vue'),
    children: [
      {
        path: '',
        name: 'home',
        // El guard de abajo rebota a la pantalla del perfil: un Técnico o un Cliente que
        // entre a "/" no se queda en el dashboard del Dueño.
        redirect: { name: 'dashboard' },
      },
      {
        path: 'dashboard',
        name: 'dashboard',
        component: () => import('@/views/owner/DashboardView.vue'),
        meta: { roles: [Roles.Owner] },
      },
      {
        path: 'mis-asignaciones',
        name: 'my-assignments',
        component: () => import('@/views/technician/MyAssignmentsView.vue'),
        meta: { roles: [Roles.Technician] },
      },
      {
        path: 'mis-vehiculos',
        name: 'my-vehicles',
        component: () => import('@/views/customer/MyVehiclesView.vue'),
        meta: { roles: [Roles.Customer] },
      },
    ],
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('@/views/NotFoundView.vue'),
    meta: { public: true },
  },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

/** Pantalla inicial de cada perfil. */
export function homeRouteFor(role: Role | null): string {
  switch (role) {
    case Roles.Owner:
      return 'dashboard'
    case Roles.Technician:
      return 'my-assignments'
    case Roles.Customer:
      return 'my-vehicles'
    default:
      return 'login'
  }
}

router.beforeEach(async (to) => {
  const auth = useAuthStore()

  // Al primer navegado tras recargar el navegador todavía no hay usuario en memoria.
  if (!auth.isAuthenticated && loadSession()) {
    await auth.loadCurrentUser()
  }

  if (to.meta.public) {
    return auth.isAuthenticated && to.name === 'login'
      ? { name: homeRouteFor(auth.role) }
      : true
  }

  if (!auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  if (to.meta.roles && !to.meta.roles.includes(auth.role!)) {
    return { name: homeRouteFor(auth.role) }
  }

  return true
})

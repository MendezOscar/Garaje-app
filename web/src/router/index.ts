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
    // La cotización que el cliente abre desde WhatsApp. Va fuera del layout y sin sesión:
    // quien la abre no tiene cuenta en el sistema y no debe necesitarla.
    path: '/q/:token',
    name: 'public-quote',
    component: () => import('@/views/PublicQuoteView.vue'),
    meta: { public: true },
  },
  {
    // El estado de cuenta que el cliente abre desde WhatsApp. Igual que la cotización: sin
    // sesión, con el token de la URL como única credencial.
    path: '/c/:token',
    name: 'public-statement',
    component: () => import('@/views/PublicStatementView.vue'),
    meta: { public: true },
  },
  {
    // El seguimiento de la orden. Es el enlace que el taller manda al recibir el vehículo y
    // el que más se va a abrir de los tres: casi ningún cliente instala la app.
    path: '/o/:token',
    name: 'public-order',
    component: () => import('@/views/PublicOrderView.vue'),
    meta: { public: true },
  },
  {
    // La raíz es la página de venta, no el panel: quien recibe el enlace por WhatsApp no
    // tiene cuenta, y antes caía en el formulario de acceso. El panel entra por "/login".
    path: '/',
    name: 'landing',
    component: () => import('@/views/LandingView.vue'),
    meta: { public: true },
  },
  {
    // Sigue montado en "/" —así ninguna ruta del panel cambió— pero ya no resuelve la ruta
    // vacía: de eso se encarga el registro de arriba.
    path: '/',
    component: () => import('@/layouts/AppLayout.vue'),
    children: [
      {
        path: 'ordenes',
        name: 'work-orders',
        component: () => import('@/views/owner/WorkOrderBoardView.vue'),
        meta: { roles: [Roles.Owner] },
      },
      {
        // El Técnico también entra: es quien muchas veces recibe la moto en el mostrador y
        // registra el requerimiento. Aprobarlo sigue siendo del Dueño, y el backend lo
        // impone; la pantalla solo esconde los botones que no le tocan.
        path: 'requerimientos',
        name: 'service-requests',
        component: () => import('@/views/owner/ServiceRequestsView.vue'),
        meta: { roles: [Roles.Owner, Roles.Technician] },
      },
      {
        path: 'clientes',
        name: 'customers',
        component: () => import('@/views/owner/CustomersView.vue'),
        meta: { roles: [Roles.Owner] },
      },
      {
        // Aparte de Reportes: esto no se mira, se trabaja —se busca al cliente que llamó y
        // se le anota el abono—. En Reportes queda solo el total.
        path: 'por-cobrar',
        name: 'receivables',
        component: () => import('@/views/owner/ReceivablesView.vue'),
        meta: { roles: [Roles.Owner] },
      },
      {
        path: 'reportes',
        name: 'reports',
        component: () => import('@/views/owner/ReportsView.vue'),
        meta: { roles: [Roles.Owner] },
      },
      {
        // Lo cobrado en el día, que no es lo facturado: se abre al cerrar, con el efectivo
        // en la mano.
        path: 'caja',
        name: 'cash-close',
        component: () => import('@/views/owner/CashCloseView.vue'),
        meta: { roles: [Roles.Owner] },
      },
      {
        // La ficha del taller: lo que sale impreso en la cotización y en la factura.
        path: 'taller',
        name: 'workshop',
        component: () => import('@/views/owner/WorkshopView.vue'),
        meta: { roles: [Roles.Owner] },
      },
      {
        path: 'usuarios',
        name: 'users',
        component: () => import('@/views/owner/UsersView.vue'),
        meta: { roles: [Roles.Owner] },
      },
      {
        path: 'mano-de-obra',
        name: 'labor-services',
        component: () => import('@/views/owner/LaborServicesView.vue'),
        meta: { roles: [Roles.Owner] },
      },
      {
        path: 'cotizaciones',
        name: 'quotes',
        component: () => import('@/views/owner/QuotesView.vue'),
        meta: { roles: [Roles.Owner] },
      },
      {
        // El Técnico también entra: necesita saber si hay existencia antes de prometer una
        // reparación. El backend le muestra solo la bodega de sus sucursales.
        path: 'inventario',
        name: 'inventory',
        component: () => import('@/views/owner/InventoryView.vue'),
        meta: { roles: [Roles.Owner, Roles.Technician] },
      },
      {
        // El detalle es la misma pantalla para los tres perfiles: el backend decide qué
        // datos y qué acciones devuelve según quién pregunta.
        path: 'ordenes/:id',
        name: 'work-order',
        component: () => import('@/views/WorkOrderDetailView.vue'),
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
      return 'work-orders'
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
    // A quien ya entró no se le enseña ni el login ni la página de venta: va a su pantalla.
    const forVisitorsOnly = to.name === 'login' || to.name === 'landing'

    return auth.isAuthenticated && forVisitorsOnly
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

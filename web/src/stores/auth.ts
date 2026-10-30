import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { api } from '@/api/client'
import { clearSession, loadSession, saveSession } from '@/api/session'
import { Roles, type AuthResponse, type CurrentUser, type Role } from '@/types/api'

/** Lo que se guarda cuando se eligió «todas», para distinguirlo de no haber elegido. */
const TODAS = 'todas'

function leerSucursalGuardada(user: CurrentUser | null): string | null {
  const guardado = localStorage.getItem('garaj.activeBranch')

  if (guardado === TODAS) return null
  if (guardado) return guardado

  // La primera vez se abre en la primera sucursal: es lo que el taller de una sola tiene,
  // y para el de varias es una elección concreta en vez de un promedio de todo.
  return user?.branches[0]?.id ?? null
}

export const useAuthStore = defineStore('auth', () => {
  const user = ref<CurrentUser | null>(loadSession()?.user ?? null)

  /**
   * La sucursal que se está mirando, y la obedece todo el panel.
   *
   * Null es «todas las sucursales», que no es lo mismo que «todavía no eligió»: por eso lo
   * guardado puede decir TODAS. Antes cada pantalla traía su propio selector y abría en
   * «todas» mientras la barra de arriba decía «Sucursal Centro»; las cifras no cuadraban
   * entre Inicio, Por cobrar y Reportes y nadie sabía cuál creer.
   */
  const activeBranchId = ref<string | null>(leerSucursalGuardada(user.value))

  const isAuthenticated = computed(() => user.value !== null)
  const role = computed<Role | null>(() => user.value?.role ?? null)
  const isOwner = computed(() => role.value === Roles.Owner)
  const isManager = computed(() => role.value === Roles.Manager)

  /**
   * Quien manda: el Dueño en todo el taller, el Encargado en sus sucursales. Es lo que
   * pregunta casi todo el panel —facturar, cobrar, cotizar—; lo que es del taller entero
   * pregunta por `isOwner`.
   */
  const isManagement = computed(() => isOwner.value || isManager.value)
  const isTechnician = computed(() => role.value === Roles.Technician)
  const isCustomer = computed(() => role.value === Roles.Customer)

  async function login(email: string, password: string): Promise<void> {
    const { data } = await api.post<AuthResponse>('/api/auth/login', { email, password })
    saveSession(data)
    user.value = data.user
    setActiveBranch(data.user.branches[0]?.id ?? null)
  }

  async function logout(): Promise<void> {
    const session = loadSession()
    if (session) {
      // Si el backend no responde igual cerramos la sesión local: el usuario pidió salir.
      await api.post('/api/auth/logout', { refreshToken: session.refreshToken }).catch(() => {})
    }
    clearSession()
    localStorage.removeItem('garaj.activeBranch')
    user.value = null
    activeBranchId.value = null
  }

  /** Revalida la sesión guardada contra el backend al arrancar la app. */
  async function loadCurrentUser(): Promise<void> {
    if (!loadSession()) {
      user.value = null
      return
    }

    try {
      const { data } = await api.get<CurrentUser>('/api/auth/me')
      user.value = data
      if (!activeBranchId.value) setActiveBranch(data.branches[0]?.id ?? null)
    } catch {
      // Solo se cierra si el interceptor ya borró la sesión porque el servidor la rechazó.
      // Si sigue guardada, lo que falló fue la red: se conserva lo último que se supo del
      // usuario y cada pantalla vuelve a pedir sus datos cuando haya conexión.
      if (loadSession()) return
      user.value = null
    }
  }

  function setActiveBranch(branchId: string | null): void {
    activeBranchId.value = branchId
    localStorage.setItem('garaj.activeBranch', branchId ?? TODAS)
  }

  /** Cómo se llama lo que se está mirando. Va en el subtítulo de cada pantalla. */
  const activeBranchName = computed(() =>
    activeBranchId.value
      ? (user.value?.branches.find((b) => b.id === activeBranchId.value)?.name ??
        'Todas las sucursales')
      : 'Todas las sucursales',
  )

  return {
    user,
    activeBranchId,
    activeBranchName,
    isAuthenticated,
    role,
    isOwner,
    isManager,
    isManagement,
    isTechnician,
    isCustomer,
    login,
    logout,
    loadCurrentUser,
    setActiveBranch,
  }
})

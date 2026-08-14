import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { api } from '@/api/client'
import { clearSession, loadSession, saveSession } from '@/api/session'
import { Roles, type AuthResponse, type CurrentUser, type Role } from '@/types/api'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<CurrentUser | null>(loadSession()?.user ?? null)
  const activeBranchId = ref<string | null>(
    localStorage.getItem('garaj.activeBranch') ?? user.value?.branches[0]?.id ?? null,
  )

  const isAuthenticated = computed(() => user.value !== null)
  const role = computed<Role | null>(() => user.value?.role ?? null)
  const isOwner = computed(() => role.value === Roles.Owner)
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
    if (branchId) localStorage.setItem('garaj.activeBranch', branchId)
    else localStorage.removeItem('garaj.activeBranch')
  }

  return {
    user,
    activeBranchId,
    isAuthenticated,
    role,
    isOwner,
    isTechnician,
    isCustomer,
    login,
    logout,
    loadCurrentUser,
    setActiveBranch,
  }
})

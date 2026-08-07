import type { AuthResponse, CurrentUser } from '@/types/api'

const STORAGE_KEY = 'garaj.session'

export interface Session {
  accessToken: string
  refreshToken: string
  expiresAt: string
  user: CurrentUser
}

/**
 * La sesión vive en localStorage y no en el store de Pinia porque el interceptor de axios
 * la necesita fuera del ciclo de vida de los componentes, y para sobrevivir al refresh
 * del navegador.
 */
export function loadSession(): Session | null {
  const raw = localStorage.getItem(STORAGE_KEY)
  if (!raw) return null

  try {
    return JSON.parse(raw) as Session
  } catch {
    localStorage.removeItem(STORAGE_KEY)
    return null
  }
}

export function saveSession(auth: AuthResponse): Session {
  const session: Session = {
    accessToken: auth.accessToken,
    refreshToken: auth.refreshToken,
    expiresAt: auth.expiresAt,
    user: auth.user,
  }
  localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
  return session
}

export function clearSession(): void {
  localStorage.removeItem(STORAGE_KEY)
}

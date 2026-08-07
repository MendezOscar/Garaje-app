import axios, {
  AxiosError,
  type AxiosInstance,
  type InternalAxiosRequestConfig,
} from 'axios'
import type { AuthResponse, ProblemDetails } from '@/types/api'
import { clearSession, loadSession, saveSession } from './session'

const baseURL = import.meta.env.VITE_API_URL ?? 'https://localhost:7080'

export const api: AxiosInstance = axios.create({
  baseURL,
  headers: { 'Content-Type': 'application/json' },
})

api.interceptors.request.use((config) => {
  const session = loadSession()
  if (session?.accessToken) {
    config.headers.Authorization = `Bearer ${session.accessToken}`
  }
  return config
})

/**
 * Refresco de token con una sola petición en vuelo: si varias llamadas fallan con 401 a la
 * vez, todas esperan el mismo refresh en lugar de gastar el refresh token N veces (la
 * rotación del backend invalidaría los intentos siguientes y cerraría la sesión).
 */
let refreshInFlight: Promise<string> | null = null

async function refreshAccessToken(): Promise<string> {
  const session = loadSession()
  if (!session?.refreshToken) throw new Error('No hay sesión que refrescar.')

  const { data } = await axios.post<AuthResponse>(
    `${baseURL}/api/auth/refresh`,
    { refreshToken: session.refreshToken },
    { headers: { 'Content-Type': 'application/json' } },
  )

  saveSession(data)
  return data.accessToken
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<ProblemDetails>) => {
    const original = error.config as InternalAxiosRequestConfig & { _retried?: boolean }

    const isAuthCall = original?.url?.includes('/api/auth/')
    if (error.response?.status !== 401 || original?._retried || isAuthCall) {
      return Promise.reject(error)
    }

    original._retried = true

    try {
      refreshInFlight ??= refreshAccessToken().finally(() => {
        refreshInFlight = null
      })
      const token = await refreshInFlight
      original.headers.Authorization = `Bearer ${token}`
      return api(original)
    } catch {
      clearSession()
      // Recarga dura en vez de router.push: evita importar el router aquí y garantiza que
      // no quede estado de una sesión muerta en memoria.
      window.location.assign('/login')
      return Promise.reject(error)
    }
  },
)

/** Extrae el mensaje legible de un error de la API para mostrarlo en pantalla. */
export function errorMessage(error: unknown, fallback = 'Ocurrió un error inesperado.'): string {
  if (axios.isAxiosError<ProblemDetails>(error)) {
    return error.response?.data?.detail ?? error.message ?? fallback
  }
  return error instanceof Error ? error.message : fallback
}

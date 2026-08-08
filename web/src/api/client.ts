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

/**
 * Baja un archivo de la API y lo guarda con el nombre indicado.
 *
 * No sirve un `<a href>` apuntando al endpoint: el token viaja en la cabecera Authorization
 * y el navegador no la manda al abrir una URL en una pestaña nueva, así que el PDF respondía
 * 401. Aquí la petición pasa por axios —con interceptor de token y de refresco— y lo que se
 * abre es un blob local.
 *
 * El nombre lo pone quien llama en vez de leerlo del Content-Disposition: exponer esa
 * cabecera al navegador exigiría tocar el CORS del backend para nada.
 */
export async function download(
  url: string,
  fileName: string,
  params?: Record<string, unknown>,
): Promise<void> {
  const { data } = await api.get<Blob>(url, { params, responseType: 'blob' })

  const objectUrl = URL.createObjectURL(data)
  const link = document.createElement('a')
  link.href = objectUrl
  link.download = fileName
  link.click()

  // Sin revocar, el blob queda en memoria hasta recargar la página. Se difiere porque
  // Firefox cancela la descarga si la URL muere en el mismo tick del click.
  setTimeout(() => URL.revokeObjectURL(objectUrl), 10_000)
}

/** Extrae el mensaje legible de un error de la API para mostrarlo en pantalla. */
export function errorMessage(error: unknown, fallback = 'Ocurrió un error inesperado.'): string {
  if (axios.isAxiosError<ProblemDetails>(error)) {
    // En una descarga la respuesta viene como blob, también cuando es el error: leerla
    // exigiría await y aquí no lo hay, así que se usa el mensaje de respaldo.
    if (error.response?.data instanceof Blob) return fallback

    return error.response?.data?.detail ?? error.message ?? fallback
  }
  return error instanceof Error ? error.message : fallback
}

// Espejo de los DTOs de Garaj.Application. A partir de la Fase 1 conviene generarlos desde
// el OpenAPI del backend (`openapi-typescript`) para que no se desincronicen.

export const Roles = {
  Owner: 'Owner',
  Technician: 'Technician',
  Customer: 'Customer',
  /** Nosotros: damos de alta talleres y les cobramos. No pertenece a ningún taller. */
  Platform: 'Platform',
} as const

export type Role = (typeof Roles)[keyof typeof Roles]

export interface BranchSummary {
  id: string
  name: string
  code: string | null
}

export interface CurrentUser {
  id: string
  email: string
  fullName: string
  role: Role
  tenantId: string
  tenantName: string
  /** Ruta del logo del taller relativa a la base de la API, o null si no ha subido ninguno. */
  tenantLogoUrl: string | null
  branches: BranchSummary[]
  customerId: string | null
  /** Cómo va el taller con su mensualidad. Null salvo para el Dueño: el backend no lo manda. */
  subscription: SubscriptionInfo | null
}

export type SubscriptionState = 'Active' | 'DueSoon' | 'Grace' | 'ReadOnly' | 'Suspended'

export interface SubscriptionInfo {
  state: SubscriptionState
  /** Si el taller puede registrar trabajo. En false, la API responde 402 a todo lo que escribe. */
  canWrite: boolean
  paidThrough: string | null
  daysLeft: number | null
  readOnlyOn: string | null
  agreementThrough: string | null
  agreementNote: string | null
  /** El texto ya redactado por el backend: los tres clientes dicen lo mismo. */
  message: string
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  expiresAt: string
  user: CurrentUser
}

/** Forma del ProblemDetails que devuelve ExceptionHandlingMiddleware. */
export interface ProblemDetails {
  status: number
  title: string
  detail: string
  instance?: string
}

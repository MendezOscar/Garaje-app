// Espejo de los DTOs de Garaj.Application. A partir de la Fase 1 conviene generarlos desde
// el OpenAPI del backend (`openapi-typescript`) para que no se desincronicen.

export const Roles = {
  Owner: 'Owner',
  Technician: 'Technician',
  Customer: 'Customer',
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
  branches: BranchSummary[]
  customerId: string | null
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

// Espejo de los DTOs de la Fase 1. Los enums numéricos coinciden con los de
// Garaj.Domain.Enums: si cambian allá, hay que cambiarlos aquí.

// Objetos const en vez de `enum`: el tsconfig usa `erasableSyntaxOnly`, que prohíbe la
// sintaxis que emite código en tiempo de ejecución. El uso queda igual (`VehicleType.Car`).

export const VehicleType = {
  Car: 1,
  Motorcycle: 2,
} as const
export type VehicleType = (typeof VehicleType)[keyof typeof VehicleType]

export const ServiceRequestStatus = {
  Pending: 1,
  Quoted: 2,
  Approved: 3,
  Rejected: 4,
  Converted: 5,
} as const
export type ServiceRequestStatus = (typeof ServiceRequestStatus)[keyof typeof ServiceRequestStatus]

export const WorkOrderStatus = {
  Received: 1,
  Diagnosing: 2,
  WaitingApproval: 3,
  WaitingParts: 4,
  InProgress: 5,
  Testing: 6,
  Ready: 7,
  Delivered: 8,
  Cancelled: 9,
} as const
export type WorkOrderStatus = (typeof WorkOrderStatus)[keyof typeof WorkOrderStatus]

export const MediaOwnerType = {
  ServiceRequest: 1,
  WorkOrder: 2,
  WorkOrderTask: 3,
} as const
export type MediaOwnerType = (typeof MediaOwnerType)[keyof typeof MediaOwnerType]

/** Columnas del tablero, en el orden en que avanza el trabajo. */
export const KANBAN_COLUMNS: WorkOrderStatus[] = [
  WorkOrderStatus.Received,
  WorkOrderStatus.Diagnosing,
  WorkOrderStatus.WaitingApproval,
  WorkOrderStatus.WaitingParts,
  WorkOrderStatus.InProgress,
  WorkOrderStatus.Testing,
  WorkOrderStatus.Ready,
]

export const WORK_ORDER_STATUS_LABEL: Record<WorkOrderStatus, string> = {
  [WorkOrderStatus.Received]: 'Recibida',
  [WorkOrderStatus.Diagnosing]: 'En diagnóstico',
  [WorkOrderStatus.WaitingApproval]: 'Esperando aprobación',
  [WorkOrderStatus.WaitingParts]: 'Esperando repuestos',
  [WorkOrderStatus.InProgress]: 'En proceso',
  [WorkOrderStatus.Testing]: 'En pruebas',
  [WorkOrderStatus.Ready]: 'Lista para entrega',
  [WorkOrderStatus.Delivered]: 'Entregada',
  [WorkOrderStatus.Cancelled]: 'Cancelada',
}

export const SERVICE_REQUEST_STATUS_LABEL: Record<ServiceRequestStatus, string> = {
  [ServiceRequestStatus.Pending]: 'Pendiente',
  [ServiceRequestStatus.Quoted]: 'Cotizado',
  [ServiceRequestStatus.Approved]: 'Aprobado',
  [ServiceRequestStatus.Rejected]: 'Rechazado',
  [ServiceRequestStatus.Converted]: 'Convertido en orden',
}

export const VEHICLE_TYPE_LABEL: Record<VehicleType, string> = {
  [VehicleType.Car]: 'Vehículo',
  [VehicleType.Motorcycle]: 'Moto',
}

export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
  totalPages: number
}

export interface Branch {
  id: string
  name: string
  code: string | null
  address: string | null
  city: string | null
  phone: string | null
  isActive: boolean
}

export interface User {
  id: string
  email: string
  fullName: string
  role: string
  isActive: boolean
  customerId: string | null
  branchIds: string[]
  lastLoginAt: string | null
}

export interface Customer {
  id: string
  fullName: string
  phone: string
  email: string | null
  documentId: string | null
  address: string | null
  notes: string | null
  isActive: boolean
  vehicleCount: number
}

export interface Vehicle {
  id: string
  customerId: string
  customerName: string
  type: VehicleType
  brand: string
  model: string
  year: number | null
  plate: string | null
  vin: string | null
  color: string | null
  mileage: number | null
  notes: string | null
  isActive: boolean
}

export interface ServiceRequest {
  id: string
  branchId: string
  branchName: string
  vehicleId: string
  vehicleLabel: string
  customerId: string
  customerName: string
  customerPhone: string
  description: string
  reportedSymptoms: string | null
  status: ServiceRequestStatus
  preferredDate: string | null
  mileage: number | null
  rejectionReason: string | null
  workOrderId: string | null
  workOrderNumber: string | null
  createdAt: string
}

export interface WorkOrderListItem {
  id: string
  number: string
  branchId: string
  branchName: string
  status: WorkOrderStatus
  vehicleId: string
  vehicleLabel: string
  vehicleType: VehicleType
  plate: string | null
  customerId: string
  customerName: string
  customerPhone: string
  assignedTechnicianId: string | null
  assignedTechnicianName: string | null
  description: string
  openedAt: string
  promisedAt: string | null
  taskCount: number
  tasksDone: number
}

export interface WorkOrderTask {
  id: string
  title: string
  description: string | null
  sequence: number
  isDone: boolean
  assignedTechnicianId: string | null
  assignedTechnicianName: string | null
  estimatedHours: number | null
  actualHours: number | null
  technicianNotes: string | null
  startedAt: string | null
  completedAt: string | null
}

export interface WorkOrderStatusEntry {
  fromStatus: WorkOrderStatus | null
  toStatus: WorkOrderStatus
  changedAt: string
  changedByName: string
  note: string | null
  isVisibleToCustomer: boolean
}

export interface MediaAttachment {
  id: string
  ownerType: MediaOwnerType
  ownerId: string
  /** URL prefirmada temporal: caduca, así que no se guarda ni se comparte. */
  url: string
  thumbnailUrl: string
  contentType: string
  sizeBytes: number
  caption: string | null
  uploadedByUserId: string
  uploadedByName: string
  takenAt: string
  uploadedAt: string
  isVisibleToCustomer: boolean
  taskTitle: string | null
}

export interface PresignedUpload {
  attachmentId: string
  uploadUrl: string
  key: string
  headers: Record<string, string>
  expiresAt: string
}

export interface WorkOrderDetail {
  id: string
  number: string
  branchId: string
  branchName: string
  status: WorkOrderStatus
  allowedNextStatuses: WorkOrderStatus[]
  vehicleId: string
  vehicleLabel: string
  vehicleType: VehicleType
  plate: string | null
  vehicleMileage: number | null
  customerId: string
  customerName: string
  customerPhone: string
  assignedTechnicianId: string | null
  assignedTechnicianName: string | null
  description: string
  diagnosis: string | null
  mileageIn: number | null
  openedAt: string
  promisedAt: string | null
  closedAt: string | null
  serviceRequestId: string | null
  tasks: WorkOrderTask[]
  timeline: WorkOrderStatusEntry[]
}

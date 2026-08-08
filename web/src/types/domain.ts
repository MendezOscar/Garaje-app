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

export const PaymentMethod = {
  Cash: 1,
  Card: 2,
  Transfer: 3,
  Other: 4,
} as const
export type PaymentMethod = (typeof PaymentMethod)[keyof typeof PaymentMethod]

export const PAYMENT_METHOD_LABEL: Record<PaymentMethod, string> = {
  [PaymentMethod.Cash]: 'Efectivo',
  [PaymentMethod.Card]: 'Tarjeta',
  [PaymentMethod.Transfer]: 'Transferencia',
  [PaymentMethod.Other]: 'Otro',
}

export const RevenueGrouping = {
  Day: 1,
  Week: 2,
  Month: 3,
} as const
export type RevenueGrouping = (typeof RevenueGrouping)[keyof typeof RevenueGrouping]

export const QuoteStatus = {
  Draft: 1,
  Sent: 2,
  Approved: 3,
  Rejected: 4,
  Expired: 5,
} as const
export type QuoteStatus = (typeof QuoteStatus)[keyof typeof QuoteStatus]

export const QUOTE_STATUS_LABEL: Record<QuoteStatus, string> = {
  [QuoteStatus.Draft]: 'Borrador',
  [QuoteStatus.Sent]: 'Enviada',
  [QuoteStatus.Approved]: 'Aprobada',
  [QuoteStatus.Rejected]: 'Rechazada',
  [QuoteStatus.Expired]: 'Vencida',
}

export const LineType = {
  Part: 1,
  Labor: 2,
} as const
export type LineType = (typeof LineType)[keyof typeof LineType]

export const LINE_TYPE_LABEL: Record<LineType, string> = {
  [LineType.Part]: 'Repuesto',
  [LineType.Labor]: 'Mano de obra',
}

export const StockMovementType = {
  In: 1,
  Out: 2,
  Adjustment: 3,
  TransferIn: 4,
  TransferOut: 5,
} as const
export type StockMovementType = (typeof StockMovementType)[keyof typeof StockMovementType]

export const STOCK_MOVEMENT_LABEL: Record<StockMovementType, string> = {
  [StockMovementType.In]: 'Entrada',
  [StockMovementType.Out]: 'Salida',
  [StockMovementType.Adjustment]: 'Ajuste',
  [StockMovementType.TransferIn]: 'Traslado recibido',
  [StockMovementType.TransferOut]: 'Traslado enviado',
}

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

export interface Part {
  id: string
  sku: string
  name: string
  description: string | null
  brand: string | null
  category: string | null
  unit: string
  costPrice: number
  salePrice: number
  isActive: boolean
  /** Suma de existencias en las sucursales que el usuario puede ver. */
  totalQuantity: number
}

export interface StockItem {
  partId: string
  sku: string
  partName: string
  brand: string | null
  category: string | null
  unit: string
  branchId: string
  branchName: string
  quantity: number
  minQuantity: number
  location: string | null
  salePrice: number
  isBelowMinimum: boolean
}

export interface StockMovement {
  id: string
  partId: string
  sku: string
  partName: string
  branchId: string
  branchName: string
  type: StockMovementType
  quantity: number
  /** Con signo: lo que sumó o restó al saldo. */
  signedQuantity: number
  unitCost: number | null
  resultingQuantity: number
  reference: string | null
  notes: string | null
  workOrderNumber: string | null
  counterpartBranchName: string | null
  movedAt: string
  movedByName: string
}

export interface WorkOrderPart {
  id: string
  partId: string
  sku: string
  partName: string
  unit: string
  quantity: number
  unitPrice: number
  /** Llega en cero para el Cliente: el costo del taller no es asunto suyo. */
  unitCost: number
  total: number
  workOrderTaskId: string | null
  taskTitle: string | null
}

export interface LaborService {
  id: string
  code: string
  name: string
  description: string | null
  category: string | null
  standardHours: number
  hourlyRate: number
  isFixedPrice: boolean
  fixedPrice: number
  isActive: boolean
  /** Lo que se cobra por una unidad, ya resuelto por el backend. */
  price: number
}

export interface QuoteLine {
  id: string
  lineType: LineType
  partId: string | null
  laborServiceId: string | null
  description: string
  sequence: number
  quantity: number
  unitPrice: number
  discount: number
  total: number
}

export interface QuoteListItem {
  id: string
  number: string
  status: QuoteStatus
  branchId: string
  branchName: string
  customerId: string
  customerName: string
  customerPhone: string
  vehicleLabel: string | null
  workOrderId: string | null
  workOrderNumber: string | null
  total: number
  validUntil: string | null
  sentAt: string | null
  respondedAt: string | null
  createdAt: string
  isExpired: boolean
}

export interface QuoteDetail extends Omit<QuoteListItem, 'vehicleLabel'> {
  vehicleId: string | null
  vehicleLabel: string | null
  plate: string | null
  serviceRequestId: string | null
  notes: string | null
  subtotal: number
  discountTotal: number
  taxRate: number
  taxTotal: number
  currency: string
  customerResponseNote: string | null
  isEditable: boolean
  /** Link que se comparte por WhatsApp. Null mientras sea un borrador. */
  publicUrl: string | null
  lines: QuoteLine[]
}

export interface SaveQuoteLine {
  lineType: LineType
  partId?: string | null
  laborServiceId?: string | null
  /** Si va vacío se toma el del catálogo. Se congela en la línea. */
  description?: string
  quantity: number
  unitPrice?: number
  discount?: number
}

export interface WhatsAppLink {
  url: string
  phone: string
  message: string
}

/** Lo que ve el cliente en `/q/:token`, sin login y sin ids internos. */
export interface PublicQuote {
  number: string
  status: QuoteStatus
  tenantName: string
  tenantPhone: string | null
  branchName: string
  customerName: string
  vehicleLabel: string | null
  plate: string | null
  notes: string | null
  subtotal: number
  discountTotal: number
  taxRate: number
  taxTotal: number
  total: number
  currency: string
  validUntil: string | null
  respondedAt: string | null
  isExpired: boolean
  canRespond: boolean
  lines: {
    lineType: LineType
    description: string
    quantity: number
    unitPrice: number
    discount: number
    total: number
  }[]
}

export interface SaleLine {
  id: string
  lineType: LineType
  partId: string | null
  laborServiceId: string | null
  description: string
  sequence: number
  quantity: number
  unitPrice: number
  /** Llega en cero para el Cliente: el costo del taller no es asunto suyo. */
  unitCost: number
  discount: number
  total: number
}

export interface SaleListItem {
  id: string
  number: string
  branchId: string
  branchName: string
  customerId: string | null
  customerName: string | null
  workOrderId: string | null
  workOrderNumber: string | null
  saleDate: string
  paymentMethod: PaymentMethod
  total: number
  isVoided: boolean
}

export interface SaleDetail extends SaleListItem {
  customerPhone: string | null
  vehicleLabel: string | null
  subtotal: number
  discountTotal: number
  taxRate: number
  taxTotal: number
  costTotal: number
  margin: number
  currency: string
  notes: string | null
  voidReason: string | null
  lines: SaleLine[]
}

export interface RevenuePoint {
  periodStart: string
  /** Ya formateado por el backend: "08/08", "sem. 32", "ago 2026". */
  periodLabel: string
  partsRevenue: number
  laborRevenue: number
  total: number
  cost: number
  margin: number
  saleCount: number
}

export interface RevenueReport {
  from: string
  to: string
  groupBy: RevenueGrouping
  currency: string
  partsRevenue: number
  laborRevenue: number
  total: number
  cost: number
  margin: number
  marginPercent: number
  saleCount: number
  points: RevenuePoint[]
  branches: {
    branchId: string
    branchName: string
    partsRevenue: number
    laborRevenue: number
    total: number
    saleCount: number
  }[]
  topParts: {
    partId: string
    sku: string
    name: string
    quantity: number
    revenue: number
    margin: number
  }[]
}

export interface Dashboard {
  currency: string
  revenueToday: number
  revenueWeek: number
  revenueMonth: number
  marginMonth: number
  openWorkOrders: number
  pendingRequests: number
  lateWorkOrders: number
  quotesAwaitingResponse: number
  partsBelowMinimum: number
  workOrdersByStatus: { status: WorkOrderStatus; count: number }[]
  lastDays: RevenuePoint[]
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
  parts: WorkOrderPart[]
  partsTotal: number
}

export const NotificationType = {
  ServiceRequestCreated: 1,
  WorkOrderAssigned: 2,
  WorkOrderStatusChanged: 3,
  QuoteSent: 4,
  QuoteAnswered: 5,
} as const
export type NotificationType = (typeof NotificationType)[keyof typeof NotificationType]

export const NOTIFICATION_ICON: Record<NotificationType, string> = {
  [NotificationType.ServiceRequestCreated]: '📥',
  [NotificationType.WorkOrderAssigned]: '🔧',
  [NotificationType.WorkOrderStatusChanged]: '🚗',
  [NotificationType.QuoteSent]: '📄',
  [NotificationType.QuoteAnswered]: '✅',
}

export interface Notification {
  id: string
  type: NotificationType
  title: string
  body: string
  workOrderId: string | null
  quoteId: string | null
  serviceRequestId: string | null
  isRead: boolean
  createdAt: string
}

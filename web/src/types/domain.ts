// Espejo de los DTOs de la Fase 1. Los enums numéricos coinciden con los de
// Garaj.Domain.Enums: si cambian allá, hay que cambiarlos aquí.

import type { SubscriptionState } from './api'

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

/** Cómo se cobra la mano de obra de una orden. Espejo de `Garaj.Domain.Enums.LaborMode`. */
export const LaborMode = {
  Catalog: 1,
  Manual: 2,
} as const
export type LaborMode = (typeof LaborMode)[keyof typeof LaborMode]

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
  Quote: 4,
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

/** Un abono, como lo ve el cliente en su estado de cuenta: sin quién lo recibió. */
export interface StatementPayment {
  paidAt: string
  method: PaymentMethod
  reference: string | null
  amount: number
}

export interface StatementSale {
  number: string
  workOrderNumber: string | null
  branchName: string
  saleDate: string
  dueDate: string | null
  isOverdue: boolean
  total: number
  amountPaid: number
  balance: number
  payments: StatementPayment[]
}

/** Lo que un cliente debe hoy, factura por factura. Solo las que tienen saldo. */
export interface CustomerStatement {
  customerId: string
  customerName: string
  tenantName: string
  tenantLogoUrl: string | null
  tenantPhone: string | null
  billingName: string | null
  taxId: string | null
  phone: string
  currency: string
  /** El corte: un estado de cuenta es de un momento, no de siempre. */
  asOf: string
  total: number
  overdue: number
  sales: StatementSale[]
}

export interface Customer {
  id: string
  fullName: string
  phone: string
  email: string | null
  documentId: string | null
  /** RTN, para la factura con CAI. Distinto de la identidad. */
  taxId: string | null
  /** A nombre de quién sale su factura, si no es a su propio nombre. */
  billingName: string | null
  address: string | null
  notes: string | null
  isActive: boolean
  vehicleCount: number
  /** Si el cliente puede entrar a la app, y con qué correo. */
  hasAppAccess: boolean
  appUserEmail: string | null
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
  /** El servicio del catálogo que le pone precio al paso. Sin él, el paso no se cobra. */
  laborServiceId: string | null
  laborServiceName: string | null
  laborPrice: number | null
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
  costPrice: number
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
  /** Null en un repuesto cargado a mano: ese no está en el catálogo. */
  partId: string | null
  /** Vacío en uno cargado a mano, que no tiene código. */
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

/**
 * Un trabajo que el taller repite, guardado con sus pasos y sus repuestos.
 *
 * Los totales vienen **a precios de hoy**: la plantilla guarda referencias al catálogo, no
 * importes, así que subir mañana el precio de un repuesto no la deja mintiendo.
 */
export interface JobTemplate {
  id: string
  name: string
  description: string | null
  isActive: boolean
  usageCount: number
  lastUsedAt: string | null
  tasks: JobTemplateTask[]
  parts: JobTemplatePart[]
  laborTotal: number
  partsTotal: number
  total: number
}

export interface JobTemplateTask {
  id: string
  title: string
  description: string | null
  sequence: number
  laborServiceId: string | null
  laborServiceName: string | null
  estimatedHours: number | null
  price: number | null
}

export interface JobTemplatePart {
  id: string
  partId: string | null
  sku: string
  partName: string
  unit: string
  quantity: number
  unitPrice: number
  total: number
}

/**
 * Lo que dejó aplicar la plantilla: los pasos ya creados y los repuestos **propuestos**.
 * No se cargan solos porque cargarlos descuenta bodega, y al aplicarla el trabajo todavía no
 * se ha hecho.
 */
export interface ApplyJobTemplateResult {
  templateName: string
  tasks: WorkOrderTask[]
  suggestedParts: SuggestedPart[]
}

export interface SuggestedPart {
  partId: string | null
  sku: string
  partName: string
  unit: string
  quantity: number
  unitPrice: number
  /** Existencia en la bodega de la sucursal de la orden. */
  available: number
  description: string | null
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
  /** Ruta del logo bajo el token de la cotización, relativa a la base de la API. */
  tenantLogoUrl: string | null
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
  /** Las fotos del daño que el taller adjuntó al presupuesto. */
  photos: {
    url: string
    thumbnailUrl: string
    caption: string | null
  }[]
}

/**
 * Un vehículo al que le toca servicio, según lo que el taller recomendó al entregarlo.
 *
 * Lo que dispara el recordatorio es la fecha. El kilometraje se muestra como contexto: hasta
 * que el vehículo no vuelve, el taller no sabe cuánto ha rodado.
 */
export interface ServiceReminder {
  workOrderId: string
  orderNumber: string
  customerId: string
  customerName: string
  customerPhone: string
  vehicleId: string
  vehicleLabel: string
  plate: string | null
  branchName: string
  /** Qué se le hizo la última vez. Da de qué hablar al llamarlo. */
  lastService: string
  closedAt: string | null
  nextServiceAt: string
  /** Días hasta que toque. Negativo si ya pasó. */
  daysUntil: number
  nextServiceMileage: number | null
  lastMileage: number | null
  remindedAt: string | null
}

/** Cuál de los tres mensajes se le arma al cliente. Espejo de `OrderMessageKind`. */
export type OrderMessageKind = 'received' | 'ready' | 'invoice'

/**
 * Lo que ve el cliente en `/o/:token`: en qué va su vehículo. Sin ids, sin costo del taller y
 * solo con las fotos que el taller marcó como visibles para él.
 */
export interface OrderTracking {
  number: string
  tenantName: string
  /** Ruta del logo bajo el token de la orden, relativa a la base de la API. */
  tenantLogoUrl: string | null
  tenantPhone: string | null
  branchName: string
  customerName: string
  vehicleLabel: string
  plate: string | null
  status: WorkOrderStatus
  statusLabel: string
  description: string
  openedAt: string
  promisedAt: string | null
  closedAt: string | null
  currency: string
  steps: { title: string; isDone: boolean; completedAt: string | null }[]
  timeline: {
    status: WorkOrderStatus
    statusLabel: string
    changedAt: string
    note: string | null
  }[]
  photos: {
    url: string
    thumbnailUrl: string
    caption: string | null
    takenAt: string
    stepTitle: string | null
  }[]
  /** La factura, cuando ya se cerró. Null mientras el vehículo está en el taller. */
  invoice: {
    number: string
    total: number
    paid: number
    balance: number
    dueDate: string | null
  } | null
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
  amountPaid: number
  /** Lo que falta por cobrar. Cero en una venta de contado. */
  balance: number
  dueDate: string | null
  isOverdue: boolean
  isVoided: boolean
}

export interface SalePayment {
  id: string
  amount: number
  method: PaymentMethod
  paidAt: string
  reference: string | null
  notes: string | null
  registeredByName: string | null
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
  /** Régimen de facturación: null en la venta sin CAI, que es comprobante de entrega. */
  fiscalNumber: string | null
  fiscalCai: string | null
  fiscalRangeText: string | null
  fiscalIssueDeadline: string | null
  customerTaxId: string | null
  lines: SaleLine[]
  payments: SalePayment[]
}

/** Rango de facturación autorizado por el SAR para una sucursal. */
export interface FiscalRange {
  id: string
  branchId: string
  branchName: string
  cai: string
  establishmentCode: string
  pointOfSaleCode: string
  documentType: string
  rangeStart: number
  rangeEnd: number
  nextNumber: number
  remaining: number
  rangeText: string
  nextFiscalNumber: string
  issueDeadline: string
  isActive: boolean
  isExpired: boolean
  isExhausted: boolean
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
  /** Repuestos vendidos en mostrador, sin pasar por una orden de trabajo. */
  counterPartsRevenue: number
  counterSaleCount: number
  points: RevenuePoint[]
  branches: {
    branchId: string
    branchName: string
    partsRevenue: number
    laborRevenue: number
    total: number
    saleCount: number
  }[]
  /**
   * Reparto por técnico responsable de la orden. `technicianId` es null en lo vendido en
   * mostrador, que no pasó por nadie.
   */
  technicians: {
    technicianId: string | null
    technicianName: string
    partsRevenue: number
    laborRevenue: number
    total: number
    cost: number
    margin: number
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

/**
 * Lo **cobrado** en un día, que no es lo facturado: una venta a crédito suma en los ingresos
 * el día que se emite y aquí el día que el cliente paga.
 */
export interface CashClose {
  day: string
  dayLabel: string
  branchId: string | null
  branchName: string | null
  currency: string
  total: number
  paymentCount: number
  byMethod: { method: PaymentMethod; total: number; count: number }[]
  byReceiver: { receiverName: string; total: number; count: number }[]
  payments: {
    paidAt: string
    saleNumber: string
    customerName: string | null
    branchName: string
    method: PaymentMethod
    reference: string | null
    receiverName: string
    amount: number
  }[]
  /** Abonos que quedaron fuera porque su venta está anulada. Se informan, no se esconden. */
  voidedCount: number
  voidedAmount: number
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
  /** Facturado y no cobrado, y cuánto de eso ya venció. */
  receivables: number
  overdueReceivables: number
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
  /** La mano de obra que se cobraría hoy: la suma de los pasos, o el total escrito a mano. */
  laborTotal: number
  laborMode: LaborMode
  /** El total escrito a mano. Solo cuenta en modo manual. */
  manualLaborTotal: number | null
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

/**
 * La ficha del taller. Estos datos se imprimen en la cotización y en la factura que recibe
 * el cliente final, así que el Dueño los corrige desde la pantalla «Taller».
 */
export interface TenantSettings {
  id: string
  name: string
  legalName: string | null
  taxId: string | null
  phone: string | null
  email: string | null
  /** Dirección de la casa matriz. La factura fiscal la pide aparte de la de la sucursal. */
  address: string | null
  currency: string
  defaultTaxRate: number
  defaultPhoneCountryCode: string
  /** Ruta relativa a la base de la API, o null. Se abre con `apiUrl()`. */
  logoUrl: string | null
}

/**
 * Un taller visto desde nuestro lado, el de GarajApp: no lo que hace, sino cómo va con la
 * mensualidad. Solo lo recibe el perfil Plataforma.
 */
export interface PlatformTenant {
  id: string
  name: string
  legalName: string | null
  phone: string | null
  email: string | null
  planName: string | null
  monthlyFee: number
  currency: string
  paidThrough: string | null
  graceDays: number
  unblockedThrough: string | null
  unblockNote: string | null
  isActive: boolean
  state: SubscriptionState
  daysLeft: number | null
  readOnlyOn: string | null
  lastPaymentOn: string | null
  branchCount: number
  createdAt: string
}

export interface PlatformTenantDetail {
  tenant: PlatformTenant
  payments: SubscriptionPayment[]
}

export interface SubscriptionPayment {
  id: string
  paidOn: string
  amount: number
  currency: string
  method: string | null
  reference: string | null
  /** A qué fecha quedó corrida la suscripción con este pago. */
  coversThrough: string
  note: string | null
  createdAt: string
}

/**
 * Lo que devuelve el alta de un taller. La contraseña viene **una sola vez**: no se guarda en
 * claro en ninguna parte y no hay forma de volver a consultarla.
 */
export interface CreatedTenant {
  tenantId: string
  branchId: string
  ownerEmail: string
  password: string
}

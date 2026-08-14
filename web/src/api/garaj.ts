import axios from 'axios'
import { api, download } from './client'
import type {
  Branch,
  Customer,
  FiscalRange,
  LaborService,
  MediaAttachment,
  MediaOwnerType,
  Notification,
  Dashboard,
  LaborMode,
  Paged,
  Part,
  PaymentMethod,
  PresignedUpload,
  PublicQuote,
  QuoteDetail,
  QuoteListItem,
  QuoteStatus,
  RevenueGrouping,
  RevenueReport,
  SaleDetail,
  SaleListItem,
  SaveQuoteLine,
  ServiceRequest,
  StockItem,
  StockMovement,
  StockMovementType,
  TenantSettings,
  User,
  Vehicle,
  VehicleType,
  WorkOrderDetail,
  WorkOrderListItem,
  WorkOrderPart,
  WorkOrderStatus,
  WorkOrderTask,
  WhatsAppLink,
} from '@/types/domain'

/** Quita claves vacías para no mandar `?status=` y que el backend lo lea como filtro. */
function params(source: Record<string, unknown>): Record<string, unknown> {
  return Object.fromEntries(
    Object.entries(source).filter(([, v]) => v !== undefined && v !== null && v !== ''),
  )
}

export const branchesApi = {
  async list(includeInactive = false): Promise<Branch[]> {
    const { data } = await api.get<Branch[]>('/api/branches', { params: { includeInactive } })
    return data
  },
}

export const usersApi = {
  async list(role?: string): Promise<User[]> {
    const { data } = await api.get<User[]>('/api/users', { params: params({ role }) })
    return data
  },
  async create(body: {
    email: string
    fullName: string
    role: string
    password: string
    branchIds?: string[]
    customerId?: string
  }) {
    const { data } = await api.post<User>('/api/users', body)
    return data
  },
  async update(id: string, body: { fullName: string; isActive: boolean; branchIds?: string[] }) {
    const { data } = await api.put<User>(`/api/users/${id}`, body)
    return data
  },
  /** El Dueño no conoce la contraseña actual: la reemplaza y cierra las sesiones abiertas. */
  async resetPassword(id: string, newPassword: string) {
    await api.post(`/api/users/${id}/password`, { newPassword })
  },
}

export const customersApi = {
  async list(query: { search?: string; page?: number; pageSize?: number } = {}) {
    const { data } = await api.get<Paged<Customer>>('/api/customers', { params: params(query) })
    return data
  },
  async get(id: string) {
    const { data } = await api.get<Customer>(`/api/customers/${id}`)
    return data
  },
  /** Lo registra cualquiera del taller: el que recibe el vehículo en el mostrador. */
  async create(body: {
    fullName: string
    phone: string
    email?: string
    documentId?: string
    /** RTN, solo en los clientes que piden factura con CAI. */
    taxId?: string
    /** A nombre de quién sale su factura, si no es a su propio nombre. */
    billingName?: string
    address?: string
    notes?: string
  }) {
    const { data } = await api.post<Customer>('/api/customers', body)
    return data
  },
  async update(id: string, body: {
    fullName: string
    phone: string
    email?: string | null
    documentId?: string | null
    taxId?: string | null
    billingName?: string | null
    address?: string | null
    notes?: string | null
    isActive?: boolean
  }) {
    const { data } = await api.put<Customer>(`/api/customers/${id}`, body)
    return data
  },
  /** Le abre acceso a la app. Opcional: el cliente que no lo pide no lo necesita. */
  async grantAppAccess(id: string, body: { email: string; password: string }) {
    const { data } = await api.post<Customer>(`/api/customers/${id}/app-access`, body)
    return data
  },
}

export const vehiclesApi = {
  async list(query: { search?: string; customerId?: string; page?: number; pageSize?: number } = {}) {
    const { data } = await api.get<Paged<Vehicle>>('/api/vehicles', { params: params(query) })
    return data
  },
  async create(body: {
    customerId: string
    type: VehicleType
    brand: string
    model: string
    year?: number
    plate?: string
    color?: string
    mileage?: number
  }) {
    const { data } = await api.post<Vehicle>('/api/vehicles', body)
    return data
  },
}

export const serviceRequestsApi = {
  async list(
    query: {
      status?: number
      branchId?: string
      /** Desde cuándo, en ISO. Filtra por fecha de ingreso del requerimiento. */
      from?: string
      to?: string
      page?: number
      pageSize?: number
    } = {},
  ) {
    const { data } = await api.get<Paged<ServiceRequest>>('/api/service-requests', {
      params: params(query),
    })
    return data
  },
  async create(body: {
    branchId: string
    vehicleId: string
    description: string
    reportedSymptoms?: string
    preferredDate?: string
    mileage?: number
  }) {
    const { data } = await api.post<ServiceRequest>('/api/service-requests', body)
    return data
  },
  /** Devuelve el id de la orden creada. */
  async approve(id: string, body: { assignedTechnicianId?: string; promisedAt?: string }) {
    const { data } = await api.post<{ workOrderId: string }>(
      `/api/service-requests/${id}/approve`,
      body,
    )
    return data.workOrderId
  },
  async reject(id: string, reason: string) {
    const { data } = await api.post<ServiceRequest>(`/api/service-requests/${id}/reject`, { reason })
    return data
  },
}

export const workOrdersApi = {
  async list(query: {
    status?: WorkOrderStatus
    branchId?: string
    technicianId?: string
    vehicleId?: string
    search?: string
    onlyOpen?: boolean
    page?: number
    pageSize?: number
  } = {}) {
    const { data } = await api.get<Paged<WorkOrderListItem>>('/api/work-orders', {
      params: params(query),
    })
    return data
  },
  async get(id: string) {
    const { data } = await api.get<WorkOrderDetail>(`/api/work-orders/${id}`)
    return data
  },
  async update(id: string, body: { description: string; diagnosis?: string; promisedAt?: string }) {
    const { data } = await api.put<WorkOrderDetail>(`/api/work-orders/${id}`, body)
    return data
  },
  async assign(id: string, technicianId: string | null) {
    const { data } = await api.put<WorkOrderDetail>(`/api/work-orders/${id}/assign`, { technicianId })
    return data
  },
  /** Elige si la mano de obra sale del catálogo o de un total escrito a mano. */
  async setLaborMode(id: string, body: { mode: LaborMode; total?: number | null }) {
    const { data } = await api.put<WorkOrderDetail>(`/api/work-orders/${id}/labor`, body)
    return data
  },
  async changeStatus(
    id: string,
    body: { status: WorkOrderStatus; note?: string; isVisibleToCustomer?: boolean },
  ) {
    const { data } = await api.post<WorkOrderDetail>(`/api/work-orders/${id}/status`, body)
    return data
  },
  async addTask(
    id: string,
    body: {
      title: string
      description?: string
      /** El servicio del catálogo que le pone precio al paso. */
      laborServiceId?: string | null
      estimatedHours?: number
    },
  ) {
    const { data } = await api.post<WorkOrderTask>(`/api/work-orders/${id}/tasks`, body)
    return data
  },
  /** Reemplaza el paso completo: lo que se omite se borra. */
  async updateTask(
    id: string,
    taskId: string,
    body: {
      title: string
      description?: string | null
      laborServiceId?: string | null
      estimatedHours?: number | null
    },
  ) {
    const { data } = await api.put<WorkOrderTask>(`/api/work-orders/${id}/tasks/${taskId}`, body)
    return data
  },
  async completeTask(
    id: string,
    taskId: string,
    body: { isDone: boolean; actualHours?: number; technicianNotes?: string },
  ) {
    const { data } = await api.post<WorkOrderTask>(
      `/api/work-orders/${id}/tasks/${taskId}/complete`,
      body,
    )
    return data
  },
  async deleteTask(id: string, taskId: string) {
    await api.delete(`/api/work-orders/${id}/tasks/${taskId}`)
  },
  /**
   * Con `partId` sale del catálogo y descuenta de la bodega. Sin él se carga a mano —hacen
   * falta `description` y `unitPrice`— y no toca el inventario.
   */
  async addPart(
    id: string,
    body: {
      partId?: string
      description?: string
      quantity: number
      unitPrice?: number
      unitCost?: number
      workOrderTaskId?: string
    },
  ) {
    const { data } = await api.post<WorkOrderPart>(`/api/work-orders/${id}/parts`, body)
    return data
  },
  async removePart(id: string, partLineId: string) {
    await api.delete(`/api/work-orders/${id}/parts/${partLineId}`)
  },
}

export const partsApi = {
  async list(query: {
    search?: string
    category?: string
    includeInactive?: boolean
    page?: number
    pageSize?: number
  } = {}) {
    const { data } = await api.get<Paged<Part>>('/api/parts', { params: params(query) })
    return data
  },
  async categories(): Promise<string[]> {
    const { data } = await api.get<string[]>('/api/parts/categories')
    return data
  },
  async create(body: Omit<Part, 'id' | 'totalQuantity'>) {
    const { data } = await api.post<Part>('/api/parts', body)
    return data
  },
  async update(id: string, body: Omit<Part, 'id' | 'totalQuantity'>) {
    const { data } = await api.put<Part>(`/api/parts/${id}`, body)
    return data
  },
}

export const stockApi = {
  async list(query: {
    branchId?: string
    partId?: string
    search?: string
    category?: string
    onlyBelowMinimum?: boolean
    page?: number
    pageSize?: number
  } = {}) {
    const { data } = await api.get<Paged<StockItem>>('/api/stock', { params: params(query) })
    return data
  },
  async alerts(branchId?: string): Promise<StockItem[]> {
    const { data } = await api.get<StockItem[]>('/api/stock/alerts', { params: params({ branchId }) })
    return data
  },
  async movements(query: {
    branchId?: string
    partId?: string
    type?: StockMovementType
    page?: number
    pageSize?: number
  } = {}) {
    const { data } = await api.get<Paged<StockMovement>>('/api/stock/movements', {
      params: params(query),
    })
    return data
  },
  async receive(body: {
    branchId: string
    partId: string
    quantity: number
    unitCost?: number
    reference?: string
    notes?: string
  }) {
    const { data } = await api.post<StockItem>('/api/stock/receive', body)
    return data
  },
  /** Se envía lo contado físicamente, no la diferencia. */
  async adjust(body: {
    branchId: string
    partId: string
    countedQuantity: number
    reason: string
  }) {
    const { data } = await api.post<StockItem>('/api/stock/adjust', body)
    return data
  },
  async transfer(body: {
    fromBranchId: string
    toBranchId: string
    partId: string
    quantity: number
    notes?: string
  }) {
    const { data } = await api.post<StockItem[]>('/api/stock/transfer', body)
    return data
  },
  async saveSettings(body: {
    branchId: string
    partId: string
    minQuantity: number
    location?: string
  }) {
    const { data } = await api.put<StockItem>('/api/stock/settings', body)
    return data
  },
}

export const mediaApi = {
  /** Galería completa de una orden: sus fotos y las de todos sus pasos. */
  async listForWorkOrder(workOrderId: string): Promise<MediaAttachment[]> {
    const { data } = await api.get<MediaAttachment[]>(`/api/media/work-order/${workOrderId}`)
    return data
  },

  async list(ownerType: MediaOwnerType, ownerId: string): Promise<MediaAttachment[]> {
    const { data } = await api.get<MediaAttachment[]>('/api/media', {
      params: { ownerType, ownerId },
    })
    return data
  },

  /**
   * Sube el archivo en tres pasos: pedir la URL, hacer el PUT directo al bucket y confirmar.
   * El binario nunca pasa por la API, así que una foto grande no ocupa un hilo del servidor.
   */
  async upload(
    file: File,
    owner: { ownerType: MediaOwnerType; ownerId: string },
    options: { caption?: string; isVisibleToCustomer?: boolean } = {},
  ): Promise<MediaAttachment> {
    const { data: presigned } = await api.post<PresignedUpload>('/api/media/upload-url', {
      ...owner,
      contentType: file.type,
      sizeBytes: file.size,
      fileName: file.name,
      caption: options.caption,
      takenAt: new Date(file.lastModified).toISOString(),
      isVisibleToCustomer: options.isVisibleToCustomer ?? true,
    })

    // axios "pelado": el interceptor de `api` añadiría el Authorization del taller a una
    // petición al bucket, y S3 rechaza la firma si llega esa cabecera de más.
    await axios.put(presigned.uploadUrl, file, { headers: presigned.headers })

    const { data } = await api.post<MediaAttachment>(`/api/media/${presigned.attachmentId}/confirm`)
    return data
  },

  async remove(id: string) {
    await api.delete(`/api/media/${id}`)
  },
}

export const laborServicesApi = {
  async list(includeInactive = false): Promise<LaborService[]> {
    const { data } = await api.get<LaborService[]>('/api/labor-services', {
      params: { includeInactive },
    })
    return data
  },
  async create(body: Omit<LaborService, 'id' | 'price'>) {
    const { data } = await api.post<LaborService>('/api/labor-services', body)
    return data
  },
  async update(id: string, body: Omit<LaborService, 'id' | 'price'>) {
    const { data } = await api.put<LaborService>(`/api/labor-services/${id}`, body)
    return data
  },
}

export const quotesApi = {
  async list(query: {
    status?: QuoteStatus
    customerId?: string
    workOrderId?: string
    /** Desde cuándo, en ISO. Filtra por fecha de creación de la cotización. */
    from?: string
    to?: string
    page?: number
    pageSize?: number
  } = {}) {
    const { data } = await api.get<Paged<QuoteListItem>>('/api/quotes', { params: params(query) })
    return data
  },
  async get(id: string) {
    const { data } = await api.get<QuoteDetail>(`/api/quotes/${id}`)
    return data
  },
  /** Arma la cotización con lo que la orden ya tiene: repuestos consumidos y pasos. */
  async createFromWorkOrder(body: {
    workOrderId: string
    validUntil?: string
    notes?: string
    includeParts?: boolean
    includeTasks?: boolean
  }) {
    const { data } = await api.post<QuoteDetail>('/api/quotes/from-work-order', body)
    return data
  },
  async create(body: { customerId: string; vehicleId?: string; branchId?: string; notes?: string }) {
    const { data } = await api.post<QuoteDetail>('/api/quotes', body)
    return data
  },
  async update(id: string, body: { validUntil?: string; notes?: string; taxRate?: number }) {
    const { data } = await api.put<QuoteDetail>(`/api/quotes/${id}`, body)
    return data
  },
  async addLine(id: string, body: SaveQuoteLine) {
    const { data } = await api.post<QuoteDetail>(`/api/quotes/${id}/lines`, body)
    return data
  },
  async updateLine(id: string, lineId: string, body: SaveQuoteLine) {
    const { data } = await api.put<QuoteDetail>(`/api/quotes/${id}/lines/${lineId}`, body)
    return data
  },
  async removeLine(id: string, lineId: string) {
    const { data } = await api.delete<QuoteDetail>(`/api/quotes/${id}/lines/${lineId}`)
    return data
  },
  /** La marca como enviada y devuelve el link de WhatsApp con el mensaje ya armado. */
  async send(id: string) {
    const { data } = await api.post<WhatsAppLink>(`/api/quotes/${id}/send`)
    return data
  },
  async whatsappLink(id: string) {
    const { data } = await api.get<WhatsAppLink>(`/api/quotes/${id}/whatsapp-link`)
    return data
  },
  /** Baja el PDF con la sesión puesta. Un enlace directo respondería 401. */
  async downloadPdf(id: string, number: string) {
    await download(`/api/quotes/${id}/pdf`, `${number}.pdf`)
  },
  async respond(id: string, approve: boolean, note?: string) {
    const { data } = await api.post<QuoteDetail>(`/api/quotes/${id}/respond`, { approve, note })
    return data
  },
}

/**
 * La cotización vista desde el link de WhatsApp. Usa axios pelado a propósito: la página es
 * anónima y el interceptor de `api` mandaría el token de quien tuviera sesión abierta en ese
 * navegador, que no tiene nada que ver con quien abre el enlace.
 */
export const publicQuotesApi = {
  async get(token: string): Promise<PublicQuote> {
    const { data } = await axios.get<PublicQuote>(
      `${api.defaults.baseURL}/public/quotes/${token}`,
    )
    return data
  },
  async respond(token: string, approve: boolean, note?: string): Promise<PublicQuote> {
    const { data } = await axios.post<PublicQuote>(
      `${api.defaults.baseURL}/public/quotes/${token}/respond`,
      { approve, note },
    )
    return data
  },
  pdfUrl(token: string) {
    return `${api.defaults.baseURL}/public/quotes/${token}/pdf`
  },
}

export const salesApi = {
  async list(query: {
    branchId?: string
    customerId?: string
    workOrderId?: string
    from?: string
    to?: string
    includeVoided?: boolean
    onlyUnpaid?: boolean
    page?: number
    pageSize?: number
  } = {}) {
    const { data } = await api.get<Paged<SaleListItem>>('/api/sales', { params: params(query) })
    return data
  },
  async get(id: string) {
    const { data } = await api.get<SaleDetail>(`/api/sales/${id}`)
    return data
  },
  /** Venta directa de mostrador. Descuenta los repuestos de la bodega. */
  async create(body: {
    branchId: string
    customerId?: string
    paymentMethod: PaymentMethod
    notes?: string
    lines: {
      lineType: number
      partId?: string | null
      laborServiceId?: string | null
      description?: string
      quantity: number
      unitPrice?: number
      discount?: number
    }[]
  }) {
    const { data } = await api.post<SaleDetail>('/api/sales', body)
    return data
  },
  /** Factura lo trabajado en la orden y, si se pide, la marca como entregada. */
  async closeWorkOrder(body: {
    workOrderId: string
    paymentMethod: PaymentMethod
    notes?: string
    includeLabor?: boolean
    /** Cobra la mano de obra de esta cotización en vez de la de los pasos. */
    laborFromQuoteId?: string | null
    markAsDelivered?: boolean
    /** Solo a crédito: fecha acordada de pago. */
    dueDate?: string
    /** Lo que el cliente deja al recoger. Omitido = paga todo. */
    initialPayment?: number
    /** Consume un número del rango autorizado por el SAR. */
    fiscal?: boolean
    /** RTN para esta factura. Vacío = el de la ficha del cliente. */
    customerTaxId?: string
    /** A nombre de quién sale. Vacío = el de la ficha, o el nombre del cliente. */
    customerName?: string
  }) {
    const { data } = await api.post<SaleDetail>('/api/sales/close-work-order', body)
    return data
  },
  /** Un abono a una venta con saldo. Nunca por encima de lo que falta. */
  async registerPayment(id: string, body: {
    amount: number
    method: PaymentMethod
    paidAt?: string
    reference?: string
    notes?: string
  }) {
    const { data } = await api.post<SaleDetail>(`/api/sales/${id}/payments`, body)
    return data
  },
  /** Borra un abono mal capturado. Para devolver dinero se anula la venta. */
  async removePayment(id: string, paymentId: string) {
    const { data } = await api.delete<SaleDetail>(`/api/sales/${id}/payments/${paymentId}`)
    return data
  },
  async void(id: string, reason: string) {
    const { data } = await api.post<SaleDetail>(`/api/sales/${id}/void`, { reason })
    return data
  },
  /** La factura en PDF, para imprimirla o mandarla por WhatsApp. */
  async downloadPdf(id: string, number: string) {
    await download(`/api/sales/${id}/pdf`, `${number}.pdf`)
  },
}

export const reportsApi = {
  async revenue(query: {
    from?: string
    to?: string
    groupBy?: RevenueGrouping
    branchId?: string
    technicianId?: string
  } = {}) {
    const { data } = await api.get<RevenueReport>('/api/reports/revenue', { params: params(query) })
    return data
  },
  async dashboard(branchId?: string) {
    const { data } = await api.get<Dashboard>('/api/reports/dashboard', {
      params: params({ branchId }),
    })
    return data
  },
  async downloadCsv(query: {
    from?: string
    to?: string
    groupBy?: RevenueGrouping
    branchId?: string
    technicianId?: string
  }) {
    await download('/api/reports/revenue.csv', 'ingresos.csv', params(query))
  },
}

export const notificationsApi = {
  async list(query: { onlyUnread?: boolean; page?: number; pageSize?: number } = {}) {
    const { data } = await api.get<Paged<Notification>>('/api/notifications', {
      params: params(query),
    })
    return data
  },
  async unreadCount(): Promise<number> {
    const { data } = await api.get<{ unread: number }>('/api/notifications/unread-count')
    return data.unread
  },
  async markRead(id: string) {
    await api.post(`/api/notifications/${id}/read`)
  },
  async markAllRead() {
    await api.post('/api/notifications/read-all')
  },
}

export const tenantApi = {
  async get(): Promise<TenantSettings> {
    const { data } = await api.get<TenantSettings>('/api/tenant')
    return data
  },
  async update(body: {
    name: string
    legalName: string | null
    taxId: string | null
    phone: string | null
    email: string | null
    address: string | null
    defaultTaxRate: number
    defaultPhoneCountryCode: string | null
  }): Promise<TenantSettings> {
    const { data } = await api.put<TenantSettings>('/api/tenant', body)
    return data
  },
  /**
   * El logo sí pasa por la API, al revés que las fotos: es un archivo pequeño que se sube
   * una vez, y el servidor lo valida y lo normaliza a PNG de 512 px.
   */
  async setLogo(file: File): Promise<TenantSettings> {
    const form = new FormData()
    form.append('file', file)

    // Sin Content-Type explícito: axios le pone el boundary que necesita el multipart.
    const { data } = await api.post<TenantSettings>('/api/tenant/logo', form, {
      headers: { 'Content-Type': undefined },
    })
    return data
  },
  async removeLogo(): Promise<TenantSettings> {
    const { data } = await api.delete<TenantSettings>('/api/tenant/logo')
    return data
  },

  async fiscalRanges(): Promise<FiscalRange[]> {
    const { data } = await api.get<FiscalRange[]>('/api/tenant/fiscal-ranges')
    return data
  },
  /** Registrar uno nuevo desactiva el que la sucursal tuviera: el SAR autoriza uno a la vez. */
  async saveFiscalRange(body: {
    branchId: string
    cai: string
    establishmentCode: string
    pointOfSaleCode: string
    documentType: string
    rangeStart: number
    rangeEnd: number
    issueDeadline: string
  }): Promise<FiscalRange> {
    const { data } = await api.post<FiscalRange>('/api/tenant/fiscal-ranges', body)
    return data
  },
  async deactivateFiscalRange(id: string) {
    await api.delete(`/api/tenant/fiscal-ranges/${id}`)
  },
}

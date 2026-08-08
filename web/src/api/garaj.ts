import { api } from './client'
import type {
  Branch,
  Customer,
  Paged,
  ServiceRequest,
  User,
  Vehicle,
  WorkOrderDetail,
  WorkOrderListItem,
  WorkOrderStatus,
  WorkOrderTask,
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
}

export const vehiclesApi = {
  async list(query: { search?: string; customerId?: string; page?: number; pageSize?: number } = {}) {
    const { data } = await api.get<Paged<Vehicle>>('/api/vehicles', { params: params(query) })
    return data
  },
}

export const serviceRequestsApi = {
  async list(query: { status?: number; branchId?: string; page?: number; pageSize?: number } = {}) {
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
  async changeStatus(
    id: string,
    body: { status: WorkOrderStatus; note?: string; isVisibleToCustomer?: boolean },
  ) {
    const { data } = await api.post<WorkOrderDetail>(`/api/work-orders/${id}/status`, body)
    return data
  },
  async addTask(id: string, body: { title: string; description?: string; estimatedHours?: number }) {
    const { data } = await api.post<WorkOrderTask>(`/api/work-orders/${id}/tasks`, body)
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
}

import type {
  CreateTicketRequest,
  DashboardFilters,
  DashboardMetrics,
  FlightRecord,
  Role,
  StaffProfile,
  Ticket,
  TicketNote,
  TicketSort,
  TicketStatus,
} from '../types'

const API_BASE = import.meta.env.VITE_API_URL ?? ''

export class ApiError extends Error {
  status: number
  details?: { validNextStatuses?: string[] }

  constructor(message: string, status: number, details?: { validNextStatuses?: string[] }) {
    super(message)
    this.status = status
    this.details = details
  }
}

async function request<T>(path: string, options: RequestInit = {}, role?: Role): Promise<T> {
  const headers = new Headers(options.headers)
  if (!(options.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }
  if (role) headers.set('X-Demo-Role', role)

  const response = await fetch(`${API_BASE}${path}`, { ...options, headers })
  if (!response.ok) {
    let body: { message?: string; validNextStatuses?: string[] } | undefined
    try { body = await response.json() } catch { body = undefined }
    throw new ApiError(body?.message ?? `Request failed (${response.status}).`, response.status, body)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

function qs(values: Record<string, string | number | boolean | undefined>) {
  const params = new URLSearchParams()
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== '' && value !== 'All') params.set(key, String(value))
  })
  return params.size ? `?${params.toString()}` : ''
}

export const api = {
  getTickets(sort: TicketSort) {
    return request<Ticket[]>(`/api/tickets${qs({ sort })}`)
  },

  createTicket(payload: CreateTicketRequest) {
    return request<{ message: string; ticket: Ticket }>('/api/tickets', {
      method: 'POST', body: JSON.stringify(payload),
    })
  },

  updateTicket(id: number, payload: { status?: TicketStatus; assignee?: string; workaround?: string }, role: Role) {
    return request<Ticket>(`/api/tickets/${id}`, {
      method: 'PUT', body: JSON.stringify(payload),
    }, role)
  },

  claimTicket(id: number, technicianName: string, role: Role) {
    return request<Ticket>(`/api/tickets/${id}/claim`, {
      method: 'POST', body: JSON.stringify({ technicianName }),
    }, role)
  },

  reassignTicket(id: number, assignee: string, role: Role) {
    return request<Ticket>(`/api/tickets/${id}/reassign`, {
      method: 'POST', body: JSON.stringify({ assignee }),
    }, role)
  },

  escalateTicket(id: number, reason: string, role: Role) {
    return request<Ticket>(`/api/tickets/${id}/escalate`, {
      method: 'POST', body: JSON.stringify({ reason }),
    }, role)
  },

  getNotes(id: number, role: Role) {
    return request<TicketNote[]>(`/api/tickets/${id}/notes`, {}, role)
  },

  addNote(id: number, author: string, body: string, role: Role) {
    return request<TicketNote>(`/api/tickets/${id}/notes`, {
      method: 'POST', body: JSON.stringify({ author, body }),
    }, role)
  },

  uploadAttachment(id: number, file: File) {
    const form = new FormData()
    form.append('file', file)
    return request<{ message: string; ticket: Ticket }>(`/api/tickets/${id}/attachment`, {
      method: 'POST', body: form,
    })
  },

  verifyBadge(badgeId: string) {
    return request<StaffProfile>(`/api/staff/badge/${encodeURIComponent(badgeId.trim())}`)
  },

  searchFlight(query: string, offline: boolean) {
    return request<FlightRecord>(`/api/flights/search${qs({ q: query, offline })}`)
  },

  getDashboard(role: Role) {
    return request<DashboardMetrics>('/api/dashboard', {}, role)
  },

  getDashboardTickets(filters: DashboardFilters, role: Role) {
    return request<Ticket[]>(`/api/dashboard/tickets${qs(filters)}`, {}, role)
  },

  async exportDashboard(filters: DashboardFilters, role: Role) {
    const response = await fetch(`${API_BASE}/api/dashboard/export${qs(filters)}`, {
      headers: { 'X-Demo-Role': role },
    })
    if (!response.ok) {
      const body = await response.json().catch(() => ({})) as { message?: string }
      throw new ApiError(body.message ?? 'Export failed.', response.status)
    }
    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = 'airport-helpdesk-dashboard.csv'
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
    URL.revokeObjectURL(url)
  },

  seedPerformanceData(count: number, role: Role) {
    return request<{ message: string; count: number; databaseSeedMilliseconds: number }>(
      `/api/demo/performance-data?count=${count}`, { method: 'POST' }, role,
    )
  },

  clearPerformanceData(role: Role) {
    return request<{ message: string }>('/api/demo/performance-data', { method: 'DELETE' }, role)
  },
}

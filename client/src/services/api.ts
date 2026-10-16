import type {
  CreateTicketRequest,
  DashboardFilters,
  DashboardMetrics,
  ExportKind,
  FlightRecord,
  LoginResponse,
  Role,
  SessionUser,
  StaffProfile,
  Technician,
  Ticket,
  TicketNote,
  TicketSort,
  TicketStatus,
} from '../types'

const API_BASE = import.meta.env.VITE_API_URL ?? ''

/**
 * Session token issued by POST /api/auth/login. Held in memory for requests and mirrored to
 * sessionStorage (by the App) so a page refresh keeps the user signed in, while closing the
 * tab signs them out. The server stores only a hash of it.
 */
let sessionToken: string | null = null
let unauthorizedHandler: (() => void) | null = null

export function setSessionToken(token: string | null) {
  sessionToken = token
}

export function getSessionToken() {
  return sessionToken
}

/** Called when an authenticated request comes back 401, i.e. the session expired or was revoked. */
export function onUnauthorized(handler: (() => void) | null) {
  unauthorizedHandler = handler
}

export class ApiError extends Error {
  status: number
  details?: { validNextStatuses?: string[] }

  constructor(message: string, status: number, details?: { validNextStatuses?: string[] }) {
    super(message)
    this.status = status
    this.details = details
  }
}

function authHeaders(): Record<string, string> {
  return sessionToken ? { Authorization: `Bearer ${sessionToken}` } : {}
}

async function request<T>(path: string, options: RequestInit = {}, role?: Role): Promise<T> {
  const headers = new Headers(options.headers)
  if (!(options.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }
  // Identity comes only from the session token. The role argument is never sent; the server
  // reads the role from the signed-in account. It is kept in the log for diagnostics.
  for (const [name, value] of Object.entries(authHeaders())) headers.set(name, value)

  const url = `${API_BASE}${path}`
  // Request bodies are deliberately not logged: the sign-in body contains a password.
  console.debug(`[API] ${options.method || 'GET'} ${url}`, { role })

  const response = await fetch(url, { ...options, headers })

  if (!response.ok) {
    let body: { message?: string; validNextStatuses?: string[] } | undefined
    try { body = await response.json() } catch { body = undefined }
    if (response.status === 401 && sessionToken && !path.startsWith('/api/auth/')) {
      unauthorizedHandler?.()
    }
    throw new ApiError(body?.message ?? `Request failed (${response.status}).`, response.status, body)
  }
  if (response.status === 204) return undefined as T
  return await response.json() as T
}

function qs(values: Record<string, string | number | boolean | undefined>) {
  const params = new URLSearchParams()
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== '' && value !== 'All') params.set(key, String(value))
  })
  return params.size ? `?${params.toString()}` : ''
}

/** File name from the server's Content-Disposition header (readable when the API is same-origin). */
export function downloadName(header: string | null): string | undefined {
  const match = header?.match(/filename="?([^";]+)"?/i)
  const name = match?.[1]?.trim()
  // Only plain names: never let a header steer the download into a path.
  return name && /^[\w.-]+$/.test(name) ? name : undefined
}

function fallbackName(kind: ExportKind) {
  const d = new Date()
  const date = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  return `airport-helpdesk-${kind}-${date}.csv`
}

export const api = {
  login(username: string, password: string) {
    return request<LoginResponse>('/api/auth/login', {
      method: 'POST', body: JSON.stringify({ username, password }),
    })
  },

  me() {
    return request<SessionUser>('/api/auth/me')
  },

  logout() {
    return request<void>('/api/auth/logout', { method: 'POST' })
  },

  getTickets(sort: TicketSort) {
    return request<Ticket[]>(`/api/tickets${qs({ sort })}`)
  },

  getTechnicians() {
    return request<Technician[]>('/api/technicians')
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
      method: 'POST', body: JSON.stringify({ author, body, isInternal: true }),
    }, role)
  },

  addComment(id: number, author: string, body: string, role: Role) {
    return request<TicketNote>(`/api/tickets/${id}/notes`, {
      method: 'POST', body: JSON.stringify({ author, body, isInternal: false }),
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

  /**
   * Downloads one dashboard export and returns the file name used. Filters only apply to the
   * ticket list; the summary files cover every ticket, like the summary cards.
   */
  async exportDashboard(kind: ExportKind, filters: DashboardFilters, role: Role): Promise<string> {
    const path = kind === 'tickets' ? `/api/dashboard/export${qs(filters)}` : `/api/dashboard/export/${kind}`
    console.debug(`[API] GET ${path}`, { role })
    const response = await fetch(`${API_BASE}${path}`, { headers: authHeaders() })
    if (!response.ok) {
      const body = await response.json().catch(() => ({})) as { message?: string }
      if (response.status === 401 && sessionToken) unauthorizedHandler?.()
      throw new ApiError(body.message ?? 'Export failed.', response.status)
    }
    const fileName = downloadName(response.headers.get('Content-Disposition')) ?? fallbackName(kind)
    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = fileName
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
    // Revoking in the same tick can cancel the download in some browsers.
    setTimeout(() => URL.revokeObjectURL(url), 0)
    return fileName
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

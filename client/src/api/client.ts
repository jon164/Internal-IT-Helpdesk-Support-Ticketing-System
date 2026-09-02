import type {
  ApiProblem,
  BacklogReport,
  CreateTicketRequest,
  MetricsSummary,
  SampleDataCleared,
  SampleDataStatus,
  SlaPolicy,
  TicketDetail,
  TicketPriority,
  TicketStatus,
  TicketSummary,
  User,
  UserAuditEntry,
  UserRole,
} from './types'

/**
 * Thin wrapper over the API.
 *
 * The acting user is sent as a header on every request. That header names an identity only — the
 * server reads the role from the database record and re-checks every permission itself — so nothing
 * here is a security boundary. Removing this file would not let a caller do anything they cannot
 * already do with curl.
 */

const BASE = '/api'

/** A refusal from the API, carrying the server's own message so the UI can show it verbatim. */
export class ApiError extends Error {
  // Declared as fields rather than constructor parameter properties: the project builds with
  // erasableSyntaxOnly, which rules out the shorthand.
  readonly status: number
  readonly problem: ApiProblem

  constructor(status: number, problem: ApiProblem) {
    super(problem.message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }

  /** True when the caller is not permitted, as opposed to the request being malformed. */
  get isForbidden(): boolean {
    return this.status === 403
  }
}

async function request<T>(path: string, userId: string | null, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers)
  headers.set('Accept', 'application/json')

  if (init?.body) {
    headers.set('Content-Type', 'application/json')
  }

  if (userId) {
    headers.set('X-User-Id', userId)
  }

  const response = await fetch(`${BASE}${path}`, { ...init, headers })

  if (!response.ok) {
    let problem: ApiProblem = {
      error: 'Invalid',
      message: `The server returned ${response.status}.`,
    }

    try {
      problem = (await response.json()) as ApiProblem
    } catch {
      // A non-JSON error body (an unhandled exception page, say) leaves the default in place.
    }

    throw new ApiError(response.status, problem)
  }

  return (await response.json()) as T
}

export const api = {
  /**
   * Establishes which account the caller is acting as.
   *
   * The prototype verifies no credential — this selects an identity rather than authenticating one.
   * The seam is here deliberately: adding a password means adding a field to this call and a check
   * in the handler, not rewriting every caller.
   */
  signIn: (userId: string) =>
    request<User>('/session', null, {
      method: 'POST',
      body: JSON.stringify({ userId }),
    }),

  getUsers: () => request<User[]>('/users', null),

  getAdminUsers: (userId: string) => request<User[]>('/admin/users', userId),

  getUserAudit: (userId: string) => request<UserAuditEntry[]>('/admin/audit', userId),

  changeUserRole: (actingUserId: string, subjectId: string, role: UserRole, reason: string) =>
    request<User>(`/admin/users/${encodeURIComponent(subjectId)}/role`, actingUserId, {
      method: 'POST',
      body: JSON.stringify({ role, reason }),
    }),

  setUserActive: (actingUserId: string, subjectId: string, isActive: boolean, reason: string) =>
    request<User>(`/admin/users/${encodeURIComponent(subjectId)}/active`, actingUserId, {
      method: 'POST',
      body: JSON.stringify({ isActive, reason }),
    }),

  /**
   * Demonstration data. These three routes exist only when the API runs in Development — a
   * production build does not register them at all, so a 404 here is the expected answer rather
   * than a fault.
   */
  getSampleDataStatus: (userId: string) =>
    request<SampleDataStatus>('/admin/sample-data', userId),

  generateSampleData: (userId: string) =>
    request<SampleDataStatus>('/admin/sample-data', userId, { method: 'POST' }),

  clearSampleData: (userId: string) =>
    request<SampleDataCleared>('/admin/sample-data', userId, { method: 'DELETE' }),

  getPriorities: () => request<SlaPolicy[]>('/reference/priorities', null),

  getTickets: (userId: string, params: Record<string, string> = {}) => {
    const query = new URLSearchParams(params).toString()
    return request<TicketSummary[]>(`/tickets${query ? `?${query}` : ''}`, userId)
  },

  getCategories: () => request<string[]>('/reference/categories', null),

  getTicket: (userId: string, id: number) => request<TicketDetail>(`/tickets/${id}`, userId),

  createTicket: (userId: string, ticket: CreateTicketRequest) =>
    request<TicketSummary>('/tickets', userId, {
      method: 'POST',
      body: JSON.stringify(ticket),
    }),

  /**
   * Move a ticket to a new status. The note carries the resolution notes when resolving and the
   * reason when placing a ticket on hold — the server decides which, and refuses when it is missing.
   */
  transitionTicket: (userId: string, id: number, status: TicketStatus, note?: string) =>
    request<TicketSummary>(`/tickets/${id}/transition`, userId, {
      method: 'POST',
      body: JSON.stringify({ status, note: note ?? null }),
    }),

  assignTicket: (userId: string, id: number, technicianId: string) =>
    request<TicketSummary>(`/tickets/${id}/assign`, userId, {
      method: 'POST',
      body: JSON.stringify({ technicianId }),
    }),

  changeTicketPriority: (
    userId: string,
    id: number,
    priority: TicketPriority,
    justification: string,
  ) =>
    request<TicketSummary>(`/tickets/${id}/priority`, userId, {
      method: 'POST',
      body: JSON.stringify({ priority, justification }),
    }),

  addTicketComment: (userId: string, id: number, comment: string) =>
    request<TicketSummary>(`/tickets/${id}/comments`, userId, {
      method: 'POST',
      body: JSON.stringify({ comment }),
    }),

  /**
   * The management report. Refused with 403 for anyone but the service manager — the dashboard
   * relies on that refusal rather than hiding the page, so the restriction is real.
   */
  getMetrics: (userId: string, from: Date, to: Date) => {
    const query = new URLSearchParams({
      from: from.toISOString(),
      to: to.toISOString(),
    }).toString()

    return request<MetricsSummary>(`/reports/summary?${query}`, userId)
  },

  /**
   * Backlog movement over the last N weeks. Same 403 restriction as the performance report.
   * The window is chosen server-side from the week count so both reports cannot drift apart.
   */
  getBacklog: (userId: string, weeks: number) =>
    request<BacklogReport>(`/reports/backlog?weeks=${weeks}`, userId),
}

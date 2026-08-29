import type { ApiProblem, MetricsSummary, SlaPolicy, TicketSummary, User } from './types'

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
  getUsers: () => request<User[]>('/users', null),

  getPriorities: () => request<SlaPolicy[]>('/reference/priorities', null),

  getTickets: (userId: string, params: Record<string, string> = {}) => {
    const query = new URLSearchParams(params).toString()
    return request<TicketSummary[]>(`/tickets${query ? `?${query}` : ''}`, userId)
  },

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
}

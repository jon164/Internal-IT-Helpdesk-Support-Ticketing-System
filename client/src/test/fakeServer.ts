import { vi } from 'vitest'

export type Call = { url: string; method: string; headers: Headers; body: string | undefined }

/** `text` sends a raw body (e.g. a CSV file) instead of JSON; `headers` adds response headers. */
export type Reply = { status: number; body?: unknown; text?: string; headers?: Record<string, string> }

type Handler = (call: Call) => Reply | undefined

/**
 * Replaces fetch with a small in-memory router so client tests run without the ASP.NET API.
 * Every call is recorded, so tests can assert on exactly what the browser would have sent.
 */
export function installFakeServer(handler: Handler) {
  const calls: Call[] = []
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init: RequestInit = {}) => {
    const call: Call = {
      url: String(input),
      method: (init.method ?? 'GET').toUpperCase(),
      headers: new Headers(init.headers),
      body: typeof init.body === 'string' ? init.body : undefined,
    }
    calls.push(call)
    const reply = handler(call) ?? defaultReply(call)
    if (reply.status === 204) return new Response(null, { status: 204 })
    if (reply.text !== undefined) return new Response(reply.text, { status: reply.status, headers: reply.headers })
    return new Response(JSON.stringify(reply.body ?? {}), {
      status: reply.status,
      headers: { 'Content-Type': 'application/json' },
    })
  })
  vi.stubGlobal('fetch', fetchMock)
  return { calls, fetchMock }
}

function defaultReply(call: Call): Reply {
  if (call.url.includes('/api/dashboard/tickets')) return { status: 200, body: [] }
  if (call.url.includes('/api/dashboard')) {
    return { status: 200, body: { openBacklog: 0, averageResolutionMinutes: 0, overdueCount: 0, recurringIssueCategory: 'Incident', recurringIssueCount: 0, totalTickets: 0 } }
  }
  if (call.url.includes('/api/tickets') || call.url.includes('/api/technicians')) return { status: 200, body: [] }
  return { status: 404, body: { message: 'Not found' } }
}

export const MANAGER = { id: 'it-manager', displayName: 'IT Manager', department: 'IT Services', role: 'TeamLead', isActive: true }
export const STAFF = { id: 'airport-staff', displayName: 'Airport Staff', department: 'Operations', role: 'Requester', isActive: true }

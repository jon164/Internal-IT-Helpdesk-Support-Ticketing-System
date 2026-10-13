import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import { setSessionToken } from '../services/api'
import { axeViolations } from '../test/axe'
import { installFakeServer, MANAGER, type Call } from '../test/fakeServer'

/*
 * USER STORY: As an IT manager, I want to filter tickets by status, priority, and assignee,
 * so that I can investigate bottlenecks or a technician's workload.
 *   AC1 Given a status filter is applied, when I search, then only tickets with that status are returned.
 *   AC2 Given no tickets match the filter, when I search, then an empty result is shown gracefully.
 *
 * The fake server really filters its rows by the query string, the way the API does, so these tests
 * check what the page sends AND what it shows for the answer.
 */

type Row = { id: number; status: string; priority: string; assignee: string }

const ROWS: Row[] = [
  { id: 101, status: 'New', priority: 'Critical', assignee: 'Unassigned' },
  { id: 102, status: 'Resolved', priority: 'Low', assignee: 'Alex Morgan' },
  { id: 103, status: 'Resolved', priority: 'High', assignee: 'Unassigned' },
  { id: 104, status: 'InProgress', priority: 'Medium', assignee: 'Alex Morgan' },
]

// Priya Shah has no tickets: the workload case the Assignee list must still offer.
const TECHNICIANS = ['Alex Morgan', 'Priya Shah', 'Jordan Lee'].map((name, i) => ({ id: `t${i}`, name, email: `${i}@airport.test`, isActive: true }))

function toTicket(r: Row) {
  return {
    id: r.id, terminal: 'T-01', area: 'Operations', systemType: 'Incident', description: `Ticket ${r.id}`, passengerImpact: false,
    flightOpsImpact: true, reporterName: 'Sam', reporterEmail: 'sam@airport.test', staffId: '', priority: r.priority, status: r.status,
    assignee: r.assignee, workaround: '', isEscalated: false, escalationReason: '', createdAtUtc: '2026-10-11T00:00:00Z',
    resolvedAtUtc: null, attachmentUrl: null, attachmentOriginalName: null, attachmentContentType: null, attachmentSize: null,
    slaOverdue: false, isPerformanceTest: false,
  }
}

beforeEach(() => {
  setSessionToken(null)
  vi.spyOn(console, 'debug').mockImplementation(() => {})
})

function startServer() {
  return installFakeServer((call: Call) => {
    if (call.url.endsWith('/api/auth/login')) {
      return { status: 200, body: { token: 'token-for-it-manager', expiresAtUtc: '2026-10-12T17:00:00Z', user: MANAGER } }
    }
    if (call.url.includes('/api/technicians')) return { status: 200, body: TECHNICIANS }
    if (call.url.includes('/api/dashboard/tickets')) {
      const q = new URL(call.url, 'http://localhost').searchParams
      const rows = ROWS.filter(r =>
        (!q.get('status') || r.status === q.get('status')) &&
        (!q.get('priority') || r.priority === q.get('priority')) &&
        (!q.get('assignee') || r.assignee === q.get('assignee')))
      return { status: 200, body: rows.map(toTicket) }
    }
    return undefined
  })
}

async function openDashboard() {
  const user = userEvent.setup()
  const server = startServer()
  const view = render(<App />)
  await user.type(screen.getByLabelText('Username'), 'it-manager')
  await user.type(screen.getByLabelText('Password'), 'any-password{Enter}')
  await screen.findByText('#101')
  return { user, ...server, ...view }
}

/** Ticket numbers currently in the results table. */
function shownIds() {
  const rows = within(screen.getByRole('table')).getAllByRole('row').slice(1) // skip header row
  return rows.map(r => within(r).getAllByRole('cell')[0].textContent)
}

function lastTicketsQuery(calls: Call[]) {
  const url = [...calls].reverse().find(c => c.url.includes('/api/dashboard/tickets'))!.url
  return new URL(url, 'http://localhost').searchParams
}

describe('Dashboard filters — AC1 status filter', () => {
  it('FILT-C01 choosing a status asks the server for that status and shows only those tickets', async () => {
    const { user, calls } = await openDashboard()

    await user.selectOptions(screen.getByLabelText('Status'), 'Resolved')

    await waitFor(() => expect(shownIds()).toEqual(['#102', '#103']))
    expect(lastTicketsQuery(calls).get('status')).toBe('Resolved')
    expect(screen.getByText('2 results')).toBeInTheDocument()
  })

  it('FILT-C02 status, priority and assignee are all sent together (AND)', async () => {
    const { user, calls } = await openDashboard()

    await user.selectOptions(screen.getByLabelText('Status'), 'Resolved')
    await user.selectOptions(screen.getByLabelText('Priority'), 'High')
    await user.selectOptions(screen.getByLabelText('Assignee'), 'Unassigned')

    await waitFor(() => expect(shownIds()).toEqual(['#103']))
    const q = lastTicketsQuery(calls)
    expect([q.get('status'), q.get('priority'), q.get('assignee')]).toEqual(['Resolved', 'High', 'Unassigned'])
  })
})

describe('Dashboard filters — AC2 empty result', () => {
  it('FILT-C03 no match shows a clear empty message, no error, and a way back', async () => {
    const { user } = await openDashboard()

    await user.selectOptions(screen.getByLabelText('Status'), 'Closed')

    expect(await screen.findByText('No tickets match these filters.')).toBeInTheDocument()
    expect(screen.getByText('0 results')).toBeInTheDocument()
    expect(screen.queryByText(/failed|could not/i)).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Clear filters' }))

    await waitFor(() => expect(shownIds()).toEqual(['#101', '#102', '#103', '#104']))
    expect(screen.getByLabelText('Status')).toHaveValue('All')
    expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument()
  })

  it('FILT-C04 a technician with no tickets can be picked, and gives the empty message', async () => {
    const { user } = await openDashboard()

    await user.selectOptions(screen.getByLabelText('Assignee'), 'Priya Shah')

    expect(await screen.findByText('No tickets match these filters.')).toBeInTheDocument()
  })
})

describe('Dashboard filters — usability and accessibility', () => {
  it('FILT-C05 regression: the Assignee list keeps every technician after another filter narrows the results', async () => {
    const { user } = await openDashboard()

    await user.selectOptions(screen.getByLabelText('Status'), 'New') // only an unassigned ticket is left
    await screen.findByText('1 result')

    const options = within(screen.getByLabelText('Assignee')).getAllByRole('option').map(o => o.textContent)
    expect(options).toEqual(['All', 'Alex Morgan', 'Jordan Lee', 'Priya Shah', 'Unassigned'])
  })

  it('FILT-C06 the result count is announced to screen readers when it changes', async () => {
    const { user } = await openDashboard()
    const count = screen.getByText('4 results')
    expect(count).toHaveAttribute('role', 'status')
    expect(count).toHaveAttribute('aria-live', 'polite')

    await user.selectOptions(screen.getByLabelText('Status'), 'Resolved')

    await waitFor(() => expect(count).toHaveTextContent('2 results'))
  })

  it('FILT-C07 the empty-result state passes the axe WCAG 2.1 A/AA scan', async () => {
    const { user, container } = await openDashboard()

    await user.selectOptions(screen.getByLabelText('Status'), 'Closed')
    await screen.findByText('No tickets match these filters.')

    expect(await axeViolations(container)).toEqual([])
  })
})

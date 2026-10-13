import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import { setSessionToken } from '../services/api'
import { axeViolations } from '../test/axe'
import { installFakeServer, MANAGER, STAFF, type Call } from '../test/fakeServer'

/*
 * USER STORY: As an IT manager, I want manager-only visibility of the dashboard,
 * so that analytics aren't exposed to employees.
 *   AC1 Given I am logged in as an Employee, when I attempt to view the dashboard, then access is denied.
 *   AC2 Given I am logged in as a Manager, when I view the dashboard, then it displays.
 *
 * The fake server behaves like the real API: it decides from the bearer token, never from
 * anything the page claims, and answers 403 to anyone who is not the IT Manager.
 */

const TECHNICIAN = { id: 'it-technician', displayName: 'IT Technician', department: 'IT Services', role: 'Technician', isActive: true }
type User = typeof MANAGER

const METRICS = { openBacklog: 7, averageResolutionMinutes: 95, overdueCount: 2, recurringIssueCategory: 'Incident', recurringIssueCount: 5, totalTickets: 12 }
const ROW = {
  id: 4242, terminal: 'T-01', area: 'Operations', systemType: 'Incident', description: 'Gate kiosk', passengerImpact: false,
  flightOpsImpact: true, reporterName: 'Sam', reporterEmail: 'sam@airport.test', staffId: '', priority: 'Critical', status: 'New',
  assignee: 'Unassigned', workaround: '', isEscalated: false, escalationReason: '', createdAtUtc: '2026-10-11T00:00:00Z',
  resolvedAtUtc: null, attachmentUrl: null, attachmentOriginalName: null, attachmentContentType: null, attachmentSize: null,
  slaOverdue: true, isPerformanceTest: false,
}

beforeEach(() => {
  setSessionToken(null)
  vi.spyOn(console, 'debug').mockImplementation(() => {})
})

function startServer(signInAs: User, options: { managerRevoked?: () => boolean } = {}) {
  return installFakeServer((call: Call) => {
    if (call.url.endsWith('/api/auth/login')) {
      return { status: 200, body: { token: `token-for-${signInAs.id}`, expiresAtUtc: '2026-10-12T17:00:00Z', user: signInAs } }
    }
    if (call.url.includes('/api/dashboard')) {
      const isManager = call.headers.get('Authorization') === 'Bearer token-for-it-manager' && !options.managerRevoked?.()
      if (!isManager) return { status: 403, body: { error: 'Forbidden', message: 'The dashboard is available to the IT Manager only.' } }
      return call.url.includes('/api/dashboard/tickets') ? { status: 200, body: [ROW] } : { status: 200, body: METRICS }
    }
    return undefined
  })
}

async function signIn(user: ReturnType<typeof userEvent.setup>, username: string) {
  await user.type(screen.getByLabelText('Username'), username)
  await user.type(screen.getByLabelText('Password'), 'any-password{Enter}')
  await screen.findByRole('button', { name: 'Sign out' })
}

/** Text that only appears when analytics are on screen. */
function expectNoAnalytics() {
  expect(screen.queryByText('Service health dashboard')).not.toBeInTheDocument()
  expect(screen.queryByText('Open backlog')).not.toBeInTheDocument()
  expect(screen.queryByText('Overdue')).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Export current CSV' })).not.toBeInTheDocument()
  expect(screen.queryByRole('table')).not.toBeInTheDocument()
}

describe('Manager-only dashboard — security', () => {
  it('DASH-C01 AC1: an employee who opens the dashboard is denied and sees no analytics', async () => {
    const user = userEvent.setup()
    const { calls } = startServer(STAFF)
    render(<App />)
    await signIn(user, 'airport-staff')

    await user.click(screen.getByRole('button', { name: /Dashboard/ }))

    expect(await screen.findByRole('heading', { name: 'Access denied' })).toBeInTheDocument()
    expect(screen.getByText(/available to the IT Manager only/)).toBeInTheDocument()
    expectNoAnalytics()
    // The refusal came from the server, for this user's own token.
    const dashboardCall = calls.find(c => c.url.endsWith('/api/dashboard'))!
    expect(dashboardCall.headers.get('Authorization')).toBe('Bearer token-for-airport-staff')
  })

  it('DASH-C02 AC1: a technician is not a manager either, and is denied', async () => {
    const user = userEvent.setup()
    startServer(TECHNICIAN)
    render(<App />)
    await signIn(user, 'it-technician')

    await user.click(screen.getByRole('button', { name: /Dashboard/ }))

    expect(await screen.findByRole('heading', { name: 'Access denied' })).toBeInTheDocument()
    expectNoAnalytics()
  })

  it('DASH-C03 AC1: the empty dashboard layout is never shown while the server decides', async () => {
    const user = userEvent.setup()
    startServer(STAFF)
    render(<App />)
    await signIn(user, 'airport-staff')

    // Watch every DOM change between the click and the refusal, so a layout that flashes up
    // for a moment and then disappears is still caught.
    const seen: string[] = []
    const observer = new MutationObserver(() => {
      const text = document.body.textContent ?? ''
      for (const marker of ['Service health dashboard', 'Open backlog', 'Export current CSV']) {
        if (text.includes(marker) && !seen.includes(marker)) seen.push(marker)
      }
    })
    observer.observe(document.body, { childList: true, subtree: true, characterData: true })

    await user.click(screen.getByRole('button', { name: /Dashboard/ }))
    await screen.findByRole('heading', { name: 'Access denied' })
    observer.disconnect()

    expect(seen).toEqual([])
  })

  it('DASH-C04 AC2: the manager lands on the dashboard and sees the figures and tickets', async () => {
    const user = userEvent.setup()
    startServer(MANAGER)
    render(<App />)
    await signIn(user, 'it-manager')

    expect(await screen.findByText('Service health dashboard')).toBeInTheDocument()
    expect(await screen.findByText('7')).toBeInTheDocument()            // open backlog from the server
    expect(screen.getByText('Incident (5)')).toBeInTheDocument()        // recurring issue
    expect(screen.getByText('#4242')).toBeInTheDocument()               // ticket row
    expect(screen.getByRole('button', { name: 'Export current CSV' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Access denied' })).not.toBeInTheDocument()
  })

  it('DASH-C05 if the server stops allowing access mid-session, figures already on screen are removed', async () => {
    const user = userEvent.setup()
    let revoked = false
    startServer(MANAGER, { managerRevoked: () => revoked })
    render(<App />)
    await signIn(user, 'it-manager')
    await screen.findByText('#4242')

    revoked = true // e.g. the account was moved out of the IT Manager role
    await user.selectOptions(screen.getByLabelText('Priority'), 'High')

    expect(await screen.findByRole('heading', { name: 'Access denied' })).toBeInTheDocument()
    expectNoAnalytics()
    expect(screen.queryByText('#4242')).not.toBeInTheDocument()
  })
})

describe('Manager-only dashboard — accessibility and usability', () => {
  it('DASH-C06 the denial moves focus to its heading and passes the axe WCAG 2.1 A/AA scan', async () => {
    const user = userEvent.setup()
    startServer(STAFF)
    const { container } = render(<App />)
    await signIn(user, 'airport-staff')

    await user.click(screen.getByRole('button', { name: /Dashboard/ }))
    const heading = await screen.findByRole('heading', { name: 'Access denied' })

    await waitFor(() => expect(heading).toHaveFocus())
    expect(screen.getByText(/signed in as Airport Staff/)).toBeInTheDocument()
    expect(await axeViolations(container)).toEqual([])
  })

  it('DASH-C07 the manager dashboard passes the axe WCAG 2.1 A/AA scan', async () => {
    const user = userEvent.setup()
    startServer(MANAGER)
    const { container } = render(<App />)
    await signIn(user, 'it-manager')
    await screen.findByText('#4242')

    expect(await axeViolations(container)).toEqual([])
  })
})

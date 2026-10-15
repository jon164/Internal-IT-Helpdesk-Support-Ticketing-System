import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import { downloadName, setSessionToken } from '../services/api'
import { axeViolations } from '../test/axe'
import { installFakeServer, MANAGER, type Call, type Reply } from '../test/fakeServer'

/*
 * USER STORY: As an IT manager, I want to export the dashboard summary, so that I can include
 * service data in reports to senior management.
 *   AC1 Given the dashboard is displayed, when I choose export, then a file (e.g. CSV) of the
 *       current metrics is produced.
 *   AC2 Given no ticket data exists, when I export, then a valid file with headers and zero rows
 *       is produced.
 *
 * The file contents are the server's job and are tested in DashboardExportTests.cs. These tests
 * check the browser side: the right file is asked for, what comes back is saved under the right
 * name, and the choice panel works by keyboard and passes axe.
 */

const METRICS = { openBacklog: 3, averageResolutionMinutes: 780, overdueCount: 1, recurringIssueCategory: 'Incident', recurringIssueCount: 3, totalTickets: 5 }
const ROW = {
  id: 101, terminal: 'T-01', area: 'Operations', systemType: 'Incident', description: 'Gate kiosk', passengerImpact: false,
  flightOpsImpact: true, reporterName: 'Sam', reporterEmail: 'sam@airport.test', staffId: '', priority: 'Critical', status: 'New',
  assignee: 'Unassigned', workaround: '', isEscalated: false, escalationReason: '', createdAtUtc: '2026-10-11T00:00:00Z',
  resolvedAtUtc: null, attachmentUrl: null, attachmentOriginalName: null, attachmentContentType: null, attachmentSize: null,
  slaOverdue: true, isPerformanceTest: false,
}

const CSV: Record<string, string> = {
  summary: '\uFEFFmetric,value,unit\r\nTotal tickets,5,tickets\r\n',
  'by-priority': '\uFEFFpriority,totalTickets,openTickets,overdueTickets,resolvedTickets,averageResolutionMinutes\r\nCritical,2,1,1,1,120\r\n',
  tickets: '\uFEFFid,terminal,area,systemType,priority,status,assignee,reporterName,slaOverdue\r\n',
}

function csvReply(kind: string): Reply {
  return {
    status: 200,
    text: CSV[kind],
    headers: { 'Content-Type': 'text/csv; charset=utf-8', 'Content-Disposition': `attachment; filename=airport-helpdesk-${kind}-2026-10-15.csv` },
  }
}

type Options = { noTickets?: boolean; exportReply?: (call: Call) => Reply | undefined }

/** Captures what the browser would save: the blob given to createObjectURL and the anchor's download name. */
function captureDownloads() {
  const saved: { name: string; blob: Blob }[] = []
  let lastBlob: Blob | undefined
  // jsdom has no createObjectURL; give it one that records the blob.
  URL.createObjectURL = vi.fn((blob: Blob) => { lastBlob = blob; return 'blob:test' })
  URL.revokeObjectURL = vi.fn()
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    saved.push({ name: this.download, blob: lastBlob! })
  })
  return saved
}

beforeEach(() => {
  setSessionToken(null)
  vi.spyOn(console, 'debug').mockImplementation(() => {})
})

async function openDashboard(options: Options = {}) {
  const user = userEvent.setup()
  const server = installFakeServer((call: Call) => {
    if (call.url.endsWith('/api/auth/login')) {
      return { status: 200, body: { token: 'token-for-it-manager', expiresAtUtc: '2026-10-12T17:00:00Z', user: MANAGER } }
    }
    if (call.url.includes('/api/dashboard/export')) {
      const custom = options.exportReply?.(call)
      if (custom) return custom
      const kind = call.url.includes('/export/summary') ? 'summary' : call.url.includes('/export/by-priority') ? 'by-priority' : 'tickets'
      return csvReply(kind)
    }
    if (call.url.includes('/api/dashboard/tickets')) return { status: 200, body: options.noTickets ? [] : [ROW] }
    if (call.url.includes('/api/dashboard')) {
      return { status: 200, body: options.noTickets ? { ...METRICS, openBacklog: 0, averageResolutionMinutes: 0, overdueCount: 0, recurringIssueCategory: 'None', recurringIssueCount: 0, totalTickets: 0 } : METRICS }
    }
    return undefined
  })
  const saved = captureDownloads()
  const view = render(<App />)
  await user.type(screen.getByLabelText('Username'), 'it-manager')
  await user.type(screen.getByLabelText('Password'), 'any-password{Enter}')
  await screen.findByText('Service health dashboard')
  return { user, saved, ...server, ...view }
}

function exportCalls(calls: Call[]) {
  return calls.filter(c => c.url.includes('/api/dashboard/export'))
}

const exportButton = () => screen.getByRole('button', { name: 'Export current CSV' })

describe('Dashboard export — AC1 choosing what to export', () => {
  it('EXP-C01 Export opens a panel with the three file choices; Metric summary is selected and focused', async () => {
    const { user, calls } = await openDashboard()

    expect(exportButton()).toHaveAttribute('aria-expanded', 'false')
    await user.click(exportButton())

    const panel = screen.getByRole('region', { name: 'Export dashboard data' })
    const choices = within(panel).getAllByRole('radio')
    expect(choices.map(c => c.closest('label')!.querySelector('strong')!.textContent)).toEqual(['Metric summary', 'Breakdown by priority', 'Ticket list'])
    expect(within(panel).getByRole('radio', { name: /Metric summary/ })).toBeChecked()
    expect(within(panel).getByRole('radio', { name: /Metric summary/ })).toHaveFocus()
    expect(exportButton()).toHaveAttribute('aria-expanded', 'true')
    // Nothing is downloaded just by opening the panel.
    expect(exportCalls(calls)).toHaveLength(0)
  })

  it('EXP-C02 Metric summary downloads /export/summary with the session token and saves it under the server\'s name', async () => {
    const { user, calls, saved } = await openDashboard()

    await user.click(exportButton())
    await user.click(screen.getByRole('button', { name: 'Download CSV' }))

    expect(await screen.findByText('Metric summary downloaded as airport-helpdesk-summary-2026-10-15.csv.')).toBeInTheDocument()
    const [call] = exportCalls(calls)
    expect(new URL(call.url, 'http://localhost').pathname).toBe('/api/dashboard/export/summary')
    expect(new URL(call.url, 'http://localhost').search).toBe('')
    expect(call.headers.get('Authorization')).toBe('Bearer token-for-it-manager')
    expect(saved).toHaveLength(1)
    expect(saved[0].name).toBe('airport-helpdesk-summary-2026-10-15.csv')
    expect(await saved[0].blob.text()).toContain('Total tickets,5,tickets')
  })

  it('EXP-C03 Breakdown by priority downloads /export/by-priority', async () => {
    const { user, calls, saved } = await openDashboard()

    await user.click(exportButton())
    await user.click(screen.getByRole('radio', { name: /Breakdown by priority/ }))
    await user.click(screen.getByRole('button', { name: 'Download CSV' }))

    await screen.findByText(/Breakdown by priority downloaded/)
    expect(new URL(exportCalls(calls)[0].url, 'http://localhost').pathname).toBe('/api/dashboard/export/by-priority')
    expect(saved[0].name).toBe('airport-helpdesk-by-priority-2026-10-15.csv')
  })

  it('EXP-C04 regression: the original ticket list export is still offered and still sends the table filters', async () => {
    const { user, calls } = await openDashboard()

    await user.selectOptions(screen.getByLabelText('Status'), 'New')
    await user.click(exportButton())
    expect(screen.getByRole('radio', { name: /Ticket list/ })).toHaveAccessibleDescription(/current filters \(1 row\)/)
    await user.click(screen.getByRole('radio', { name: /Ticket list/ }))
    await user.click(screen.getByRole('button', { name: 'Download CSV' }))

    await screen.findByText(/Ticket list downloaded/)
    const url = new URL(exportCalls(calls)[0].url, 'http://localhost')
    expect(url.pathname).toBe('/api/dashboard/export')
    expect(url.searchParams.get('status')).toBe('New')
  })
})

describe('Dashboard export — AC2 no ticket data', () => {
  it('EXP-C05 with no tickets, a header-only file is still downloaded and reported as a success', async () => {
    const { user, saved } = await openDashboard({ noTickets: true })
    await screen.findByText('There are no tickets yet.')

    await user.click(exportButton())
    await user.click(screen.getByRole('radio', { name: /Ticket list/ }))
    await user.click(screen.getByRole('button', { name: 'Download CSV' }))

    expect(await screen.findByText(/Ticket list downloaded/)).toBeInTheDocument()
    expect((await saved[0].blob.text()).replace(/^\uFEFF/, '')).toBe('id,terminal,area,systemType,priority,status,assignee,reporterName,slaOverdue\r\n')
    expect(screen.queryByText(/failed/i)).not.toBeInTheDocument()
  })
})

describe('Dashboard export — usability, accessibility, reliability, security', () => {
  it('EXP-C06 Escape and Cancel close the panel and put focus back on the Export button', async () => {
    const { user } = await openDashboard()

    await user.click(exportButton())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('region', { name: 'Export dashboard data' })).not.toBeInTheDocument()
    await waitFor(() => expect(exportButton()).toHaveFocus())

    await user.click(exportButton())
    await user.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(screen.queryByRole('region', { name: 'Export dashboard data' })).not.toBeInTheDocument()
    await waitFor(() => expect(exportButton()).toHaveFocus())
  })

  it('EXP-C07 the whole export can be done from the keyboard alone', async () => {
    const { user, calls } = await openDashboard()

    exportButton().focus()
    await user.keyboard('{Enter}')                 // open; focus lands on Metric summary
    await user.keyboard('{ArrowDown}')             // Breakdown by priority
    await user.tab()                               // Download CSV
    await user.keyboard('{Enter}')

    await screen.findByText(/Breakdown by priority downloaded/)
    expect(new URL(exportCalls(calls)[0].url, 'http://localhost').pathname).toBe('/api/dashboard/export/by-priority')
    await waitFor(() => expect(exportButton()).toHaveFocus())
  })

  it('EXP-C08 axe-core finds no WCAG 2.1 A/AA violations with the export panel open', async () => {
    const { user, container } = await openDashboard()
    await user.click(exportButton())

    expect(await axeViolations(container)).toEqual([])
  })

  it('EXP-C09 a failed download keeps the panel open, says so, and can be retried', async () => {
    let fail = true
    const { user, saved } = await openDashboard({
      exportReply: () => fail ? { status: 500, body: { message: 'Export failed on the server.' } } : undefined,
    })

    await user.click(exportButton())
    await user.click(screen.getByRole('button', { name: 'Download CSV' }))

    expect(await screen.findByText('Export failed on the server.')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Export dashboard data' })).toBeInTheDocument()
    expect(saved).toHaveLength(0)

    fail = false
    await user.click(screen.getByRole('button', { name: 'Download CSV' }))
    expect(await screen.findByText(/Metric summary downloaded/)).toBeInTheDocument()
    expect(saved).toHaveLength(1)
  })

  it('EXP-C10 security: if the server refuses the export (403), the dashboard figures are removed', async () => {
    const { user, saved } = await openDashboard({
      exportReply: () => ({ status: 403, body: { error: 'Forbidden', message: 'The dashboard is available to the IT Manager only.' } }),
    })

    await user.click(exportButton())
    await user.click(screen.getByRole('button', { name: 'Download CSV' }))

    expect(await screen.findByRole('heading', { name: 'Access denied' })).toBeInTheDocument()
    expect(screen.queryByText('Open backlog')).not.toBeInTheDocument()
    expect(saved).toHaveLength(0)
  })

  it('EXP-C11 security: a file name from the server is only used if it is a plain name', () => {
    expect(downloadName('attachment; filename=airport-helpdesk-summary-2026-10-15.csv')).toBe('airport-helpdesk-summary-2026-10-15.csv')
    expect(downloadName('attachment; filename="report.csv"')).toBe('report.csv')
    expect(downloadName('attachment; filename="../../Windows/evil.csv"')).toBeUndefined()
    expect(downloadName('attachment; filename="C:\\evil.csv"')).toBeUndefined()
    expect(downloadName(null)).toBeUndefined()
  })
})

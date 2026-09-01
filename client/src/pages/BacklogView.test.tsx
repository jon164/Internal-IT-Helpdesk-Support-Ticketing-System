import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from '../api/client'
import type { BacklogReport, User } from '../api/types'
import { BacklogView } from './BacklogView'

vi.mock('../api/client', async () => {
  const actual = await vi.importActual<typeof import('../api/client')>('../api/client')

  return {
    ...actual,
    api: { ...actual.api, getBacklog: vi.fn() },
  }
})

const getBacklog = vi.mocked(api.getBacklog)

const manager: User = {
  id: 'lead-maia',
  displayName: 'Maia Thornton',
  department: 'IT Support',
  role: 'TeamLead',
  isActive: true,
}

const technician: User = {
  id: 'tech-nikau',
  displayName: 'Nikau Ashford',
  department: 'IT Support',
  role: 'Technician',
  isActive: true,
}

function report(overrides: Partial<BacklogReport> = {}): BacklogReport {
  return {
    from: '2026-07-04T00:00:00Z',
    to: '2026-08-29T00:00:00Z',
    trend: [
      {
        periodStart: '2026-08-16T00:00:00Z',
        periodEnd: '2026-08-23T00:00:00Z',
        raised: 15,
        resolved: 16,
        unresolvedAtEnd: 29,
        netChange: -1,
      },
      {
        periodStart: '2026-08-23T00:00:00Z',
        periodEnd: '2026-08-29T00:00:00Z',
        raised: 18,
        resolved: 23,
        unresolvedAtEnd: 24,
        netChange: -5,
      },
    ],
    unresolvedAtStart: 28,
    unresolvedNow: 24,
    unresolvedBreached: 17,
    netChange: -4,
    direction: 'Steady',
    clearanceRatePercent: 100,
    ageBuckets: [
      { label: 'Under 1 day', count: 1 },
      { label: '1 to 3 days', count: 3 },
      { label: '3 to 7 days', count: 3 },
      { label: '1 to 2 weeks', count: 2 },
      { label: 'Over 2 weeks', count: 15 },
    ],
    oldestUnresolvedDays: 148.4,
    oldestUnresolvedReference: 'TKT-000204',
    unresolvedByAssignee: {
      'Raj Bhandari': 8,
      'Simone Delacroix': 8,
      'Nikau Ashford': 4,
      Unassigned: 4,
    },
    ...overrides,
  }
}

describe('BacklogView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('leads with the number of unresolved tickets', async () => {
    getBacklog.mockResolvedValue(report())

    render(<BacklogView currentUser={manager} />)

    const heading = await screen.findByText('Unresolved right now')
    expect(heading.closest('section')?.querySelector('.stat-value')).toHaveTextContent('24')
  })

  it('states the direction of travel, not just the count', async () => {
    // A bare number cannot answer "are we falling behind" — the whole point of this view is that it
    // says which way the queue is moving.
    getBacklog.mockResolvedValue(report({ direction: 'Growing' }))

    render(<BacklogView currentUser={manager} />)

    expect(await screen.findByText('Queue growing')).toBeInTheDocument()
  })

  it('reports a shrinking queue as good news', async () => {
    getBacklog.mockResolvedValue(report({ direction: 'Shrinking' }))

    render(<BacklogView currentUser={manager} />)

    expect(await screen.findByText('Queue shrinking')).toBeInTheDocument()
  })

  it('compares the current queue against where it started', async () => {
    getBacklog.mockResolvedValue(report({ unresolvedNow: 24, unresolvedAtStart: 28, netChange: -4 }))

    render(<BacklogView currentUser={manager} />)

    expect(await screen.findByText(/Down 4 from 28/)).toBeInTheDocument()
  })

  it('flags a clearance rate below 100 per cent', async () => {
    // Below 100% work arrives faster than it is cleared, which is the definition of falling behind.
    getBacklog.mockResolvedValue(report({ clearanceRatePercent: 82 }))

    render(<BacklogView currentUser={manager} />)

    expect(await screen.findByText('Arriving faster than cleared')).toBeInTheDocument()
  })

  it('does not flag a clearance rate at or above 100 per cent', async () => {
    getBacklog.mockResolvedValue(report({ clearanceRatePercent: 104 }))

    render(<BacklogView currentUser={manager} />)

    await screen.findByText('Clearance rate')
    expect(screen.queryByText('Arriving faster than cleared')).not.toBeInTheDocument()
  })

  it('names the longest-waiting ticket so it can be chased', async () => {
    getBacklog.mockResolvedValue(report())

    render(<BacklogView currentUser={manager} />)

    expect(await screen.findByText('Longest wait')).toBeInTheDocument()
    expect(screen.getByText('148 days')).toBeInTheDocument()
    expect(screen.getByText(/TKT-000204 has been open the longest/)).toBeInTheDocument()
  })

  it('breaks the waiting work down by age', async () => {
    getBacklog.mockResolvedValue(report())

    render(<BacklogView currentUser={manager} />)

    expect(await screen.findByText('How long work has been waiting')).toBeInTheDocument()
    expect(screen.getByText('Over 2 weeks')).toBeInTheDocument()
    expect(screen.getByText('Under 1 day')).toBeInTheDocument()
  })

  it('shows unassigned work as its own bar rather than dropping it', async () => {
    getBacklog.mockResolvedValue(report())

    render(<BacklogView currentUser={manager} />)

    expect(await screen.findByText('Who is holding the queue')).toBeInTheDocument()
    expect(screen.getByText('Unassigned')).toBeInTheDocument()
  })

  it('distinguishes an empty window from a zero clearance rate', async () => {
    getBacklog.mockResolvedValue(
      report({ clearanceRatePercent: null, oldestUnresolvedDays: null, oldestUnresolvedReference: null }),
    )

    render(<BacklogView currentUser={manager} />)

    await waitFor(() => expect(getBacklog).toHaveBeenCalled())

    expect(screen.getAllByText('—').length).toBeGreaterThan(0)
    expect(screen.queryByText('0%')).not.toBeInTheDocument()
  })

  it('requests the window the reader selected', async () => {
    getBacklog.mockResolvedValue(report())
    const user = userEvent.setup()

    render(<BacklogView currentUser={manager} />)

    await screen.findByText('Unresolved right now')
    expect(getBacklog).toHaveBeenCalledWith('lead-maia', 8)

    await user.click(screen.getByRole('button', { name: '12 weeks' }))

    await waitFor(() => expect(getBacklog).toHaveBeenCalledWith('lead-maia', 12))
  })

  it('reports the server refusal when the role is not permitted', async () => {
    getBacklog.mockRejectedValue(
      new ApiError(403, {
        error: 'Forbidden',
        message: 'Management reporting is restricted to the service manager.',
      }),
    )

    render(<BacklogView currentUser={technician} />)

    expect(await screen.findByText('Not available to your role')).toBeInTheDocument()
    expect(screen.queryByText('Unresolved right now')).not.toBeInTheDocument()
  })

  it('offers the weekly series as a table', async () => {
    getBacklog.mockResolvedValue(report())
    const user = userEvent.setup()

    render(<BacklogView currentUser={manager} />)

    await user.click(await screen.findByRole('button', { name: 'View as table' }))

    expect(screen.getByText('All figures')).toBeInTheDocument()
    expect(screen.getByRole('row', { name: /Unresolved at start/ })).toBeInTheDocument()
    expect(screen.getByText(/18 raised, 23 resolved, 24 unresolved/)).toBeInTheDocument()
  })
})

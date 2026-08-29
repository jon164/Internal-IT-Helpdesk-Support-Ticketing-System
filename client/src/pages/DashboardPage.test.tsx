import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from '../api/client'
import type { MetricsSummary, User } from '../api/types'
import { DashboardPage } from './DashboardPage'

vi.mock('../api/client', async () => {
  const actual = await vi.importActual<typeof import('../api/client')>('../api/client')

  return {
    ...actual,
    api: { ...actual.api, getMetrics: vi.fn() },
  }
})

const getMetrics = vi.mocked(api.getMetrics)

const manager: User = {
  id: 'lead-maia',
  displayName: 'Maia Thornton',
  department: 'IT Support',
  role: 'TeamLead',
}

const technician: User = {
  id: 'tech-nikau',
  displayName: 'Nikau Ashford',
  department: 'IT Support',
  role: 'Technician',
}

function metrics(overrides: Partial<MetricsSummary> = {}): MetricsSummary {
  return {
    from: '2026-07-30T00:00:00Z',
    to: '2026-08-29T00:00:00Z',
    totalCreated: 84,
    totalResolved: 83,
    openBacklog: 28,
    awaitingClosure: 6,
    openBreached: 4,
    openAtRisk: 3,
    responseAttainmentPercent: 85,
    resolutionAttainmentPercent: 84.3,
    medianResolutionMinutes: 1258,
    meanResolutionMinutes: 1500,
    throughputRatioPercent: 98.8,
    createdByPriority: { Critical: 4, High: 13, Standard: 50, Low: 17 },
    openByStatus: { InProgress: 18, Triaged: 6, OnHold: 4 },
    createdByCategory: { Network: 20, Hardware: 15, Printing: 9 },
    ...overrides,
  }
}

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('leads with the resolution attainment figure', async () => {
    getMetrics.mockResolvedValue(metrics())

    render(<DashboardPage currentUser={manager} />)

    expect(await screen.findByText('84.3%')).toBeInTheDocument()
    expect(screen.getByText('Resolution attainment')).toBeInTheDocument()
  })

  it('flags attainment below target rather than presenting it neutrally', async () => {
    getMetrics.mockResolvedValue(metrics({ resolutionAttainmentPercent: 61 }))

    render(<DashboardPage currentUser={manager} />)

    expect(await screen.findByText('61.0%')).toBeInTheDocument()
    expect(screen.getAllByText('Action needed').length).toBeGreaterThan(0)
  })

  it('distinguishes "nothing qualified" from "nothing met its target"', async () => {
    // A period with no resolved tickets must not render as 0% attainment, which would read as
    // total failure when in fact there is simply nothing to judge.
    getMetrics.mockResolvedValue(
      metrics({
        totalResolved: 0,
        resolutionAttainmentPercent: null,
        medianResolutionMinutes: null,
        meanResolutionMinutes: null,
      }),
    )

    render(<DashboardPage currentUser={manager} />)

    await waitFor(() => expect(getMetrics).toHaveBeenCalled())

    expect(screen.getAllByText('—').length).toBeGreaterThan(0)
    expect(screen.queryByText('0.0%')).not.toBeInTheDocument()
    expect(screen.getByText('Nothing was resolved in this period.')).toBeInTheDocument()
  })

  it('surfaces the number of breached tickets still open', async () => {
    getMetrics.mockResolvedValue(metrics({ openBreached: 4 }))

    render(<DashboardPage currentUser={manager} />)

    const heading = await screen.findByText('Breached and still open')
    const tile = heading.closest('section')

    // Scoped to the tile: the figure 4 also appears in the charts below, and asserting on the whole
    // document would pass for the wrong reason.
    expect(tile).not.toBeNull()
    expect(tile!.querySelector('.stat-value')).toHaveTextContent('4')
  })

  it('reports the server refusal when the role is not permitted', async () => {
    // The page is not hidden from technicians — it asks and shows what the server said. This is the
    // test that proves access control is enforced server-side rather than by hiding a menu item.
    getMetrics.mockRejectedValue(
      new ApiError(403, {
        error: 'Forbidden',
        message: 'Management reporting is restricted to the service manager.',
      }),
    )

    render(<DashboardPage currentUser={technician} />)

    expect(await screen.findByText('Not available to your role')).toBeInTheDocument()
    expect(
      screen.getByText('Management reporting is restricted to the service manager.'),
    ).toBeInTheDocument()
    expect(screen.queryByText('Resolution attainment')).not.toBeInTheDocument()
  })

  it('breaks demand down by category so recurring faults are visible', async () => {
    getMetrics.mockResolvedValue(metrics())

    render(<DashboardPage currentUser={manager} />)

    expect(await screen.findByText('Raised by category')).toBeInTheDocument()
    expect(screen.getByText('Network')).toBeInTheDocument()
    expect(screen.getByText('Printing')).toBeInTheDocument()
  })

  it('offers every figure as a table for readers who cannot use the charts', async () => {
    getMetrics.mockResolvedValue(metrics())

    const { default: userEvent } = await import('@testing-library/user-event')
    const user = userEvent.setup()

    render(<DashboardPage currentUser={manager} />)

    await user.click(await screen.findByRole('button', { name: 'View as table' }))

    expect(screen.getByText('All figures')).toBeInTheDocument()
    expect(screen.getByRole('row', { name: /Open backlog/ })).toBeInTheDocument()
  })
})

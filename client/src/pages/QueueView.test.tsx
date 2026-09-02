import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from '../api/client'
import type { SlaState, TicketSummary, User } from '../api/types'
import { QueueView } from './QueueView'

vi.mock('../api/client', async () => {
  const actual = await vi.importActual<typeof import('../api/client')>('../api/client')

  return { ...actual, api: { ...actual.api, getTickets: vi.fn() } }
})

const getTickets = vi.mocked(api.getTickets)

const technician: User = {
  id: 'tech-nikau',
  displayName: 'Nikau Ashford',
  department: 'IT Support',
  role: 'Technician',
  isActive: true,
}

const manager: User = {
  id: 'lead-maia',
  displayName: 'Maia Thornton',
  department: 'IT Support',
  role: 'TeamLead',
  isActive: true,
}

function ticket(id: number, resolutionState: SlaState, overrides: Partial<TicketSummary> = {}) {
  return {
    id,
    reference: `TKT-${id.toString().padStart(6, '0')}`,
    title: 'Wi-Fi dropping in the north admin block',
    category: 'Network',
    priority: 'High',
    priorityLabel: 'P2 High',
    status: 'InProgress',
    requesterName: 'Kiri Anderson',
    department: 'Facilities Management',
    assignedTechnicianId: 'tech-nikau',
    assignedTechnicianName: 'Nikau Ashford',
    isRestricted: false,
    createdAt: '2026-08-27T03:00:00Z',
    response: {
      state: 'Met',
      targetMinutes: 60,
      consumedMinutes: 20,
      remainingMinutes: 40,
      dueAt: '2026-08-27T04:00:00Z',
      percentConsumed: 33,
    },
    resolution: {
      state: resolutionState,
      targetMinutes: 480,
      consumedMinutes: resolutionState === 'Breached' ? 600 : 200,
      remainingMinutes: resolutionState === 'Breached' ? -120 : 280,
      dueAt: '2026-08-27T11:00:00Z',
      percentConsumed: resolutionState === 'Breached' ? 125 : 41.7,
    },
    ...overrides,
  } satisfies TicketSummary
}

describe('QueueView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    getTickets.mockResolvedValue([ticket(1, 'Breached'), ticket(2, 'OnTrack')])
  })

  it('opens a technician on their own work', async () => {
    render(<QueueView currentUser={technician} onOpen={vi.fn()} />)

    await waitFor(() =>
      expect(getTickets).toHaveBeenCalledWith('tech-nikau', {
        assignedTo: 'tech-nikau',
        needsWorkOnly: 'true',
      }),
    )
    expect(
      await screen.findByRole('button', { name: 'My work', pressed: true }),
    ).toBeInTheDocument()
  })

  it('opens the service manager on the whole open desk', async () => {
    render(<QueueView currentUser={manager} onOpen={vi.fn()} />)

    await waitFor(() =>
      expect(getTickets).toHaveBeenCalledWith('lead-maia', { needsWorkOnly: 'true' }),
    )
  })

  it('counts breached and at-risk work from the rows on screen', async () => {
    // Computed from the list rather than fetched separately, so the summary cannot disagree with
    // the table beneath it.
    render(<QueueView currentUser={technician} onOpen={vi.fn()} />)

    const heading = await screen.findByText('Past resolution target')
    const tile = heading.closest('section')

    expect(tile).not.toBeNull()
    expect(tile!.querySelector('.stat-value')).toHaveTextContent('1')
  })

  it('states how overdue a ticket is in words', async () => {
    render(<QueueView currentUser={technician} onOpen={vi.fn()} />)

    expect(await screen.findByText('overdue by 2h')).toBeInTheDocument()
  })

  it('asks the server for unassigned work rather than filtering locally', async () => {
    // Filtering in the browser would mean the client had already been sent tickets it may not be
    // entitled to. The filters are query parameters; the server decides what comes back.
    const user = userEvent.setup()

    render(<QueueView currentUser={technician} onOpen={vi.fn()} />)

    await user.click(await screen.findByRole('button', { name: 'Unassigned' }))

    await waitFor(() =>
      expect(getTickets).toHaveBeenLastCalledWith('tech-nikau', {
        needsWorkOnly: 'true',
        unassignedOnly: 'true',
      }),
    )
  })

  it('passes status and priority filters through to the server', async () => {
    const user = userEvent.setup()

    render(<QueueView currentUser={technician} onOpen={vi.fn()} />)

    await user.selectOptions(await screen.findByRole('combobox', { name: /Status/ }), 'OnHold')
    await user.selectOptions(screen.getByRole('combobox', { name: /Priority/ }), 'Critical')

    await waitFor(() =>
      expect(getTickets).toHaveBeenLastCalledWith('tech-nikau', {
        assignedTo: 'tech-nikau',
        needsWorkOnly: 'true',
        status: 'OnHold',
        priority: 'Critical',
      }),
    )
  })

  it('separates resolved work awaiting the requester from live work', async () => {
    // Resolved-but-unconfirmed tickets are not closed, but nothing on them is the technician's to
    // do. Mixing them into the working queue is what buried the live tickets in the first build.
    const user = userEvent.setup()

    render(<QueueView currentUser={technician} onOpen={vi.fn()} />)

    await user.click(await screen.findByRole('button', { name: 'Awaiting confirmation' }))

    await waitFor(() =>
      expect(getTickets).toHaveBeenLastCalledWith('tech-nikau', { status: 'Resolved' }),
    )

    // The status control would be fighting the view, so it says so rather than doing nothing.
    expect(screen.getByRole('combobox', { name: /Status/ })).toBeDisabled()
  })

  it('opens a ticket by its reference', async () => {
    const onOpen = vi.fn()
    const user = userEvent.setup()

    render(<QueueView currentUser={technician} onOpen={onOpen} />)

    await user.click(await screen.findByRole('button', { name: 'TKT-000001' }))

    expect(onOpen).toHaveBeenCalledWith(1)
  })

  it('shows the server refusal rather than an empty queue', async () => {
    getTickets.mockRejectedValue(
      new ApiError(401, {
        error: 'Unauthenticated',
        message: 'Supply a known user in the X-User-Id header.',
      }),
    )

    render(<QueueView currentUser={technician} onOpen={vi.fn()} />)

    expect(
      await screen.findByText('Supply a known user in the X-User-Id header.'),
    ).toBeInTheDocument()
  })

  it('says so when nothing matches, rather than rendering an empty table', async () => {
    getTickets.mockResolvedValue([])

    render(<QueueView currentUser={technician} onOpen={vi.fn()} />)

    expect(await screen.findByText('No tickets match these filters.')).toBeInTheDocument()
  })
})

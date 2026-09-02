import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from '../api/client'
import type { TicketSummary, User } from '../api/types'
import { MyRequestsView } from './MyRequestsView'

vi.mock('../api/client', async () => {
  const actual = await vi.importActual<typeof import('../api/client')>('../api/client')

  return {
    ...actual,
    api: {
      ...actual.api,
      getTickets: vi.fn(),
      getCategories: vi.fn(),
      createTicket: vi.fn(),
    },
  }
})

const getTickets = vi.mocked(api.getTickets)
const getCategories = vi.mocked(api.getCategories)
const createTicket = vi.mocked(api.createTicket)

const requester: User = {
  id: 'user-01',
  displayName: 'Aroha Ngata',
  department: 'Administration',
  role: 'Requester',
  isActive: true,
}

function ticket(overrides: Partial<TicketSummary> = {}): TicketSummary {
  return {
    id: 7,
    reference: 'TKT-000007',
    title: 'Docking station not detecting second monitor',
    category: 'Hardware',
    priority: 'Standard',
    priorityLabel: 'P3 Standard',
    status: 'InProgress',
    requesterName: 'Aroha Ngata',
    department: 'Administration',
    assignedTechnicianId: 'tech-raj',
    assignedTechnicianName: 'Raj Bhandari',
    isRestricted: false,
    createdAt: '2026-08-28T21:00:00Z',
    response: {
      state: 'Met',
      targetMinutes: 240,
      consumedMinutes: 12,
      remainingMinutes: 228,
      dueAt: '2026-08-29T01:00:00Z',
      percentConsumed: 5,
    },
    resolution: {
      state: 'OnTrack',
      targetMinutes: 2160,
      consumedMinutes: 300,
      remainingMinutes: 1860,
      dueAt: '2026-09-01T09:00:00Z',
      percentConsumed: 13.9,
    },
    ...overrides,
  }
}

describe('MyRequestsView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    getTickets.mockResolvedValue([ticket()])
    getCategories.mockResolvedValue(['Hardware', 'Printing', 'Network'])
  })

  it('shows the requester what they asked for and who has it', async () => {
    render(<MyRequestsView currentUser={requester} onOpen={vi.fn()} />)

    expect(await screen.findByText('TKT-000007')).toBeInTheDocument()
    expect(screen.getByText('Raj Bhandari')).toBeInTheDocument()
    expect(screen.getByText('On track')).toBeInTheDocument()
  })

  it('does not repeat the requester back to themselves', async () => {
    // Their own name in every row of their own list is noise, and it pushes the columns that carry
    // information off the edge on a narrow screen.
    render(<MyRequestsView currentUser={requester} onOpen={vi.fn()} />)

    await screen.findByText('TKT-000007')
    expect(screen.queryByRole('columnheader', { name: 'Raised by' })).not.toBeInTheDocument()
  })

  it('offers the category catalogue rather than free text', async () => {
    const user = userEvent.setup()

    render(<MyRequestsView currentUser={requester} onOpen={vi.fn()} />)

    await user.click(await screen.findByRole('button', { name: 'Raise a request' }))

    const categories = screen.getByRole('combobox', { name: /Category/ })
    expect(categories).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Printing' })).toBeInTheDocument()
  })

  it('will not submit an incomplete request', async () => {
    const user = userEvent.setup()

    render(<MyRequestsView currentUser={requester} onOpen={vi.fn()} />)

    await user.click(await screen.findByRole('button', { name: 'Raise a request' }))

    const submit = screen.getByRole('button', { name: 'Raise this request' })
    expect(submit).toBeDisabled()

    await user.type(screen.getByRole('textbox', { name: /What is the problem/ }), 'Printer jams')
    expect(submit).toBeDisabled()

    await user.selectOptions(screen.getByRole('combobox', { name: /Category/ }), 'Printing')
    expect(submit).toBeDisabled()

    await user.type(screen.getByRole('textbox', { name: /Tell us more/ }), 'Every duplex job.')
    expect(submit).toBeEnabled()
  })

  it('raises the request and gives back the reference', async () => {
    createTicket.mockResolvedValue(ticket({ id: 8, reference: 'TKT-000008', status: 'New' }))

    const user = userEvent.setup()

    render(<MyRequestsView currentUser={requester} onOpen={vi.fn()} />)

    await user.click(await screen.findByRole('button', { name: 'Raise a request' }))
    await user.type(screen.getByRole('textbox', { name: /What is the problem/ }), 'Printer jams')
    await user.selectOptions(screen.getByRole('combobox', { name: /Category/ }), 'Printing')
    await user.type(screen.getByRole('textbox', { name: /Tell us more/ }), 'Every duplex job.')
    await user.click(screen.getByRole('button', { name: 'Raise this request' }))

    await waitFor(() =>
      expect(createTicket).toHaveBeenCalledWith('user-01', {
        title: 'Printer jams',
        description: 'Every duplex job.',
        category: 'Printing',
        priority: 'Standard',
        markRestricted: false,
      }),
    )

    expect(await screen.findByText('TKT-000008')).toBeInTheDocument()
  })

  it('marks a request confidential when asked', async () => {
    createTicket.mockResolvedValue(ticket({ id: 9, reference: 'TKT-000009', isRestricted: true }))

    const user = userEvent.setup()

    render(<MyRequestsView currentUser={requester} onOpen={vi.fn()} />)

    await user.click(await screen.findByRole('button', { name: 'Raise a request' }))
    await user.type(screen.getByRole('textbox', { name: /What is the problem/ }), 'Payroll access')
    await user.selectOptions(screen.getByRole('combobox', { name: /Category/ }), 'Hardware')
    await user.type(screen.getByRole('textbox', { name: /Tell us more/ }), 'Sensitive.')
    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: 'Raise this request' }))

    await waitFor(() =>
      expect(createTicket).toHaveBeenCalledWith(
        'user-01',
        expect.objectContaining({ markRestricted: true }),
      ),
    )
  })

  it("shows the server's validation message rather than inventing one", async () => {
    createTicket.mockRejectedValue(
      new ApiError(400, {
        error: 'Invalid',
        message: 'The summary must be at least 5 characters so the queue is readable.',
      }),
    )

    const user = userEvent.setup()

    render(<MyRequestsView currentUser={requester} onOpen={vi.fn()} />)

    await user.click(await screen.findByRole('button', { name: 'Raise a request' }))
    await user.type(screen.getByRole('textbox', { name: /What is the problem/ }), 'Broken')
    await user.selectOptions(screen.getByRole('combobox', { name: /Category/ }), 'Hardware')
    await user.type(screen.getByRole('textbox', { name: /Tell us more/ }), 'Details.')
    await user.click(screen.getByRole('button', { name: 'Raise this request' }))

    expect(
      await screen.findByText(
        'The summary must be at least 5 characters so the queue is readable.',
      ),
    ).toBeInTheDocument()
  })

  it('flags requests waiting on the requester to confirm', async () => {
    getTickets.mockResolvedValue([ticket({ status: 'Resolved' })])

    render(<MyRequestsView currentUser={requester} onOpen={vi.fn()} />)

    expect(await screen.findByText('Awaiting your confirmation')).toBeInTheDocument()
    expect(screen.getByText('Needs you')).toBeInTheDocument()
  })
})

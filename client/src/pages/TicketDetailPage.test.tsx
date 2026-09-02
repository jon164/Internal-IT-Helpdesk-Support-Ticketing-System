import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from '../api/client'
import type { TicketDetail, TicketPermissions, User } from '../api/types'
import { TicketDetailPage } from './TicketDetailPage'

vi.mock('../api/client', async () => {
  const actual = await vi.importActual<typeof import('../api/client')>('../api/client')

  return {
    ...actual,
    api: {
      ...actual.api,
      getTicket: vi.fn(),
      getUsers: vi.fn(),
      transitionTicket: vi.fn(),
      assignTicket: vi.fn(),
      changeTicketPriority: vi.fn(),
      addTicketComment: vi.fn(),
    },
  }
})

const getTicket = vi.mocked(api.getTicket)
const getUsers = vi.mocked(api.getUsers)
const transitionTicket = vi.mocked(api.transitionTicket)
const assignTicket = vi.mocked(api.assignTicket)
const changeTicketPriority = vi.mocked(api.changeTicketPriority)
const addTicketComment = vi.mocked(api.addTicketComment)

const technician: User = {
  id: 'tech-nikau',
  displayName: 'Nikau Ashford',
  department: 'IT Support',
  role: 'Technician',
  isActive: true,
}

const requester: User = {
  id: 'user-01',
  displayName: 'Aroha Ngata',
  department: 'Administration',
  role: 'Requester',
  isActive: true,
}

function detail(
  permissions: Partial<TicketPermissions> = {},
  overrides: Partial<TicketDetail> = {},
): TicketDetail {
  return {
    summary: {
      id: 42,
      reference: 'TKT-000042',
      title: 'Level 2 printer jamming repeatedly',
      category: 'Printing',
      priority: 'Standard',
      priorityLabel: 'P3 Standard',
      status: 'Assigned',
      requesterName: 'Aroha Ngata',
      department: 'Administration',
      assignedTechnicianId: 'tech-nikau',
      assignedTechnicianName: 'Nikau Ashford',
      isRestricted: false,
      createdAt: '2026-08-28T21:00:00Z',
      response: {
        state: 'Met',
        targetMinutes: 240,
        consumedMinutes: 35,
        remainingMinutes: 205,
        dueAt: '2026-08-29T01:00:00Z',
        percentConsumed: 14.6,
      },
      resolution: {
        state: 'AtRisk',
        targetMinutes: 2160,
        consumedMinutes: 1700,
        remainingMinutes: 460,
        dueAt: '2026-09-01T09:00:00Z',
        percentConsumed: 78.7,
      },
    },
    description: 'Jams on every double-sided job.',
    resolutionNotes: null,
    firstRespondedAt: '2026-08-28T21:35:00Z',
    resolvedAt: null,
    closedAt: null,
    policy: {
      priority: 'Standard',
      displayName: 'P3 Standard',
      description: 'Degraded service with a workaround.',
      responseTargetMinutes: 240,
      resolutionTargetMinutes: 2160,
      clock: 'BusinessHours',
      warningThreshold: 0.75,
    },
    holdPeriods: [],
    events: [
      {
        id: 1,
        occurredAt: '2026-08-28T21:00:00Z',
        eventType: 'Created',
        actorName: 'Aroha Ngata',
        actorRole: 'Requester',
        fromStatus: null,
        toStatus: 'New',
        detail: 'Ticket raised.',
      },
      {
        id: 2,
        occurredAt: '2026-08-28T21:35:00Z',
        eventType: 'Assigned',
        actorName: 'Maia Thornton',
        actorRole: 'TeamLead',
        fromStatus: null,
        toStatus: null,
        detail: 'Assigned to Nikau Ashford.',
      },
    ],
    permissions: {
      canComment: true,
      canChangePriority: false,
      canAssignToSelf: false,
      canAssignToOthers: false,
      allowedNextStatuses: ['InProgress', 'OnHold', 'Cancelled'],
      ...permissions,
    },
    ...overrides,
  }
}

describe('TicketDetailPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    getTicket.mockResolvedValue(detail())
    getUsers.mockResolvedValue([])
  })

  it('shows the complete audit trail with who did what', async () => {
    render(<TicketDetailPage currentUser={technician} ticketId={42} onBack={vi.fn()} />)

    expect(await screen.findByText('History')).toBeInTheDocument()
    expect(screen.getByText('Ticket raised.')).toBeInTheDocument()
    expect(screen.getByText('Assigned to Nikau Ashford.')).toBeInTheDocument()
    expect(screen.getByText('Maia Thornton · Service manager')).toBeInTheDocument()
  })

  it('reports each SLA clock with a word, not only a colour', async () => {
    render(<TicketDetailPage currentUser={technician} ticketId={42} onBack={vi.fn()} />)

    expect(await screen.findByText('At risk')).toBeInTheDocument()
    expect(screen.getByText('Met')).toBeInTheDocument()
    expect(screen.getByText('7h 40m left')).toBeInTheDocument()
  })

  it('offers only the actions the server permitted', async () => {
    render(<TicketDetailPage currentUser={technician} ticketId={42} onBack={vi.fn()} />)

    expect(await screen.findByRole('button', { name: 'Start work' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Place on hold' })).toBeInTheDocument()
    // Not permitted for this caller, so the control is absent rather than present and failing.
    expect(screen.queryByRole('button', { name: 'Assign to me' })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Justification')).not.toBeInTheDocument()
  })

  it('refuses to resolve until resolution notes are written', async () => {
    const user = userEvent.setup()

    render(<TicketDetailPage currentUser={technician} ticketId={42} onBack={vi.fn()} />)

    await user.click(await screen.findByRole('button', { name: 'Place on hold' }))

    const confirm = screen.getByRole('button', { name: 'Confirm: Place on hold' })
    expect(confirm).toBeDisabled()

    await user.type(screen.getByRole('textbox', { name: /Reason for the hold/ }), 'Awaiting parts.')
    expect(confirm).toBeEnabled()

    await user.click(confirm)

    await waitFor(() =>
      expect(transitionTicket).toHaveBeenCalledWith(
        'tech-nikau',
        42,
        'OnHold',
        'Awaiting parts.',
      ),
    )
  })

  it('shows the server refusal verbatim when an action is rejected', async () => {
    transitionTicket.mockRejectedValue(
      new ApiError(403, {
        error: 'Forbidden',
        message: 'Only the assigned technician can start work on this ticket.',
      }),
    )

    const user = userEvent.setup()

    render(<TicketDetailPage currentUser={technician} ticketId={42} onBack={vi.fn()} />)

    await user.click(await screen.findByRole('button', { name: 'Start work' }))
    await user.click(screen.getByRole('button', { name: 'Confirm: Start work' }))

    expect(
      await screen.findByText('Only the assigned technician can start work on this ticket.'),
    ).toBeInTheDocument()
  })

  it('requires a justification before a priority change', async () => {
    getTicket.mockResolvedValue(detail({ canChangePriority: true }))

    const user = userEvent.setup()

    render(<TicketDetailPage currentUser={technician} ticketId={42} onBack={vi.fn()} />)

    await user.selectOptions(await screen.findByLabelText('Priority'), 'High')

    const apply = screen.getByRole('button', { name: 'Apply' })
    expect(apply).toBeDisabled()

    await user.type(screen.getByLabelText('Justification'), 'Now affecting the whole floor.')
    expect(apply).toBeEnabled()

    await user.click(apply)

    await waitFor(() =>
      expect(changeTicketPriority).toHaveBeenCalledWith(
        'tech-nikau',
        42,
        'High',
        'Now affecting the whole floor.',
      ),
    )
  })

  it('lets a technician claim a ticket nobody holds', async () => {
    getTicket.mockResolvedValue(
      detail(
        { canAssignToSelf: true },
        {
          summary: {
            ...detail().summary,
            status: 'Triaged',
            assignedTechnicianId: null,
            assignedTechnicianName: null,
          },
        },
      ),
    )

    const user = userEvent.setup()

    render(<TicketDetailPage currentUser={technician} ticketId={42} onBack={vi.fn()} />)

    await user.click(await screen.findByRole('button', { name: 'Assign to me' }))

    await waitFor(() =>
      expect(assignTicket).toHaveBeenCalledWith('tech-nikau', 42, 'tech-nikau'),
    )
  })

  it('does not offer to assign a ticket to the person who already holds it', async () => {
    // The server permits it and it would succeed, but it would change nothing. A control that does
    // nothing teaches the reader to distrust the rest of them.
    getTicket.mockResolvedValue(detail({ canAssignToSelf: true }))

    render(<TicketDetailPage currentUser={technician} ticketId={42} onBack={vi.fn()} />)

    await screen.findByText('What you can do')
    expect(screen.queryByRole('button', { name: 'Assign to me' })).not.toBeInTheDocument()
  })

  it('posts a comment and says when it will stop the response clock', async () => {
    getTicket.mockResolvedValue(detail({}, { firstRespondedAt: null }))

    const user = userEvent.setup()

    render(<TicketDetailPage currentUser={technician} ticketId={42} onBack={vi.fn()} />)

    expect(
      await screen.findByText(
        'This will be the first response from support, so it stops the response clock.',
      ),
    ).toBeInTheDocument()

    await user.type(screen.getByLabelText('Comment'), 'Looking at it now.')
    await user.click(screen.getByRole('button', { name: 'Post comment' }))

    await waitFor(() =>
      expect(addTicketComment).toHaveBeenCalledWith('tech-nikau', 42, 'Looking at it now.'),
    )
  })

  it('names the requester\'s two options in their terms, not the workflow\'s', async () => {
    // "Start work" and "Close" are what the state machine calls these. To the person who raised the
    // ticket they mean "the fix did not hold" and "yes, that fixed it".
    getTicket.mockResolvedValue(
      detail(
        { canComment: true, allowedNextStatuses: ['InProgress', 'Closed'] },
        { summary: { ...detail().summary, status: 'Resolved' } },
      ),
    )

    render(<TicketDetailPage currentUser={requester} ticketId={42} onBack={vi.fn()} />)

    expect(await screen.findByRole('button', { name: 'Reopen' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirm the fix' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Start work' })).not.toBeInTheDocument()
  })

  it('does not print raw enum names beside the readable transition', async () => {
    // The stored detail is "InProgress -> Resolved. <notes>". The header renders the transition;
    // printing the prefix again puts enum names on screen. The notes must survive the trim.
    getTicket.mockResolvedValue(
      detail(
        {},
        {
          events: [
            {
              id: 3,
              occurredAt: '2026-08-29T02:00:00Z',
              eventType: 'StatusChanged',
              actorName: 'Nikau Ashford',
              actorRole: 'Technician',
              fromStatus: 'InProgress',
              toStatus: 'Resolved',
              detail: 'InProgress -> Resolved. Replaced the dock.',
            },
          ],
        },
      ),
    )

    render(<TicketDetailPage currentUser={technician} ticketId={42} onBack={vi.fn()} />)

    expect(await screen.findByText('Replaced the dock.')).toBeInTheDocument()
    expect(screen.getByText('In progress → Resolved')).toBeInTheDocument()
    expect(screen.queryByText(/InProgress -> Resolved/)).not.toBeInTheDocument()
  })

  it('shows a requester the record even when there is nothing for them to do', async () => {
    // The audit trail is not reserved for staff. A requester being able to see who has their request
    // and what has happened to it is the point of replacing the shared mailbox.
    getTicket.mockResolvedValue(
      detail({
        canComment: false,
        allowedNextStatuses: [],
      }),
    )

    render(<TicketDetailPage currentUser={requester} ticketId={42} onBack={vi.fn()} />)

    expect(await screen.findByText('Ticket raised.')).toBeInTheDocument()
    expect(screen.getByText(/there is nothing further/)).toBeInTheDocument()
  })

  it('shows the server refusal rather than an empty page when the ticket is not visible', async () => {
    // A restricted ticket a technician does not hold answers 404, the same as a ticket that does not
    // exist — the refusal must not disclose that it is there.
    getTicket.mockRejectedValue(
      new ApiError(404, { error: 'NotFound', message: 'No such ticket.' }),
    )

    render(<TicketDetailPage currentUser={technician} ticketId={42} onBack={vi.fn()} />)

    expect(await screen.findByText('No such ticket.')).toBeInTheDocument()
    expect(screen.queryByText('History')).not.toBeInTheDocument()
  })
})

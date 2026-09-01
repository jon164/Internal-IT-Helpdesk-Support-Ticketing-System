import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from '../api/client'
import type { User, UserAuditEntry } from '../api/types'
import { AdminView } from './AdminView'

vi.mock('../api/client', async () => {
  const actual = await vi.importActual<typeof import('../api/client')>('../api/client')

  return {
    ...actual,
    api: {
      ...actual.api,
      getAdminUsers: vi.fn(),
      getUserAudit: vi.fn(),
      changeUserRole: vi.fn(),
      setUserActive: vi.fn(),
    },
  }
})

const getAdminUsers = vi.mocked(api.getAdminUsers)
const getUserAudit = vi.mocked(api.getUserAudit)
const changeUserRole = vi.mocked(api.changeUserRole)
const setUserActive = vi.mocked(api.setUserActive)

/** The service manager holds account administration; there is no separate administrator role. */
const manager: User = {
  id: 'lead-maia',
  displayName: 'Maia Thornton',
  department: 'IT Support',
  role: 'TeamLead',
  isActive: true,
}

const technician: User = {
  id: 'tech-raj',
  displayName: 'Raj Bhandari',
  department: 'IT Support',
  role: 'Technician',
  isActive: true,
}

const accounts: User[] = [
  manager,
  {
    id: 'tech-nikau',
    displayName: 'Nikau Ashford',
    department: 'IT Support',
    role: 'Technician',
    isActive: true,
  },
  {
    id: 'user-05',
    displayName: 'Emma Tuilagi',
    department: 'Human Resources',
    role: 'Requester',
    isActive: false,
  },
]

const auditEntries: UserAuditEntry[] = [
  {
    id: 2,
    occurredAt: '2026-08-29T09:00:00Z',
    eventType: 'Deactivated',
    subjectUserId: 'user-05',
    subjectDisplayName: 'Emma Tuilagi',
    actorDisplayName: 'Maia Thornton',
    fromRole: null,
    toRole: null,
    reason: 'Left the organisation.',
  },
]

describe('AdminView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    getAdminUsers.mockResolvedValue(accounts)
    getUserAudit.mockResolvedValue(auditEntries)
  })

  it('lists every account, including deactivated ones', async () => {
    render(<AdminView currentUser={manager} />)

    expect(await screen.findByText('Maia Thornton')).toBeInTheDocument()
    // A deactivated account stays visible — it is still part of the record.
    expect(screen.getByText('Emma Tuilagi')).toBeInTheDocument()
    expect(screen.getByText('Inactive')).toBeInTheDocument()
  })

  it('refuses a caller who is not the service manager', async () => {
    getAdminUsers.mockRejectedValue(
      new ApiError(403, {
        error: 'Forbidden',
        message: 'Account administration is restricted to the service manager.',
      }),
    )

    render(<AdminView currentUser={technician} />)

    expect(await screen.findByText('Not available to your role')).toBeInTheDocument()
    expect(screen.queryByText('Nikau Ashford')).not.toBeInTheDocument()
  })

  it('prevents the service manager from editing their own account', async () => {
    // Self-demotion cannot be undone by the person who did it, so the control is closed off in the
    // interface as well as refused on the server.
    render(<AdminView currentUser={manager} />)

    const row = (await screen.findByText('Maia Thornton')).closest('tr')
    expect(row).not.toBeNull()

    expect(within(row!).getByRole('combobox')).toBeDisabled()
    expect(within(row!).getByRole('button', { name: 'Deactivate' })).toBeDisabled()
    expect(within(row!).getByText('you')).toBeInTheDocument()
  })

  it('requires a reason before a role change can be applied', async () => {
    const user = userEvent.setup()

    render(<AdminView currentUser={manager} />)

    const row = (await screen.findByText('Nikau Ashford')).closest('tr')
    await user.selectOptions(within(row!).getByRole('combobox'), 'TeamLead')

    const apply = screen.getByRole('button', { name: 'Apply change' })
    expect(apply).toBeDisabled()

    await user.type(screen.getByRole('textbox'), 'Covering the service manager on leave.')
    expect(apply).toBeEnabled()
  })

  it('sends the reason with the role change', async () => {
    changeUserRole.mockResolvedValue({ ...accounts[1], role: 'TeamLead' })
    const user = userEvent.setup()

    render(<AdminView currentUser={manager} />)

    const row = (await screen.findByText('Nikau Ashford')).closest('tr')
    await user.selectOptions(within(row!).getByRole('combobox'), 'TeamLead')
    await user.type(screen.getByRole('textbox'), 'Covering the service manager on leave.')
    await user.click(screen.getByRole('button', { name: 'Apply change' }))

    await waitFor(() =>
      expect(changeUserRole).toHaveBeenCalledWith(
        'lead-maia',
        'tech-nikau',
        'TeamLead',
        'Covering the service manager on leave.',
      ),
    )
  })

  it('shows the server refusal verbatim when a change is rejected', async () => {
    // The last-service-manager guard lives on the server. The client must report what it said rather
    // than guessing at its own explanation.
    changeUserRole.mockRejectedValue(
      new ApiError(409, {
        error: 'Conflict',
        message: 'This is the only active service manager. Promote somebody else first.',
      }),
    )

    const user = userEvent.setup()

    render(<AdminView currentUser={manager} />)

    const row = (await screen.findByText('Nikau Ashford')).closest('tr')
    await user.selectOptions(within(row!).getByRole('combobox'), 'Requester')
    await user.type(screen.getByRole('textbox'), 'Testing the guard.')
    await user.click(screen.getByRole('button', { name: 'Apply change' }))

    expect(
      await screen.findByText(
        'This is the only active service manager. Promote somebody else first.',
      ),
    ).toBeInTheDocument()
  })

  it('reactivates a deactivated account through the same reasoned flow', async () => {
    setUserActive.mockResolvedValue({ ...accounts[2], isActive: true })
    const user = userEvent.setup()

    render(<AdminView currentUser={manager} />)

    const row = (await screen.findByText('Emma Tuilagi')).closest('tr')
    await user.click(within(row!).getByRole('button', { name: 'Reactivate' }))
    await user.type(screen.getByRole('textbox'), 'Returned from secondment.')
    await user.click(screen.getByRole('button', { name: 'Apply change' }))

    await waitFor(() =>
      expect(setUserActive).toHaveBeenCalledWith(
        'lead-maia',
        'user-05',
        true,
        'Returned from secondment.',
      ),
    )
  })

  it('shows the account change history with its reasons', async () => {
    render(<AdminView currentUser={manager} />)

    expect(await screen.findByText('Account change history')).toBeInTheDocument()
    expect(screen.getByText('Deactivated')).toBeInTheDocument()
    expect(screen.getByText('Left the organisation.')).toBeInTheDocument()
  })

  it('warns when only one service manager account is active', async () => {
    render(<AdminView currentUser={manager} />)

    expect(await screen.findByText('Single point of failure')).toBeInTheDocument()
  })

  it('does not warn once a second service manager exists', async () => {
    getAdminUsers.mockResolvedValue([
      ...accounts,
      {
        id: 'lead-second',
        displayName: 'Second Manager',
        department: 'IT Support',
        role: 'TeamLead',
        isActive: true,
      },
    ])

    render(<AdminView currentUser={manager} />)

    await screen.findByText('Service managers')
    expect(screen.queryByText('Single point of failure')).not.toBeInTheDocument()
  })
})

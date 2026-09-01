import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from '../api/client'
import type { User } from '../api/types'
import { LoginPage } from './LoginPage'

vi.mock('../api/client', async () => {
  const actual = await vi.importActual<typeof import('../api/client')>('../api/client')

  return {
    ...actual,
    api: { ...actual.api, getUsers: vi.fn(), signIn: vi.fn() },
  }
})

const getUsers = vi.mocked(api.getUsers)
const signIn = vi.mocked(api.signIn)

const accounts: User[] = [
  {
    id: 'lead-maia',
    displayName: 'Maia Thornton',
    department: 'IT Support',
    role: 'TeamLead',
    isActive: true,
  },
  {
    id: 'user-01',
    displayName: 'Aroha Ngata',
    department: 'Administration',
    role: 'Requester',
    isActive: true,
  },
]

describe('LoginPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    getUsers.mockResolvedValue(accounts)
  })

  it('states plainly that no password is verified', async () => {
    // The notice is load-bearing. A demonstration where the audience assumes a credential was
    // checked is worse than having no sign-in screen at all.
    render(<LoginPage onSignedIn={vi.fn()} />)

    expect(
      await screen.findByText(/no password is required/i),
    ).toBeInTheDocument()
    expect(screen.getByText(/does not verify a credential/i)).toBeInTheDocument()
  })

  it('offers no password field, rather than one that is ignored', async () => {
    render(<LoginPage onSignedIn={vi.fn()} />)

    await screen.findByRole('combobox')
    expect(document.querySelector('input[type="password"]')).toBeNull()
  })

  it('groups the accounts by role', async () => {
    render(<LoginPage onSignedIn={vi.fn()} />)

    await screen.findByRole('combobox')

    const groups = Array.from(document.querySelectorAll('optgroup')).map((g) => g.label)
    expect(groups).toEqual(['Service manager', 'Requester'])
  })

  it('signs in and hands the account up to the shell', async () => {
    signIn.mockResolvedValue(accounts[0])
    const onSignedIn = vi.fn()
    const user = userEvent.setup()

    render(<LoginPage onSignedIn={onSignedIn} />)

    await user.selectOptions(await screen.findByRole('combobox'), 'lead-maia')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    await waitFor(() => expect(signIn).toHaveBeenCalledWith('lead-maia'))
    expect(onSignedIn).toHaveBeenCalledWith(accounts[0])
  })

  it('reports the server refusal for an account that cannot sign in', async () => {
    signIn.mockRejectedValue(
      new ApiError(401, {
        error: 'Unauthenticated',
        message: 'That account cannot sign in.',
      }),
    )

    const onSignedIn = vi.fn()
    const user = userEvent.setup()

    render(<LoginPage onSignedIn={onSignedIn} />)

    await screen.findByRole('combobox')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('That account cannot sign in.')).toBeInTheDocument()
    expect(onSignedIn).not.toHaveBeenCalled()
  })

  it('explains what to do when the API is unreachable', async () => {
    getUsers.mockRejectedValue(new Error('network'))

    render(<LoginPage onSignedIn={vi.fn()} />)

    expect(await screen.findByText(/Could not reach the API/)).toBeInTheDocument()
  })
})

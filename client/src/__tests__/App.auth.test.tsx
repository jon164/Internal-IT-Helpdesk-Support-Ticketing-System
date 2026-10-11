import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import { setSessionToken } from '../services/api'
import { SESSION_TOKEN_KEY } from '../services/auth'
import { installFakeServer, MANAGER, STAFF } from '../test/fakeServer'

beforeEach(() => {
  setSessionToken(null)
  vi.spyOn(console, 'debug').mockImplementation(() => {})
})

function loginReply(user: typeof MANAGER) {
  return { status: 200, body: { token: `token-for-${user.id}`, expiresAtUtc: '2026-10-12T17:00:00Z', user } }
}

describe('App sign-in journey — reliability and security', () => {
  it('AUTH-C21 signs in, lands the manager on the dashboard, and keeps no role in localStorage', async () => {
    const user = userEvent.setup()
    installFakeServer(call => (call.url.endsWith('/api/auth/login') ? loginReply(MANAGER) : undefined))
    render(<App />)

    await user.type(screen.getByLabelText('Username'), 'it-manager')
    await user.type(screen.getByLabelText('Password'), 'Manager#Demo2026{Enter}')

    expect(await screen.findByText('Service health dashboard')).toBeInTheDocument()
    expect(sessionStorage.getItem(SESSION_TOKEN_KEY)).toBe('token-for-it-manager')
    expect(localStorage.getItem('airport-helpdesk-role')).toBeNull()
  })

  it('AUTH-C22 offers no control for changing your own role once signed in', async () => {
    const user = userEvent.setup()
    installFakeServer(call => (call.url.endsWith('/api/auth/login') ? loginReply(STAFF) : undefined))
    render(<App />)

    await user.type(screen.getByLabelText('Username'), 'airport-staff')
    await user.type(screen.getByLabelText('Password'), 'Staff#Demo2026{Enter}')

    await screen.findByRole('button', { name: 'Sign out' })
    expect(screen.queryByLabelText(/demo role/i)).not.toBeInTheDocument()
    expect(screen.getAllByText(/airport-staff/).length).toBeGreaterThan(0)
  })

  it('AUTH-C23 restores the session after a refresh by asking the server who the token belongs to', async () => {
    sessionStorage.setItem(SESSION_TOKEN_KEY, 'stored-token')
    const { calls } = installFakeServer(call => (call.url.endsWith('/api/auth/me') ? { status: 200, body: MANAGER } : undefined))

    render(<App />)

    expect(screen.getByRole('status')).toHaveTextContent('Checking your session')
    expect(await screen.findByText('Service health dashboard')).toBeInTheDocument()
    expect(calls.find(c => c.url.endsWith('/api/auth/me'))!.headers.get('Authorization')).toBe('Bearer stored-token')
  })

  it('AUTH-C24 returns to sign-in, and forgets the token, when a stored session has expired', async () => {
    sessionStorage.setItem(SESSION_TOKEN_KEY, 'expired-token')
    installFakeServer(call => (call.url.endsWith('/api/auth/me') ? { status: 401, body: { message: 'Sign in to continue.' } } : undefined))

    render(<App />)

    expect(await screen.findByLabelText('Username')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Your session has ended')
    expect(sessionStorage.getItem(SESSION_TOKEN_KEY)).toBeNull()
  })

  it('AUTH-C25 signs out on the server as well as in the browser', async () => {
    const user = userEvent.setup()
    const { calls } = installFakeServer(call => {
      if (call.url.endsWith('/api/auth/login')) return loginReply(MANAGER)
      if (call.url.endsWith('/api/auth/logout')) return { status: 204 }
      return undefined
    })
    render(<App />)
    await user.type(screen.getByLabelText('Username'), 'it-manager')
    await user.type(screen.getByLabelText('Password'), 'x{Enter}')

    await user.click(await screen.findByRole('button', { name: 'Sign out' }))

    const logout = calls.find(c => c.url.endsWith('/api/auth/logout'))
    expect(logout?.method).toBe('POST')
    expect(logout?.headers.get('Authorization')).toBe('Bearer token-for-it-manager')
    expect(await screen.findByRole('status')).toHaveTextContent('You have signed out.')
    expect(sessionStorage.getItem(SESSION_TOKEN_KEY)).toBeNull()
  })

  it('AUTH-C26 still signs the user out locally when the server cannot be reached', async () => {
    const user = userEvent.setup()
    installFakeServer(call => {
      if (call.url.endsWith('/api/auth/login')) return loginReply(MANAGER)
      if (call.url.endsWith('/api/auth/logout')) return { status: 503, body: { message: 'down' } }
      return undefined
    })
    render(<App />)
    await user.type(screen.getByLabelText('Username'), 'it-manager')
    await user.type(screen.getByLabelText('Password'), 'x{Enter}')

    await user.click(await screen.findByRole('button', { name: 'Sign out' }))

    expect(await screen.findByLabelText('Username')).toBeInTheDocument()
    expect(sessionStorage.getItem(SESSION_TOKEN_KEY)).toBeNull()
  })
})

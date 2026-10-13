import { afterEach, describe, expect, it, vi } from 'vitest'
import { api, onUnauthorized, setSessionToken } from '../services/api'
import { describeSignInError, readStoredToken, SESSION_TOKEN_KEY, storeToken, toClientRole } from '../services/auth'
import { ApiError } from '../services/api'
import { installFakeServer, MANAGER } from '../test/fakeServer'

afterEach(() => {
  setSessionToken(null)
  onUnauthorized(null)
})

describe('API client — security', () => {
  it('AUTH-C13 posts credentials to /api/auth/login and never writes the password to the console', async () => {
    const { calls } = installFakeServer(call =>
      call.url.endsWith('/api/auth/login') ? { status: 200, body: { token: 't', expiresAtUtc: '2026-10-12T17:00:00Z', user: MANAGER } } : undefined)
    const spies = (['log', 'debug', 'info', 'warn', 'error'] as const).map(name => vi.spyOn(console, name).mockImplementation(() => {}))

    await api.login('it-manager', 'Sup3r-Secret!')

    expect(calls[0].method).toBe('POST')
    expect(JSON.parse(calls[0].body!)).toEqual({ username: 'it-manager', password: 'Sup3r-Secret!' })
    const logged = JSON.stringify(spies.flatMap(spy => spy.mock.calls))
    expect(logged).not.toContain('Sup3r-Secret!')
  })

  it('AUTH-C14 sends the session as a Bearer token and never the old X-User-Id header', async () => {
    const { calls } = installFakeServer(() => undefined)
    vi.spyOn(console, 'debug').mockImplementation(() => {})
    setSessionToken('abc123')

    await api.getDashboard('IT Manager')

    expect(calls[0].headers.get('Authorization')).toBe('Bearer abc123')
    expect(calls[0].headers.has('X-User-Id')).toBe(false)
  })

  it('AUTH-C15 sends no Authorization header when nobody is signed in', async () => {
    const { calls } = installFakeServer(() => undefined)
    vi.spyOn(console, 'debug').mockImplementation(() => {})

    await api.getTickets('newest')

    expect(calls[0].headers.has('Authorization')).toBe(false)
  })

  it('AUTH-C16 reports an expired session (401 mid-session) but not a failed sign-in', async () => {
    vi.spyOn(console, 'debug').mockImplementation(() => {})
    const handler = vi.fn()
    onUnauthorized(handler)
    installFakeServer(() => ({ status: 401, body: { message: 'Sign in to continue.' } }))

    await expect(api.login('x', 'y')).rejects.toBeInstanceOf(ApiError)
    expect(handler).not.toHaveBeenCalled()

    setSessionToken('expired')
    await expect(api.getDashboard('IT Manager')).rejects.toMatchObject({ status: 401 })
    expect(handler).toHaveBeenCalledTimes(1)
  })

  it('AUTH-C17 maps server roles to UI roles, defaulting anything unknown to the least privileged', () => {
    expect(toClientRole('Requester')).toBe('Airport Staff')
    expect(toClientRole('Technician')).toBe('IT Technician')
    expect(toClientRole('TeamLead')).toBe('IT Manager')
    expect(toClientRole('Administrator')).toBe('Airport Staff')
  })
})

describe('Session storage — reliability', () => {
  it('AUTH-C18 keeps the token in sessionStorage (cleared when the tab closes), not localStorage', () => {
    storeToken('tok')
    expect(sessionStorage.getItem(SESSION_TOKEN_KEY)).toBe('tok')
    expect(localStorage.getItem(SESSION_TOKEN_KEY)).toBeNull()
    storeToken(null)
    expect(readStoredToken()).toBeNull()
  })

  it('AUTH-C19 still works when browser storage throws (private mode, blocked storage)', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked') })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked') })

    expect(() => storeToken('tok')).not.toThrow()
    expect(readStoredToken()).toBeNull()
  })

  it('AUTH-C20 gives a specific message for each failure class', () => {
    expect(describeSignInError(new ApiError('ignored', 401))).toBe('Username or password is incorrect.')
    expect(describeSignInError(new ApiError('Too many failed attempts. Try again in 3 minutes.', 429))).toContain('3 minutes')
    expect(describeSignInError(new ApiError('boom', 500))).toContain('server had a problem')
    expect(describeSignInError(new TypeError('Failed to fetch'))).toContain("Can't reach")
  })
})

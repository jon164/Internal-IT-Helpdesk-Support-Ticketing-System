import { ApiError } from './api'
import type { Role, ServerRole } from '../types'

const ROLE_BY_SERVER_ROLE: Record<ServerRole, Role> = {
  Requester: 'Airport Staff',
  Technician: 'IT Technician',
  TeamLead: 'IT Manager',
}

/** Maps the server's role name to the label the UI uses. Unknown values get the least-privileged role. */
export function toClientRole(serverRole: string): Role {
  return ROLE_BY_SERVER_ROLE[serverRole as ServerRole] ?? 'Airport Staff'
}

export const SESSION_TOKEN_KEY = 'airport-helpdesk-session-token'

/** sessionStorage can throw (private mode, blocked storage); signing in must still work without it. */
export function readStoredToken(): string | null {
  try {
    return sessionStorage.getItem(SESSION_TOKEN_KEY)
  } catch {
    return null
  }
}

export function storeToken(token: string | null) {
  try {
    if (token) sessionStorage.setItem(SESSION_TOKEN_KEY, token)
    else sessionStorage.removeItem(SESSION_TOKEN_KEY)
  } catch {
    // The session still works for this page; it just will not survive a refresh.
  }
}

/**
 * Demo accounts for local testing. The passwords must match
 * src/Helpdesk.Api/appsettings.Development.json (a test checks this). They work only when the
 * API runs in Development; Production has no passwords configured, so these grant nothing there.
 */
type DemoAccount = { username: string; password: string; role: string }

// The literal sits behind import.meta.env.DEV, which Vite replaces with `false` in a production
// build, so the passwords are removed from the production bundle entirely.
export const DEMO_ACCOUNTS: readonly DemoAccount[] = import.meta.env.DEV
  ? [
      { username: 'airport-staff', password: 'Staff#Demo2026', role: 'Airport Staff' },
      { username: 'it-technician', password: 'Tech#Demo2026', role: 'IT Technician' },
      { username: 'it-manager', password: 'Manager#Demo2026', role: 'IT Manager' },
    ]
  : []

export const DEMO_USERNAMES = ['airport-staff', 'it-technician', 'it-manager'] as const

export function describeSignInError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) return 'Username or password is incorrect.'
    if (error.status === 429 || error.status === 400) return error.message
    if (error.status >= 500) return 'The help desk server had a problem. Try again in a moment.'
    return error.message
  }
  return "Can't reach the help desk server. Check that it is running and try again."
}

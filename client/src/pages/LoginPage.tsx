import { useEffect, useState, type FormEvent } from 'react'
import { ApiError, api } from '../api/client'
import type { User } from '../api/types'
import './login.css'

const ROLE_LABEL: Record<User['role'], string> = {
  Requester: 'Requester',
  Technician: 'Technician',
  TeamLead: 'Service manager',
}

/**
 * Sign-in screen.
 *
 * **This screen does not authenticate anybody.** It establishes which account the caller is acting
 * as; the server then decides what that account may do, reading the role from the database record
 * rather than from anything the client sends. That distinction is what makes the authorisation
 * demonstrable even though the authentication is not real, and it is stated plainly on the screen so
 * that nobody watching a demonstration is misled about what they are seeing.
 *
 * Replacing this with real authentication means adding a credential to the sign-in call and a check
 * in its handler. No authorisation rule changes.
 */
export function LoginPage({ onSignedIn }: { onSignedIn: (user: User) => void }) {
  const [users, setUsers] = useState<User[]>([])
  const [selected, setSelected] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    let cancelled = false

    api
      .getUsers()
      .then((loaded) => {
        if (cancelled) return

        setUsers(loaded)
        setSelected(loaded.find((u) => u.role === 'TeamLead')?.id ?? loaded[0]?.id ?? '')
        setLoading(false)
      })
      .catch(() => {
        if (cancelled) return

        setError('Could not reach the API. Start it with: dotnet run --project src/Helpdesk.Api')
        setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [])

  const submit = async (event: FormEvent) => {
    event.preventDefault()

    if (!selected) {
      return
    }

    setSubmitting(true)
    setError(null)

    try {
      onSignedIn(await api.signIn(selected))
    } catch (caught) {
      setError(
        caught instanceof ApiError ? caught.message : 'Sign-in failed. Is the API running?',
      )
      setSubmitting(false)
    }
  }

  const grouped = groupByRole(users)

  return (
    <main className="login">
      <form className="login-card" onSubmit={submit}>
        <div className="login-brand">
          <span className="app-mark" aria-hidden="true" />
          <div>
            <strong>IT Support Desk</strong>
            <span className="app-context">Corporate systems</span>
          </div>
        </div>

        <h1>Sign in</h1>

        <label className="login-field">
          <span>Account</span>
          <select
            value={selected}
            onChange={(event) => setSelected(event.target.value)}
            disabled={loading || users.length === 0}
          >
            {grouped.map(([role, members]) => (
              <optgroup key={role} label={ROLE_LABEL[role]}>
                {members.map((user) => (
                  <option key={user.id} value={user.id}>
                    {user.displayName} — {user.department}
                  </option>
                ))}
              </optgroup>
            ))}
          </select>
        </label>

        {error && (
          <p className="login-error" role="alert">
            {error}
          </p>
        )}

        <button type="submit" className="login-submit" disabled={loading || submitting || !selected}>
          {submitting ? 'Signing in…' : 'Sign in'}
        </button>

        {/*
          Stated on the screen rather than buried in documentation. A demonstration where the
          audience assumes a password was checked is worse than no login screen at all.
        */}
        <aside className="login-notice">
          <strong>Prototype build — no password is required.</strong>
          <p>
            This screen selects an identity; it does not verify a credential. What it does establish
            is real: the server reads each account's role from its own records and enforces every
            permission itself, so a signed-in technician genuinely cannot reach the management
            reports or another department's restricted tickets.
          </p>
          <p>Authentication is recorded as out of scope in the project's problem definition.</p>
        </aside>
      </form>
    </main>
  )
}

/** Groups accounts by role, in a fixed order, so the picker is navigable at 25 accounts. */
function groupByRole(users: User[]): Array<[User['role'], User[]]> {
  const order: Array<User['role']> = ['TeamLead', 'Technician', 'Requester']

  return order
    .map((role) => [role, users.filter((u) => u.role === role)] as [User['role'], User[]])
    .filter(([, members]) => members.length > 0)
}

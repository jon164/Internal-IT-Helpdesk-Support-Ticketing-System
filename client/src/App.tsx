import { useCallback, useEffect, useState } from 'react'
import { api } from './api/client'
import type { User } from './api/types'
import { DashboardPage } from './pages/DashboardPage'
import { LoginPage } from './pages/LoginPage'
import './App.css'

const SESSION_KEY = 'helpdesk.session.userId'

const ROLE_LABEL: Record<User['role'], string> = {
  Requester: 'Requester',
  Technician: 'Technician',
  TeamLead: 'Service manager',
}

/** Reads the remembered account id, tolerating browsers where storage is unavailable. */
function readStoredUserId(): string | null {
  try {
    return window.localStorage.getItem(SESSION_KEY)
  } catch {
    return null
  }
}

function writeStoredUserId(userId: string | null): void {
  try {
    if (userId === null) {
      window.localStorage.removeItem(SESSION_KEY)
    } else {
      window.localStorage.setItem(SESSION_KEY, userId)
    }
  } catch {
    // A browser with storage disabled simply forgets the session on refresh. Not worth failing over.
  }
}

/**
 * Application shell.
 *
 * A remembered session is re-established through the sign-in endpoint rather than trusted from
 * storage. That matters: an account deactivated since the last visit is rejected on the way back in,
 * so revoking access takes effect on the next page load rather than whenever the browser happens to
 * forget. The stored value is only an account identifier — no role and no privilege is cached.
 */
export default function App() {
  const [user, setUser] = useState<User | null>(null)
  const [restoring, setRestoring] = useState(true)

  useEffect(() => {
    const storedId = readStoredUserId()

    if (!storedId) {
      setRestoring(false)
      return
    }

    let cancelled = false

    api
      .signIn(storedId)
      .then((restored) => {
        if (!cancelled) {
          setUser(restored)
        }
      })
      .catch(() => {
        // Unknown, deactivated, or the API is down. Either way, back to the sign-in screen.
        writeStoredUserId(null)
      })
      .finally(() => {
        if (!cancelled) {
          setRestoring(false)
        }
      })

    return () => {
      cancelled = true
    }
  }, [])

  const signIn = useCallback((signedIn: User) => {
    writeStoredUserId(signedIn.id)
    setUser(signedIn)
  }, [])

  const signOut = useCallback(() => {
    writeStoredUserId(null)
    setUser(null)
  }, [])

  if (restoring) {
    return (
      <main className="login">
        <p className="dashboard-message">Restoring your session…</p>
      </main>
    )
  }

  if (!user) {
    return <LoginPage onSignedIn={signIn} />
  }

  return (
    <div className="app">
      <header className="app-bar">
        <div className="app-identity">
          <span className="app-mark" aria-hidden="true" />
          <div>
            <strong>IT Support Desk</strong>
            <span className="app-context">Corporate systems</span>
          </div>
        </div>

        <div className="app-session">
          <div className="app-user">
            <strong>{user.displayName}</strong>
            <span className="app-context">{ROLE_LABEL[user.role]}</span>
          </div>
          <button type="button" className="link-button" onClick={signOut}>
            Sign out
          </button>
        </div>
      </header>

      <main className="app-main">
        <DashboardPage currentUser={user} />
      </main>
    </div>
  )
}

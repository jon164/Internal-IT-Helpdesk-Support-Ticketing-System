import { useEffect, useState } from 'react'
import { api } from './api/client'
import type { User } from './api/types'
import { DashboardPage } from './pages/DashboardPage'
import './App.css'

/**
 * Application shell.
 *
 * The role switcher is a prototype affordance, not authentication. It changes which identity the
 * client claims; it does not grant anything. Every permission is decided by the server against that
 * user's stored role, which is why switching to a technician and watching the dashboard get refused
 * demonstrates something real rather than a hidden menu item.
 */
export default function App() {
  const [users, setUsers] = useState<User[]>([])
  const [currentUser, setCurrentUser] = useState<User | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    api
      .getUsers()
      .then((loaded) => {
        if (cancelled) return

        setUsers(loaded)
        // Open as the service manager: the dashboard is their view, and it is where a demo starts.
        setCurrentUser(loaded.find((u) => u.role === 'TeamLead') ?? loaded[0] ?? null)
      })
      .catch(() => {
        if (!cancelled) {
          setLoadError(
            'Could not reach the API. Start it with: dotnet run --project src/Helpdesk.Api',
          )
        }
      })

    return () => {
      cancelled = true
    }
  }, [])

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

        <label className="role-switcher">
          <span>Viewing as</span>
          <select
            value={currentUser?.id ?? ''}
            onChange={(event) =>
              setCurrentUser(users.find((u) => u.id === event.target.value) ?? null)
            }
            disabled={users.length === 0}
          >
            {users.map((user) => (
              <option key={user.id} value={user.id}>
                {user.displayName} — {user.role === 'TeamLead' ? 'Service manager' : user.role}
              </option>
            ))}
          </select>
        </label>
      </header>

      <main className="app-main">
        {loadError && (
          <div className="dashboard-notice" role="alert">
            <h2>The API is not responding</h2>
            <p>{loadError}</p>
          </div>
        )}

        {currentUser && <DashboardPage currentUser={currentUser} />}
      </main>
    </div>
  )
}

import { useEffect, useState } from 'react'
import './App.css'
import { NoticeBanner } from './components/NoticeBanner'
import { DashboardPage } from './pages/DashboardPage'
import { FlightLookupPage } from './pages/FlightLookupPage'
import { TicketsPage } from './pages/TicketsPage'
import type { Notice, Role } from './types'

type Page = 'tickets' | 'flight' | 'dashboard'

const THEME_KEY = 'airport-helpdesk-theme'

const PAGE_LABELS: Record<Page, string> = {
  tickets: 'Tickets',
  flight: 'Flight lookup',
  dashboard: 'Manager dashboard',
}

export default function App() {
  const [role, setRole] = useState<Role>('Airport Staff')
  const [page, setPage] = useState<Page>('tickets')

  // USER STORY: Light / Dark Mode
  // The selected theme is saved so it stays after refreshing.
  const [dark, setDark] = useState(() => {
    const saved = localStorage.getItem(THEME_KEY)

    if (saved === 'light') return false
    if (saved === 'dark') return true

    return true
  })

  const [notice, setNotice] = useState<Notice | null>(null)

  useEffect(() => {
    localStorage.setItem(
      THEME_KEY,
      dark ? 'dark' : 'light',
    )
  }, [dark])

  function changeRole(next: Role) {
    setRole(next)
    setNotice(null)

    if (next === 'IT Manager') {
      setPage('dashboard')
    } else if (page === 'dashboard') {
      setPage('tickets')
    }
  }

  function goTo(next: Page) {
    setPage(next)
    setNotice(null)
  }

  return (
    <div className={dark ? 'app dark' : 'app light'}>
      <aside className="sidebar">
        <div className="brand sidebar-brand">
          <div className="brand-mark">IT</div>

          <div>
            <h1>HelpDesk</h1>
            <p>Airport Support Platform</p>
          </div>
        </div>

        <div className="sidebar-section">
          <div className="sidebar-title">
            Navigation
          </div>

          <button
            type="button"
            className={
              page === 'tickets'
                ? 'nav active'
                : 'nav'
            }
            onClick={() => goTo('tickets')}
          >
            <span className="nav-icon">▤</span>
            <span>Tickets</span>
          </button>

          <button
            type="button"
            className={
              page === 'flight'
                ? 'nav active'
                : 'nav'
            }
            onClick={() => goTo('flight')}
          >
            <span className="nav-icon">✈</span>
            <span>Flight lookup</span>
          </button>
        </div>

        <div className="sidebar-divider" />

        <div className="sidebar-section">
          <div className="sidebar-title">
            Management
          </div>

          <button
            type="button"
            className={
              page === 'dashboard'
                ? 'nav active'
                : 'nav'
            }
            onClick={() => goTo('dashboard')}
          >
            <span className="nav-icon">▥</span>
            <span>Dashboard</span>

            {role !== 'IT Manager' && (
              <small>Manager</small>
            )}
          </button>
        </div>

        <div className="sidebar-spacer" />

        <div className="sidebar-note">
          <strong>Student prototype</strong>

          <p>
            Tickets, notes and attachments use the
            backend. Badge identity, demo roles and
            flight records use sample data because
            real airport systems are unavailable.
          </p>
        </div>

        <div className="sidebar-profile">
          <div className="profile-avatar">
            {role === 'Airport Staff'
              ? 'AS'
              : role === 'IT Technician'
                ? 'IT'
                : 'IM'}
          </div>

          <div>
            <strong>{role}</strong>
            <span>Demo workspace</span>
          </div>
        </div>
      </aside>

      <div className="workspace">
        <header className="topbar">
          <div className="topbar-title">
            <span>Support Platform</span>
            <strong>{PAGE_LABELS[page]}</strong>
          </div>

          <div className="topbar-actions">
            <label className="role-switcher">
              <span>Demo role</span>

              <select
                value={role}
                onChange={event =>
                  changeRole(
                    event.target.value as Role,
                  )
                }
              >
                <option>Airport Staff</option>
                <option>IT Technician</option>
                <option>IT Manager</option>
              </select>
            </label>

            {/* USER STORY: Light / Dark Mode */}
            <button
              type="button"
              className="toggle-btn"
              onClick={() =>
                setDark(current => !current)
              }
              aria-label={
                dark
                  ? 'Switch to light mode'
                  : 'Switch to dark mode'
              }
              title={
                dark
                  ? 'Switch to light mode'
                  : 'Switch to dark mode'
              }
            >
              <span
                className="toggle-track"
                aria-hidden="true"
              >
                <span className="toggle-thumb" />
              </span>

              <span
                className="toggle-icon"
                aria-hidden="true"
              >
                {dark ? '☾' : '☀'}
              </span>

              <span className="toggle-label">
                {dark ? 'Dark' : 'Light'}
              </span>
            </button>
          </div>
        </header>

        <main className="content">
          {notice && (
            <NoticeBanner
              notice={notice}
              onClose={() => setNotice(null)}
            />
          )}

          {page === 'tickets' && (
            <TicketsPage
              role={role}
              setNotice={setNotice}
            />
          )}

          {page === 'flight' && (
            <FlightLookupPage />
          )}

          {page === 'dashboard' && (
            <DashboardPage
              role={role}
              setNotice={setNotice}
            />
          )}
        </main>
      </div>
    </div>
  )
}
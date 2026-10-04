import { useEffect, useState } from 'react'
import './App.css'
import { NoticeBanner } from './components/NoticeBanner'
import { DashboardPage } from './pages/DashboardPage'
import { FlightLookupPage } from './pages/FlightLookupPage'
import { TicketsPage } from './pages/TicketsPage'
import { TicketNotifications } from './components/TicketNotifications'
import { api } from './services/api'
import type { Notice, Role, TicketNotification } from './types'

type Page = 'tickets' | 'flight' | 'dashboard'

const THEME_KEY = 'airport-helpdesk-theme'
const SESSION_KEY = 'airport-helpdesk-auth'
const ROLE_KEY = 'airport-helpdesk-role'
const NOTIFICATIONS_KEY = 'airport-helpdesk-notifications'
const NOTIFICATION_SNAPSHOT_KEY = 'airport-helpdesk-notification-snapshot'
const NOTIFICATION_NOTES_KEY = 'airport-helpdesk-notification-notes'
const DEMO_REPORTER_EMAIL = 'jamie.santos@airport.test'
const NOTIFICATION_POLL_INTERVAL = 15_000

function readStoredNotifications(): TicketNotification[] {
  try {
    const stored = localStorage.getItem(NOTIFICATIONS_KEY)
    const parsed: unknown = stored ? JSON.parse(stored) : []
    return Array.isArray(parsed) ? parsed as TicketNotification[] : []
  } catch {
    return []
  }
}

const PAGE_LABELS: Record<Page, string> = {
  tickets: 'Tickets',
  flight: 'Flight lookup',
  dashboard: 'Manager dashboard',
}

const ROLE_OPTIONS: Role[] = ['Airport Staff', 'IT Technician', 'IT Manager']

export default function App() {
  const [role, setRole] = useState<Role>(() => {
    const savedRole = localStorage.getItem(ROLE_KEY)
    return (savedRole as Role | null) ?? 'Airport Staff'
  })
  const [page, setPage] = useState<Page>('tickets')
  const [focusTicketId, setFocusTicketId] = useState<number | null>(null)
  const [ticketNotifications, setTicketNotifications] = useState<TicketNotification[]>(readStoredNotifications)
  const [isSignedIn, setIsSignedIn] = useState(() => {
    const saved = localStorage.getItem(SESSION_KEY)
    return saved !== 'signed-out'
  })

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

  useEffect(() => {
    localStorage.setItem(ROLE_KEY, role)
  }, [role])

  useEffect(() => {
    localStorage.setItem(SESSION_KEY, isSignedIn ? 'signed-in' : 'signed-out')
  }, [isSignedIn])

  useEffect(() => {
    if (!isSignedIn || role !== 'Airport Staff') return

    let active = true
    let hasSnapshot = false
    let previousSnapshot: Record<string, { status: string; assignee: string }> = {}
    let knownNoteIds = new Set<number>()
    let notifications = readStoredNotifications()

    try {
      const snapshot = localStorage.getItem(NOTIFICATION_SNAPSHOT_KEY)
      if (snapshot) {
        previousSnapshot = JSON.parse(snapshot) as typeof previousSnapshot
        hasSnapshot = true
      }
      knownNoteIds = new Set(JSON.parse(localStorage.getItem(NOTIFICATION_NOTES_KEY) ?? '[]') as number[])
    } catch {
      hasSnapshot = false
    }

    async function refreshNotifications() {
      try {
        const tickets = await api.getTickets('newest')
        const ownedTickets = tickets.filter(ticket => ticket.reporterEmail.toLowerCase() === DEMO_REPORTER_EMAIL)
        const nextSnapshot: Record<string, { status: string; assignee: string }> = {}
        const nextKnownNoteIds = new Set(knownNoteIds)
        const additions: TicketNotification[] = []

        const publicNotes = await Promise.all(ownedTickets.map(async ticket => {
          try {
            const notes = await api.getNotes(ticket.id, role)
            return { ticket, notes: notes.filter(note => !note.isInternal && note.author !== ticket.reporterName) }
          } catch {
            return { ticket, notes: [] }
          }
        }))

        for (const ticket of ownedTickets) {
          const key = String(ticket.id)
          const current = { status: ticket.status, assignee: ticket.assignee }
          const previous = previousSnapshot[key]
          nextSnapshot[key] = current

          if (hasSnapshot && previous && previous.status !== current.status) {
            const detectedAt = new Date().toISOString()
            additions.push({
              id: `status:${key}:${detectedAt}:${current.status}`,
              ticketId: ticket.id,
              title: `Ticket #${ticket.id} status updated`,
              message: `Status changed to ${ticket.status}.`,
              createdAtUtc: detectedAt,
              isRead: false,
            })
          }

          if (hasSnapshot && previous && previous.assignee !== current.assignee && current.assignee !== 'Unassigned') {
            const detectedAt = new Date().toISOString()
            additions.push({
              id: `assignment:${key}:${detectedAt}:${current.assignee}`,
              ticketId: ticket.id,
              title: `Ticket #${ticket.id} assigned`,
              message: `Assigned to ${current.assignee}.`,
              createdAtUtc: detectedAt,
              isRead: false,
            })
          }
        }

        for (const { ticket, notes } of publicNotes) {
          for (const note of notes) {
            if (hasSnapshot && !knownNoteIds.has(note.id)) {
              additions.push({
                id: `reply:${note.id}`,
                ticketId: ticket.id,
                title: `New reply on ticket #${ticket.id}`,
                message: `${note.author} responded to your ticket.`,
                createdAtUtc: note.createdAtUtc,
                isRead: false,
              })
            }
            nextKnownNoteIds.add(note.id)
          }
        }

        if (!active) return

        const existingIds = new Set(notifications.map(notification => notification.id))
        notifications = [...additions.filter(notification => !existingIds.has(notification.id)), ...notifications].slice(0, 100)
        previousSnapshot = nextSnapshot
        knownNoteIds = nextKnownNoteIds
        hasSnapshot = true
        localStorage.setItem(NOTIFICATION_SNAPSHOT_KEY, JSON.stringify(nextSnapshot))
        localStorage.setItem(NOTIFICATION_NOTES_KEY, JSON.stringify([...nextKnownNoteIds]))
        localStorage.setItem(NOTIFICATIONS_KEY, JSON.stringify(notifications))
        setTicketNotifications(notifications)
      } catch {
        // Keep the existing notification list available when the API is offline.
      }
    }

    void refreshNotifications()
    const interval = window.setInterval(() => void refreshNotifications(), NOTIFICATION_POLL_INTERVAL)

    return () => {
      active = false
      window.clearInterval(interval)
    }
  }, [isSignedIn, role])

  function markNotificationsRead() {
    const updated = ticketNotifications.map(notification => ({ ...notification, isRead: true }))
    setTicketNotifications(updated)
    localStorage.setItem(NOTIFICATIONS_KEY, JSON.stringify(updated))
  }

  function openNotificationTicket(ticketId: number) {
    markNotificationsRead()
    setFocusTicketId(ticketId)
    setPage('tickets')
    setNotice(null)
  }

  async function signIn(next: Role) {
    try {
      await api.signIn(next)
      setRole(next)
      setNotice(null)
      setIsSignedIn(true)

      if (next === 'IT Manager') {
        setPage('dashboard')
      } else if (page === 'dashboard') {
        setPage('tickets')
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Sign in failed.'
      setNotice({ type: 'error', text: message })
    }
  }

  function signOut() {
    setIsSignedIn(false)
    setRole('Airport Staff')
    setPage('tickets')
    setNotice({ type: 'info', text: 'Signed out. Choose a demo account to continue.' })
  }

  function changeRole(next: Role) {
    if (!isSignedIn) {
      signIn(next)
      return
    }

    setRole(next)
    setNotice(null)

    if (next === 'IT Manager') {
      setPage('dashboard')
    } else if (page === 'dashboard') {
      setPage('tickets')
    }
  }

  function goTo(next: Page) {
    if (!isSignedIn && next !== 'tickets') {
      return
    }

    setPage(next)
    setNotice(null)
  }

  if (!isSignedIn) {
    return (
      <div className={dark ? 'app dark' : 'app light'}>
        <div className="auth-shell">
          <div className="auth-card">
            <div className="auth-brand">
              <div className="brand-mark">IT</div>

              <div>
                <strong>HelpDesk</strong>
                <span>Airport Support Platform</span>
              </div>
            </div>

            <h1>Sign in</h1>
            <p className="auth-subtitle">
              Use the demo identity to access the ticket workflow and manager analytics.
            </p>

            <label className="auth-field">
              <span>Demo account</span>
              <select
                value={role}
                onChange={event => setRole(event.target.value as Role)}
              >
                {ROLE_OPTIONS.map(option => (
                  <option key={option} value={option}>{option}</option>
                ))}
              </select>
            </label>

            <button
              type="button"
              className="primary-button auth-button"
              onClick={() => void signIn(role)}
            >
              Sign in
            </button>

            <div className="auth-note">
              This is a demo sign-in flow for the support workspace; the backend still enforces role-based access on requests.
            </div>
          </div>
        </div>
      </div>
    )
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
            {role === 'Airport Staff' && (
              <TicketNotifications
                notifications={ticketNotifications}
                onMarkAllRead={markNotificationsRead}
                onOpenTicket={openNotificationTicket}
              />
            )}

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
                {ROLE_OPTIONS.map(option => (
                  <option key={option} value={option}>{option}</option>
                ))}
              </select>
            </label>

            <button type="button" className="secondary-button signout-btn" onClick={signOut}>
              Sign out
            </button>

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

            <button
              type="button"
              className="secondary-button"
              onClick={async () => {
                console.log('[App] Testing API connection...')
                try {
                  const result = await api.getTickets('priority-age')
                  console.log('[App] API test SUCCESS:', result.length, 'tickets returned')
                  setNotice({ type: 'success', text: `API working! Got ${result.length} tickets.` })
                } catch (error) {
                  console.error('[App] API test FAILED:', error)
                  const msg = error instanceof Error ? error.message : 'Unknown error'
                  setNotice({ type: 'error', text: `API test failed: ${msg}` })
                }
              }}
              title="Test if the API is responding"
            >
              🔧 Test API
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
              key={`${role}:${focusTicketId ?? 'tickets'}`}
              role={role}
              setNotice={setNotice}
              focusTicketId={focusTicketId}
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
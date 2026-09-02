import { useState } from 'react'
import type { User, UserRole } from '../api/types'
import { AdminView } from './AdminView'
import { BacklogView } from './BacklogView'
import { MyRequestsView } from './MyRequestsView'
import { PerformanceView } from './PerformanceView'
import { QueueView } from './QueueView'
import { TicketDetailPage } from './TicketDetailPage'
import './dashboard.css'

type TabId = 'requests' | 'queue' | 'performance' | 'backlog' | 'accounts'

interface Tab {
  id: TabId
  label: string
  question: string
}

const REQUESTS: Tab = {
  id: 'requests',
  label: 'My requests',
  question: 'What did I ask for, and what is happening to it?',
}

const QUEUE: Tab = {
  id: 'queue',
  label: 'Queue',
  question: 'What should I pick up next?',
}

const PERFORMANCE: Tab = {
  id: 'performance',
  label: 'Performance',
  question: 'Are we meeting our commitments?',
}

const BACKLOG: Tab = {
  id: 'backlog',
  label: 'Backlog',
  question: 'Is the queue growing?',
}

const ACCOUNTS: Tab = {
  id: 'accounts',
  label: 'Accounts',
  question: 'Who can reach what, and who changed it?',
}

/**
 * What each role is given, in the order that role would use it.
 *
 * The first entry is where that person lands. A technician opening the application should see work,
 * not a report they cannot read; a requester should see their own requests, not a queue. Everybody
 * keeps their own requests, because a technician is also somebody who occasionally needs a new
 * laptop.
 */
const TABS_BY_ROLE: Record<UserRole, Tab[]> = {
  Requester: [REQUESTS],
  Technician: [QUEUE, REQUESTS],
  TeamLead: [PERFORMANCE, BACKLOG, QUEUE, ACCOUNTS, REQUESTS],
}

const TITLE_BY_TAB: Record<TabId, string> = {
  requests: 'My requests',
  queue: 'Support queue',
  performance: 'Service health',
  backlog: 'Service health',
  accounts: 'Service health',
}

/**
 * The signed-in workspace.
 *
 * Which tabs appear is a convenience, not the control. Every request behind every one of them is
 * authorised server-side against the caller's stored role, and the reporting tabs deliberately do
 * not hide themselves from a technician who reaches them — they ask, and show the refusal, which is
 * what makes the restriction demonstrable rather than merely asserted.
 */
export function DashboardPage({ currentUser }: { currentUser: User }) {
  const tabs = TABS_BY_ROLE[currentUser.role]
  const [tab, setTab] = useState<TabId>(tabs[0].id)
  const [openTicketId, setOpenTicketId] = useState<number | null>(null)

  // Bumped whenever a ticket changes, so a list the reader returns to reloads rather than showing
  // the state it had before they acted on it.
  const [changeToken, setChangeToken] = useState(0)

  const active = tabs.find((t) => t.id === tab) ?? tabs[0]

  if (openTicketId !== null) {
    return (
      <div className="dashboard">
        <TicketDetailPage
          currentUser={currentUser}
          ticketId={openTicketId}
          onBack={() => setOpenTicketId(null)}
          onChanged={() => setChangeToken((token) => token + 1)}
        />
      </div>
    )
  }

  return (
    <div className="dashboard">
      <header className="dashboard-header">
        <div>
          <h1>{TITLE_BY_TAB[active.id]}</h1>
          <p className="dashboard-subtitle">{active.question}</p>
        </div>

        {tabs.length > 1 && (
          <nav className="tabs" aria-label="Views">
            {tabs.map((entry) => (
              <button
                key={entry.id}
                type="button"
                className={active.id === entry.id ? 'is-selected' : ''}
                aria-current={active.id === entry.id ? 'page' : undefined}
                onClick={() => setTab(entry.id)}
              >
                {entry.label}
              </button>
            ))}
          </nav>
        )}
      </header>

      {active.id === 'requests' && (
        <MyRequestsView
          currentUser={currentUser}
          onOpen={setOpenTicketId}
          reloadToken={changeToken}
        />
      )}
      {active.id === 'queue' && (
        <QueueView key={changeToken} currentUser={currentUser} onOpen={setOpenTicketId} />
      )}
      {active.id === 'performance' && <PerformanceView currentUser={currentUser} />}
      {active.id === 'backlog' && <BacklogView currentUser={currentUser} />}
      {active.id === 'accounts' && <AdminView currentUser={currentUser} />}
    </div>
  )
}

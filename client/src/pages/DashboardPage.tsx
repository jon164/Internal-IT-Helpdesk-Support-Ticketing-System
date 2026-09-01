import { useState } from 'react'
import type { User } from '../api/types'
import { AdminView } from './AdminView'
import { BacklogView } from './BacklogView'
import { PerformanceView } from './PerformanceView'
import './dashboard.css'

const PERFORMANCE = {
  id: 'performance',
  label: 'Performance',
  question: 'Are we meeting our commitments?',
} as const

const BACKLOG = {
  id: 'backlog',
  label: 'Backlog',
  question: 'Is the queue growing?',
} as const

const ACCOUNTS = {
  id: 'accounts',
  label: 'Accounts',
  question: 'Who can reach what, and who changed it?',
} as const

type TabId = 'performance' | 'backlog' | 'accounts'

interface Tab {
  id: TabId
  label: string
  question: string
}

interface DashboardPageProps {
  currentUser: User
}

/**
 * The service manager's workspace.
 *
 * Three tabs, because the same person asks three different questions and each needs a different
 * shape of answer: performance is a judgement over a window, backlog is a direction over time, and
 * accounts is a register of who holds what. Putting them on one page produced a scroll where none
 * could be read at a glance, which is the whole point of a dashboard.
 *
 * The Accounts tab is offered only to the service manager, but that is a convenience rather than the
 * control: every request behind it is refused server-side for anybody else, and the other two tabs
 * deliberately do not hide themselves — they ask and show the refusal.
 */
export function DashboardPage({ currentUser }: DashboardPageProps) {
  const [tab, setTab] = useState<TabId>('performance')

  const tabs: Tab[] =
    currentUser.role === 'TeamLead'
      ? [PERFORMANCE, BACKLOG, ACCOUNTS]
      : [PERFORMANCE, BACKLOG]

  const active = tabs.find((t) => t.id === tab) ?? tabs[0]

  return (
    <div className="dashboard">
      <header className="dashboard-header">
        <div>
          <h1>Service health</h1>
          <p className="dashboard-subtitle">{active.question}</p>
        </div>

        <nav className="tabs" aria-label="Dashboard views">
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
      </header>

      {active.id === 'performance' && <PerformanceView currentUser={currentUser} />}
      {active.id === 'backlog' && <BacklogView currentUser={currentUser} />}
      {active.id === 'accounts' && <AdminView currentUser={currentUser} />}
    </div>
  )
}

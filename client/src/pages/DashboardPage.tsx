import { useState } from 'react'
import type { User } from '../api/types'
import { BacklogView } from './BacklogView'
import { PerformanceView } from './PerformanceView'
import './dashboard.css'

const TABS = [
  {
    id: 'performance',
    label: 'Performance',
    question: 'Are we meeting our commitments?',
  },
  {
    id: 'backlog',
    label: 'Backlog',
    question: 'Is the queue growing?',
  },
] as const

type TabId = (typeof TABS)[number]['id']

interface DashboardPageProps {
  currentUser: User
}

/**
 * The manager's dashboard.
 *
 * Split into two views because the manager asks two questions that need different shapes of answer.
 * "Are we meeting our targets" is a judgement over a window; "are we falling behind" is a direction
 * over time. Putting both on one page produced a scroll where neither could be read at a glance,
 * which is the whole point of a dashboard.
 *
 * Neither view hides itself from other roles — each asks the server and shows the refusal. Hiding a
 * page is not access control.
 */
export function DashboardPage({ currentUser }: DashboardPageProps) {
  const [tab, setTab] = useState<TabId>('performance')

  const active = TABS.find((t) => t.id === tab) ?? TABS[0]

  return (
    <div className="dashboard">
      <header className="dashboard-header">
        <div>
          <h1>Service health</h1>
          <p className="dashboard-subtitle">{active.question}</p>
        </div>

        <nav className="tabs" aria-label="Dashboard views">
          {TABS.map((entry) => (
            <button
              key={entry.id}
              type="button"
              className={tab === entry.id ? 'is-selected' : ''}
              aria-current={tab === entry.id ? 'page' : undefined}
              onClick={() => setTab(entry.id)}
            >
              {entry.label}
            </button>
          ))}
        </nav>
      </header>

      {tab === 'performance' ? (
        <PerformanceView currentUser={currentUser} />
      ) : (
        <BacklogView currentUser={currentUser} />
      )}
    </div>
  )
}

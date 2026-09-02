import { useCallback, useEffect, useMemo, useState } from 'react'
import { ApiError, api } from '../api/client'
import type { TicketPriority, TicketStatus, TicketSummary, User } from '../api/types'
import { ReportNotice } from '../components/ReportNotice'
import { StatTile } from '../components/StatTile'
import { TicketTable } from '../components/TicketTable'
import { breachTone } from '../utils/format'
import './queue.css'

const STATUSES: TicketStatus[] = [
  'New',
  'Triaged',
  'Assigned',
  'InProgress',
  'OnHold',
  'Resolved',
  'Closed',
  'Cancelled',
]

const PRIORITIES: TicketPriority[] = ['Critical', 'High', 'Standard', 'Low']

type Scope = 'mine' | 'open' | 'unassigned' | 'awaiting' | 'all'

const SCOPES: { id: Scope; label: string; hint: string }[] = [
  { id: 'mine', label: 'My work', hint: 'Assigned to you and still needing your attention.' },
  { id: 'open', label: 'All open work', hint: 'Everything on the desk still needing a technician.' },
  { id: 'unassigned', label: 'Unassigned', hint: 'Open work nobody has picked up yet.' },
  {
    id: 'awaiting',
    label: 'Awaiting confirmation',
    hint: 'Resolved, waiting on the requester to confirm the fix.',
  },
  { id: 'all', label: 'Everything', hint: 'Including resolved, closed and cancelled tickets.' },
]

/**
 * The support queue.
 *
 * Available to technicians and to the service manager. What comes back is decided entirely by the
 * server: a technician sees the open desk workload minus any restricted ticket they do not hold, and
 * the filters below are conveniences applied *on top of* that, never a way around it. Asking for
 * `assignedTo` somebody else returns only what the caller was already entitled to see.
 *
 * The counts across the top are computed from the rows on screen rather than fetched separately, so
 * the summary can never disagree with the list underneath it.
 */
export function QueueView({
  currentUser,
  onOpen,
}: {
  currentUser: User
  onOpen: (id: number) => void
}) {
  const [scope, setScope] = useState<Scope>(currentUser.role === 'Technician' ? 'mine' : 'open')
  const [status, setStatus] = useState<TicketStatus | ''>('')
  const [priority, setPriority] = useState<TicketPriority | ''>('')
  const [tickets, setTickets] = useState<TicketSummary[]>([])
  const [error, setError] = useState<ApiError | null>(null)
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)

    const params: Record<string, string> = {}

    if (scope === 'open') {
      params.needsWorkOnly = 'true'
    } else if (scope === 'mine') {
      // Work still needing a technician, not everything ever assigned. The first version of this
      // screen asked only for `assignedTo` and returned 159 rows, almost all of them closed; the
      // second added `openOnly` and still buried eleven live tickets under twenty-four that were
      // resolved and waiting on somebody else. Those have their own tab now.
      params.assignedTo = currentUser.id
      params.needsWorkOnly = 'true'
    } else if (scope === 'unassigned') {
      params.needsWorkOnly = 'true'
      params.unassignedOnly = 'true'
    } else if (scope === 'awaiting') {
      params.status = 'Resolved'
    }

    if (status !== '' && scope !== 'awaiting') {
      params.status = status
    }

    if (priority !== '') {
      params.priority = priority
    }

    try {
      setTickets(await api.getTickets(currentUser.id, params))
    } catch (caught) {
      setTickets([])
      setError(
        caught instanceof ApiError
          ? caught
          : new ApiError(0, {
              error: 'Invalid',
              message: 'The queue could not be loaded. Is the API running?',
            }),
      )
    } finally {
      setLoading(false)
    }
  }, [currentUser.id, scope, status, priority])

  useEffect(() => {
    void load()
  }, [load])

  const counts = useMemo(() => {
    const breached = tickets.filter((t) => t.resolution.state === 'Breached').length
    const atRisk = tickets.filter((t) => t.resolution.state === 'AtRisk').length

    return { total: tickets.length, breached, atRisk }
  }, [tickets])

  const activeScope = SCOPES.find((s) => s.id === scope) ?? SCOPES[0]

  return (
    <div className="view">
      <div className="view-filters">
        <p className="view-range">{loading ? ' ' : activeScope.hint}</p>
        <div className="filter-controls">
          <button type="button" className="link-button" onClick={() => void load()}>
            Refresh
          </button>
        </div>
      </div>

      <div className="queue-filters">
        <div className="segmented" role="group" aria-label="Queue scope">
          {SCOPES.map((entry) => (
            <button
              key={entry.id}
              type="button"
              className={scope === entry.id ? 'is-selected' : ''}
              aria-pressed={scope === entry.id}
              onClick={() => setScope(entry.id)}
            >
              {entry.label}
            </button>
          ))}
        </div>

        <label className="queue-select">
          <span>Status</span>
          {/* Disabled rather than ignored on the Awaiting confirmation view, which is itself a
              status filter. A control that silently does nothing is worse than one that says why. */}
          <select
            value={scope === 'awaiting' ? 'Resolved' : status}
            disabled={scope === 'awaiting'}
            title={scope === 'awaiting' ? 'This view is already filtered to resolved tickets.' : undefined}
            onChange={(event) => setStatus(event.target.value as TicketStatus)}
          >
            <option value="">Any</option>
            {STATUSES.map((entry) => (
              <option key={entry} value={entry}>
                {entry}
              </option>
            ))}
          </select>
        </label>

        <label className="queue-select">
          <span>Priority</span>
          <select
            value={priority}
            onChange={(event) => setPriority(event.target.value as TicketPriority)}
          >
            <option value="">Any</option>
            {PRIORITIES.map((entry) => (
              <option key={entry} value={entry}>
                {entry}
              </option>
            ))}
          </select>
        </label>
      </div>

      {loading && <p className="dashboard-message">Loading the queue…</p>}

      {error && <ReportNotice error={error} currentUser={currentUser} />}

      {!loading && !error && (
        <>
          <div className="tile-grid">
            <StatTile
              label="In this view"
              value={counts.total.toLocaleString()}
              hint="Tickets matching the filters above."
            />
            <StatTile
              label="Past resolution target"
              value={counts.breached.toLocaleString()}
              tone={breachTone(counts.breached)}
              toneLabel={counts.breached === 0 ? 'None here' : undefined}
              hint="Already overdue. These come first."
            />
            <StatTile
              label="Approaching target"
              value={counts.atRisk.toLocaleString()}
              tone={counts.atRisk === 0 ? 'good' : 'warning'}
              toneLabel={counts.atRisk === 0 ? 'None here' : 'Watch'}
              hint="Past three quarters of the time allowed."
            />
          </div>

          <section className="panel">
            <header className="panel-header">
              <h2>{activeScope.label}</h2>
              <p>Most urgent first. Open a reference to see the ticket and act on it.</p>
            </header>

            <TicketTable
              tickets={tickets}
              onOpen={onOpen}
              emptyMessage="No tickets match these filters."
            />
          </section>

          <footer className="dashboard-footer">
            <p>
              This queue is what the server is willing to show you, not a filtered copy of everything.
              A restricted ticket you are not assigned to is absent rather than hidden, and asking for
              it directly returns the same "not found" a stranger would get — so the refusal does not
              reveal that it exists.
            </p>
          </footer>
        </>
      )}
    </div>
  )
}

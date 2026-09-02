import { useCallback, useEffect, useState } from 'react'
import { ApiError, api } from '../api/client'
import type { TicketDetail, User } from '../api/types'
import { ReportNotice } from '../components/ReportNotice'
import { TicketActions } from '../components/TicketActions'
import {
  PriorityBadge,
  RestrictedBadge,
  SlaClock,
  StatusBadge,
} from '../components/TicketBadges'
import { TicketTimeline } from '../components/TicketTimeline'
import { formatDateTime, formatMinutes } from '../utils/format'
import './ticket-detail.css'

interface TicketDetailPageProps {
  currentUser: User
  ticketId: number
  onBack: () => void
  /** Called after a change, so the list the reader came from is not left showing stale state. */
  onChanged?: () => void
}

/**
 * One ticket, in full.
 *
 * The same screen serves all three roles. It differs only in what the server says the caller may do
 * with it and, for a restricted ticket, in whether the caller can open it at all — a technician who
 * is not assigned gets the same **404** a stranger would, so the refusal does not disclose that the
 * ticket exists.
 *
 * The audit trail is shown to everybody who can see the ticket, not reserved for management. A
 * requester being able to see who picked their request up and when is the whole point of replacing
 * the shared mailbox.
 */
export function TicketDetailPage({
  currentUser,
  ticketId,
  onBack,
  onChanged,
}: TicketDetailPageProps) {
  const [detail, setDetail] = useState<TicketDetail | null>(null)
  const [technicians, setTechnicians] = useState<User[]>([])
  const [error, setError] = useState<ApiError | null>(null)
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)

    try {
      setDetail(await api.getTicket(currentUser.id, ticketId))
    } catch (caught) {
      setDetail(null)
      setError(
        caught instanceof ApiError
          ? caught
          : new ApiError(0, {
              error: 'Invalid',
              message: 'The ticket could not be loaded. Is the API running?',
            }),
      )
    } finally {
      setLoading(false)
    }
  }, [currentUser.id, ticketId])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    if (currentUser.role !== 'TeamLead') {
      return
    }

    // Only the service manager can assign to somebody other than themselves, so only they need the
    // list. Fetching it for everybody would be a request whose answer is never used.
    api
      .getUsers()
      .then((users) => setTechnicians(users.filter((u) => u.role === 'Technician')))
      .catch(() => setTechnicians([]))
  }, [currentUser.role])

  const afterChange = () => {
    void load()
    onChanged?.()
  }

  return (
    <div className="ticket-detail">
      <button type="button" className="link-button ticket-back" onClick={onBack}>
        ← Back to the list
      </button>

      {loading && <p className="dashboard-message">Loading the ticket…</p>}

      {error && <ReportNotice error={error} currentUser={currentUser} />}

      {detail && !loading && (
        <>
          <header className="ticket-headline">
            <p className="ticket-reference">{detail.summary.reference}</p>
            <h1>{detail.summary.title}</h1>
            <div className="ticket-badge-row">
              <PriorityBadge
                priority={detail.summary.priority}
                label={detail.summary.priorityLabel}
              />
              <StatusBadge status={detail.summary.status} />
              {detail.summary.isRestricted && <RestrictedBadge />}
              <span className="ticket-category">{detail.summary.category}</span>
            </div>
          </header>

          <section className="panel">
            <header className="panel-header">
              <h2>Against target</h2>
              <p>
                {detail.policy.displayName} — respond within{' '}
                {formatMinutes(detail.policy.responseTargetMinutes)}, resolve within{' '}
                {formatMinutes(detail.policy.resolutionTargetMinutes)},{' '}
                {detail.policy.clock === 'Continuous'
                  ? 'measured around the clock.'
                  : 'measured in working hours only.'}
              </p>
            </header>

            <div className="ticket-clocks">
              <SlaClock
                caption="Response"
                state={detail.summary.response.state}
                remainingMinutes={detail.summary.response.remainingMinutes}
              />
              <SlaClock
                caption="Resolution"
                state={detail.summary.resolution.state}
                remainingMinutes={detail.summary.resolution.remainingMinutes}
              />
            </div>

            {detail.holdPeriods.length > 0 && (
              <div className="ticket-holds">
                <p className="ticket-holds-title">
                  Time on hold is excluded from the resolution clock
                </p>
                <ul>
                  {detail.holdPeriods.map((hold, index) => (
                    <li key={`${hold.startedAt}-${index}`}>
                      {formatDateTime(hold.startedAt)} →{' '}
                      {hold.endedAt ? formatDateTime(hold.endedAt) : 'still on hold'}
                      {hold.reason && <> · {hold.reason}</>}
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </section>

          <section className="panel">
            <header className="panel-header">
              <h2>The request</h2>
            </header>

            <dl className="ticket-facts">
              <div>
                <dt>Raised by</dt>
                <dd>
                  {detail.summary.requesterName} · {detail.summary.department}
                </dd>
              </div>
              <div>
                <dt>Raised</dt>
                <dd>{formatDateTime(detail.summary.createdAt)}</dd>
              </div>
              <div>
                <dt>Assigned to</dt>
                <dd>{detail.summary.assignedTechnicianName ?? 'Nobody yet'}</dd>
              </div>
              <div>
                <dt>First response</dt>
                <dd>
                  {detail.firstRespondedAt
                    ? formatDateTime(detail.firstRespondedAt)
                    : 'Not yet answered'}
                </dd>
              </div>
              {detail.resolvedAt && (
                <div>
                  <dt>Resolved</dt>
                  <dd>{formatDateTime(detail.resolvedAt)}</dd>
                </div>
              )}
              {detail.closedAt && (
                <div>
                  <dt>Closed</dt>
                  <dd>{formatDateTime(detail.closedAt)}</dd>
                </div>
              )}
            </dl>

            <p className="ticket-description">{detail.description}</p>

            {detail.resolutionNotes && (
              <div className="ticket-resolution">
                <p className="ticket-resolution-title">Resolution</p>
                <p>{detail.resolutionNotes}</p>
              </div>
            )}
          </section>

          <section className="panel">
            <header className="panel-header">
              <h2>What you can do</h2>
              <p>
                Only the actions the server permits you are offered here — and each one is checked
                again when you use it.
              </p>
            </header>

            <TicketActions
              currentUser={currentUser}
              detail={detail}
              technicians={technicians}
              onChanged={afterChange}
            />
          </section>

          <section className="panel">
            <header className="panel-header">
              <h2>History</h2>
              <p>
                Every change to this ticket, in order, with who made it. Nothing in the system edits
                or removes an entry.
              </p>
            </header>

            <TicketTimeline events={detail.events} />
          </section>
        </>
      )}
    </div>
  )
}

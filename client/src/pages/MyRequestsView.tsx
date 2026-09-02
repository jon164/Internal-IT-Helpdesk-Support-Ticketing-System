import { useCallback, useEffect, useState } from 'react'
import { ApiError, api } from '../api/client'
import type { TicketPriority, TicketSummary, User } from '../api/types'
import { ReportNotice } from '../components/ReportNotice'
import { StatTile } from '../components/StatTile'
import { TicketTable } from '../components/TicketTable'
import './my-requests.css'

/** What a requester may ask for. Critical is deliberately absent — see the note in the form. */
const REQUESTABLE_PRIORITIES: { value: TicketPriority; label: string; hint: string }[] = [
  { value: 'High', label: 'High', hint: 'I cannot work at all, or a passenger-facing system is down.' },
  { value: 'Standard', label: 'Standard', hint: 'Something is broken but I have a way around it.' },
  { value: 'Low', label: 'Low', hint: 'A request or a minor annoyance. No hurry.' },
]

interface MyRequestsViewProps {
  currentUser: User
  onOpen: (id: number) => void
  /** Bumped by the parent when a ticket changes elsewhere, so the list reloads. */
  reloadToken?: number
}

/**
 * The requester's screen: raise a request, and see what happened to the ones already raised.
 *
 * This is the role the whole system exists to serve, and until now it was the only one with no
 * screen at all. Two things matter here beyond the form working. The requester can see the state of
 * their own request without asking anybody — which is what the shared mailbox could never offer —
 * and what they see is limited to their own requests by the server, not by this component choosing
 * to ask a narrow question.
 */
export function MyRequestsView({ currentUser, onOpen, reloadToken }: MyRequestsViewProps) {
  const [tickets, setTickets] = useState<TicketSummary[]>([])
  const [categories, setCategories] = useState<string[]>([])
  const [error, setError] = useState<ApiError | null>(null)
  const [loading, setLoading] = useState(true)

  const [showForm, setShowForm] = useState(false)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [category, setCategory] = useState('')
  const [priority, setPriority] = useState<TicketPriority>('Standard')
  const [markRestricted, setMarkRestricted] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [raised, setRaised] = useState<TicketSummary | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)

    try {
      setTickets(await api.getTickets(currentUser.id))
    } catch (caught) {
      setTickets([])
      setError(
        caught instanceof ApiError
          ? caught
          : new ApiError(0, {
              error: 'Invalid',
              message: 'Your requests could not be loaded. Is the API running?',
            }),
      )
    } finally {
      setLoading(false)
    }
  }, [currentUser.id])

  useEffect(() => {
    void load()
  }, [load, reloadToken])

  useEffect(() => {
    api.getCategories().then(setCategories).catch(() => setCategories([]))
  }, [])

  const resetForm = () => {
    setTitle('')
    setDescription('')
    setCategory('')
    setPriority('Standard')
    setMarkRestricted(false)
    setFormError(null)
  }

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    setSubmitting(true)
    setFormError(null)

    try {
      const created = await api.createTicket(currentUser.id, {
        title: title.trim(),
        description: description.trim(),
        category,
        priority,
        markRestricted,
      })

      setRaised(created)
      setShowForm(false)
      resetForm()
      await load()
    } catch (caught) {
      // The server validates and returns every problem at once, so the requester is not made to
      // resubmit repeatedly to discover them one at a time.
      setFormError(caught instanceof ApiError ? caught.message : 'The request could not be raised.')
    } finally {
      setSubmitting(false)
    }
  }

  const open = tickets.filter(
    (t) => t.status !== 'Closed' && t.status !== 'Cancelled' && t.status !== 'Resolved',
  )
  const awaitingConfirmation = tickets.filter((t) => t.status === 'Resolved')

  return (
    <div className="view">
      <div className="view-filters">
        <p className="view-range">
          {loading ? ' ' : `${tickets.length} request${tickets.length === 1 ? '' : 's'} raised`}
        </p>
        <div className="filter-controls">
          <button type="button" className="link-button" onClick={() => void load()}>
            Refresh
          </button>
        </div>
      </div>

      {raised && (
        <div className="request-receipt" role="status">
          <p>
            <strong>{raised.reference}</strong> has been raised and is in the support queue. You will
            see its progress below — you do not need to chase anybody.
          </p>
          <button type="button" className="link-button" onClick={() => onOpen(raised.id)}>
            Open it
          </button>
        </div>
      )}

      <section className="panel">
        <header className="panel-header">
          <h2>Raise a request</h2>
          <p>
            One request per problem. Everything you type here is visible to the support team and is
            kept as part of the record.
          </p>
        </header>

        {showForm ? (
          <form className="request-form" onSubmit={(event) => void submit(event)}>
            <label className="request-field">
              <span>What is the problem?</span>
              <input
                type="text"
                value={title}
                autoFocus
                maxLength={200}
                placeholder="e.g. Level 2 printer jamming repeatedly"
                onChange={(event) => setTitle(event.target.value)}
              />
              <small>A short summary. This is what the desk sees in its queue.</small>
            </label>

            <div className="request-row">
              <label className="request-field">
                <span>Category</span>
                <select value={category} onChange={(event) => setCategory(event.target.value)}>
                  <option value="">Choose one…</option>
                  {categories.map((entry) => (
                    <option key={entry} value={entry}>
                      {entry}
                    </option>
                  ))}
                </select>
                <small>Grouping requests is how recurring causes become visible.</small>
              </label>

              <label className="request-field">
                <span>How urgent is it?</span>
                <select
                  value={priority}
                  onChange={(event) => setPriority(event.target.value as TicketPriority)}
                >
                  {REQUESTABLE_PRIORITIES.map((entry) => (
                    <option key={entry.value} value={entry.value}>
                      {entry.label}
                    </option>
                  ))}
                </select>
                <small>
                  {REQUESTABLE_PRIORITIES.find((entry) => entry.value === priority)?.hint}
                </small>
              </label>
            </div>

            <label className="request-field">
              <span>Tell us more</span>
              <textarea
                rows={5}
                value={description}
                maxLength={4000}
                placeholder="What you were doing, what happened, and anything you have already tried."
                onChange={(event) => setDescription(event.target.value)}
              />
              <small>{description.length} of 4000 characters.</small>
            </label>

            <label className="request-check">
              <input
                type="checkbox"
                checked={markRestricted}
                onChange={(event) => setMarkRestricted(event.target.checked)}
              />
              <span>
                This request is confidential
                <small>
                  Restricts it to you, the technician it is assigned to, and the service manager.
                  Use it for HR, payroll or anything commercially sensitive.
                </small>
              </span>
            </label>

            {formError && (
              <p className="admin-action-error" role="alert">
                {formError}
              </p>
            )}

            <div className="request-actions">
              <button
                type="submit"
                className="login-submit"
                disabled={
                  submitting ||
                  title.trim().length < 5 ||
                  description.trim().length === 0 ||
                  category === ''
                }
              >
                {submitting ? 'Raising…' : 'Raise this request'}
              </button>
              <button
                type="button"
                className="link-button"
                disabled={submitting}
                onClick={() => {
                  setShowForm(false)
                  resetForm()
                }}
              >
                Cancel
              </button>
            </div>

            <p className="request-note">
              A P1 Critical priority is set by the support desk, not requested. It carries a
              fifteen-minute response target measured around the clock, and is reserved for a
              service outage — the desk triages it in, and records why.
            </p>
          </form>
        ) : (
          <button
            type="button"
            className="login-submit"
            onClick={() => {
              setShowForm(true)
              setRaised(null)
            }}
          >
            Raise a request
          </button>
        )}
      </section>

      {loading && <p className="dashboard-message">Loading your requests…</p>}

      {error && <ReportNotice error={error} currentUser={currentUser} />}

      {!loading && !error && tickets.length > 0 && (
        <>
          <div className="tile-grid">
            <StatTile
              label="Still open"
              value={open.length.toLocaleString()}
              hint="With the support desk now."
            />
            <StatTile
              label="Awaiting your confirmation"
              value={awaitingConfirmation.length.toLocaleString()}
              tone={awaitingConfirmation.length > 0 ? 'warning' : 'neutral'}
              toneLabel={awaitingConfirmation.length > 0 ? 'Needs you' : undefined}
              hint="Marked resolved. Open one to confirm it is fixed, or reopen it."
            />
            <StatTile
              label="Raised in total"
              value={tickets.length.toLocaleString()}
              hint="Everything you have asked for, including closed requests."
            />
          </div>

          <section className="panel">
            <header className="panel-header">
              <h2>Your requests</h2>
              <p>Most urgent first. Open one to see who has it and everything that has happened.</p>
            </header>

            <TicketTable
              tickets={tickets}
              onOpen={onOpen}
              showRequester={false}
              emptyMessage="You have not raised anything yet."
            />
          </section>
        </>
      )}

      {!loading && !error && tickets.length === 0 && (
        <p className="chart-empty">
          You have not raised anything yet. When you do, it will appear here with its current state.
        </p>
      )}
    </div>
  )
}

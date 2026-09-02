import { useState } from 'react'
import { ApiError, api } from '../api/client'
import type { TicketDetail, TicketPriority, TicketStatus, User } from '../api/types'
import { humaniseStatus } from '../utils/format'
import './ticket-actions.css'

/**
 * What the signed-in person may do with this ticket.
 *
 * Every control here is rendered from `detail.permissions`, which the **server** computed. The
 * client never decides for itself whether an action is allowed — if it did, the rule would exist in
 * two places and the two would eventually disagree. Each action is re-checked server-side when
 * attempted, so this is an affordance, not a control: hiding a button prevents confusion, not abuse.
 */

const PRIORITIES: TicketPriority[] = ['Critical', 'High', 'Standard', 'Low']

/** Verbs, because "Move to In progress" reads like a database operation rather than a decision. */
const TRANSITION_VERB: Partial<Record<TicketStatus, string>> = {
  Triaged: 'Triage',
  Assigned: 'Mark assigned',
  InProgress: 'Start work',
  OnHold: 'Place on hold',
  Resolved: 'Resolve',
  Closed: 'Close',
  Cancelled: 'Cancel',
}

/**
 * The same transition means different things depending on where the ticket is.
 *
 * Moving a resolved ticket back to In progress is not "starting work", it is the requester saying
 * the fix did not hold; closing one is them confirming that it did. Labelling both by the target
 * status would make the requester's two options read as jargon aimed at somebody else.
 */
function verbFor(from: TicketStatus, to: TicketStatus): string {
  if (from === 'Resolved' && to === 'InProgress') {
    return 'Reopen'
  }

  if (from === 'Resolved' && to === 'Closed') {
    return 'Confirm the fix'
  }

  if (from === 'OnHold' && to === 'InProgress') {
    return 'Resume work'
  }

  return TRANSITION_VERB[to] ?? humaniseStatus(to)
}

/** Transitions that must be justified in writing before the server will accept them. */
const NOTE_REQUIRED: Partial<Record<TicketStatus, { label: string; placeholder: string }>> = {
  Resolved: {
    label: 'Resolution notes (required)',
    placeholder: 'What was wrong, and what you did about it.',
  },
  OnHold: {
    label: 'Reason for the hold (required)',
    placeholder: 'Who or what the ticket is waiting on.',
  },
  Cancelled: {
    label: 'Reason for cancelling',
    placeholder: 'Why this request is not going ahead.',
  },
  InProgress: {
    label: 'What is still wrong? (optional)',
    placeholder: 'Only needed if you are reopening because the fix did not hold.',
  },
}

interface TicketActionsProps {
  currentUser: User
  detail: TicketDetail
  technicians: User[]
  onChanged: () => void
}

export function TicketActions({
  currentUser,
  detail,
  technicians,
  onChanged,
}: TicketActionsProps) {
  const [pendingStatus, setPendingStatus] = useState<TicketStatus | null>(null)
  const [note, setNote] = useState('')
  const [comment, setComment] = useState('')
  const [assignee, setAssignee] = useState('')
  const [priority, setPriority] = useState<TicketPriority | ''>('')
  const [justification, setJustification] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const { permissions, summary } = detail
  const id = summary.id

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true)
    setError(null)

    try {
      await action()
      setPendingStatus(null)
      setNote('')
      setComment('')
      setJustification('')
      setPriority('')
      onChanged()
    } catch (caught) {
      // The server's wording, verbatim. It knows why it refused; the client would be guessing.
      setError(caught instanceof ApiError ? caught.message : 'The change could not be saved.')
    } finally {
      setBusy(false)
    }
  }

  const noteSpec = pendingStatus ? NOTE_REQUIRED[pendingStatus] : undefined
  const noteMandatory = pendingStatus === 'Resolved' || pendingStatus === 'OnHold'
  const nothingOffered =
    permissions.allowedNextStatuses.length === 0 &&
    !permissions.canAssignToSelf &&
    !permissions.canAssignToOthers &&
    !permissions.canChangePriority &&
    !permissions.canComment

  if (nothingOffered) {
    return (
      <p className="actions-none">
        This ticket is {humaniseStatus(summary.status).toLowerCase()} and there is nothing further
        for you to do with it. The record below remains readable.
      </p>
    )
  }

  return (
    <div className="ticket-actions">
      {permissions.allowedNextStatuses.length > 0 && (
        <div className="actions-row">
          <span className="actions-label">Move this ticket</span>
          <div className="actions-buttons">
            {permissions.allowedNextStatuses.map((status) => (
              <button
                key={status}
                type="button"
                className={pendingStatus === status ? 'action-chip is-selected' : 'action-chip'}
                disabled={busy}
                onClick={() => {
                  setPendingStatus(pendingStatus === status ? null : status)
                  setNote('')
                  setError(null)
                }}
              >
                {verbFor(summary.status, status)}
              </button>
            ))}
          </div>
        </div>
      )}

      {pendingStatus && (
        <div className="actions-form">
          {noteSpec && (
            <label className="actions-field">
              <span>{noteSpec.label}</span>
              <textarea
                rows={3}
                value={note}
                autoFocus
                placeholder={noteSpec.placeholder}
                onChange={(event) => setNote(event.target.value)}
              />
            </label>
          )}

          <div className="actions-confirm">
            <button
              type="button"
              className="login-submit"
              disabled={busy || (noteMandatory && note.trim().length === 0)}
              onClick={() =>
                void run(() =>
                  api.transitionTicket(
                    currentUser.id,
                    id,
                    pendingStatus,
                    note.trim() === '' ? undefined : note.trim(),
                  ),
                )
              }
            >
              {busy ? 'Saving…' : `Confirm: ${verbFor(summary.status, pendingStatus)}`}
            </button>
            <button
              type="button"
              className="link-button"
              disabled={busy}
              onClick={() => setPendingStatus(null)}
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* The server allows reassigning a ticket to yourself; it is harmless but does nothing, and a
          button that does nothing is a defect. Compared by id rather than display name. */}
      {permissions.canAssignToSelf && summary.assignedTechnicianId !== currentUser.id && (
        <div className="actions-row">
          <span className="actions-label">Ownership</span>
          <button
            type="button"
            className="action-chip"
            disabled={busy}
            onClick={() => void run(() => api.assignTicket(currentUser.id, id, currentUser.id))}
          >
            Assign to me
          </button>
        </div>
      )}

      {permissions.canAssignToOthers && (
        <div className="actions-row">
          <span className="actions-label">Assign to</span>
          <div className="actions-inline">
            <select
              value={assignee}
              disabled={busy}
              onChange={(event) => setAssignee(event.target.value)}
              aria-label="Technician"
            >
              <option value="">Choose a technician…</option>
              {technicians.map((technician) => (
                <option key={technician.id} value={technician.id}>
                  {technician.displayName}
                </option>
              ))}
            </select>
            <button
              type="button"
              className="action-chip"
              disabled={busy || assignee === ''}
              onClick={() => void run(() => api.assignTicket(currentUser.id, id, assignee))}
            >
              Assign
            </button>
          </div>
        </div>
      )}

      {permissions.canChangePriority && (
        <div className="actions-row">
          <span className="actions-label">Priority</span>
          <div className="actions-inline">
            <select
              value={priority}
              disabled={busy}
              onChange={(event) => setPriority(event.target.value as TicketPriority)}
              aria-label="Priority"
            >
              <option value="">Change priority…</option>
              {PRIORITIES.filter((p) => p !== summary.priority).map((p) => (
                <option key={p} value={p}>
                  {p}
                </option>
              ))}
            </select>
            <input
              type="text"
              value={justification}
              disabled={busy || priority === ''}
              placeholder="Justification (required)"
              aria-label="Justification"
              onChange={(event) => setJustification(event.target.value)}
            />
            <button
              type="button"
              className="action-chip"
              disabled={busy || priority === '' || justification.trim().length === 0}
              onClick={() =>
                void run(() =>
                  api.changeTicketPriority(
                    currentUser.id,
                    id,
                    priority as TicketPriority,
                    justification.trim(),
                  ),
                )
              }
            >
              Apply
            </button>
          </div>
          <p className="actions-note">
            Re-prioritising moves the target times this ticket is measured against, so the reason is
            recorded permanently alongside the change.
          </p>
        </div>
      )}

      {permissions.canComment && (
        <div className="actions-row">
          <span className="actions-label">Add a comment</span>
          <div className="actions-stack">
            <textarea
              rows={3}
              value={comment}
              disabled={busy}
              placeholder="Visible to everyone who can see this ticket."
              aria-label="Comment"
              onChange={(event) => setComment(event.target.value)}
            />
            <button
              type="button"
              className="action-chip"
              disabled={busy || comment.trim().length === 0}
              onClick={() =>
                void run(() => api.addTicketComment(currentUser.id, id, comment.trim()))
              }
            >
              Post comment
            </button>
          </div>
          {currentUser.role !== 'Requester' && detail.firstRespondedAt === null && (
            <p className="actions-note">
              This will be the first response from support, so it stops the response clock.
            </p>
          )}
        </div>
      )}

      {error && (
        <p className="admin-action-error" role="alert">
          {error}
        </p>
      )}
    </div>
  )
}

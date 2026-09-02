import type { TicketEvent, TicketEventType } from '../api/types'
import { formatDateTime, humaniseStatus } from '../utils/format'
import './ticket-timeline.css'

/**
 * A ticket's complete audit trail.
 *
 * The problem definition opens with a shared mailbox where nobody could say who had picked something
 * up, when it was answered, or why it stalled. This is the answer to that, and it is the reason the
 * screen exists at all: every state-changing operation writes an entry, the API exposes no route
 * that edits or deletes one, and the trail is shown to every role that can see the ticket rather
 * than being reserved for management.
 */

const EVENT_LABEL: Record<TicketEventType, string> = {
  Created: 'Raised',
  StatusChanged: 'Status changed',
  Assigned: 'Assigned',
  Unassigned: 'Unassigned',
  PriorityChanged: 'Priority changed',
  FirstResponseRecorded: 'First response',
  HoldStarted: 'Placed on hold',
  HoldEnded: 'Taken off hold',
  CommentAdded: 'Comment',
  Reopened: 'Reopened',
}

const ROLE_LABEL: Record<string, string> = {
  Requester: 'Requester',
  Technician: 'Technician',
  TeamLead: 'Service manager',
}

/**
 * The part of a stored detail worth showing, or null when it says nothing new.
 *
 * A status change records its detail as "InProgress -> Resolved." with any note appended. The header
 * already renders that transition in readable form, so repeating the raw enum names beside it is
 * noise — but the note that follows is exactly what a reader came for. This strips the prefix and
 * keeps the remainder, rather than choosing between showing both and losing the note.
 */
function meaningfulDetail(event: TicketEvent): string | null {
  const detail = event.detail.trim()

  if (event.eventType !== 'StatusChanged' || !event.fromStatus || !event.toStatus) {
    return detail === '' ? null : detail
  }

  const prefix = `${event.fromStatus} -> ${event.toStatus}.`
  const remainder = detail.startsWith(prefix) ? detail.slice(prefix.length).trim() : detail

  return remainder === '' ? null : remainder
}

export function TicketTimeline({ events }: { events: TicketEvent[] }) {
  if (events.length === 0) {
    // Not reachable through the application — raising a ticket writes an entry — but a screen that
    // renders nothing at all when handed an empty list is a worse failure than one that says so.
    return <p className="chart-empty">No activity has been recorded.</p>
  }

  return (
    <ol className="timeline">
      {events.map((event) => (
        <li key={event.id} className={`timeline-entry event-${event.eventType.toLowerCase()}`}>
          <div className="timeline-head">
            <strong>{EVENT_LABEL[event.eventType]}</strong>
            {event.fromStatus && event.toStatus && (
              <span className="timeline-transition">
                {humaniseStatus(event.fromStatus)} → {humaniseStatus(event.toStatus)}
              </span>
            )}
            <span className="timeline-when">{formatDateTime(event.occurredAt)}</span>
          </div>
          {meaningfulDetail(event) && (
            <p className="timeline-detail">{meaningfulDetail(event)}</p>
          )}
          <p className="timeline-actor">
            {event.actorName} · {ROLE_LABEL[event.actorRole] ?? event.actorRole}
          </p>
        </li>
      ))}
    </ol>
  )
}

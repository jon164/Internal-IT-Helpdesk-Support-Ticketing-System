import type { SlaState, TicketPriority, TicketStatus } from '../api/types'
import { formatRemaining, humaniseStatus } from '../utils/format'
import './ticket-badges.css'

/**
 * The small labels that carry a ticket's state.
 *
 * Every one of them pairs its colour with a word, and the SLA badge adds a glyph. State that is
 * carried by hue alone disappears for a colour-blind reader, in a printed appendix, and in
 * forced-colours mode — and a breached ticket is precisely the thing that must not disappear.
 */

const SLA_LABEL: Record<SlaState, string> = {
  NotApplicable: 'N/A',
  OnTrack: 'On track',
  AtRisk: 'At risk',
  Breached: 'Breached',
  Met: 'Met',
  Missed: 'Missed',
}

const SLA_GLYPH: Record<SlaState, string> = {
  NotApplicable: '–',
  OnTrack: '✓',
  AtRisk: '!',
  Breached: '×',
  Met: '✓',
  Missed: '×',
}

export function SlaBadge({ state, label }: { state: SlaState; label?: string }) {
  return (
    <span className={`sla-badge sla-${state.toLowerCase()}`}>
      <span aria-hidden="true">{SLA_GLYPH[state]}</span>
      {label ?? SLA_LABEL[state]}
    </span>
  )
}

/**
 * A target, its state and the time left against it.
 *
 * The remaining figure is the one a technician acts on, so it sits beside the state rather than
 * being left for them to infer from a percentage.
 */
export function SlaClock({
  caption,
  state,
  remainingMinutes,
}: {
  caption: string
  state: SlaState
  remainingMinutes: number
}) {
  const settled = state === 'Met' || state === 'Missed' || state === 'NotApplicable'

  return (
    <div className="sla-clock">
      <span className="sla-clock-caption">{caption}</span>
      <SlaBadge state={state} />
      {!settled && <span className="sla-clock-remaining">{formatRemaining(remainingMinutes)}</span>}
    </div>
  )
}

export function StatusBadge({ status }: { status: TicketStatus }) {
  return (
    <span className={`status-badge status-${status.toLowerCase()}`}>{humaniseStatus(status)}</span>
  )
}

/**
 * Priority, on the same four-step ordinal ramp the dashboard's charts use, so P1 means the same
 * shade in the queue as it does in "Raised by priority".
 */
export function PriorityBadge({
  priority,
  label,
}: {
  priority: TicketPriority
  label?: string
}) {
  return (
    <span className={`priority-badge priority-${priority.toLowerCase()}`}>
      {label ?? priority}
    </span>
  )
}

/**
 * Marks a ticket whose contents are restricted to the assigned technician, the requester and the
 * service manager. Shown wherever such a ticket is listed, so nobody is surprised that a colleague
 * cannot see what they are discussing.
 */
export function RestrictedBadge() {
  return (
    <span className="restricted-badge" title="Visible only to the requester, the assigned technician and the service manager.">
      Restricted
    </span>
  )
}

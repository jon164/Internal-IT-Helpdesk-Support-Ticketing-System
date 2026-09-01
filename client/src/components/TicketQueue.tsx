import type {
  Ticket,
  TicketSort,
} from '../types'

import {
  formatDate,
  STATUS_DISPLAY,
} from '../utils'

export function TicketQueue({
  tickets,
  selectedId,
  onSelect,
  sort,
  onSort,
  technicianView,
}: {
  tickets: Ticket[]
  selectedId: number | null
  onSelect: (id: number) => void
  sort: TicketSort
  onSort: (sort: TicketSort) => void
  technicianView: boolean
}) {
  return (
    <section className="panel queue-panel">
      <div className="panel-header queue-header">
        <div>
          <p className="eyebrow">
            LIVE QUEUE
          </p>

          <h3>
            {technicianView
              ? 'Technician ticket queue'
              : 'Ticket status'}
          </h3>
        </div>

        {/* USER STORY: Newest / Oldest sorting */}
        <label className="sort-box">
          <span>Sort</span>

          <select
            value={sort}
            onChange={event =>
              onSort(
                event.target
                  .value as TicketSort,
              )
            }
          >
            <option value="newest">
              Newest first
            </option>

            <option value="oldest">
              Oldest first
            </option>

            {technicianView && (
              <option value="priority-age">
                Priority + age
              </option>
            )}
          </select>
        </label>
      </div>

      <div className="ticket-list">
        {tickets.map(ticket => (
          <button
            type="button"
            key={ticket.id}
            className={
              selectedId === ticket.id
                ? 'ticket-card selected'
                : 'ticket-card'
            }
            onClick={() =>
              onSelect(ticket.id)
            }
          >
            <div className="ticket-top">
              <div>
                <strong>
                  #{ticket.id}
                </strong>

                <h4>
                  {ticket.systemType}
                </h4>
              </div>

              <span
                className={`status ${ticket.status.toLowerCase()}`}
              >
                {
                  STATUS_DISPLAY[
                    ticket.status
                  ]
                }
              </span>
            </div>

            <div className="ticket-location">
              {ticket.terminal} •{' '}
              {ticket.area}
            </div>

            <p>{ticket.description}</p>

            <div className="chips">
              <span
                className={`priority ${ticket.priority.toLowerCase()}`}
              >
                {ticket.priority}
              </span>

              {ticket.passengerImpact && (
                <span className="passenger-chip">
                  ● Passenger impact
                </span>
              )}

              {ticket.flightOpsImpact && (
                <span className="flight-chip">
                  ✈ Flight ops
                </span>
              )}

              {ticket.slaOverdue && (
                <span className="overdue-chip">
                  ! SLA overdue
                </span>
              )}

              {ticket.isEscalated && (
                <span className="escalated-chip">
                  ↑ Escalated
                </span>
              )}
            </div>

            <div className="ticket-meta">
              <span>
                {ticket.assignee}
              </span>

              {/* USER STORY:
                  Automatic creation date/time */}
              <span>
                {formatDate(
                  ticket.createdAtUtc,
                )}
              </span>
            </div>
          </button>
        ))}

        {tickets.length === 0 && (
          <div className="empty-state">
            No tickets are available.
          </div>
        )}
      </div>
    </section>
  )
}
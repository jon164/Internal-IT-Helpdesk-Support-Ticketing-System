import type { TicketSummary } from '../api/types'
import { formatDate, formatRemaining } from '../utils/format'
import { PriorityBadge, RestrictedBadge, SlaBadge, StatusBadge } from './TicketBadges'
import './ticket-table.css'

interface TicketTableProps {
  tickets: TicketSummary[]
  onOpen: (id: number) => void
  /** The requester's own list has no use for a column repeating their own name. */
  showRequester?: boolean
  showAssignee?: boolean
  emptyMessage: string
}

/**
 * A list of tickets, ordered as the server returned them — most urgent first.
 *
 * The row carries the resolution clock rather than the response clock. Both matter, but a queue is
 * scanned to answer "what should I pick up next", and that is a question about what is closest to
 * breaching its resolution target. The response state is on the ticket itself.
 */
export function TicketTable({
  tickets,
  onOpen,
  showRequester = true,
  showAssignee = true,
  emptyMessage,
}: TicketTableProps) {
  if (tickets.length === 0) {
    return <p className="chart-empty">{emptyMessage}</p>
  }

  return (
    <div className="ticket-table-wrap">
      <table className="ticket-table">
        <thead>
          <tr>
            <th scope="col">Reference</th>
            <th scope="col">Summary</th>
            {showRequester && <th scope="col">Raised by</th>}
            <th scope="col">Priority</th>
            <th scope="col">Status</th>
            {showAssignee && <th scope="col">Assigned</th>}
            <th scope="col">Resolution target</th>
          </tr>
        </thead>
        <tbody>
          {tickets.map((ticket) => {
            const settled =
              ticket.resolution.state === 'Met' ||
              ticket.resolution.state === 'Missed' ||
              ticket.resolution.state === 'NotApplicable'

            return (
              <tr key={ticket.id}>
                <td>
                  <button
                    type="button"
                    className="ticket-open link-button"
                    onClick={() => onOpen(ticket.id)}
                  >
                    {ticket.reference}
                  </button>
                </td>
                <td>
                  <span className="ticket-title">{ticket.title}</span>
                  <span className="ticket-meta">
                    {ticket.category}
                    {ticket.isRestricted && (
                      <>
                        {' '}
                        <RestrictedBadge />
                      </>
                    )}
                  </span>
                </td>
                {showRequester && (
                  <td>
                    <span className="ticket-title">{ticket.requesterName}</span>
                    <span className="ticket-meta">{ticket.department}</span>
                  </td>
                )}
                <td>
                  <PriorityBadge priority={ticket.priority} />
                </td>
                <td>
                  <StatusBadge status={ticket.status} />
                </td>
                {showAssignee && (
                  <td className="ticket-assignee">
                    {ticket.assignedTechnicianName ?? <span className="ticket-meta">Unassigned</span>}
                  </td>
                )}
                <td>
                  <div className="ticket-target">
                    <SlaBadge state={ticket.resolution.state} />
                    <span className="ticket-meta">
                      {settled
                        ? `Raised ${formatDate(ticket.createdAt)}`
                        : formatRemaining(ticket.resolution.remainingMinutes)}
                    </span>
                  </div>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

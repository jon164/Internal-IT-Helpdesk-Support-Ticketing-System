import { useState } from 'react'
import type { TicketNotification } from '../types'
import './TicketNotifications.css'

export function TicketNotifications({
  notifications,
  onMarkAllRead,
  onOpenTicket,
}: {
  notifications: TicketNotification[]
  onMarkAllRead: () => void
  onOpenTicket: (ticketId: number) => void
}) {
  const [isOpen, setIsOpen] = useState(false)
  const unreadCount = notifications.filter(notification => !notification.isRead).length

  return (
    <div className="ticket-notifications">
      <button
        type="button"
        className="secondary-button ticket-notifications-trigger"
        aria-expanded={isOpen}
        aria-controls="ticket-notifications-panel"
        onClick={() => setIsOpen(open => !open)}
      >
        Updates
        {unreadCount > 0 && <span className="notification-count">{unreadCount}</span>}
      </button>

      {isOpen && (
        <section className="ticket-notifications-panel" id="ticket-notifications-panel" aria-label="Ticket updates">
          <header>
            <strong>Ticket updates</strong>
            <button type="button" onClick={onMarkAllRead} disabled={unreadCount === 0}>
              Mark all read
            </button>
          </header>

          {notifications.length === 0 ? (
            <p className="ticket-notifications-empty">No ticket updates yet.</p>
          ) : (
            <ul>
              {notifications.map(notification => (
                <li key={notification.id}>
                  <button
                    type="button"
                    className={notification.isRead ? 'ticket-notification read' : 'ticket-notification'}
                    onClick={() => onOpenTicket(notification.ticketId)}
                  >
                    <strong>{notification.title}</strong>
                    <span>{notification.message}</span>
                    <time dateTime={notification.createdAtUtc}>
                      {new Date(notification.createdAtUtc).toLocaleString()}
                    </time>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}
    </div>
  )
}
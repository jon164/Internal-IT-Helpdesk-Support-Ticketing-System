import type { ApiError } from '../api/client'
import type { User } from '../api/types'

/**
 * What the reader sees when a report cannot be shown.
 *
 * The refusal case is deliberately explicit rather than the page simply not existing. Hiding a
 * route is not access control, and a manager who has handed a technician the wrong account needs to
 * understand what happened. It also makes the restriction demonstrable in a walkthrough.
 */
export function ReportNotice({ error, currentUser }: { error: ApiError; currentUser: User }) {
  return (
    <div className={`dashboard-notice${error.isForbidden ? ' is-forbidden' : ''}`} role="alert">
      <h2>{error.isForbidden ? 'Not available to your role' : 'The report could not be loaded'}</h2>
      <p>{error.message}</p>
      {error.isForbidden && (
        <p className="dashboard-notice-detail">
          You are signed in as {currentUser.displayName} ({currentUser.role}). Reporting is restricted
          to the service manager. The server refused this request — the page is not merely hidden.
        </p>
      )}
    </div>
  )
}

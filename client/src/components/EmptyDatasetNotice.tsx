import type { User } from '../api/types'

/**
 * Shown when a report has no tickets to describe.
 *
 * The application deliberately starts with an empty ticket table, so this is the first thing a new
 * reader sees. Without it the dashboard reads as broken — every figure zero, every chart blank, no
 * indication whether that is the truth or a failure. Saying which, and what to do about it, costs one
 * paragraph and removes the ambiguity.
 *
 * The instruction is only offered to somebody who can act on it. Telling a technician to press a
 * button they cannot reach would be worse than saying nothing.
 */
export function EmptyDatasetNotice({ currentUser }: { currentUser: User }) {
  return (
    <div className="empty-dataset" role="status">
      <p className="empty-dataset-title">No tickets to report on.</p>
      <p>
        {currentUser.role === 'TeamLead'
          ? 'This is an empty system rather than a failed report. Open the Accounts tab and use ' +
            'Demonstration data to generate a sample history.'
          : 'This is an empty system rather than a failed report. No support requests have been ' +
            'recorded yet.'}
      </p>
    </div>
  )
}

import { useCallback, useEffect, useState } from 'react'
import { ApiError, api } from '../api/client'
import type { SampleDataStatus, User } from '../api/types'

/**
 * Generates or removes the demonstration dataset.
 *
 * The application starts with no tickets. That is deliberate: a prototype that opens onto five
 * hundred pre-existing requests invites the reader to assume they came from somewhere real, and it
 * gives a demonstration nowhere to begin. Pressing Generate here produces the same fixed population
 * every time, so a walkthrough can be repeated and a defect reproduced on another machine.
 *
 * The routes behind these buttons are registered only when the API runs in Development. When they
 * are absent the API answers 404 and this panel renders nothing at all — which is the correct
 * behaviour for a production build, where the ability to fabricate or destroy every ticket should
 * not exist rather than merely be hidden.
 */
export function SampleDataPanel({ currentUser }: { currentUser: User }) {
  const [status, setStatus] = useState<SampleDataStatus | null>(null)
  const [available, setAvailable] = useState(true)
  const [busy, setBusy] = useState<'generate' | 'clear' | null>(null)
  const [confirmingClear, setConfirmingClear] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setStatus(await api.getSampleDataStatus(currentUser.id))
      setAvailable(true)
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 404) {
        // Not a fault. The endpoint does not exist outside Development.
        setAvailable(false)
        return
      }

      setError(
        caught instanceof ApiError ? caught.message : 'The dataset state could not be read.',
      )
    }
  }, [currentUser.id])

  useEffect(() => {
    void load()
  }, [load])

  const run = async (action: 'generate' | 'clear') => {
    setBusy(action)
    setError(null)
    setNotice(null)

    try {
      if (action === 'generate') {
        const result = await api.generateSampleData(currentUser.id)
        setStatus(result)
        setNotice(`Generated ${result.ticketCount.toLocaleString()} tickets.`)
      } else {
        const result = await api.clearSampleData(currentUser.id)
        setStatus(result.status)
        setConfirmingClear(false)
        setNotice(`Removed ${result.removedCount.toLocaleString()} tickets.`)
      }
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'The request failed.')
    } finally {
      setBusy(null)
    }
  }

  if (!available || !status) {
    return null
  }

  return (
    <section className="panel">
      <header className="panel-header">
        <h2>Demonstration data</h2>
        <p>
          Development build only. The generated population is fictional and identical on every
          machine, so figures quoted in a report can be reproduced exactly.
        </p>
      </header>

      <div className="sample-data">
        <p className="sample-data-state">
          {status.isEmpty ? (
            <>
              <strong>No tickets.</strong> The Performance and Backlog views have nothing to report
              until a dataset exists.
            </>
          ) : (
            <>
              <strong>{status.ticketCount.toLocaleString()} tickets</strong> spanning roughly the
              last six months.
            </>
          )}
        </p>

        <div className="sample-data-actions">
          <button
            type="button"
            className="login-submit"
            disabled={!status.isEmpty || busy !== null}
            onClick={() => void run('generate')}
          >
            {busy === 'generate'
              ? 'Generating…'
              : `Generate ${status.generatedCount.toLocaleString()} tickets`}
          </button>

          {!status.isEmpty && !confirmingClear && (
            <span className="sample-data-hint">
              Clear the existing tickets before generating again.
            </span>
          )}

          {!status.isEmpty &&
            (confirmingClear ? (
              <span className="sample-data-confirm" role="group" aria-label="Confirm clear">
                <span>
                  Delete all {status.ticketCount.toLocaleString()} tickets and their history?
                </span>
                <button
                  type="button"
                  className="link-button is-destructive"
                  disabled={busy !== null}
                  onClick={() => void run('clear')}
                >
                  {busy === 'clear' ? 'Clearing…' : 'Yes, clear'}
                </button>
                <button
                  type="button"
                  className="link-button"
                  disabled={busy !== null}
                  onClick={() => setConfirmingClear(false)}
                >
                  Cancel
                </button>
              </span>
            ) : (
              <button
                type="button"
                className="link-button is-destructive"
                disabled={busy !== null}
                onClick={() => {
                  setConfirmingClear(true)
                  setNotice(null)
                }}
              >
                Clear all tickets
              </button>
            ))}
        </div>

        {notice && <p className="sample-data-notice">{notice}</p>}

        {error && (
          <p className="admin-action-error" role="alert">
            {error}
          </p>
        )}

        <p className="sample-data-note">
          Accounts and the account change history are never touched by either button. Clearing them
          would sign everybody out and erase a record that is meant to be permanent.
        </p>
      </div>
    </section>
  )
}

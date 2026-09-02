import { useCallback, useEffect, useMemo, useState } from 'react'
import { ApiError, api } from '../api/client'
import type { User, UserAuditEntry, UserRole } from '../api/types'
import { ReportNotice } from '../components/ReportNotice'
import { StatTile } from '../components/StatTile'
import { formatDate } from '../utils/format'
import { SampleDataPanel } from './SampleDataPanel'
import './admin.css'

const ROLES: UserRole[] = ['Requester', 'Technician', 'TeamLead']

const ROLE_LABEL: Record<UserRole, string> = {
  Requester: 'Requester',
  Technician: 'Technician',
  TeamLead: 'Service manager',
}

const EVENT_LABEL: Record<UserAuditEntry['eventType'], string> = {
  RoleChanged: 'Role changed',
  Deactivated: 'Deactivated',
  Reactivated: 'Reactivated',
}

/** A change the administrator has begun but not yet justified. */
interface PendingChange {
  user: User
  kind: 'role' | 'active'
  role?: UserRole
  isActive?: boolean
}

/**
 * Account administration, held by the service manager.
 *
 * Every change here requires a reason and is written to an immutable log. That is not ceremony: the
 * problem definition opens with a shared mailbox that two departed staff could still read because
 * nobody recorded, or noticed, that their access was never revoked. An account whose privileges can
 * be changed silently reproduces the same failure in a new system.
 */
export function AdminView({ currentUser }: { currentUser: User }) {
  const [users, setUsers] = useState<User[]>([])
  const [audit, setAudit] = useState<UserAuditEntry[]>([])
  const [pending, setPending] = useState<PendingChange | null>(null)
  const [reason, setReason] = useState('')
  const [error, setError] = useState<ApiError | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)

    try {
      const [loadedUsers, loadedAudit] = await Promise.all([
        api.getAdminUsers(currentUser.id),
        api.getUserAudit(currentUser.id),
      ])

      setUsers(loadedUsers)
      setAudit(loadedAudit)
    } catch (caught) {
      setUsers([])
      setAudit([])
      setError(
        caught instanceof ApiError
          ? caught
          : new ApiError(0, {
              error: 'Invalid',
              message: 'Account administration could not be loaded. Is the API running?',
            }),
      )
    } finally {
      setLoading(false)
    }
  }, [currentUser.id])

  useEffect(() => {
    void load()
  }, [load])

  const counts = useMemo(() => {
    const active = users.filter((u) => u.isActive)

    return {
      total: users.length,
      active: active.length,
      inactive: users.length - active.length,
      managers: active.filter((u) => u.role === 'TeamLead').length,
      support: active.filter((u) => u.role === 'Technician' || u.role === 'TeamLead').length,
    }
  }, [users])

  const beginChange = (change: PendingChange) => {
    setPending(change)
    setReason('')
    setActionError(null)
  }

  const apply = async () => {
    if (!pending || reason.trim().length === 0) {
      return
    }

    setSaving(true)
    setActionError(null)

    try {
      if (pending.kind === 'role' && pending.role) {
        await api.changeUserRole(currentUser.id, pending.user.id, pending.role, reason.trim())
      } else if (pending.kind === 'active' && pending.isActive !== undefined) {
        await api.setUserActive(currentUser.id, pending.user.id, pending.isActive, reason.trim())
      }

      setPending(null)
      setReason('')
      await load()
    } catch (caught) {
      // The server's own wording is shown verbatim — it explains refusals such as the
      // last-administrator guard better than anything the client could invent.
      setActionError(caught instanceof ApiError ? caught.message : 'The change could not be saved.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="view">
      <div className="view-filters">
        <p className="view-range">
          {loading ? ' ' : `${counts.total} accounts`}
        </p>
        <div className="filter-controls">
          <button type="button" className="link-button" onClick={() => void load()}>
            Refresh
          </button>
        </div>
      </div>

      {loading && <p className="dashboard-message">Loading accounts…</p>}

      {error && <ReportNotice error={error} currentUser={currentUser} />}

      {!loading && !error && (
        <>
          <div className="tile-grid">
            <StatTile
              label="Active accounts"
              value={counts.active.toLocaleString()}
              hint={`${counts.inactive} deactivated and unable to sign in.`}
            />
            <StatTile
              label="Support staff"
              value={counts.support.toLocaleString()}
              hint="Technicians and the service manager."
            />
            <StatTile
              label="Service managers"
              value={counts.managers.toLocaleString()}
              tone={counts.managers <= 1 ? 'warning' : 'neutral'}
              toneLabel={counts.managers <= 1 ? 'Single point of failure' : undefined}
              hint={
                counts.managers <= 1
                  ? 'The last active service manager cannot be removed, so nobody can lock the system.'
                  : 'More than one account can administer the system.'
              }
            />
          </div>

          <section className="panel">
            <header className="panel-header">
              <h2>Accounts</h2>
              <p>Changing a role changes what that person can reach. Every change is recorded.</p>
            </header>

            <div className="admin-table-wrap">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th scope="col">Name</th>
                    <th scope="col">Department</th>
                    <th scope="col">Role</th>
                    <th scope="col">Status</th>
                  </tr>
                </thead>
                <tbody>
                  {users.map((user) => {
                    const isSelf = user.id === currentUser.id
                    const editing = pending?.user.id === user.id

                    return (
                      <tr key={user.id} className={user.isActive ? undefined : 'is-inactive'}>
                        <td>
                          {user.displayName}
                          {isSelf && <span className="admin-you">you</span>}
                        </td>
                        <td>{user.department}</td>
                        <td>
                          <select
                            value={editing && pending.role ? pending.role : user.role}
                            disabled={isSelf || saving}
                            onChange={(event) =>
                              beginChange({
                                user,
                                kind: 'role',
                                role: event.target.value as UserRole,
                              })
                            }
                          >
                            {ROLES.map((role) => (
                              <option key={role} value={role}>
                                {ROLE_LABEL[role]}
                              </option>
                            ))}
                          </select>
                        </td>
                        <td>
                          <button
                            type="button"
                            className="link-button"
                            disabled={isSelf || saving}
                            onClick={() =>
                              beginChange({ user, kind: 'active', isActive: !user.isActive })
                            }
                          >
                            {user.isActive ? 'Deactivate' : 'Reactivate'}
                          </button>
                          {!user.isActive && <span className="admin-inactive-tag">Inactive</span>}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>

            {pending && (
              <div className="admin-confirm" role="group" aria-label="Confirm change">
                <p className="admin-confirm-summary">
                  {pending.kind === 'role' ? (
                    <>
                      Change <strong>{pending.user.displayName}</strong> from{' '}
                      {ROLE_LABEL[pending.user.role]} to{' '}
                      <strong>{ROLE_LABEL[pending.role ?? pending.user.role]}</strong>.
                    </>
                  ) : (
                    <>
                      {pending.isActive ? 'Reactivate' : 'Deactivate'}{' '}
                      <strong>{pending.user.displayName}</strong>.
                      {!pending.isActive && ' They will be unable to sign in.'}
                    </>
                  )}
                </p>

                <label className="admin-reason">
                  <span>Reason (required)</span>
                  <input
                    type="text"
                    value={reason}
                    autoFocus
                    placeholder="e.g. Joining the support desk on secondment"
                    onChange={(event) => setReason(event.target.value)}
                  />
                </label>

                {actionError && (
                  <p className="admin-action-error" role="alert">
                    {actionError}
                  </p>
                )}

                <div className="admin-confirm-actions">
                  <button
                    type="button"
                    className="login-submit"
                    disabled={saving || reason.trim().length === 0}
                    onClick={() => void apply()}
                  >
                    {saving ? 'Saving…' : 'Apply change'}
                  </button>
                  <button
                    type="button"
                    className="link-button"
                    disabled={saving}
                    onClick={() => {
                      setPending(null)
                      setActionError(null)
                    }}
                  >
                    Cancel
                  </button>
                </div>
              </div>
            )}
          </section>

          <section className="panel">
            <header className="panel-header">
              <h2>Account change history</h2>
              <p>Who changed whose access, when, and why. Entries are never edited or removed.</p>
            </header>

            {audit.length === 0 ? (
              <p className="chart-empty">No account changes have been made yet.</p>
            ) : (
              <ol className="admin-audit">
                {audit.map((entry) => (
                  <li key={entry.id}>
                    <div className="admin-audit-head">
                      <strong>{EVENT_LABEL[entry.eventType]}</strong>
                      <span>{formatDate(entry.occurredAt)}</span>
                    </div>
                    <p>
                      {entry.subjectDisplayName}
                      {entry.fromRole && entry.toRole && (
                        <>
                          {' '}
                          — {ROLE_LABEL[entry.fromRole]} to {ROLE_LABEL[entry.toRole]}
                        </>
                      )}
                      {' · by '}
                      {entry.actorDisplayName}
                    </p>
                    <p className="admin-audit-reason">{entry.reason}</p>
                  </li>
                ))}
              </ol>
            )}
          </section>

          <SampleDataPanel currentUser={currentUser} />

          <footer className="dashboard-footer">
            <p>
              Account administration belongs to the service manager. On a four-person desk there is
              nobody else to hold it, so the same person carries both operational and account
              authority. That concentration is recorded as a known weakness: the compensating control
              is that every change here demands a reason and is written to a log nothing can edit.
            </p>
            <p>
              You cannot change your own role or deactivate your own account, and the last active
              service manager cannot be removed. Both guards exist so that the system cannot be
              locked in a state only direct database access could undo.
            </p>
          </footer>
        </>
      )}
    </div>
  )
}

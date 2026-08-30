import { useEffect, useMemo, useState } from 'react'
import './App.css'

type Technician = {
  id: string
  name: string
  email: string
  isActive: boolean
}

type TicketSummary = {
  id: string
  title: string
  status: number
  priority: number
  type: number
  createdAt: string
  updatedAt: string
  assignedTechnicianId: string | null
  assignedTechnicianName: string | null
  resolvedAt: string | null
  closedAt: string | null
  pendingSince: string | null
}

type TicketNote = {
  id: string
  authorId: string
  authorName: string
  content: string
  isInternal: boolean
  createdAt: string
}

type TicketAuditEntry = {
  id: string
  changedBy: string
  fromStatus: number | null
  toStatus: number | null
  action: string
  comment: string | null
  timestamp: string
}

type TicketLink = {
  id: string
  otherTicketId: string
  otherTicketTitle: string | null
  linkType: number
  createdAt: string
  direction: string
}

type TicketDetail = {
  id: string
  title: string
  description: string
  status: number
  priority: number
  type: number
  submitterName: string
  submitterEmail: string
  createdAt: string
  updatedAt: string
  assignedTechnicianId: string | null
  assignedTechnicianName: string | null
  resolvedAt: string | null
  closedAt: string | null
  pendingSince: string | null
  closedReason: string | null
  notes: TicketNote[]
  audit: TicketAuditEntry[]
  links: TicketLink[]
  allowedNextStatuses: number[]
}

const statusLabels: Record<number, string> = {
  0: 'New',
  1: 'In Progress',
  2: 'Pending Employee Response',
  3: 'Resolved',
  4: 'Closed',
}

const priorityLabels: Record<number, string> = {
  0: 'Low',
  1: 'Medium',
  2: 'High',
  3: 'Critical',
}

const priorityWeight: Record<number, number> = {
  3: 4,
  2: 3,
  1: 2,
  0: 1,
}

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {}),
    },
  })

  if (!response.ok) {
    const errorBody = await response.json().catch(() => null)
    const detail = errorBody?.detail ?? errorBody?.title ?? 'Request failed.'
    throw new Error(detail)
  }

  return (await response.json()) as T
}

function formatRelativeTime(value: string | null) {
  if (!value) return '—'

  const diffMs = Date.now() - new Date(value).getTime()
  const hours = Math.max(1, Math.round(diffMs / (1000 * 60 * 60)))
  if (hours < 24) return `${hours}h ago`

  const days = Math.round(hours / 24)
  return `${days}d ago`
}

function formatShortDate(value: string | null) {
  if (!value) return '—'
  return new Date(value).toLocaleString([], {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

function App() {
  const [technicians, setTechnicians] = useState<Technician[]>([])
  const [selectedTechnicianId, setSelectedTechnicianId] = useState('')
  const [assignedTickets, setAssignedTickets] = useState<TicketSummary[]>([])
  const [unassignedTickets, setUnassignedTickets] = useState<TicketSummary[]>([])
  const [selectedTicketId, setSelectedTicketId] = useState('')
  const [selectedTicket, setSelectedTicket] = useState<TicketDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [noteText, setNoteText] = useState('')
  const [statusComment, setStatusComment] = useState('')

  const selectedTechnician = useMemo(
    () => technicians.find((tech) => tech.id === selectedTechnicianId) ?? null,
    [technicians, selectedTechnicianId],
  )

  const loadTechnicians = async () => {
    const data = await apiFetch<Technician[]>('/api/technicians')
    setTechnicians(data)
    if (data.length > 0) {
      setSelectedTechnicianId((current) => current || data[0].id)
    }
  }

  const loadAssignedTickets = async (technicianId: string) => {
    const data = await apiFetch<TicketSummary[]>(`/api/technicians/${technicianId}/tickets`)
    setAssignedTickets(data)
    if (data.length > 0 && !data.some((ticket) => ticket.id === selectedTicketId)) {
      setSelectedTicketId(data[0].id)
    }
  }

  const loadUnassignedTickets = async () => {
    const data = await apiFetch<TicketSummary[]>('/api/tickets/unassigned')
    setUnassignedTickets(data)
  }

  const loadTicketDetail = async (ticketId: string) => {
    const data = await apiFetch<TicketDetail>(`/api/tickets/${ticketId}`)
    setSelectedTicket(data)
  }

  const refreshAll = async (technicianId: string) => {
    setLoading(true)
    setError('')

    try {
      await Promise.all([
        loadAssignedTickets(technicianId),
        loadUnassignedTickets(),
      ])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to load the ticket queue.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void (async () => {
      try {
        await loadTechnicians()
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Unable to load technicians.')
      }
    })()
  }, [])

  useEffect(() => {
    if (!selectedTechnicianId) return
    void refreshAll(selectedTechnicianId)
  }, [selectedTechnicianId])

  useEffect(() => {
    if (!selectedTicketId) return
    void loadTicketDetail(selectedTicketId)
  }, [selectedTicketId])

  const handleClaimTicket = async (ticketId: string) => {
    if (!selectedTechnician) return

    try {
      await apiFetch<TicketDetail>(`/api/tickets/${ticketId}/claim`, {
        method: 'POST',
        body: JSON.stringify({ technicianId: selectedTechnician.id }),
      })
      await refreshAll(selectedTechnician.id)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to claim the ticket.')
    }
  }

  const handleStatusChange = async (newStatus: number) => {
    if (!selectedTicket || !selectedTechnician) return

    try {
      await apiFetch<TicketDetail>(`/api/tickets/${selectedTicket.id}/status`, {
        method: 'POST',
        body: JSON.stringify({
          newStatus,
          comment: statusComment || undefined,
          changedBy: selectedTechnician.name,
        }),
      })
      setStatusComment('')
      await refreshAll(selectedTechnician.id)
      await loadTicketDetail(selectedTicket.id)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to update the ticket status.')
    }
  }

  const handleCloseTicket = async () => {
    if (!selectedTicket || !selectedTechnician) return

    try {
      await apiFetch<TicketDetail>(`/api/tickets/${selectedTicket.id}/close`, {
        method: 'POST',
        body: JSON.stringify({
          comment: 'Closed after technician validation',
          changedBy: selectedTechnician.name,
        }),
      })
      await refreshAll(selectedTechnician.id)
      await loadTicketDetail(selectedTicket.id)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to close the ticket.')
    }
  }

  const handleAddNote = async () => {
    if (!selectedTicket || !selectedTechnician || !noteText.trim()) {
      setError('A note cannot be blank.')
      return
    }

    try {
      await apiFetch<TicketDetail>(`/api/tickets/${selectedTicket.id}/notes`, {
        method: 'POST',
        body: JSON.stringify({
          authorId: selectedTechnician.id,
          authorName: selectedTechnician.name,
          content: noteText.trim(),
          isInternal: true,
        }),
      })
      setNoteText('')
      setError('')
      await refreshAll(selectedTechnician.id)
      await loadTicketDetail(selectedTicket.id)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to save the note.')
    }
  }

  const activeTicketList = useMemo(
    () => [...assignedTickets].sort((a, b) => {
      const priorityDiff = priorityWeight[b.priority] - priorityWeight[a.priority]
      if (priorityDiff !== 0) return priorityDiff
      return new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
    }),
    [assignedTickets],
  )

  if (!selectedTechnicianId && !loading) {
    return <div className="empty-state">No active technicians were found.</div>
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <div>
          <p className="eyebrow">Internal IT Helpdesk</p>
          <h1>Technician Queue</h1>
        </div>

        <div className="topbar-controls">
          <label>
            Technician
            <select
              value={selectedTechnicianId}
              onChange={(event) => setSelectedTechnicianId(event.target.value)}
            >
              {technicians.map((tech) => (
                <option key={tech.id} value={tech.id}>
                  {tech.name}
                </option>
              ))}
            </select>
          </label>
        </div>
      </header>

      {error ? <div className="error-banner">{error}</div> : null}

      <div className="content-grid">
        <aside className="queue-panel">
          <div className="panel-header">
            <h2>Assigned Tickets</h2>
            <span>{activeTicketList.length}</span>
          </div>

          {loading ? (
            <div className="empty-state compact">Loading tickets…</div>
          ) : activeTicketList.length === 0 ? (
            <div className="empty-state compact">
              No tickets assigned.
              <small>Check the unassigned pool for new work.</small>
            </div>
          ) : (
            <ul className="ticket-list">
              {activeTicketList.map((ticket) => (
                <li
                  key={ticket.id}
                  className={ticket.id === selectedTicketId ? 'selected' : ''}
                  onClick={() => setSelectedTicketId(ticket.id)}
                >
                  <div className="ticket-row-top">
                    <span className={`badge priority-${priorityLabels[ticket.priority].toLowerCase()}`}>
                      {priorityLabels[ticket.priority]}
                    </span>
                    <span className="badge status-badge">{statusLabels[ticket.status]}</span>
                  </div>

                  <h3>{ticket.title}</h3>
                  <p>{ticket.assignedTechnicianName ?? 'Unassigned'}</p>
                  <div className="ticket-meta">
                    <span>{formatRelativeTime(ticket.createdAt)}</span>
                    <span>{ticket.id.slice(0, 8)}</span>
                  </div>
                </li>
              ))}
            </ul>
          )}

          <div className="panel-header secondary">
            <h2>Unassigned</h2>
            <span>{unassignedTickets.length}</span>
          </div>

          <ul className="ticket-list compact-list">
            {unassignedTickets.slice(0, 5).map((ticket) => (
              <li key={ticket.id} className="unassigned-item">
                <div>
                  <strong>{ticket.title}</strong>
                  <small>{priorityLabels[ticket.priority]} • {formatRelativeTime(ticket.createdAt)}</small>
                </div>
                <button type="button" onClick={() => handleClaimTicket(ticket.id)}>
                  Claim
                </button>
              </li>
            ))}
          </ul>
        </aside>

        <main className="detail-panel">
          {!selectedTicket ? (
            <div className="empty-state detail-empty">Select a ticket to review the details.</div>
          ) : (
            <>
              <div className="detail-header">
                <div>
                  <p className="eyebrow">Ticket #{selectedTicket.id.slice(0, 8)}</p>
                  <h2>{selectedTicket.title}</h2>
                </div>
                <div className="detail-badges">
                  <span className={`badge priority-${priorityLabels[selectedTicket.priority].toLowerCase()}`}>
                    {priorityLabels[selectedTicket.priority]}
                  </span>
                  <span className="badge status-badge">{statusLabels[selectedTicket.status]}</span>
                </div>
              </div>

              <div className="detail-grid">
                <section className="detail-card">
                  <h3>Ticket details</h3>
                  <dl>
                    <div>
                      <dt>Submitter</dt>
                      <dd>{selectedTicket.submitterName}</dd>
                    </div>
                    <div>
                      <dt>Assigned</dt>
                      <dd>{selectedTicket.assignedTechnicianName ?? 'Unassigned'}</dd>
                    </div>
                    <div>
                      <dt>Created</dt>
                      <dd>{formatShortDate(selectedTicket.createdAt)}</dd>
                    </div>
                    <div>
                      <dt>Updated</dt>
                      <dd>{formatShortDate(selectedTicket.updatedAt)}</dd>
                    </div>
                  </dl>
                  <p className="description">{selectedTicket.description}</p>
                </section>

                <section className="detail-card actions-card">
                  <h3>Actions</h3>
                  <label>
                    Change status
                    <select
                      value={selectedTicket.status}
                      onChange={(event) => handleStatusChange(Number(event.target.value))}
                    >
                      {selectedTicket.allowedNextStatuses.map((status) => (
                        <option key={status} value={status}>
                          {statusLabels[status]}
                        </option>
                      ))}
                    </select>
                  </label>

                  <textarea
                    value={statusComment}
                    onChange={(event) => setStatusComment(event.target.value)}
                    placeholder="Optional status update comment"
                  />

                  <div className="button-row">
                    <button type="button" className="primary" onClick={() => handleStatusChange(selectedTicket.status)}>
                      Save status
                    </button>
                    <button type="button" onClick={handleCloseTicket}>
                      Close ticket
                    </button>
                  </div>
                </section>
              </div>

              <section className="detail-card notes-card">
                <h3>Internal notes</h3>
                <div className="note-list">
                  {selectedTicket.notes.length === 0 ? (
                    <p className="muted">No internal notes yet.</p>
                  ) : (
                    selectedTicket.notes.map((note) => (
                      <div key={note.id} className="note-item">
                        <div className="note-header">
                          <strong>{note.authorName}</strong>
                          <span>{formatShortDate(note.createdAt)}</span>
                        </div>
                        <p>{note.content}</p>
                      </div>
                    ))
                  )}
                </div>

                <textarea
                  value={noteText}
                  onChange={(event) => setNoteText(event.target.value)}
                  placeholder="Add an internal note for the technician team"
                />
                <button type="button" className="primary" onClick={handleAddNote}>
                  Add note
                </button>
              </section>

              <section className="detail-card audit-card">
                <h3>Audit trail</h3>
                <ul className="audit-list">
                  {selectedTicket.audit.map((entry) => (
                    <li key={entry.id}>
                      <strong>{entry.action}</strong>
                      <span>{entry.changedBy}</span>
                      <small>{entry.comment ?? 'No comment'}</small>
                      <small>{formatShortDate(entry.timestamp)}</small>
                    </li>
                  ))}
                </ul>
              </section>
            </>
          )}
        </main>
      </div>
    </div>
  )
}

export default App

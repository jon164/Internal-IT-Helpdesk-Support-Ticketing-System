import {
  useEffect,
  useState,
} from 'react'

import {
  api,
  ApiError,
} from '../services/api'

import type {
  Notice,
  Role,
  Technician,
  Ticket,
  TicketNote,
  TicketStatus,
} from '../types'

import {
  formatDate,
  formatFileSize,
  STATUS_DISPLAY,
  STATUS_OPTIONS,
} from '../utils'

async function getTicketNotes(id: number, role: Role): Promise<TicketNote[]> {
  try {
    const loaded = await api.getNotes(id, role)
    return role === 'Airport Staff'
      ? loaded.filter(note => !note.isInternal)
      : loaded
  } catch {
    return []
  }
}

export function TicketDetails({
  ticket,
  role,
  onChanged,
  setNotice,
}: {
  ticket: Ticket | null
  role: Role
  onChanged:
    | ((ticket: Ticket) => Promise<void>)
    | ((ticket: Ticket) => void)
  setNotice: (notice: Notice) => void
}) {
  const [status, setStatus] =
    useState<TicketStatus>(ticket?.status ?? 'New')

  const [assignee, setAssignee] =
    useState(ticket?.assignee ?? 'Unassigned')

  const [workaround, setWorkaround] =
    useState(ticket?.workaround ?? '')

  const [
    escalationReason,
    setEscalationReason,
  ] = useState('')

  const [noteBody, setNoteBody] =
    useState('')

  const [commentBody, setCommentBody] =
    useState('')

  const [replyBody, setReplyBody] =
    useState('')

  const [notes, setNotes] =
    useState<TicketNote[]>([])

  const [technicians, setTechnicians] =
    useState<Technician[]>([])

  const isIT =
    role === 'IT Technician' ||
    role === 'IT Manager'

  useEffect(() => {
    async function loadTechnicians() {
      try {
        console.log('[TicketDetails] Loading technicians...')
        const techs = await api.getTechnicians()
        console.log('[TicketDetails] Technicians loaded:', techs)
        setTechnicians(techs)
      } catch (error) {
        console.error('[TicketDetails] Failed to load technicians:', error)
        setTechnicians([])
      }
    }
    void loadTechnicians()
  }, [])

  useEffect(() => {
    if (!ticket) return

    let active = true
    void getTicketNotes(ticket.id, role).then(loaded => {
      if (active) setNotes(loaded)
    })

    return () => {
      active = false
    }
  }, [ticket?.id, role])

  async function loadNotes(id: number) {
    setNotes(await getTicketNotes(id, role))
  }

  function errorText(error: unknown) {
    if (
      error instanceof ApiError &&
      error.details?.validNextStatuses
    ) {
      return `${
        error.message
      } Valid next status: ${
        error.details.validNextStatuses.join(
          ', ',
        ) || 'none'
      }.`
    }

    return error instanceof ApiError
      ? error.message
      : 'The operation failed.'
  }

  async function save() {
    if (!ticket) return

    try {
      console.log('[TicketDetails] Saving ticket', { ticketId: ticket.id, status, assignee, role })
      const updated =
        await api.updateTicket(
          ticket.id,
          {
            status,
            assignee,
            workaround,
          },
          role,
        )

      console.log('[TicketDetails] Save successful', updated)
      setNotice({
        type: 'success',
        text: `Ticket #${ticket.id} updated.`,
      })

      await onChanged(updated)
    } catch (error) {
      console.error('[TicketDetails] Save failed', error)
      setNotice({
        type: 'error',
        text: errorText(error),
      })
    }
  }

  async function claim() {
    if (!ticket) return

    // Use first available technician
    const technicianName = technicians.length > 0 ? technicians[0].name : 'Unassigned'

    try {
      console.log('[TicketDetails] Claiming ticket', { ticketId: ticket.id, technicianName, role })
      const updated =
        await api.claimTicket(
          ticket.id,
          technicianName,
          role,
        )

      console.log('[TicketDetails] Claim successful', updated)
      setNotice({
        type: 'success',
        text: `Ticket #${ticket.id} claimed by ${updated.assignee}.`,
      })

      await onChanged(updated)
    } catch (error) {
      console.error('[TicketDetails] Claim failed', error)
      setNotice({
        type: 'error',
        text: errorText(error),
      })
    }
  }

  async function reassign() {
    if (!ticket) return

    try {
      console.log('[TicketDetails] Reassigning ticket', { ticketId: ticket.id, assignee, role })
      const updated =
        await api.reassignTicket(
          ticket.id,
          assignee,
          role,
        )

      console.log('[TicketDetails] Reassign successful', updated)
      setNotice({
        type: 'success',
        text: `Ticket #${ticket.id} reassigned to ${updated.assignee}.`,
      })

      await onChanged(updated)
    } catch (error) {
      console.error('[TicketDetails] Reassign failed', error)
      setNotice({
        type: 'error',
        text: errorText(error),
      })
    }
  }

  async function escalate() {
    if (!ticket) return

    try {
      console.log('[TicketDetails] Escalating ticket', { ticketId: ticket.id, escalationReason, role })
      const updated =
        await api.escalateTicket(
          ticket.id,
          escalationReason,
          role,
        )

      setEscalationReason('')

      console.log('[TicketDetails] Escalate successful', updated)
      setNotice({
        type: 'success',
        text: `Ticket #${ticket.id} escalated to ${updated.priority} priority.`,
      })

      await onChanged(updated)
      await loadNotes(ticket.id)
    } catch (error) {
      console.error('[TicketDetails] Escalate failed', error)
      setNotice({
        type: 'error',
        text: errorText(error),
      })
    }
  }

  async function addNote() {
    if (!ticket) return

    // Determine note author based on role
    let author = 'Technician'
    if (role === 'IT Manager') {
      author = 'IT Manager'
    } else if (technicians.length > 0) {
      author = technicians[0].name
    }

    try {
      console.log('[TicketDetails] Adding note', { ticketId: ticket.id, author, role })
      await api.addNote(
        ticket.id,
        author,
        noteBody,
        role,
      )

      setNoteBody('')

      console.log('[TicketDetails] Note added, reloading notes')
      await loadNotes(ticket.id)

      setNotice({
        type: 'success',
        text: 'Internal note added.',
      })
    } catch (error) {
      console.error('[TicketDetails] Add note failed', error)
      setNotice({
        type: 'error',
        text: errorText(error),
      })
    }
  }

  async function addComment() {
    if (!ticket) return

    const content = commentBody.trim()
    if (!content) {
      setNotice({
        type: 'error',
        text: 'Please enter a comment before submitting it.',
      })
      return
    }

    const author = ticket.reporterName || 'Airport Staff'

    try {
      console.log('[TicketDetails] Adding comment', { ticketId: ticket.id, author, role })
      const note = await api.addComment(
        ticket.id,
        author,
        content,
        role,
      )

      setCommentBody('')
      setNotes(current => [
        ...current,
        {
          id: note.id,
          ticketId: note.ticketId ?? ticket.id,
          author: note.author,
          body: note.body,
          createdAtUtc: note.createdAtUtc,
          isInternal: false,
        },
      ])

      setNotice({
        type: 'success',
        text: 'Your comment has been added to the ticket.',
      })
    } catch (error) {
      console.error('[TicketDetails] Add comment failed', error)
      setNotice({
        type: 'error',
        text: errorText(error),
      })
    }
  }

  async function replyToEmployee() {
    if (!ticket) return

    const content = replyBody.trim()
    if (!content) {
      setNotice({ type: 'error', text: 'Enter a reply before sending it.' })
      return
    }

    const author = ticket.assignee !== 'Unassigned'
      ? ticket.assignee
      : technicians[0]?.name ?? 'IT Technician'

    try {
      const note = await api.addComment(ticket.id, author, content, role)
      setReplyBody('')
      setNotes(current => [...current, {
        id: note.id,
        ticketId: note.ticketId ?? ticket.id,
        author: note.author,
        body: note.body,
        createdAtUtc: note.createdAtUtc,
        isInternal: false,
      }])
      setNotice({ type: 'success', text: 'Your reply has been sent to the employee.' })
    } catch (error) {
      setNotice({ type: 'error', text: errorText(error) })
    }
  }

  if (!ticket) {
    return (
      <section className="panel details-panel">
        <div className="empty-state">
          Select a ticket to view its
          details.
        </div>
      </section>
    )
  }

  return (
    <section className="panel details-panel">
      <div className="panel-header">
        <div>
          <p className="eyebrow">
            TICKET #{ticket.id}
          </p>

          <h3>{ticket.systemType}</h3>

          <p>
            {ticket.terminal} •{' '}
            {ticket.area}
          </p>
        </div>

        <span
          className={`status ${ticket.status.toLowerCase()}`}
        >
          {STATUS_DISPLAY[ticket.status]}
        </span>
      </div>

      <div className="detail-grid">
        <Detail
          label="Priority"
          value={ticket.priority}
        />

        {/* USER STORY:
            Automatic creation date/time */}
        <Detail
          label="Created"
          value={formatDate(
            ticket.createdAtUtc,
          )}
        />

        <Detail
          label="Passenger impact"
          value={
            ticket.passengerImpact
              ? 'Yes'
              : 'No'
          }
        />

        <Detail
          label="Flight ops impact"
          value={
            ticket.flightOpsImpact
              ? 'Yes'
              : 'No'
          }
        />
      </div>

      {ticket.slaOverdue && (
        <div className="alert-card danger">
          <strong>SLA breach</strong>

          <span>
            This unresolved ticket is
            outside the configured
            response window.
          </span>
        </div>
      )}

      {ticket.isEscalated && (
        <div className="alert-card warning">
          <strong>Escalated</strong>

          <span>
            {ticket.escalationReason ||
              'Escalation reason not provided.'}
          </span>
        </div>
      )}

      <div className="detail-section">
        <h4>Problem description</h4>
        <p>{ticket.description}</p>
      </div>

      {ticket.attachmentUrl && (
        <div className="detail-section">
          <h4>Attachment</h4>

          <a
            className="attachment-preview"
            href={ticket.attachmentUrl}
            target="_blank"
            rel="noreferrer"
          >
            <img
              src={ticket.attachmentUrl}
              alt="Ticket attachment"
            />

            <div>
              <strong>
                {ticket.attachmentOriginalName ??
                  'Attachment'}
              </strong>

              <span>
                {formatFileSize(
                  ticket.attachmentSize,
                )}{' '}
                • Open image
              </span>
            </div>
          </a>
        </div>
      )}

      {/* USER STORY:
          Reporter contact details */}
      <div className="detail-section">
        <h4>Reporter contact</h4>

        <div className="contact-card">
          <div className="avatar">
            {(ticket.reporterName ||
              'Airport Staff')
              .split(' ')
              .map(
                name =>
                  name[0] ?? '',
              )
              .join('')
              .slice(0, 2)}
          </div>

          <div>
            <strong>
              {ticket.reporterName ||
                'Airport Staff'}
            </strong>

            <span>
              {ticket.reporterEmail ||
                'No email provided'}
            </span>

            <small>
              Staff ID:{' '}
              {ticket.staffId ||
                'Not recorded'}
            </small>
          </div>
        </div>
      </div>

      <div className="detail-section">
        <h4>
          Status and suggested workaround
        </h4>

        <div className="workaround-box">
          <div>
            <span>Current status</span>

            <strong>
              {
                STATUS_DISPLAY[
                  ticket.status
                ]
              }
            </strong>
          </div>

          {ticket.workaround ? (
            <div className="workaround">
              <span>
                Suggested workaround
              </span>

              <strong>
                {ticket.workaround}
              </strong>
            </div>
          ) : (
            <div className="no-workaround">
              No workaround has been
              provided yet.
            </div>
          )}
        </div>
      </div>

      {role === 'Airport Staff' && (
        <div className="notes-box">
          <h4>Ticket comments</h4>

          <p>
            Add any extra details or follow-up information for the technician.
          </p>

          <div className="note-compose">
            <textarea
              rows={2}
              value={commentBody}
              onChange={event =>
                setCommentBody(
                  event.target.value,
                )
              }
              placeholder="Add more context about the issue, what you tried, or what passengers are seeing..."
            />

            <button
              type="button"
              className="secondary-button"
              onClick={addComment}
            >
              Add comment
            </button>
          </div>

          <div className="note-list">
            {notes.map(note => (
              <article key={note.id}>
                <div>
                  <strong>
                    {note.author}
                  </strong>

                  <span>
                    {formatDate(
                      note.createdAtUtc,
                    )}
                  </span>
                </div>

                <p>{note.body}</p>
              </article>
            ))}

            {notes.length === 0 && (
              <small>
                No comments yet.
              </small>
            )}
          </div>
        </div>
      )}

      {isIT && (
        <div className="technician-tools">
          <div className="tool-heading">
            <div>
              <p className="eyebrow">
                IT ONLY
              </p>

              <h4>
                Technician controls
              </h4>
            </div>

            {ticket.assignee ===
              'Unassigned' && (
              <button
                type="button"
                className="primary-button"
                onClick={claim}
              >
                Claim ticket
              </button>
            )}
          </div>

          <div className="form-grid two">
            <label>
              <span>Status</span>

              <select
                value={status}
                onChange={event =>
                  setStatus(
                    event.target
                      .value as TicketStatus,
                  )
                }
              >
                {STATUS_OPTIONS.map(
                  option => (
                    <option
                      key={option}
                      value={option}
                    >
                      {
                        STATUS_DISPLAY[
                          option
                        ]
                      }
                    </option>
                  ),
                )}
              </select>
            </label>

            <label>
              <span>Assignee</span>

              <select
                value={assignee}
                onChange={event =>
                  setAssignee(
                    event.target.value,
                  )
                }
              >
                <option value="Unassigned">
                  Unassigned
                </option>

                {technicians.map(
                  technician => (
                    <option
                      key={technician.id}
                      value={technician.name}
                    >
                      {technician.name}
                    </option>
                  ),
                )}
              </select>
            </label>
          </div>

          <label>
            <span>
              Workaround message
            </span>

            <textarea
              rows={3}
              value={workaround}
              onChange={event =>
                setWorkaround(
                  event.target.value,
                )
              }
              placeholder="Give staff a safe temporary workaround if one is available."
            />
          </label>

          <div className="form-actions">
            <button
              type="button"
              className="primary-button"
              onClick={save}
            >
              Save status/workaround
            </button>

            <button
              type="button"
              className="secondary-button"
              onClick={reassign}
            >
              Reassign
            </button>
          </div>

          <div className="escalation-box">
            <label>
              <span>
                Escalation reason
              </span>

              <textarea
                rows={2}
                value={escalationReason}
                onChange={event =>
                  setEscalationReason(
                    event.target.value,
                  )
                }
                placeholder="Explain why this issue needs higher priority or attention."
              />
            </label>

            <button
              type="button"
              className="danger-button"
              onClick={escalate}
            >
              Escalate ticket
            </button>
          </div>

          <div className="notes-box">
            <h4>Reply to employee</h4>
            <p>This response is visible to the ticket submitter and creates an update notification.</p>
            <div className="note-compose">
              <textarea
                rows={2}
                value={replyBody}
                onChange={event => setReplyBody(event.target.value)}
                placeholder="Write a response or request for the employee..."
              />
              <button
                type="button"
                className="primary-button"
                onClick={replyToEmployee}
              >
                Send reply
              </button>
            </div>
          </div>

          <div className="notes-box">
            <h4>Internal IT notes</h4>

            <p>
              These notes are available
              only through IT-role API
              access.
            </p>

            <div className="note-compose">
              <textarea
                rows={2}
                value={noteBody}
                onChange={event =>
                  setNoteBody(
                    event.target.value,
                  )
                }
                placeholder="Add investigation details for IT staff..."
              />

              <button
                type="button"
                className="secondary-button"
                onClick={addNote}
              >
                Add note
              </button>
            </div>

            <div className="note-list">
              {notes.map(note => (
                <article key={note.id}>
                  <div>
                    <strong>
                      {note.author}
                    </strong>

                    <span>
                      {formatDate(
                        note.createdAtUtc,
                      )}
                    </span>
                  </div>

                  <p>{note.body}</p>
                </article>
              ))}

              {notes.length === 0 && (
                <small>
                  No internal notes yet.
                </small>
              )}
            </div>
          </div>
        </div>
      )}
    </section>
  )
}

function Detail({
  label,
  value,
}: {
  label: string
  value: string
}) {
  return (
    <div className="detail">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  )
}
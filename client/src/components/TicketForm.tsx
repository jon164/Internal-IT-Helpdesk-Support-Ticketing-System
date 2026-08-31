import {
  useRef,
  useState,
  type ChangeEvent,
  type FormEvent,
} from 'react'

import {
  api,
  ApiError,
} from '../services/api'

import type {
  CreateTicketRequest,
  Notice,
  StaffProfile,
  Ticket,
} from '../types'

import {
  PRIORITY_OPTIONS,
} from '../utils'

const EMPTY_ISSUE = {
  terminal: '',
  area: '',
  systemType: '',
  description: '',
  passengerImpact: false,
  flightOpsImpact: false,
  priority: 'Medium' as const,
}

const ACCOUNT_REPORTER = {
  reporterName: 'Jamie Santos',
  reporterEmail:
    'jamie.santos@airport.test',
  staffId: 'AIR1001',
}

export function TicketForm({
  onCreated,
  setNotice,
}: {
  onCreated:
    | ((ticket: Ticket) => Promise<void>)
    | ((ticket: Ticket) => void)
  setNotice: (notice: Notice) => void
}) {
  const [mode, setMode] =
    useState<'account' | 'shared'>(
      'account',
    )

  const [badgeId, setBadgeId] =
    useState('AIR1001')

  const [verified, setVerified] =
    useState<StaffProfile | null>(null)

  const [attachment, setAttachment] =
    useState<File | null>(null)

  const fileRef =
    useRef<HTMLInputElement | null>(
      null,
    )

  const [form, setForm] =
    useState<CreateTicketRequest>({
      ...EMPTY_ISSUE,
      ...ACCOUNT_REPORTER,
    })

  function update(
    event: ChangeEvent<
      | HTMLInputElement
      | HTMLTextAreaElement
      | HTMLSelectElement
    >,
  ) {
    const target = event.target

    if (
      target instanceof
        HTMLInputElement &&
      target.type === 'checkbox'
    ) {
      setForm(current => ({
        ...current,
        [target.name]:
          target.checked,
      }))
    } else {
      setForm(current => ({
        ...current,
        [target.name]:
          target.value,
      }))
    }
  }

  function changeMode(
    next: 'account' | 'shared',
  ) {
    setMode(next)
    setVerified(null)

    if (next === 'account') {
      setForm(current => ({
        ...current,
        ...ACCOUNT_REPORTER,
      }))
    } else {
      setForm(current => ({
        ...current,
        reporterName: '',
        reporterEmail: '',
        staffId: '',
      }))
    }
  }

  async function verifyBadge() {
    try {
      const staff =
        await api.verifyBadge(badgeId)

      setVerified(staff)

      setForm(current => ({
        ...current,
        reporterName: staff.name,
        reporterEmail: staff.email,
        staffId: staff.badgeId,
      }))

      setNotice({
        type: 'success',
        text: `Badge verified for ${staff.name}.`,
      })
    } catch (error) {
      setVerified(null)

      setNotice({
        type: 'error',
        text:
          error instanceof ApiError
            ? error.message
            : 'Badge verification failed.',
      })
    }
  }

  /*
    USER STORY: Clear Ticket Form

    Ticket information and attachment are reset.

    The logged-in or verified reporter identity
    is kept because it identifies the staff member,
    rather than being part of the IT issue itself.
  */
  function clear(showNotice = true) {
    if (
      mode === 'shared' &&
      verified
    ) {
      setForm({
        ...EMPTY_ISSUE,
        reporterName: verified.name,
        reporterEmail: verified.email,
        staffId: verified.badgeId,
      })
    } else if (mode === 'account') {
      setForm({
        ...EMPTY_ISSUE,
        ...ACCOUNT_REPORTER,
      })
    } else {
      setForm({
        ...EMPTY_ISSUE,
        reporterName: '',
        reporterEmail: '',
        staffId: '',
      })
    }

    setAttachment(null)

    if (fileRef.current) {
      fileRef.current.value = ''
    }

    if (showNotice) {
      setNotice({
        type: 'info',
        text:
          'Form cleared. No ticket was created.',
      })
    }
  }

  async function submit(
    event: FormEvent,
  ) {
    event.preventDefault()

    if (
      mode === 'shared' &&
      !verified
    ) {
      setNotice({
        type: 'error',
        text:
          'Verify a valid staff badge before submitting.',
      })

      return
    }

    try {
      /*
        USER STORY:
        Successful submission confirmation.

        We wait for the backend first.
        A success message is NOT shown
        unless createTicket succeeds.
      */
      const result =
        await api.createTicket(form)

      let ticket = result.ticket
      let text = result.message

      if (attachment) {
        const upload =
          await api.uploadAttachment(
            ticket.id,
            attachment,
          )

        ticket = upload.ticket

        text += ` ${upload.message}`
      }

      /*
        USER STORY:
        Automatic Creation Date/Time

        There is intentionally no date/time
        input in this form.

        The backend creates createdAtUtc
        automatically and returns it as
        part of the created ticket.
      */

      clear(false)

      // Success appears only after
      // the backend operation succeeds.
      setNotice({
        type: 'success',
        text,
      })

      await onCreated(ticket)
    } catch (error) {
      setNotice({
        type: 'error',
        text:
          error instanceof ApiError
            ? error.message
            : 'Ticket submission failed.',
      })
    }
  }

  return (
    <section className="panel">
      <div className="panel-header">
        <div>
          <p className="eyebrow">
            NEW REQUEST
          </p>

          <h3>
            Submit an IT support ticket
          </h3>

          <p>
            Required information is
            checked again by the backend.
          </p>
        </div>
      </div>

      <div className="mode-tabs">
        <button
          type="button"
          className={
            mode === 'account'
              ? 'mode active'
              : 'mode'
          }
          onClick={() =>
            changeMode('account')
          }
        >
          Personal account
        </button>

        <button
          type="button"
          className={
            mode === 'shared'
              ? 'mode active'
              : 'mode'
          }
          onClick={() =>
            changeMode('shared')
          }
        >
          Shared terminal / Badge ID
        </button>
      </div>

      {mode === 'shared' && (
        <div className="badge-box">
          <label>
            <span>
              Staff badge ID
            </span>

            <div className="inline-input">
              <input
                value={badgeId}
                onChange={event => {
                  setBadgeId(
                    event.target.value,
                  )

                  setVerified(null)
                }}
                placeholder="Try AIR1001"
              />

              <button
                type="button"
                className="secondary-button"
                onClick={verifyBadge}
              >
                Verify badge
              </button>
            </div>

            <small>
              Demo IDs: AIR1001,
              AIR1002, AIR1003
            </small>
          </label>

          <div
            className={
              verified
                ? 'verified'
                : 'unverified'
            }
          >
            {verified
              ? `✓ ${verified.name}`
              : 'Not verified'}
          </div>
        </div>
      )}

      <form
        className="ticket-form"
        onSubmit={submit}
      >
        <div className="form-grid three">
          <label>
            <span>Terminal *</span>

            <select
              required
              name="terminal"
              value={form.terminal}
              onChange={update}
            >
              <option value="">
                Select terminal
              </option>

              <option value="T1">
                Terminal 1
              </option>

              <option value="T2">
                Terminal 2
              </option>

              <option value="T3">
                Terminal 3
              </option>
            </select>
          </label>

          <label>
            <span>Gate / Area *</span>

            <input
              required
              name="area"
              value={form.area}
              onChange={update}
              placeholder="e.g. Gate 14"
            />
          </label>

          <label>
            <span>
              Affected system *
            </span>

            <select
              required
              name="systemType"
              value={form.systemType}
              onChange={update}
            >
              <option value="">
                Select system
              </option>

              <option>Kiosk</option>

              <option>
                Flight Information Display
              </option>

              <option>
                Gate Computer
              </option>

              <option>
                Check-in System
              </option>

              <option>
                Staff Computer
              </option>

              <option>
                Network
              </option>

              <option>
                Other
              </option>
            </select>
          </label>
        </div>

        <div className="form-grid two">
          <label>
            <span>Priority</span>

            <select
              name="priority"
              value={form.priority}
              onChange={update}
            >
              {PRIORITY_OPTIONS.map(
                priority => (
                  <option
                    key={priority}
                    value={priority}
                  >
                    {priority}
                  </option>
                ),
              )}
            </select>
          </label>

          <div className="impact-box">
            <span className="field-label">
              Operational impact
            </span>

            <label className="check-row">
              <input
                type="checkbox"
                name="passengerImpact"
                checked={
                  form.passengerImpact
                }
                onChange={update}
              />

              Passenger impact
            </label>

            <label className="check-row">
              <input
                type="checkbox"
                name="flightOpsImpact"
                checked={
                  form.flightOpsImpact
                }
                onChange={update}
              />

              Flight operations impact
            </label>
          </div>
        </div>

        <label>
          <span>Description *</span>

          <textarea
            required
            name="description"
            value={form.description}
            onChange={update}
            rows={4}
            placeholder="Describe what is wrong, what you can see, and any immediate impact."
          />
        </label>

        <div className="form-grid two">
          <label>
            <span>Reporter name</span>

            <input
              required
              name="reporterName"
              value={
                form.reporterName
              }
              onChange={update}
              disabled={
                mode === 'shared'
              }
            />
          </label>

          <label>
            <span>Reporter email</span>

            <input
              required
              type="email"
              name="reporterEmail"
              value={
                form.reporterEmail
              }
              onChange={update}
              disabled={
                mode === 'shared'
              }
            />
          </label>
        </div>

        <div className="attachment-box">
          <div>
            <span className="field-label">
              Photo or screenshot
            </span>

            <p>
              JPG or PNG, maximum 5 MB.
            </p>
          </div>

          <input
            ref={fileRef}
            type="file"
            accept=".jpg,.jpeg,.png,image/jpeg,image/png"
            onChange={event =>
              setAttachment(
                event.target.files?.[0] ??
                  null,
              )
            }
          />

          {attachment && (
            <div className="selected-file">
              <strong>
                {attachment.name}
              </strong>

              <span>
                {Math.round(
                  attachment.size /
                    1024,
                )}{' '}
                KB
              </span>
            </div>
          )}
        </div>

        <div className="form-actions">
          <button
            className="primary-button"
            type="submit"
          >
            Submit ticket
          </button>

          {/* USER STORY:
              Clear Ticket Form */}
          <button
            className="secondary-button"
            type="button"
            onClick={() => clear()}
          >
            Clear form
          </button>
        </div>
      </form>
    </section>
  )
}
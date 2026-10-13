import { useId, useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent } from 'react'
import { DEMO_ACCOUNTS, DEMO_USERNAMES, describeSignInError } from '../services/auth'

type FieldErrors = { username?: string; password?: string }

type Props = {
  /** Resolves on success; rejects with an ApiError (or a network TypeError) on failure. */
  onSignIn: (username: string, password: string) => Promise<void>
  /** Shown above the form, e.g. after signing out or when a session has expired. */
  statusMessage?: string | null
  /**
   * Show the demo passwords with a button that fills them in. Defaults to Vite development
   * builds only: a production bundle never contains a list of working credentials.
   */
  showDemoPasswords?: boolean
}

export function LoginForm({ onSignIn, statusMessage, showDemoPasswords = DEMO_ACCOUNTS.length > 0 }: Props) {
  const id = useId()
  const usernameRef = useRef<HTMLInputElement>(null)
  const passwordRef = useRef<HTMLInputElement>(null)
  const submitRef = useRef<HTMLButtonElement>(null)

  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [capsLock, setCapsLock] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)

  const usernameErrorId = `${id}-username-error`
  const passwordErrorId = `${id}-password-error`
  const capsId = `${id}-caps`

  function validate(): FieldErrors {
    const errors: FieldErrors = {}
    if (!username.trim()) errors.username = 'Enter your username.'
    if (!password) errors.password = 'Enter your password.'
    return errors
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submitting) return

    const errors = validate()
    setFieldErrors(errors)
    setFormError(null)

    if (errors.username) { usernameRef.current?.focus(); return }
    if (errors.password) { passwordRef.current?.focus(); return }

    setSubmitting(true)
    try {
      await onSignIn(username.trim(), password)
    } catch (error) {
      setFormError(describeSignInError(error))
      // Do not leave a rejected password sitting in the field.
      setPassword('')
      passwordRef.current?.focus()
    } finally {
      setSubmitting(false)
    }
  }

  /** Tester shortcut: fills both fields and moves focus to Sign in, so Enter signs in. */
  function fillDemoAccount(account: (typeof DEMO_ACCOUNTS)[number]) {
    setUsername(account.username)
    setPassword(account.password)
    setFieldErrors({})
    setFormError(null)
    submitRef.current?.focus()
  }

  function trackCapsLock(event: KeyboardEvent<HTMLInputElement>) {
    setCapsLock(event.getModifierState?.('CapsLock') ?? false)
  }

  return (
    <form className="auth-form" onSubmit={event => void handleSubmit(event)} noValidate aria-busy={submitting}>
      <h1>Sign in</h1>
      <p className="auth-subtitle">Sign in with your help desk account to raise and manage support tickets.</p>

      {statusMessage && !formError && (
        <div className="auth-status" role="status">{statusMessage}</div>
      )}

      {formError && (
        <div className="auth-error" role="alert">{formError}</div>
      )}

      <div className="auth-field">
        <label htmlFor={`${id}-username`}>Username</label>
        <input
          ref={usernameRef}
          id={`${id}-username`}
          name="username"
          type="text"
          autoComplete="username"
          autoCapitalize="none"
          spellCheck={false}
          value={username}
          onChange={event => setUsername(event.target.value)}
          aria-invalid={fieldErrors.username ? true : undefined}
          aria-describedby={fieldErrors.username ? usernameErrorId : undefined}
          readOnly={submitting}
          required
        />
        {fieldErrors.username && <p id={usernameErrorId} className="field-error">{fieldErrors.username}</p>}
      </div>

      <div className="auth-field">
        <label htmlFor={`${id}-password`}>Password</label>
        <div className="password-row">
          <input
            ref={passwordRef}
            id={`${id}-password`}
            name="password"
            type={showPassword ? 'text' : 'password'}
            autoComplete="current-password"
            value={password}
            onChange={event => setPassword(event.target.value)}
            onKeyUp={trackCapsLock}
            onKeyDown={trackCapsLock}
            aria-invalid={fieldErrors.password ? true : undefined}
            aria-describedby={[fieldErrors.password ? passwordErrorId : '', capsLock ? capsId : ''].filter(Boolean).join(' ') || undefined}
            readOnly={submitting}
            required
          />
          <button
            type="button"
            className="secondary-button password-toggle"
            onClick={() => setShowPassword(current => !current)}
            aria-label={showPassword ? 'Hide password' : 'Show password'}
            aria-pressed={showPassword}
            aria-controls={`${id}-password`}
            disabled={submitting}
          >
            {showPassword ? 'Hide' : 'Show'}
          </button>
        </div>
        {capsLock && <p id={capsId} className="field-hint">Caps Lock is on.</p>}
        {fieldErrors.password && <p id={passwordErrorId} className="field-error">{fieldErrors.password}</p>}
      </div>

      <button ref={submitRef} type="submit" className="primary-button auth-button" disabled={submitting}>
        {submitting ? 'Signing in…' : 'Sign in'}
      </button>

      <details className="auth-note" open={showDemoPasswords}>
        <summary>Demo accounts</summary>
        {showDemoPasswords ? (
          <>
            <ul className="demo-accounts" aria-label="Demo accounts for local testing">
              {DEMO_ACCOUNTS.map(account => (
                <li key={account.username}>
                  <div className="demo-account-text">
                    <strong>{account.role}</strong>
                    <span>
                      <span className="visually-hidden">Username </span><code>{account.username}</code>
                      <span aria-hidden="true"> / </span>
                      <span className="visually-hidden">, password </span><code>{account.password}</code>
                    </span>
                  </div>
                  <button
                    type="button"
                    className="secondary-button demo-use"
                    onClick={() => fillDemoAccount(account)}
                    aria-label={`Use the ${account.role} demo account`}
                    disabled={submitting}
                  >
                    Use
                  </button>
                </li>
              ))}
            </ul>
            <p>
              These work only while the API runs in Development. After five wrong passwords an
              account is locked for 15 minutes.
            </p>
          </>
        ) : (
          <p>
            Usernames: {DEMO_USERNAMES.map((name, index) => (
              <span key={name}><code>{name}</code>{index < DEMO_USERNAMES.length - 1 ? ', ' : ''}</span>
            ))}.
            Development passwords are set in <code>appsettings.Development.json</code>.
            After five wrong passwords an account is locked for 15 minutes.
          </p>
        )}
      </details>
    </form>
  )
}

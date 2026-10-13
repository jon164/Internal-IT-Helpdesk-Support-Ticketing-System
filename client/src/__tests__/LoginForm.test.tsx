import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { LoginForm } from '../components/LoginForm'
import { ApiError } from '../services/api'
import { axeViolations } from '../test/axe'

function setup(onSignIn = vi.fn().mockResolvedValue(undefined), statusMessage?: string) {
  const user = userEvent.setup()
  const view = render(<LoginForm onSignIn={onSignIn} statusMessage={statusMessage} />)
  return {
    user,
    onSignIn,
    container: view.container,
    username: () => screen.getByLabelText('Username') as HTMLInputElement,
    password: () => screen.getByLabelText('Password') as HTMLInputElement,
    submit: () => screen.getByRole('button', { name: /sign in|signing in/i }),
  }
}

describe('LoginForm — accessibility', () => {
  it('AUTH-C01 gives every control an accessible name and the right autocomplete hints', () => {
    const { username, password } = setup()
    expect(username()).toHaveAttribute('autocomplete', 'username')
    expect(password()).toHaveAttribute('autocomplete', 'current-password')
    expect(password()).toHaveAttribute('type', 'password')
    expect(screen.getByRole('button', { name: 'Show password' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'Sign in' })).toBeInTheDocument()
  })

  it('AUTH-C02 has no automated WCAG 2.1 A/AA violations, before and after a failed attempt', async () => {
    const { user, container, submit } = setup()
    expect(await axeViolations(container)).toEqual([])

    await user.click(submit())
    expect(await axeViolations(container)).toEqual([])
  })

  it('AUTH-C03 links each field error to its input and moves focus to the first problem', async () => {
    const { user, onSignIn, username, password, submit } = setup()

    await user.click(submit())

    expect(onSignIn).not.toHaveBeenCalled()
    expect(username()).toHaveFocus()
    expect(username()).toHaveAttribute('aria-invalid', 'true')
    expect(username()).toHaveAccessibleDescription('Enter your username.')
    expect(password()).toHaveAccessibleDescription('Enter your password.')
  })

  it('AUTH-C04 announces a sign-in failure through an alert region', async () => {
    const onSignIn = vi.fn().mockRejectedValue(new ApiError('Username or password is incorrect.', 401))
    const { user, username, password } = setup(onSignIn)

    await user.type(username(), 'airport-staff')
    await user.type(password(), 'wrong{Enter}')

    expect(await screen.findByRole('alert')).toHaveTextContent('Username or password is incorrect.')
  })
})

describe('LoginForm — usability', () => {
  it('AUTH-C05 treats a whitespace-only username as empty (boundary case)', async () => {
    const { user, onSignIn, username, password } = setup()

    await user.type(username(), '   ')
    await user.type(password(), 'secret{Enter}')

    expect(onSignIn).not.toHaveBeenCalled()
    expect(username()).toHaveAccessibleDescription('Enter your username.')
    expect(password()).not.toHaveAttribute('aria-invalid')
  })

  it('AUTH-C06 submits with the Enter key, trimming the username but never the password', async () => {
    const { user, onSignIn, username, password } = setup()

    await user.type(username(), '  it-manager ')
    await user.type(password(), ' pass word {Enter}')

    expect(onSignIn).toHaveBeenCalledExactlyOnceWith('it-manager', ' pass word ')
  })

  it('AUTH-C07 shows progress and ignores repeat submissions while a sign-in is pending', async () => {
    let finish!: () => void
    const onSignIn = vi.fn(() => new Promise<void>(resolve => { finish = resolve }))
    const { user, username, password, submit } = setup(onSignIn)

    await user.type(username(), 'it-manager')
    await user.type(password(), 'secret')
    await user.click(submit())

    expect(submit()).toBeDisabled()
    expect(submit()).toHaveTextContent('Signing in…')
    await user.type(password(), '{Enter}')
    expect(onSignIn).toHaveBeenCalledTimes(1)

    finish()
    await waitFor(() => expect(submit()).toBeEnabled())
  })

  it('AUTH-C08 clears a rejected password and returns focus to it for the next attempt', async () => {
    const onSignIn = vi.fn().mockRejectedValue(new ApiError('Username or password is incorrect.', 401))
    const { user, username, password } = setup(onSignIn)

    await user.type(username(), 'airport-staff')
    await user.type(password(), 'wrong{Enter}')

    await screen.findByRole('alert')
    expect(password()).toHaveValue('')
    expect(password()).toHaveFocus()
    expect(username()).toHaveValue('airport-staff')
  })

  it('AUTH-C09 shows the server lockout message when the account is locked', async () => {
    const onSignIn = vi.fn().mockRejectedValue(new ApiError('Too many failed attempts. Try again in 15 minutes.', 429))
    const { user, username, password } = setup(onSignIn)

    await user.type(username(), 'airport-staff')
    await user.type(password(), 'wrong{Enter}')

    expect(await screen.findByRole('alert')).toHaveTextContent('Try again in 15 minutes')
  })

  it('AUTH-C10 explains an unreachable server differently from a wrong password', async () => {
    const onSignIn = vi.fn().mockRejectedValue(new TypeError('Failed to fetch'))
    const { user, username, password } = setup(onSignIn)

    await user.type(username(), 'airport-staff')
    await user.type(password(), 'secret{Enter}')

    expect(await screen.findByRole('alert')).toHaveTextContent("Can't reach the help desk server")
  })

  it('AUTH-C11 lets the user reveal and re-hide the password', async () => {
    const { user, password } = setup()
    const toggle = screen.getByRole('button', { name: 'Show password' })

    await user.click(toggle)
    expect(password()).toHaveAttribute('type', 'text')
    expect(screen.getByRole('button', { name: 'Hide password' })).toHaveAttribute('aria-pressed', 'true')

    await user.click(screen.getByRole('button', { name: 'Hide password' }))
    expect(password()).toHaveAttribute('type', 'password')
  })

  it('AUTH-C12 shows a status message such as "signed out" politely, not as an alert', () => {
    setup(undefined, 'You have signed out.')
    expect(screen.getByRole('status')).toHaveTextContent('You have signed out.')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

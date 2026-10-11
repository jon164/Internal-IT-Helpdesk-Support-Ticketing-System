/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { LoginForm } from '../components/LoginForm'
import { DEMO_ACCOUNTS } from '../services/auth'
import { axeViolations } from '../test/axe'

// The API's real Development configuration, read from disk so the test sees exactly what the server uses.
// Vitest runs from the client folder (locally and in CI).
const developmentSettingsRaw = readFileSync(
  resolve(process.cwd(), '../src/Helpdesk.Api/appsettings.Development.json'),
  'utf8',
)

/*
 * Tester convenience: the sign-in page lists the demo passwords, with a "Use" button that fills them
 * in, so nobody has to open appsettings.Development.json. Development builds only.
 */

function setup(showDemoPasswords?: boolean) {
  const user = userEvent.setup()
  const onSignIn = vi.fn().mockResolvedValue(undefined)
  const view = render(<LoginForm onSignIn={onSignIn} showDemoPasswords={showDemoPasswords} />)
  return { user, onSignIn, container: view.container }
}

describe('Demo accounts panel — usability', () => {
  it('AUTH-C27 lists every demo username with its password, already expanded', () => {
    setup()
    const list = screen.getByRole('list', { name: 'Demo accounts for local testing' })

    expect(list.closest('details')).toHaveAttribute('open')
    for (const account of DEMO_ACCOUNTS) {
      const row = within(list).getByText(account.username).closest('li')!
      expect(row).toHaveTextContent(account.role)
      expect(row).toHaveTextContent(account.password)
    }
    expect(DEMO_ACCOUNTS).toHaveLength(3)
  })

  it('AUTH-C28 the listed passwords are exactly the ones the API is configured with', () => {
    const settings = JSON.parse(developmentSettingsRaw.replace(/^\uFEFF/, '')) as {
      DemoAccounts: { Passwords: Record<string, string> }
    }
    const listed = Object.fromEntries(DEMO_ACCOUNTS.map(a => [a.username, a.password]))

    // If someone changes a password in appsettings.Development.json, this fails instead of the
    // sign-in page quietly showing a password that no longer works.
    expect(listed).toEqual(settings.DemoAccounts.Passwords)
  })

  it('AUTH-C29 "Use" fills both fields and puts focus on Sign in, so Enter signs in', async () => {
    const { user, onSignIn } = setup()

    await user.click(screen.getByRole('button', { name: 'Use the IT Manager demo account' }))

    expect(screen.getByLabelText('Username')).toHaveValue('it-manager')
    expect(screen.getByLabelText('Password')).toHaveValue('Manager#Demo2026')
    expect(screen.getByRole('button', { name: 'Sign in' })).toHaveFocus()

    await user.keyboard('{Enter}')
    expect(onSignIn).toHaveBeenCalledWith('it-manager', 'Manager#Demo2026')
  })

  it('AUTH-C30 "Use" clears an earlier error so the tester starts fresh', async () => {
    const { user } = setup()
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(screen.getByText('Enter your username.')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Use the Airport Staff demo account' }))

    expect(screen.queryByText('Enter your username.')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Username')).not.toHaveAttribute('aria-invalid')
  })
})

describe('Demo accounts panel — security and accessibility', () => {
  it('AUTH-C31 when switched off (as in a production build) no password is shown anywhere', () => {
    const { container } = setup(false)

    expect(container.textContent).not.toMatch(/Demo2026/)
    expect(screen.queryByRole('list', { name: 'Demo accounts for local testing' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /demo account/ })).not.toBeInTheDocument()
    expect(container.querySelector('details')).not.toHaveAttribute('open')
  })

  it('AUTH-C32 the expanded panel passes the axe WCAG 2.1 A/AA scan', async () => {
    const { container } = setup()
    expect(await axeViolations(container)).toEqual([])
  })
})

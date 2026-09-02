import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from '../api/client'
import type { User } from '../api/types'
import { SampleDataPanel } from './SampleDataPanel'

vi.mock('../api/client', async () => {
  const actual = await vi.importActual<typeof import('../api/client')>('../api/client')

  return {
    ...actual,
    api: {
      ...actual.api,
      getSampleDataStatus: vi.fn(),
      generateSampleData: vi.fn(),
      clearSampleData: vi.fn(),
    },
  }
})

const getStatus = vi.mocked(api.getSampleDataStatus)
const generate = vi.mocked(api.generateSampleData)
const clear = vi.mocked(api.clearSampleData)

const manager: User = {
  id: 'lead-maia',
  displayName: 'Maia Thornton',
  department: 'IT Support',
  role: 'TeamLead',
  isActive: true,
}

const empty = { ticketCount: 0, isEmpty: true, generatedCount: 500 }
const populated = { ticketCount: 500, isEmpty: false, generatedCount: 500 }

describe('SampleDataPanel', () => {
  beforeEach(() => vi.clearAllMocks())

  it('renders nothing when the endpoint does not exist', async () => {
    // A production build does not register the route at all. A 404 here is the correct answer, not
    // a fault, and the panel must disappear rather than report an error.
    getStatus.mockRejectedValue(
      new ApiError(404, { error: 'NotFound', message: 'Not found' }),
    )

    const { container } = render(<SampleDataPanel currentUser={manager} />)

    await waitFor(() => expect(getStatus).toHaveBeenCalled())
    expect(container).toBeEmptyDOMElement()
  })

  it('offers generation when the database is empty', async () => {
    getStatus.mockResolvedValue(empty)

    render(<SampleDataPanel currentUser={manager} />)

    expect(await screen.findByText('No tickets.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Generate 500 tickets' })).toBeEnabled()
    expect(screen.queryByRole('button', { name: 'Clear all tickets' })).not.toBeInTheDocument()
  })

  it('refuses to generate on top of an existing population', async () => {
    // Generating twice would duplicate references and destroy the guarantee that the dataset is
    // identical everywhere, so the control is closed rather than left to fail on the server.
    getStatus.mockResolvedValue(populated)

    render(<SampleDataPanel currentUser={manager} />)

    expect(await screen.findByRole('button', { name: 'Generate 500 tickets' })).toBeDisabled()
  })

  it('reports how many tickets were generated', async () => {
    getStatus.mockResolvedValue(empty)
    generate.mockResolvedValue(populated)

    const user = userEvent.setup()

    render(<SampleDataPanel currentUser={manager} />)
    await user.click(await screen.findByRole('button', { name: 'Generate 500 tickets' }))

    expect(await screen.findByText('Generated 500 tickets.')).toBeInTheDocument()
    expect(generate).toHaveBeenCalledWith('lead-maia')
  })

  it('asks for confirmation before destroying anything', async () => {
    getStatus.mockResolvedValue(populated)

    const user = userEvent.setup()

    render(<SampleDataPanel currentUser={manager} />)
    await user.click(await screen.findByRole('button', { name: 'Clear all tickets' }))

    expect(
      screen.getByText('Delete all 500 tickets and their history?'),
    ).toBeInTheDocument()
    expect(clear).not.toHaveBeenCalled()

    await user.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(clear).not.toHaveBeenCalled()
  })

  it('clears only once confirmed, and reports what was removed', async () => {
    getStatus.mockResolvedValue(populated)
    clear.mockResolvedValue({ removedCount: 500, status: empty })

    const user = userEvent.setup()

    render(<SampleDataPanel currentUser={manager} />)
    await user.click(await screen.findByRole('button', { name: 'Clear all tickets' }))
    await user.click(screen.getByRole('button', { name: 'Yes, clear' }))

    expect(await screen.findByText('Removed 500 tickets.')).toBeInTheDocument()
    expect(await screen.findByText('No tickets.')).toBeInTheDocument()
  })

  it('shows the server refusal rather than inventing one', async () => {
    getStatus.mockResolvedValue(empty)
    generate.mockRejectedValue(
      new ApiError(409, {
        error: 'Conflict',
        message: 'Tickets already exist. Clear the demonstration data first.',
      }),
    )

    const user = userEvent.setup()

    render(<SampleDataPanel currentUser={manager} />)
    await user.click(await screen.findByRole('button', { name: 'Generate 500 tickets' }))

    expect(
      await screen.findByText('Tickets already exist. Clear the demonstration data first.'),
    ).toBeInTheDocument()
  })

  it('states that accounts survive both operations', async () => {
    getStatus.mockResolvedValue(populated)

    render(<SampleDataPanel currentUser={manager} />)

    expect(
      await screen.findByText(/Accounts and the account change history are never touched/),
    ).toBeInTheDocument()
  })
})

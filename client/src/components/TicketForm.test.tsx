// @vitest-environment jsdom

import {
  fireEvent,
  render,
} from '@testing-library/react'

import {
  describe,
  expect,
  it,
  vi,
} from 'vitest'

import {
  TicketForm,
} from './TicketForm'

describe(
  'TicketForm attachment validation',
  () => {
    it(
  'clears ticket issue fields without creating a ticket',
  () => {
    const setNotice = vi.fn()
    const onCreated = vi.fn()

    const {
      container,
      getByRole,
    } = render(
      <TicketForm
        onCreated={onCreated}
        setNotice={setNotice}
      />,
    )

    const terminal =
      container.querySelector(
        'select[name="terminal"]',
      ) as HTMLSelectElement

    const area =
      container.querySelector(
        'input[name="area"]',
      ) as HTMLInputElement

    const systemType =
      container.querySelector(
        'select[name="systemType"]',
      ) as HTMLSelectElement

    const description =
      container.querySelector(
        'textarea[name="description"]',
      ) as HTMLTextAreaElement

    fireEvent.change(terminal, {
      target: {
        value: 'T1',
      },
    })

    fireEvent.change(area, {
      target: {
        value: 'Gate 14',
      },
    })

    fireEvent.change(systemType, {
      target: {
        value: 'Kiosk',
      },
    })

    fireEvent.change(description, {
      target: {
        value:
          'Test kiosk problem',
      },
    })

    fireEvent.click(
      getByRole('button', {
        name: 'Clear form',
      }),
    )

    expect(terminal.value).toBe('')
    expect(area.value).toBe('')
    expect(systemType.value).toBe('')
    expect(description.value).toBe('')

    expect(
      onCreated,
    ).not.toHaveBeenCalled()

    expect(
      setNotice,
    ).toHaveBeenCalledWith({
      type: 'info',
      text:
        'Form cleared. No ticket was created.',
    })
  },
)
    it(
      'accepts a JPG attachment smaller than 5 MB',
      () => {
        const setNotice = vi.fn()

        const { container } = render(
          <TicketForm
            onCreated={vi.fn()}
            setNotice={setNotice}
          />,
        )

        const input =
          container.querySelector(
            'input[type="file"]',
          ) as HTMLInputElement

        const file = new File(
          ['test image'],
          'airport.jpg',
          {
            type: 'image/jpeg',
          },
        )

        fireEvent.change(input, {
          target: {
            files: [file],
          },
        })

        expect(
          container.textContent,
        ).toContain('airport.jpg')

        expect(setNotice).toHaveBeenCalledWith({
          type: 'info',
          text:
            'airport.jpg is ready to upload.',
        })
      },
    )

    it(
      'rejects an unsupported file type',
      () => {
        const setNotice = vi.fn()

        const { container } = render(
          <TicketForm
            onCreated={vi.fn()}
            setNotice={setNotice}
          />,
        )

        const input =
          container.querySelector(
            'input[type="file"]',
          ) as HTMLInputElement

        const file = new File(
          ['test document'],
          'airport.txt',
          {
            type: 'text/plain',
          },
        )

        fireEvent.change(input, {
          target: {
            files: [file],
          },
        })

        expect(setNotice).toHaveBeenCalledWith({
          type: 'error',
          text:
            'Only JPG and PNG files are allowed.',
        })
      },
    )

    it(
      'rejects an image larger than 5 MB',
      () => {
        const setNotice = vi.fn()

        const { container } = render(
          <TicketForm
            onCreated={vi.fn()}
            setNotice={setNotice}
          />,
        )

        const input =
          container.querySelector(
            'input[type="file"]',
          ) as HTMLInputElement

        const sixMegabytes =
          new Uint8Array(
            6 * 1024 * 1024,
          )

        const file = new File(
          [sixMegabytes],
          'large.png',
          {
            type: 'image/png',
          },
        )

        fireEvent.change(input, {
          target: {
            files: [file],
          },
        })

        expect(
          setNotice,
        ).toHaveBeenCalledWith({
          type: 'error',
          text:
            'Attachment must be 5 MB or smaller.',
        })
      },
    )
  },
)
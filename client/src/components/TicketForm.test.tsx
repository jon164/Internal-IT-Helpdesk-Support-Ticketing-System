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
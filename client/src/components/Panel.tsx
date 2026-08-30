import type { ReactNode } from 'react'

/**
 * A titled card around one chart.
 *
 * The note under the title states what the panel measures. Charts that need a paragraph of
 * explanation are usually the wrong chart, but a single line of scope — "open tickets by status",
 * "demand mix for the period" — removes the most common misreading at almost no cost.
 */
export function Panel({
  title,
  note,
  wide = false,
  children,
}: {
  title: string
  note?: string
  /** Spans both columns of the panel grid. */
  wide?: boolean
  children: ReactNode
}) {
  return (
    <section className={`panel${wide ? ' is-wide' : ''}`}>
      <header className="panel-header">
        <h2>{title}</h2>
        {note && <p>{note}</p>}
      </header>
      {children}
    </section>
  )
}

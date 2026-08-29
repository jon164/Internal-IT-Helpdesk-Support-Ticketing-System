import type { ReactNode } from 'react'
import './stat-tile.css'

export type StatTone = 'neutral' | 'good' | 'warning' | 'serious' | 'critical'

const TONE_LABEL: Record<StatTone, string | null> = {
  neutral: null,
  good: 'On target',
  warning: 'Watch',
  serious: 'Below target',
  critical: 'Action needed',
}

/**
 * Glyphs so state is never carried by colour alone — the requirement that makes these readable for
 * a colour-blind reader, in print, and in forced-colours mode.
 */
const TONE_GLYPH: Record<StatTone, string | null> = {
  neutral: null,
  good: '✓',
  warning: '!',
  serious: '!',
  critical: '×',
}

interface StatTileProps {
  label: string
  value: string
  /** Secondary line under the value. */
  hint?: string
  tone?: StatTone
  /** Overrides the default tone wording where a more specific phrase reads better. */
  toneLabel?: string
  children?: ReactNode
  /** Renders the tile at hero size — reserve it for the single headline figure. */
  hero?: boolean
}

/**
 * A single figure with its label.
 *
 * A number this important does not need a chart around it. The manager's question is "is service
 * health acceptable right now", and one large figure answers it faster than any plot.
 */
export function StatTile({
  label,
  value,
  hint,
  tone = 'neutral',
  toneLabel,
  children,
  hero = false,
}: StatTileProps) {
  const badge = toneLabel ?? TONE_LABEL[tone]
  const glyph = TONE_GLYPH[tone]

  return (
    <section className={`stat-tile${hero ? ' is-hero' : ''} tone-${tone}`}>
      <h3 className="stat-label">{label}</h3>

      <p className="stat-value">{value}</p>

      {badge && (
        <p className="stat-badge">
          <span aria-hidden="true">{glyph}</span>
          {badge}
        </p>
      )}

      {hint && <p className="stat-hint">{hint}</p>}

      {children}
    </section>
  )
}

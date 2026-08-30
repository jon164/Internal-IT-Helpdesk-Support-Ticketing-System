import { useId, useMemo, useState } from 'react'
import './charts.css'

const WIDTH = 720
const HEIGHT = 190
const PAD_LEFT = 38
const PAD_RIGHT = 12
const PAD_TOP = 14
const PAD_BOTTOM = 26

const PLOT_WIDTH = WIDTH - PAD_LEFT - PAD_RIGHT
const PLOT_HEIGHT = HEIGHT - PAD_TOP - PAD_BOTTOM

export interface TrendPoint {
  key: string
  /** Short axis label, e.g. "4 Aug". */
  label: string
  /** Longer label for the tooltip, e.g. "Week of 4 August". */
  fullLabel: string
  value: number
}

export interface FlowPoint {
  key: string
  label: string
  fullLabel: string
  raised: number
  resolved: number
}

/**
 * Rounds an axis maximum up to something a person would choose, so gridlines land on readable
 * numbers rather than on whatever the data happened to peak at.
 */
function niceMax(value: number): number {
  if (value <= 0) return 4

  const magnitude = 10 ** Math.floor(Math.log10(value))
  const normalised = value / magnitude

  // A coarse ladder (1, 2, 5, 10) rounds a peak of 37 up to 50, leaving a third of the plot empty
  // and flattening the shape the reader is there to see. These steps still land on readable
  // gridline values while staying close to the data.
  const steps = [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]
  const step = steps.find((candidate) => normalised <= candidate) ?? 10

  return step * magnitude
}

/** Axis ticks show a decimal only when the value genuinely has one — 12.5 beats a wrong 13. */
function formatAxisValue(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(1)
}

/** Thins axis labels so they cannot collide at narrow widths. */
function labelStride(count: number): number {
  return count <= 7 ? 1 : Math.ceil(count / 6)
}

/**
 * The size of the unresolved queue at the close of each period.
 *
 * This is the chart that answers "are we falling behind". A single backlog figure cannot: the
 * question is about direction, and direction is only visible over time. One series, so no legend —
 * the panel heading names it.
 */
export function QueueTrendChart({
  points,
  caption,
}: {
  points: TrendPoint[]
  caption: string
}) {
  const [hovered, setHovered] = useState<number | null>(null)
  const captionId = useId()
  const gradientId = useId()

  const max = useMemo(() => niceMax(Math.max(...points.map((p) => p.value), 1)), [points])

  if (points.length === 0) {
    return <p className="chart-empty">No history for this period.</p>
  }

  const x = (index: number) =>
    PAD_LEFT + (points.length === 1 ? PLOT_WIDTH / 2 : (index / (points.length - 1)) * PLOT_WIDTH)

  const y = (value: number) => PAD_TOP + PLOT_HEIGHT - (value / max) * PLOT_HEIGHT

  const line = points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${x(i)} ${y(p.value)}`).join(' ')

  const area =
    `${line} L ${x(points.length - 1)} ${PAD_TOP + PLOT_HEIGHT} ` +
    `L ${x(0)} ${PAD_TOP + PLOT_HEIGHT} Z`

  const stride = labelStride(points.length)
  const active = hovered === null ? null : points[hovered]

  return (
    <div className="ts-chart">
      <svg
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        className="ts-svg"
        role="img"
        aria-describedby={captionId}
        onMouseLeave={() => setHovered(null)}
      >
        <desc id={captionId}>{caption}</desc>

        <defs>
          <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="var(--seq-450)" stopOpacity="0.22" />
            <stop offset="100%" stopColor="var(--seq-450)" stopOpacity="0.02" />
          </linearGradient>
        </defs>

        {[0, 0.5, 1].map((fraction) => {
          const value = max * fraction
          return (
            <g key={fraction}>
              <line
                x1={PAD_LEFT}
                x2={WIDTH - PAD_RIGHT}
                y1={y(value)}
                y2={y(value)}
                className={fraction === 0 ? 'ts-baseline' : 'ts-gridline'}
              />
              <text x={PAD_LEFT - 8} y={y(value) + 4} className="ts-axis-label" textAnchor="end">
                {formatAxisValue(value)}
              </text>
            </g>
          )
        })}

        <path d={area} fill={`url(#${gradientId})`} />
        <path d={line} className="ts-line" />

        {points.map((point, index) => (
          <circle
            key={point.key}
            cx={x(index)}
            cy={y(point.value)}
            r={hovered === index ? 5.5 : 4}
            className="ts-marker"
          />
        ))}

        {hovered !== null && (
          <line
            x1={x(hovered)}
            x2={x(hovered)}
            y1={PAD_TOP}
            y2={PAD_TOP + PLOT_HEIGHT}
            className="ts-crosshair"
          />
        )}

        {points.map((point, index) =>
          index % stride === 0 || index === points.length - 1 ? (
            <text
              key={`label-${point.key}`}
              x={x(index)}
              y={HEIGHT - 8}
              className="ts-axis-label"
              textAnchor="middle"
            >
              {point.label}
            </text>
          ) : null,
        )}

        {/* Hit targets are far wider than the markers, so hovering does not require precision. */}
        {points.map((point, index) => (
          <rect
            key={`hit-${point.key}`}
            x={x(index) - PLOT_WIDTH / points.length / 2}
            y={PAD_TOP}
            width={PLOT_WIDTH / points.length}
            height={PLOT_HEIGHT}
            fill="transparent"
            onMouseEnter={() => setHovered(index)}
          />
        ))}
      </svg>

      {active && (
        <div className="ts-tooltip" role="status">
          <strong>{active.fullLabel}</strong>
          <span>{active.value.toLocaleString()} unresolved at close</span>
        </div>
      )}
    </div>
  )
}

/**
 * Tickets raised against tickets resolved, per period.
 *
 * Explains the queue trend above it: a queue grows precisely when the left bar outruns the right.
 * Two series, so a legend is always present; values are labelled directly only on the most recent
 * period, because a number on every bar makes the shape unreadable.
 */
export function FlowBarsChart({
  points,
  caption,
}: {
  points: FlowPoint[]
  caption: string
}) {
  const [hovered, setHovered] = useState<number | null>(null)
  const captionId = useId()

  const max = useMemo(
    () => niceMax(Math.max(...points.flatMap((p) => [p.raised, p.resolved]), 1)),
    [points],
  )

  if (points.length === 0) {
    return <p className="chart-empty">No history for this period.</p>
  }

  const slot = PLOT_WIDTH / points.length
  const barWidth = Math.min(14, (slot - 8) / 2)
  const y = (value: number) => PAD_TOP + PLOT_HEIGHT - (value / max) * PLOT_HEIGHT
  const centre = (index: number) => PAD_LEFT + slot * index + slot / 2

  const stride = labelStride(points.length)
  const active = hovered === null ? null : points[hovered]
  const last = points.length - 1

  return (
    <div className="ts-chart">
      <div className="ts-legend">
        <span>
          <i className="ts-swatch" style={{ background: 'var(--series-1)' }} aria-hidden="true" />
          Raised
        </span>
        <span>
          <i className="ts-swatch" style={{ background: 'var(--series-2)' }} aria-hidden="true" />
          Resolved
        </span>
      </div>

      <svg
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        className="ts-svg"
        role="img"
        aria-describedby={captionId}
        onMouseLeave={() => setHovered(null)}
      >
        <desc id={captionId}>{caption}</desc>

        {[0, 0.5, 1].map((fraction) => {
          const value = max * fraction
          return (
            <g key={fraction}>
              <line
                x1={PAD_LEFT}
                x2={WIDTH - PAD_RIGHT}
                y1={y(value)}
                y2={y(value)}
                className={fraction === 0 ? 'ts-baseline' : 'ts-gridline'}
              />
              <text x={PAD_LEFT - 8} y={y(value) + 4} className="ts-axis-label" textAnchor="end">
                {formatAxisValue(value)}
              </text>
            </g>
          )
        })}

        {points.map((point, index) => {
          const baseline = PAD_TOP + PLOT_HEIGHT
          // 2px of surface between the pair keeps them from reading as one stacked mark.
          const raisedX = centre(index) - barWidth - 1
          const resolvedX = centre(index) + 1

          return (
            <g key={point.key} className={hovered === index ? 'is-hovered' : undefined}>
              <rect
                x={raisedX}
                y={y(point.raised)}
                width={barWidth}
                height={Math.max(baseline - y(point.raised), point.raised > 0 ? 2 : 0)}
                rx="3"
                fill="var(--series-1)"
              />
              <rect
                x={resolvedX}
                y={y(point.resolved)}
                width={barWidth}
                height={Math.max(baseline - y(point.resolved), point.resolved > 0 ? 2 : 0)}
                rx="3"
                fill="var(--series-2)"
              />

              {index === last && (
                <>
                  <text
                    x={raisedX + barWidth / 2}
                    y={y(point.raised) - 6}
                    className="ts-value-label"
                    textAnchor="middle"
                  >
                    {point.raised}
                  </text>
                  <text
                    x={resolvedX + barWidth / 2}
                    y={y(point.resolved) - 6}
                    className="ts-value-label"
                    textAnchor="middle"
                  >
                    {point.resolved}
                  </text>
                </>
              )}
            </g>
          )
        })}

        {points.map((point, index) =>
          index % stride === 0 || index === last ? (
            <text
              key={`label-${point.key}`}
              x={centre(index)}
              y={HEIGHT - 8}
              className="ts-axis-label"
              textAnchor="middle"
            >
              {point.label}
            </text>
          ) : null,
        )}

        {points.map((point, index) => (
          <rect
            key={`hit-${point.key}`}
            x={PAD_LEFT + slot * index}
            y={PAD_TOP}
            width={slot}
            height={PLOT_HEIGHT}
            fill="transparent"
            onMouseEnter={() => setHovered(index)}
          />
        ))}
      </svg>

      {active && (
        <div className="ts-tooltip" role="status">
          <strong>{active.fullLabel}</strong>
          <span>
            {active.raised} raised · {active.resolved} resolved
          </span>
          <span className="ts-tooltip-hint">
            {active.raised === active.resolved
              ? 'Queue unchanged'
              : active.raised > active.resolved
                ? `Queue grew by ${active.raised - active.resolved}`
                : `Queue shrank by ${active.resolved - active.raised}`}
          </span>
        </div>
      )}
    </div>
  )
}

import { useId, useState } from 'react'
import './charts.css'

export interface BarDatum {
  key: string
  label: string
  value: number
  /** Optional explicit colour. Defaults to the single sequential hue. */
  color?: string
  /** Extra context shown in the tooltip, e.g. a target time. */
  hint?: string
}

interface BarChartProps {
  data: BarDatum[]
  /**
   * Denominator for the share shown on hover. Defaults to the sum of the values, which is right for
   * a breakdown but wrong when the categories overlap — pass it explicitly in that case.
   */
  total?: number
  formatValue?: (value: number) => string
  emptyMessage?: string
  /** Accessible description of what the chart shows. */
  caption: string
}

/**
 * Horizontal bar chart for a single series.
 *
 * Horizontal rather than vertical because the category labels are words, not dates — rotated
 * x-axis labels are the most common reason a chart like this becomes unreadable at narrow widths.
 *
 * One series, so there is no legend: the surrounding heading names it. Values are labelled directly
 * at the end of each bar, so the chart is readable without hovering and without an axis.
 */
export function BarChart({
  data,
  total,
  formatValue = (value) => value.toLocaleString(),
  emptyMessage = 'No data for this period.',
  caption,
}: BarChartProps) {
  const [hovered, setHovered] = useState<string | null>(null)
  const captionId = useId()

  if (data.length === 0) {
    return <p className="chart-empty">{emptyMessage}</p>
  }

  const max = Math.max(...data.map((d) => d.value), 1)
  const denominator = total ?? data.reduce((sum, d) => sum + d.value, 0)

  return (
    <div className="bar-chart" role="img" aria-describedby={captionId}>
      <p className="visually-hidden" id={captionId}>
        {caption}
      </p>

      {data.map((datum) => {
        const share = denominator > 0 ? (datum.value / denominator) * 100 : 0
        const isHovered = hovered === datum.key

        return (
          <div
            className={`bar-row${isHovered ? ' is-hovered' : ''}`}
            key={datum.key}
            onMouseEnter={() => setHovered(datum.key)}
            onMouseLeave={() => setHovered(null)}
            onFocus={() => setHovered(datum.key)}
            onBlur={() => setHovered(null)}
            tabIndex={0}
          >
            <span className="bar-label" title={datum.label}>
              {datum.label}
            </span>

            <span className="bar-track">
              <span
                className="bar-fill"
                style={{
                  width: `${Math.max((datum.value / max) * 100, datum.value > 0 ? 1.5 : 0)}%`,
                  // A zero value must draw nothing at all. A minimum bar width applied
                  // unconditionally leaves a sliver of colour that reads as a small non-zero count.
                  minWidth: datum.value > 0 ? '3px' : 0,
                  background: datum.color ?? 'var(--seq-450)',
                }}
              />

              {isHovered && (
                <span className="bar-tooltip" role="status">
                  <strong>{datum.label}</strong>
                  <span>
                    {formatValue(datum.value)}
                    {denominator > 0 && ` · ${share.toFixed(1)}% of ${denominator.toLocaleString()}`}
                  </span>
                  {datum.hint && <span className="bar-tooltip-hint">{datum.hint}</span>}
                </span>
              )}
            </span>

            <span className="bar-value">{formatValue(datum.value)}</span>
          </div>
        )
      })}
    </div>
  )
}

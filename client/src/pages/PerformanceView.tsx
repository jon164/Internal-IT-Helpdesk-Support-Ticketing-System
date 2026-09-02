import { useEffect, useMemo, useState } from 'react'
import { ApiError, api } from '../api/client'
import type { MetricsSummary, User } from '../api/types'
import { BarChart, type BarDatum } from '../components/charts/BarChart'
import { Panel } from '../components/Panel'
import { EmptyDatasetNotice } from '../components/EmptyDatasetNotice'
import { ReportNotice } from '../components/ReportNotice'
import { StatTile } from '../components/StatTile'
import {
  ATTAINMENT_TARGET_PERCENT,
  attainmentTone,
  breachTone,
  formatDate,
  formatMinutes,
  formatPercent,
  humaniseStatus,
} from '../utils/format'

const PERIODS = [
  { days: 7, label: 'Last 7 days' },
  { days: 30, label: 'Last 30 days' },
  { days: 90, label: 'Last 90 days' },
] as const

/** Priority is ordered, so it gets an ordinal ramp of one hue rather than four unrelated colours. */
const PRIORITY_ORDER = ['Critical', 'High', 'Standard', 'Low'] as const

const PRIORITY_STYLE: Record<string, { label: string; color: string }> = {
  Critical: { label: 'P1 Critical', color: 'var(--seq-650)' },
  High: { label: 'P2 High', color: 'var(--seq-550)' },
  Standard: { label: 'P3 Standard', color: 'var(--seq-450)' },
  Low: { label: 'P4 Low', color: 'var(--seq-250)' },
}

/**
 * How the desk performed against its commitments.
 *
 * Answers "are we meeting our targets". The companion Backlog view answers "is the queue growing" —
 * a different question that a point-in-time attainment figure cannot address.
 */
export function PerformanceView({ currentUser }: { currentUser: User }) {
  const [periodDays, setPeriodDays] = useState<number>(30)
  const [metrics, setMetrics] = useState<MetricsSummary | null>(null)
  const [error, setError] = useState<ApiError | null>(null)
  const [loading, setLoading] = useState(true)
  const [showTable, setShowTable] = useState(false)

  useEffect(() => {
    let cancelled = false

    const load = async () => {
      setLoading(true)
      setError(null)

      const to = new Date()
      const from = new Date(to.getTime() - periodDays * 24 * 60 * 60 * 1000)

      try {
        const result = await api.getMetrics(currentUser.id, from, to)

        if (!cancelled) {
          setMetrics(result)
        }
      } catch (caught) {
        if (!cancelled) {
          setMetrics(null)
          setError(
            caught instanceof ApiError
              ? caught
              : new ApiError(0, {
                  error: 'Invalid',
                  message: 'The report could not be loaded. Is the API running?',
                }),
          )
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void load()

    return () => {
      cancelled = true
    }
  }, [currentUser.id, periodDays])

  const priorityData = useMemo<BarDatum[]>(() => {
    if (!metrics) return []

    return PRIORITY_ORDER.filter((p) => (metrics.createdByPriority[p] ?? 0) > 0).map((priority) => ({
      key: priority,
      label: PRIORITY_STYLE[priority].label,
      value: metrics.createdByPriority[priority] ?? 0,
      color: PRIORITY_STYLE[priority].color,
    }))
  }, [metrics])

  const categoryData = useMemo<BarDatum[]>(() => {
    if (!metrics) return []

    return Object.entries(metrics.createdByCategory)
      .map(([category, count]) => ({ key: category, label: category, value: count }))
      .sort((a, b) => b.value - a.value)
  }, [metrics])

  const statusData = useMemo<BarDatum[]>(() => {
    if (!metrics) return []

    return Object.entries(metrics.openByStatus)
      .map(([status, count]) => ({ key: status, label: humaniseStatus(status), value: count }))
      .sort((a, b) => b.value - a.value)
  }, [metrics])

  /**
   * SLA health of open work. Status colours appear here and nowhere else in this view, each beside
   * its own word, so a colour never carries the meaning alone.
   */
  const slaHealthData = useMemo<BarDatum[]>(() => {
    if (!metrics) return []

    const onTrack = Math.max(metrics.openBacklog - metrics.openBreached - metrics.openAtRisk, 0)

    return [
      { key: 'breached', label: 'Breached', value: metrics.openBreached, color: 'var(--status-critical)' },
      { key: 'atRisk', label: 'At risk', value: metrics.openAtRisk, color: 'var(--status-warning)' },
      { key: 'onTrack', label: 'On track', value: onTrack, color: 'var(--status-good)' },
    ]
  }, [metrics])

  // Nothing raised, nothing outstanding: there is no report to draw, only that fact to state.
  const isEmpty = metrics !== null && metrics.totalCreated === 0 && metrics.openBacklog === 0

  return (
    <div className="view">
      <div className="view-filters">
        <p className="view-range">
          {metrics ? `${formatDate(metrics.from)} to ${formatDate(metrics.to)}` : ' '}
        </p>

        <div className="filter-controls">
          <div className="segmented" role="group" aria-label="Reporting period">
            {PERIODS.map((period) => (
              <button
                key={period.days}
                type="button"
                className={periodDays === period.days ? 'is-selected' : ''}
                aria-pressed={periodDays === period.days}
                onClick={() => setPeriodDays(period.days)}
              >
                {period.label}
              </button>
            ))}
          </div>

          <button
            type="button"
            className="link-button"
            aria-pressed={showTable}
            onClick={() => setShowTable((shown) => !shown)}
          >
            {showTable ? 'Hide table' : 'View as table'}
          </button>
        </div>
      </div>

      {loading && <p className="dashboard-message">Loading the report…</p>}

      {error && <ReportNotice error={error} currentUser={currentUser} />}

      {metrics && !loading && isEmpty && <EmptyDatasetNotice currentUser={currentUser} />}

      {/* The tiles and charts are withheld rather than drawn as a wall of zeroes. Every figure would
          be accurate and every one of them would mislead: "0 breached" beside a green tick reads as
          an achievement when the truth is that nothing exists to breach anything. */}
      {metrics && !loading && !isEmpty && (
        <>
          <div className="tile-grid">
            <StatTile
              hero
              label="Resolution attainment"
              value={formatPercent(metrics.resolutionAttainmentPercent)}
              tone={attainmentTone(metrics.resolutionAttainmentPercent)}
              hint={
                metrics.resolutionAttainmentPercent === null
                  ? 'Nothing was resolved in this period.'
                  : `${metrics.totalResolved} resolved against a ${ATTAINMENT_TARGET_PERCENT}% target.`
              }
            >
              <AttainmentMeter percent={metrics.resolutionAttainmentPercent} />
            </StatTile>

            <StatTile
              label="Response attainment"
              value={formatPercent(metrics.responseAttainmentPercent)}
              tone={attainmentTone(metrics.responseAttainmentPercent)}
              hint="First contact, against each priority's own target."
            >
              <AttainmentMeter percent={metrics.responseAttainmentPercent} />
            </StatTile>

            <StatTile
              label="Breached and still open"
              value={metrics.openBreached.toLocaleString()}
              tone={breachTone(metrics.openBreached)}
              toneLabel={metrics.openBreached === 0 ? 'None outstanding' : undefined}
              hint={
                metrics.openAtRisk === 0
                  ? 'Nothing else is close to its target.'
                  : `${metrics.openAtRisk} more are close to breaching.`
              }
            />

            <StatTile
              label="Open backlog"
              value={metrics.openBacklog.toLocaleString()}
              hint={`Tickets still needing a technician. A further ${metrics.awaitingClosure} are resolved and awaiting the requester's confirmation.`}
            />

            <StatTile
              label="Raised / resolved"
              value={`${metrics.totalCreated} / ${metrics.totalResolved}`}
              tone={
                metrics.throughputRatioPercent !== null && metrics.throughputRatioPercent < 90
                  ? 'warning'
                  : 'neutral'
              }
              toneLabel={
                metrics.throughputRatioPercent !== null && metrics.throughputRatioPercent < 90
                  ? 'Falling behind intake'
                  : undefined
              }
              hint={
                metrics.throughputRatioPercent === null
                  ? 'No tickets raised in this period.'
                  : `Clearing ${formatPercent(metrics.throughputRatioPercent, 0)} of what comes in.`
              }
            />

            <StatTile
              label="Median time to resolve"
              value={formatMinutes(metrics.medianResolutionMinutes)}
              hint={`Mean ${formatMinutes(
                metrics.meanResolutionMinutes,
              )}. Measured on each ticket's own clock.`}
            />
          </div>

          <div className="panel-grid">
            <Panel
              title="What is at risk right now"
              note="Open tickets by position against their resolution target."
            >
              <BarChart
                data={slaHealthData}
                total={metrics.openBacklog}
                caption={`Of ${metrics.openBacklog} open tickets, ${metrics.openBreached} have breached their resolution target and ${metrics.openAtRisk} are at risk.`}
                emptyMessage="Nothing is open."
              />
            </Panel>

            <Panel title="Where the open work sits" note="Open tickets by status.">
              <BarChart
                data={statusData}
                total={metrics.openBacklog}
                caption="Open tickets grouped by workflow status."
                emptyMessage="Nothing is open."
              />
            </Panel>

            <Panel title="Raised by priority" note="Demand mix for the period.">
              <BarChart
                data={priorityData}
                total={metrics.totalCreated}
                caption="Tickets raised in the period, grouped by priority band."
              />
            </Panel>

            <Panel
              title="Raised by category"
              note="Largest first — the categories worth fixing at source."
            >
              <BarChart
                data={categoryData}
                total={metrics.totalCreated}
                caption="Tickets raised in the period, grouped by category, largest first."
              />
            </Panel>
          </div>

          {showTable && <MetricsTable metrics={metrics} />}

          <footer className="dashboard-footer">
            <p>
              Attainment counts a ticket as met when it was answered or resolved inside the target for
              its priority, with any time spent on hold awaiting the requester excluded from the
              resolution clock. P1 and P2 run continuously; P3 and P4 run on working time only, so a
              request raised on Friday evening is not penalised for the weekend.
            </p>
            <p>A dash means nothing qualified in this period. It does not mean zero.</p>
          </footer>
        </>
      )}
    </div>
  )
}

/**
 * A meter with the target marked on it.
 *
 * Attainment alone does not answer "is that good?" — the reader has to remember the target. Drawing
 * the target on the bar removes that step.
 */
function AttainmentMeter({ percent }: { percent: number | null }) {
  if (percent === null) {
    return null
  }

  const tone = attainmentTone(percent)

  const fill =
    tone === 'good'
      ? 'var(--status-good)'
      : tone === 'warning'
        ? 'var(--status-warning)'
        : 'var(--status-critical)'

  return (
    <>
      <div
        className="meter"
        role="meter"
        aria-valuenow={Math.round(percent)}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={`Attainment ${percent.toFixed(1)} percent against a ${ATTAINMENT_TARGET_PERCENT} percent target`}
      >
        <div
          className="meter-fill"
          style={{ width: `${Math.min(Math.max(percent, 0), 100)}%`, background: fill }}
        />
        <div className="meter-target" style={{ left: `${ATTAINMENT_TARGET_PERCENT}%` }} />
      </div>
      <p className="meter-caption">
        <span>0%</span>
        <span>Target {ATTAINMENT_TARGET_PERCENT}%</span>
        <span>100%</span>
      </p>
    </>
  )
}

/** Every figure on the page as text, for screen readers, copying into the report, and printing. */
function MetricsTable({ metrics }: { metrics: MetricsSummary }) {
  const rows: Array<[string, string]> = [
    ['Period', `${formatDate(metrics.from)} to ${formatDate(metrics.to)}`],
    ['Raised', metrics.totalCreated.toLocaleString()],
    ['Resolved', metrics.totalResolved.toLocaleString()],
    ['Open backlog', metrics.openBacklog.toLocaleString()],
    ['Awaiting requester confirmation', metrics.awaitingClosure.toLocaleString()],
    ['Open and breached', metrics.openBreached.toLocaleString()],
    ['Open and at risk', metrics.openAtRisk.toLocaleString()],
    ['Response attainment', formatPercent(metrics.responseAttainmentPercent)],
    ['Resolution attainment', formatPercent(metrics.resolutionAttainmentPercent)],
    ['Median time to resolve', formatMinutes(metrics.medianResolutionMinutes)],
    ['Mean time to resolve', formatMinutes(metrics.meanResolutionMinutes)],
    ['Throughput ratio', formatPercent(metrics.throughputRatioPercent, 0)],
  ]

  return (
    <section className="panel table-panel">
      <header className="panel-header">
        <h2>All figures</h2>
        <p>The same data as text.</p>
      </header>

      <table className="metrics-table">
        <tbody>
          {rows.map(([label, value]) => (
            <tr key={label}>
              <th scope="row">{label}</th>
              <td>{value}</td>
            </tr>
          ))}

          {PRIORITY_ORDER.map((priority) => (
            <tr key={priority}>
              <th scope="row">Raised — {PRIORITY_STYLE[priority].label}</th>
              <td>{(metrics.createdByPriority[priority] ?? 0).toLocaleString()}</td>
            </tr>
          ))}

          {Object.entries(metrics.createdByCategory)
            .sort((a, b) => b[1] - a[1])
            .map(([category, count]) => (
              <tr key={category}>
                <th scope="row">Raised — {category}</th>
                <td>{count.toLocaleString()}</td>
              </tr>
            ))}
        </tbody>
      </table>
    </section>
  )
}

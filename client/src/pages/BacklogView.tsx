import { useEffect, useMemo, useState } from 'react'
import { ApiError, api } from '../api/client'
import type { BacklogReport, User } from '../api/types'
import { BarChart, type BarDatum } from '../components/charts/BarChart'
import {
  FlowBarsChart,
  QueueTrendChart,
  type FlowPoint,
  type TrendPoint,
} from '../components/charts/TimeSeriesChart'
import { Panel } from '../components/Panel'
import { EmptyDatasetNotice } from '../components/EmptyDatasetNotice'
import { ReportNotice } from '../components/ReportNotice'
import { StatTile, type StatTone } from '../components/StatTile'
import { formatDate, formatDays, formatPercent, formatShortDate } from '../utils/format'

const WINDOWS = [
  { weeks: 4, label: '4 weeks' },
  { weeks: 8, label: '8 weeks' },
  { weeks: 12, label: '12 weeks' },
] as const

/**
 * Age bands read as an ordered ramp of one hue: the longer something has waited, the darker it is.
 * Four unrelated colours would imply four unrelated categories, which is the opposite of the point.
 */
const AGE_COLORS = [
  'var(--seq-250)',
  'var(--seq-350)',
  'var(--seq-450)',
  'var(--seq-550)',
  'var(--seq-650)',
]

const DIRECTION_COPY: Record<BacklogReport['direction'], { tone: StatTone; label: string }> = {
  Growing: { tone: 'critical', label: 'Queue growing' },
  Steady: { tone: 'neutral', label: 'Holding steady' },
  Shrinking: { tone: 'good', label: 'Queue shrinking' },
}

/**
 * Whether the team is falling behind.
 *
 * The companion Performance view reports attainment against targets. That cannot answer this
 * question: a backlog of 28 is healthy if it was 40 last month and alarming if it was 12. Direction
 * of travel needs a series, so this view leads with one.
 */
export function BacklogView({ currentUser }: { currentUser: User }) {
  const [weeks, setWeeks] = useState<number>(8)
  const [report, setReport] = useState<BacklogReport | null>(null)
  const [error, setError] = useState<ApiError | null>(null)
  const [loading, setLoading] = useState(true)
  const [showTable, setShowTable] = useState(false)

  useEffect(() => {
    let cancelled = false

    const load = async () => {
      setLoading(true)
      setError(null)

      try {
        const result = await api.getBacklog(currentUser.id, weeks)

        if (!cancelled) {
          setReport(result)
        }
      } catch (caught) {
        if (!cancelled) {
          setReport(null)
          setError(
            caught instanceof ApiError
              ? caught
              : new ApiError(0, {
                  error: 'Invalid',
                  message: 'The backlog report could not be loaded. Is the API running?',
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
  }, [currentUser.id, weeks])

  const trendPoints = useMemo<TrendPoint[]>(
    () =>
      (report?.trend ?? []).map((point) => ({
        key: point.periodStart,
        label: formatShortDate(point.periodStart),
        fullLabel: `Week of ${formatDate(point.periodStart)}`,
        value: point.unresolvedAtEnd,
      })),
    [report],
  )

  const flowPoints = useMemo<FlowPoint[]>(
    () =>
      (report?.trend ?? []).map((point) => ({
        key: point.periodStart,
        label: formatShortDate(point.periodStart),
        fullLabel: `Week of ${formatDate(point.periodStart)}`,
        raised: point.raised,
        resolved: point.resolved,
      })),
    [report],
  )

  const ageData = useMemo<BarDatum[]>(
    () =>
      (report?.ageBuckets ?? []).map((bucket, index) => ({
        key: bucket.label,
        label: bucket.label,
        value: bucket.count,
        color: AGE_COLORS[index] ?? 'var(--seq-450)',
      })),
    [report],
  )

  const assigneeData = useMemo<BarDatum[]>(
    () =>
      Object.entries(report?.unresolvedByAssignee ?? {})
        .map(([name, count]) => ({
          key: name,
          label: name,
          value: count,
          // Unassigned work is the manager's problem to fix, not a person's workload; the amber
          // marks it as needing action without implying anyone is underperforming.
          color: name === 'Unassigned' ? 'var(--status-warning)' : 'var(--seq-450)',
        }))
        .sort((a, b) => b.value - a.value),
    [report],
  )

  const direction = report ? DIRECTION_COPY[report.direction] : null

  // An empty queue that was also empty at the start of the window is an empty system, not a
  // cleared one, and a "Shrinking" verdict drawn over nothing would be a false reassurance.
  const isEmpty =
    report !== null &&
    report.unresolvedNow === 0 &&
    report.unresolvedAtStart === 0 &&
    report.trend.every((point) => point.raised === 0 && point.resolved === 0)

  return (
    <div className="view">
      <div className="view-filters">
        <p className="view-range">
          {report ? `${formatDate(report.from)} to ${formatDate(report.to)}` : ' '}
        </p>

        <div className="filter-controls">
          <div className="segmented" role="group" aria-label="History window">
            {WINDOWS.map((window) => (
              <button
                key={window.weeks}
                type="button"
                className={weeks === window.weeks ? 'is-selected' : ''}
                aria-pressed={weeks === window.weeks}
                onClick={() => setWeeks(window.weeks)}
              >
                {window.label}
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

      {loading && <p className="dashboard-message">Loading the backlog…</p>}

      {error && <ReportNotice error={error} currentUser={currentUser} />}

      {report && !loading && isEmpty && <EmptyDatasetNotice currentUser={currentUser} />}

      {report && direction && !loading && !isEmpty && (
        <>
          <div className="tile-grid">
            <StatTile
              hero
              label="Unresolved right now"
              value={report.unresolvedNow.toLocaleString()}
              tone={direction.tone}
              toneLabel={direction.label}
              hint={
                report.netChange === 0
                  ? `Unchanged from ${report.unresolvedAtStart} at the start of the window.`
                  : `${report.netChange > 0 ? 'Up' : 'Down'} ${Math.abs(report.netChange)} from ${
                      report.unresolvedAtStart
                    } at the start of the window.`
              }
            />

            <StatTile
              label="Clearance rate"
              value={formatPercent(report.clearanceRatePercent, 0)}
              tone={clearanceTone(report.clearanceRatePercent)}
              toneLabel={
                report.clearanceRatePercent !== null && report.clearanceRatePercent < 100
                  ? 'Arriving faster than cleared'
                  : undefined
              }
              hint="Resolved as a share of raised across the window. Under 100% means the queue grows."
            />

            <StatTile
              label="Breached and waiting"
              value={report.unresolvedBreached.toLocaleString()}
              tone={report.unresolvedBreached === 0 ? 'good' : 'critical'}
              toneLabel={report.unresolvedBreached === 0 ? 'None outstanding' : undefined}
              hint={
                report.unresolvedNow === 0
                  ? 'Nothing is waiting.'
                  : `${Math.round(
                      (report.unresolvedBreached / report.unresolvedNow) * 100,
                    )}% of the queue is already past its resolution target.`
              }
            />

            <StatTile
              label="Longest wait"
              value={formatDays(report.oldestUnresolvedDays)}
              tone={oldestTone(report.oldestUnresolvedDays)}
              hint={
                report.oldestUnresolvedReference
                  ? `${report.oldestUnresolvedReference} has been open the longest.`
                  : 'Nothing is currently unresolved.'
              }
            />
          </div>

          <div className="panel-grid">
            <Panel
              wide
              title="Unresolved queue over time"
              note="How many tickets were still awaiting a technician at the close of each week."
            >
              <QueueTrendChart
                points={trendPoints}
                caption={`The unresolved queue moved from ${report.unresolvedAtStart} to ${report.unresolvedNow} over ${weeks} weeks.`}
              />
            </Panel>

            <Panel
              wide
              title="Raised against resolved"
              note="Why the queue moved. It grows in any week where the left bar is taller."
            >
              <FlowBarsChart
                points={flowPoints}
                caption="Tickets raised and tickets resolved in each week of the window."
              />
            </Panel>

            <Panel
              title="How long work has been waiting"
              note="Age of everything currently unresolved."
            >
              <BarChart
                data={ageData}
                total={report.unresolvedNow}
                caption="Unresolved tickets grouped by how long they have been open."
                emptyMessage="Nothing is unresolved."
              />
            </Panel>

            <Panel title="Who is holding the queue" note="Unresolved tickets by assignee.">
              <BarChart
                data={assigneeData}
                total={report.unresolvedNow}
                caption="Unresolved tickets grouped by the technician they are assigned to."
                emptyMessage="Nothing is unresolved."
              />
            </Panel>
          </div>

          {showTable && <BacklogTable report={report} weeks={weeks} />}

          <footer className="dashboard-footer">
            <p>
              Unresolved means a ticket still needs work from a technician. Tickets sitting in
              Resolved awaiting the requester's confirmation are not counted, because they are no
              longer the team's outstanding work. Withdrawn requests are excluded throughout.
            </p>
            <p>
              Weekly history is reconstructed from each ticket's current timestamps. A ticket that was
              resolved and later reopened will appear never to have been resolved, so past weeks can
              overstate the queue slightly.
            </p>
          </footer>
        </>
      )}
    </div>
  )
}

/** Below 100% the queue grows by definition, so the banding is tied to that threshold, not a target. */
function clearanceTone(percent: number | null): StatTone {
  if (percent === null) return 'neutral'
  if (percent >= 100) return 'good'
  return percent >= 90 ? 'warning' : 'critical'
}

function oldestTone(days: number | null): StatTone {
  if (days === null) return 'neutral'
  if (days >= 30) return 'critical'
  return days >= 14 ? 'warning' : 'neutral'
}

/** Every figure as text, including the full weekly series. */
function BacklogTable({ report, weeks }: { report: BacklogReport; weeks: number }) {
  return (
    <section className="panel table-panel">
      <header className="panel-header">
        <h2>All figures</h2>
        <p>The same data as text.</p>
      </header>

      <table className="metrics-table">
        <tbody>
          <tr>
            <th scope="row">Window</th>
            <td>
              {formatDate(report.from)} to {formatDate(report.to)} ({weeks} weeks)
            </td>
          </tr>
          <tr>
            <th scope="row">Unresolved at start</th>
            <td>{report.unresolvedAtStart.toLocaleString()}</td>
          </tr>
          <tr>
            <th scope="row">Unresolved now</th>
            <td>{report.unresolvedNow.toLocaleString()}</td>
          </tr>
          <tr>
            <th scope="row">Net change</th>
            <td>
              {report.netChange > 0 ? '+' : ''}
              {report.netChange}
            </td>
          </tr>
          <tr>
            <th scope="row">Direction</th>
            <td>{report.direction}</td>
          </tr>
          <tr>
            <th scope="row">Clearance rate</th>
            <td>{formatPercent(report.clearanceRatePercent, 0)}</td>
          </tr>
          <tr>
            <th scope="row">Breached and waiting</th>
            <td>{report.unresolvedBreached.toLocaleString()}</td>
          </tr>
          <tr>
            <th scope="row">Longest wait</th>
            <td>
              {formatDays(report.oldestUnresolvedDays)}
              {report.oldestUnresolvedReference ? ` (${report.oldestUnresolvedReference})` : ''}
            </td>
          </tr>

          {report.ageBuckets.map((bucket) => (
            <tr key={bucket.label}>
              <th scope="row">Waiting — {bucket.label}</th>
              <td>{bucket.count.toLocaleString()}</td>
            </tr>
          ))}

          {report.trend.map((point) => (
            <tr key={point.periodStart}>
              <th scope="row">Week of {formatDate(point.periodStart)}</th>
              <td>
                {point.raised} raised, {point.resolved} resolved, {point.unresolvedAtEnd} unresolved
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}

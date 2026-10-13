import { useEffect, useMemo, useRef, useState } from 'react'
import { SummaryCard } from '../components/SummaryCard'
import { api, ApiError } from '../services/api'
import type { DashboardFilters, DashboardMetrics, Notice, Role, Ticket } from '../types'
import { PRIORITY_OPTIONS, STATUS_DISPLAY, STATUS_OPTIONS } from '../utils'

/**
 * USER STORY: Manager-only dashboard.
 * The server decides who may see analytics (403 for anyone but the IT Manager). This page never
 * renders the analytics layout until the server has returned data, so an employee sees only an
 * access-denied message — not an empty dashboard waiting for numbers.
 */
type Access = 'checking' | 'allowed' | 'denied'

const NO_FILTERS: DashboardFilters = { status: 'All', priority: 'All', assignee: 'All' }

export function DashboardPage({ role, setNotice }: { role: Role; setNotice: (notice: Notice) => void }) {
  const [access, setAccess] = useState<Access>('checking')
  const [metrics, setMetrics] = useState<DashboardMetrics | null>(null)
  const [tickets, setTickets] = useState<Ticket[]>([])
  const [performanceMessage, setPerformanceMessage] = useState('')
  const [filters, setFilters] = useState<DashboardFilters>(NO_FILTERS)
  const [technicianNames, setTechnicianNames] = useState<string[]>([])
  /*
   * USER STORY: filter by status, priority and assignee.
   * The assignee list comes from the technician roster, not from the tickets on screen. Built from
   * the results, it shrank as soon as another filter was applied, and a technician with no tickets
   * (the person most worth seeing when checking workload) could never be picked.
   */
  const assignees = useMemo(() => {
    const names = new Set([...technicianNames, ...tickets.map(x => x.assignee)])
    names.delete('Unassigned')
    if (filters.assignee !== 'All') names.add(filters.assignee)
    return [...Array.from(names).filter(n => n !== 'Unassigned').sort(), 'Unassigned']
  }, [technicianNames, tickets, filters.assignee])
  const filtering = filters.status !== 'All' || filters.priority !== 'All' || filters.assignee !== 'All'
  const deniedHeading = useRef<HTMLHeadingElement>(null)

  useEffect(() => { void load() }, [role, filters.status, filters.priority, filters.assignee])

  useEffect(() => {
    // Only fetched once the server has allowed the dashboard. The roster just fills the Assignee
    // list; if it fails, names from the results still work.
    if (access !== 'allowed') return
    api.getTechnicians()
      .then(list => setTechnicianNames(list.map(t => t.name)))
      .catch(() => setTechnicianNames([]))
  }, [access])

  useEffect(() => {
    // Move focus to the message so keyboard and screen-reader users learn why nothing appeared.
    if (access === 'denied') deniedHeading.current?.focus()
  }, [access])

  /** Any 403 — on load or on a later action — removes every figure already on screen. */
  function deny() {
    setMetrics(null)
    setTickets([])
    setPerformanceMessage('')
    setAccess('denied')
  }

  function handleError(error: unknown, fallback: string) {
    if (error instanceof ApiError && error.status === 403) deny()
    else setNotice({ type: 'error', text: error instanceof ApiError ? error.message : fallback })
  }

  async function load() {
    try {
      const [m, rows] = await Promise.all([api.getDashboard(role), api.getDashboardTickets(filters, role)])
      setMetrics(m); setTickets(rows); setAccess('allowed')
    } catch (error) {
      setMetrics(null); setTickets([])
      handleError(error, 'Dashboard failed to load.')
    }
  }

  async function runPerformance() {
    try {
      setPerformanceMessage('Preparing 500-ticket data...')
      const seed = await api.seedPerformanceData(500, role)
      const started = window.performance.now()
      const [m, rows] = await Promise.all([api.getDashboard(role), api.getDashboardTickets(filters, role)])
      const elapsed = window.performance.now() - started
      setMetrics(m); setTickets(rows)
      setPerformanceMessage(`${seed.count} records saved in ${seed.databaseSeedMilliseconds} ms. Dashboard API requests completed in ${elapsed.toFixed(1)} ms on this computer.`)
    } catch (error) { setPerformanceMessage(''); handleError(error, 'Performance test failed.') }
  }

  async function clearPerformance() {
    try { const result = await api.clearPerformanceData(role); setPerformanceMessage(result.message); await load() }
    catch (error) { handleError(error, 'Could not clear test data.') }
  }

  if (access === 'denied') {
    return <section className="panel access-denied" aria-labelledby="dashboard-denied-heading">
      <div className="denied-icon" aria-hidden="true">!</div>
      <h2 id="dashboard-denied-heading" ref={deniedHeading} tabIndex={-1}>Access denied</h2>
      <p>The dashboard is available to the IT Manager only. You are signed in as {role}, so service analytics are not shown.</p>
    </section>
  }

  if (access === 'checking') {
    return <section className="panel" role="status">Loading dashboard…</section>
  }

  return <>
    <section className="page-heading"><div><p className="eyebrow">MANAGER ANALYTICS</p><h2>Service health dashboard</h2><p>Backlog, resolution time, SLA risk, recurring issues and filtered export.</p></div><button className="primary-button" onClick={() => api.exportDashboard(filters, role).then(() => setNotice({ type:'success', text:`CSV exported with ${tickets.length} row(s).` })).catch((e: unknown) => handleError(e, 'Export failed.'))}>Export current CSV</button></section>
    <section className="summary-grid"><SummaryCard label="Open backlog" value={metrics?.openBacklog ?? '—'} note="Every ticket not Closed"/><SummaryCard label="Average resolution" value={metrics ? `${Math.round(metrics.averageResolutionMinutes)}m` : '—'} note="Resolved ticket average"/><SummaryCard label="Overdue" value={metrics?.overdueCount ?? '—'} note="Outside priority SLA" danger/><SummaryCard label="Recurring issue" value={metrics ? `${metrics.recurringIssueCategory} (${metrics.recurringIssueCount})` : '—'} note="Most common category"/></section>
    <section className="panel"><div className="panel-header dashboard-header"><div><p className="eyebrow">FILTERS</p><h3>Investigate ticket workload</h3></div><div className="performance-actions"><button className="secondary-button" onClick={runPerformance}>Run 500-ticket test</button><button className="text-button" onClick={clearPerformance}>Remove test data</button></div></div>
    {performanceMessage && <div className="performance-message">{performanceMessage}</div>}
    <div className="filter-grid"><label><span>Status</span><select value={filters.status} onChange={e => setFilters(f => ({...f,status:e.target.value}))}><option>All</option>{STATUS_OPTIONS.map(s => <option key={s} value={s}>{STATUS_DISPLAY[s]}</option>)}</select></label><label><span>Priority</span><select value={filters.priority} onChange={e => setFilters(f => ({...f,priority:e.target.value}))}><option>All</option>{PRIORITY_OPTIONS.map(p => <option key={p}>{p}</option>)}</select></label><label><span>Assignee</span><select value={filters.assignee} onChange={e => setFilters(f => ({...f,assignee:e.target.value}))}><option>All</option>{assignees.map(a => <option key={a}>{a}</option>)}</select></label></div>
    <div className="results-line"><strong role="status" aria-live="polite">{tickets.length} {tickets.length === 1 ? 'result' : 'results'}</strong><span>Filters also apply to CSV export.</span>{filtering && <button type="button" className="text-button" onClick={() => setFilters(NO_FILTERS)}>Clear filters</button>}</div>
    <div className="table-wrap"><table><thead><tr><th>Ticket</th><th>Location</th><th>System</th><th>Priority</th><th>Status</th><th>Assignee</th><th>Passenger</th><th>SLA</th></tr></thead><tbody>{tickets.map(t => <tr key={t.id}><td>#{t.id}</td><td>{t.terminal} / {t.area}</td><td>{t.systemType}</td><td>{t.priority}</td><td>{STATUS_DISPLAY[t.status]}</td><td>{t.assignee}</td><td>{t.passengerImpact ? 'Yes' : 'No'}</td><td>{t.slaOverdue ? <span className="table-overdue">Overdue</span> : 'Within SLA'}</td></tr>)}</tbody></table>{tickets.length === 0 && <div className="empty-state">{filtering
      ? <><strong>No tickets match these filters.</strong><p>Try a different status, priority or assignee, or clear the filters to see every ticket.</p></>
      : <><strong>There are no tickets yet.</strong><p>New tickets will appear here.</p></>}</div>}</div>
    </section>
  </>
}

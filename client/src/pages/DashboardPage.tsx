import { useEffect, useMemo, useState } from 'react'
import { SummaryCard } from '../components/SummaryCard'
import { api, ApiError } from '../services/api'
import type { DashboardFilters, DashboardMetrics, Notice, Role, Ticket } from '../types'
import { PRIORITY_OPTIONS, STATUS_DISPLAY, STATUS_OPTIONS } from '../utils'

export function DashboardPage({ role, setNotice }: { role: Role; setNotice: (notice: Notice) => void }) {
  const [metrics, setMetrics] = useState<DashboardMetrics | null>(null)
  const [tickets, setTickets] = useState<Ticket[]>([])
  const [denied, setDenied] = useState(false)
  const [performanceMessage, setPerformanceMessage] = useState('')
  const [filters, setFilters] = useState<DashboardFilters>({ status: 'All', priority: 'All', assignee: 'All' })
  const assignees = useMemo(() => Array.from(new Set(tickets.map(x => x.assignee))).sort(), [tickets])

  useEffect(() => { void load() }, [role, filters.status, filters.priority, filters.assignee])

  async function load() {
    try {
      setDenied(false)
      const [m, rows] = await Promise.all([api.getDashboard(role), api.getDashboardTickets(filters, role)])
      setMetrics(m); setTickets(rows)
    } catch (error) {
      setMetrics(null); setTickets([])
      if (error instanceof ApiError && error.status === 403) setDenied(true)
      else setNotice({ type: 'error', text: error instanceof ApiError ? error.message : 'Dashboard failed to load.' })
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
    } catch (error) { setPerformanceMessage(''); setNotice({ type: 'error', text: error instanceof ApiError ? error.message : 'Performance test failed.' }) }
  }

  async function clearPerformance() {
    try { const result = await api.clearPerformanceData(role); setPerformanceMessage(result.message); await load() }
    catch (error) { setNotice({ type: 'error', text: error instanceof ApiError ? error.message : 'Could not clear test data.' }) }
  }

  return <>
    <section className="page-heading"><div><p className="eyebrow">MANAGER ANALYTICS</p><h2>Service health dashboard</h2><p>Backlog, resolution time, SLA risk, recurring issues and filtered export.</p></div>{!denied && role === 'IT Manager' && <button className="primary-button" onClick={() => api.exportDashboard(filters, role).then(() => setNotice({ type:'success', text:`CSV exported with ${tickets.length} row(s).` })).catch((e: unknown) => setNotice({ type:'error', text:e instanceof ApiError ? e.message : 'Export failed.' }))}>Export current CSV</button>}</section>
    {denied ? <section className="panel access-denied"><div className="denied-icon">!</div><h3>Access denied</h3><p>The manager dashboard is restricted by the ASP.NET API to the IT Manager demo role.</p></section> : <>
      <section className="summary-grid"><SummaryCard label="Open backlog" value={metrics?.openBacklog ?? '—'} note="Every ticket not Closed"/><SummaryCard label="Average resolution" value={metrics ? `${Math.round(metrics.averageResolutionMinutes)}m` : '—'} note="Resolved ticket average"/><SummaryCard label="Overdue" value={metrics?.overdueCount ?? '—'} note="Outside priority SLA" danger/><SummaryCard label="Recurring issue" value={metrics ? `${metrics.recurringIssueCategory} (${metrics.recurringIssueCount})` : '—'} note="Most common category"/></section>
      <section className="panel"><div className="panel-header dashboard-header"><div><p className="eyebrow">FILTERS</p><h3>Investigate ticket workload</h3></div><div className="performance-actions"><button className="secondary-button" onClick={runPerformance}>Run 500-ticket test</button><button className="text-button" onClick={clearPerformance}>Remove test data</button></div></div>
      {performanceMessage && <div className="performance-message">{performanceMessage}</div>}
      <div className="filter-grid"><label><span>Status</span><select value={filters.status} onChange={e => setFilters(f => ({...f,status:e.target.value}))}><option>All</option>{STATUS_OPTIONS.map(s => <option key={s} value={s}>{STATUS_DISPLAY[s]}</option>)}</select></label><label><span>Priority</span><select value={filters.priority} onChange={e => setFilters(f => ({...f,priority:e.target.value}))}><option>All</option>{PRIORITY_OPTIONS.map(p => <option key={p}>{p}</option>)}</select></label><label><span>Assignee</span><select value={filters.assignee} onChange={e => setFilters(f => ({...f,assignee:e.target.value}))}><option>All</option>{assignees.map(a => <option key={a}>{a}</option>)}</select></label></div>
      <div className="results-line"><strong>{tickets.length} result(s)</strong><span>Filters also apply to CSV export.</span></div>
      <div className="table-wrap"><table><thead><tr><th>Ticket</th><th>Location</th><th>System</th><th>Priority</th><th>Status</th><th>Assignee</th><th>Passenger</th><th>SLA</th></tr></thead><tbody>{tickets.map(t => <tr key={t.id}><td>#{t.id}</td><td>{t.terminal} / {t.area}</td><td>{t.systemType}</td><td>{t.priority}</td><td>{STATUS_DISPLAY[t.status]}</td><td>{t.assignee}</td><td>{t.passengerImpact ? 'Yes' : 'No'}</td><td>{t.slaOverdue ? <span className="table-overdue">Overdue</span> : 'Within SLA'}</td></tr>)}</tbody></table>{tickets.length === 0 && <div className="empty-state"><strong>No tickets match these filters.</strong><p>The empty result is handled without an error.</p></div>}</div>
      </section>
    </>}
  </>
}

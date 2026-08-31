import { useState, type FormEvent } from 'react'
import { api, ApiError } from '../services/api'
import type { FlightRecord } from '../types'

export function FlightLookupPage() {
  const [query, setQuery] = useState('')
  const [offline, setOffline] = useState(false)
  const [result, setResult] = useState<FlightRecord | null>(null)
  const [error, setError] = useState('')

  async function search(event: FormEvent) {
    event.preventDefault(); setResult(null); setError('')
    try { setResult(await api.searchFlight(query, offline)) }
    catch (e) { setError(e instanceof ApiError ? e.message : 'Flight lookup failed.') }
  }

  return <>
    <section className="page-heading"><div><p className="eyebrow">PASSENGER ASSISTANCE</p><h2>Flight and gate lookup</h2><p>Sample flight data demonstrates the planned passenger-help workflow.</p></div><div className={offline ? 'offline-chip' : 'online-chip'}>{offline ? '● Lookup unavailable' : '● Lookup online'}</div></section>
    <section className="panel flight-panel"><div className="outage-control"><span>Testing control</span><button className="secondary-button" onClick={() => { setOffline(x => !x); setResult(null); setError('') }}>{offline ? 'Restore lookup' : 'Simulate lookup failure'}</button></div><form className="flight-form" onSubmit={search}><label><span>Flight number or destination</span><div className="inline-input"><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Try NZ428 or Christchurch"/><button className="primary-button" type="submit">Search flight</button></div></label></form>{error && <div className="flight-error">{error}</div>}{result && <div className="flight-result"><div className="flight-number">{result.flightNumber}</div><Info label="Destination" value={result.destination}/><Info label="Current gate" value={result.gate}/><Info label="Status" value={result.status}/></div>}<div className="sample-searches"><span>Try:</span>{['NZ101','NZ428','QF144','JQ202','EK449'].map(x => <button type="button" key={x} onClick={() => setQuery(x)}>{x}</button>)}</div></section>
  </>
}

function Info({ label, value }: { label: string; value: string }) { return <div className="detail"><span>{label}</span><strong>{value}</strong></div> }

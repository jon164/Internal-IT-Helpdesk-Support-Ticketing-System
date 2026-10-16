import { useEffect, useId, useRef, useState } from 'react'
import type { ExportKind } from '../types'
import { EXPORT_CHOICES } from '../utils'

/*
 * USER STORY: As an IT manager, I want to export the dashboard summary, so that I can include
 * service data in reports to senior management.
 *   AC1 Given the dashboard is displayed, when I choose export, then a file (e.g. CSV) of the
 *       current metrics is produced.
 *   AC2 Given no ticket data exists, when I export, then a valid file with headers and zero rows
 *       is produced.
 *
 * "Export current CSV" opens this panel so the manager picks which file to download. It is an
 * inline panel, not a modal: the dashboard stays readable behind it. Focus moves to the first
 * choice when it opens and back to the Export button when it closes (Escape, Cancel or after a
 * download), so keyboard and screen-reader users are never left on a control that vanished.
 */

export function ExportPanel({ ticketRows, onExport, onClose }: {
  ticketRows: number
  onExport: (kind: ExportKind) => Promise<boolean>
  onClose: () => void
}) {
  const [kind, setKind] = useState<ExportKind>('summary')
  const [busy, setBusy] = useState(false)
  const first = useRef<HTMLInputElement>(null)
  const id = useId()

  useEffect(() => { first.current?.focus() }, [])

  async function download() {
    setBusy(true)
    const done = await onExport(kind)
    setBusy(false)
    if (done) onClose()
  }

  return <section
    className="panel export-panel"
    id="dashboard-export-panel"
    aria-labelledby={`${id}-heading`}
    onKeyDown={e => { if (e.key === 'Escape' && !busy) { e.stopPropagation(); onClose() } }}
  >
    <h3 id={`${id}-heading`}>Export dashboard data</h3>
    <fieldset className="export-choices" disabled={busy}>
      <legend>Which file do you want?</legend>
      {EXPORT_CHOICES.map((choice, index) => <label key={choice.kind} className="export-choice">
        <input
          ref={index === 0 ? first : undefined}
          type="radio"
          name={`${id}-kind`}
          value={choice.kind}
          checked={kind === choice.kind}
          onChange={() => setKind(choice.kind)}
          aria-describedby={`${id}-${choice.kind}-help`}
        />
        <span><strong>{choice.label}</strong><small id={`${id}-${choice.kind}-help`}>{choice.description(ticketRows)}</small></span>
      </label>)}
    </fieldset>
    <div className="export-actions">
      <button type="button" className="primary-button" onClick={download} disabled={busy}>{busy ? 'Downloading…' : 'Download CSV'}</button>
      <button type="button" className="text-button" onClick={onClose} disabled={busy}>Cancel</button>
    </div>
  </section>
}

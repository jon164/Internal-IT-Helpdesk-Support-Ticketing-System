export function SummaryCard({ label, value, note, danger = false }: {
  label: string
  value: number | string
  note: string
  danger?: boolean
}) {
  return (
    <article className={danger ? 'summary danger' : 'summary'}>
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{note}</small>
    </article>
  )
}

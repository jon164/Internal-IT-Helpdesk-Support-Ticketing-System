import type { ExportKind, TicketPriority, TicketStatus } from './types'

export const STATUS_DISPLAY: Record<TicketStatus, string> = {
  New: 'New',
  InProgress: 'In Progress',
  WaitingOnUser: 'Waiting on User',
  Resolved: 'Resolved',
  Closed: 'Closed',
}

export const STATUS_OPTIONS: TicketStatus[] = ['New', 'InProgress', 'WaitingOnUser', 'Resolved', 'Closed']
export const PRIORITY_OPTIONS: TicketPriority[] = ['Low', 'Medium', 'High', 'Critical']
export const TECHNICIANS = ['Alex Morgan', 'Priya Shah', 'Jordan Lee']

export function formatDate(value: string) {
  return new Date(value).toLocaleString()
}

export function formatFileSize(value?: number | null) {
  if (!value) return ''
  return value < 1024 * 1024 ? `${Math.round(value / 1024)} KB` : `${(value / 1024 / 1024).toFixed(1)} MB`
}

/** The files the dashboard export panel offers (USER STORY: export the dashboard summary). */
export const EXPORT_CHOICES: { kind: ExportKind; label: string; description: (ticketRows: number) => string }[] = [
  { kind: 'summary', label: 'Metric summary', description: () => 'One row per dashboard figure: backlog, overdue, average resolution and most common category. Covers all tickets.' },
  { kind: 'by-priority', label: 'Breakdown by priority', description: () => 'Total, open, overdue, resolved and average resolution for each priority. Covers all tickets.' },
  { kind: 'tickets', label: 'Ticket list', description: rows => `The tickets in the table, with the current filters (${rows} ${rows === 1 ? 'row' : 'rows'}).` },
]

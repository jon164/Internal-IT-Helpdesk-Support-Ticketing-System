import type { TicketPriority, TicketStatus } from './types'

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

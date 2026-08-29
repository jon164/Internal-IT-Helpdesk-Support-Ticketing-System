/**
 * Shapes returned by the Helpdesk API.
 *
 * These mirror the DTOs in Helpdesk.Api/Contracts. They are hand-written rather than generated so
 * the client compiles without a build-time dependency on a running server; if the API changes, the
 * compiler flags the mismatch here rather than at runtime in the browser.
 */

export type UserRole = 'Requester' | 'Technician' | 'TeamLead'

export type TicketPriority = 'Critical' | 'High' | 'Standard' | 'Low'

export type TicketStatus =
  | 'New'
  | 'Triaged'
  | 'Assigned'
  | 'InProgress'
  | 'OnHold'
  | 'Resolved'
  | 'Closed'
  | 'Cancelled'

export type SlaState = 'NotApplicable' | 'OnTrack' | 'AtRisk' | 'Breached' | 'Met' | 'Missed'

export interface User {
  id: string
  displayName: string
  department: string
  role: UserRole
}

export interface SlaPolicy {
  priority: TicketPriority
  displayName: string
  description: string
  responseTargetMinutes: number
  resolutionTargetMinutes: number
  clock: 'Continuous' | 'BusinessHours'
  warningThreshold: number
}

export interface SlaStatus {
  state: SlaState
  targetMinutes: number
  consumedMinutes: number
  remainingMinutes: number
  dueAt: string
  percentConsumed: number
}

export interface TicketSummary {
  id: number
  reference: string
  title: string
  category: string
  priority: TicketPriority
  priorityLabel: string
  status: TicketStatus
  requesterName: string
  department: string
  assignedTechnicianName: string | null
  isRestricted: boolean
  createdAt: string
  response: SlaStatus
  resolution: SlaStatus
}

/**
 * The management report.
 *
 * Attainment figures are nullable by design: "no tickets were due a response this period" and
 * "every response was late" are different statements, and the dashboard must not render the second
 * when the first is true.
 */
export interface MetricsSummary {
  from: string
  to: string
  totalCreated: number
  totalResolved: number
  openBacklog: number
  awaitingClosure: number
  openBreached: number
  openAtRisk: number
  responseAttainmentPercent: number | null
  resolutionAttainmentPercent: number | null
  medianResolutionMinutes: number | null
  meanResolutionMinutes: number | null
  throughputRatioPercent: number | null
  createdByPriority: Record<string, number>
  openByStatus: Record<string, number>
  createdByCategory: Record<string, number>
}

export interface ApiProblem {
  error: 'Unauthenticated' | 'NotFound' | 'Forbidden' | 'Conflict' | 'Invalid'
  message: string
}

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
  isActive: boolean
}

export type UserEventType = 'RoleChanged' | 'Deactivated' | 'Reactivated'

/** One record of a change to somebody's account. */
export interface UserAuditEntry {
  id: number
  occurredAt: string
  eventType: UserEventType
  subjectUserId: string
  subjectDisplayName: string
  actorDisplayName: string
  fromRole: UserRole | null
  toRole: UserRole | null
  reason: string
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
  /** Sent alongside the name so the client can compare identity without matching on display text. */
  assignedTechnicianId: string | null
  assignedTechnicianName: string | null
  isRestricted: boolean
  createdAt: string
  response: SlaStatus
  resolution: SlaStatus
}

export type TicketEventType =
  | 'Created'
  | 'StatusChanged'
  | 'Assigned'
  | 'Unassigned'
  | 'PriorityChanged'
  | 'FirstResponseRecorded'
  | 'HoldStarted'
  | 'HoldEnded'
  | 'CommentAdded'
  | 'Reopened'

/**
 * One entry in a ticket's audit trail.
 *
 * Append-only by construction — the API exposes no route that edits or deletes one. The record is
 * the answer to "who changed this, when, and why", which the problem definition names as the thing
 * a shared mailbox could never provide.
 */
export interface TicketEvent {
  id: number
  occurredAt: string
  eventType: TicketEventType
  actorName: string
  actorRole: UserRole
  fromStatus: TicketStatus | null
  toStatus: TicketStatus | null
  detail: string
}

/** A window during which the resolution clock was suspended awaiting somebody outside the team. */
export interface HoldPeriod {
  startedAt: string
  endedAt: string | null
  reason: string | null
}

/**
 * What the server says this caller may do with this ticket.
 *
 * Used to decide which controls to render. It is not the enforcement point: every action is
 * re-checked server-side when attempted, so forging these flags in the browser gains nothing. The
 * client asks rather than deciding for itself, which is why the rules cannot drift.
 */
export interface TicketPermissions {
  canComment: boolean
  canChangePriority: boolean
  canAssignToSelf: boolean
  canAssignToOthers: boolean
  allowedNextStatuses: TicketStatus[]
}

export interface TicketDetail {
  summary: TicketSummary
  description: string
  resolutionNotes: string | null
  firstRespondedAt: string | null
  resolvedAt: string | null
  closedAt: string | null
  policy: SlaPolicy
  holdPeriods: HoldPeriod[]
  events: TicketEvent[]
  permissions: TicketPermissions
}

/** The payload for raising a request. Mirrors CreateTicketRequest and its validation. */
export interface CreateTicketRequest {
  title: string
  description: string
  category: string
  priority: TicketPriority
  markRestricted: boolean
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

export interface BacklogTrendPoint {
  periodStart: string
  periodEnd: string
  raised: number
  resolved: number
  unresolvedAtEnd: number
  netChange: number
}

export interface BacklogAgeBucket {
  label: string
  count: number
}

/**
 * Whether the unresolved queue is growing.
 *
 * Distinct from {@link MetricsSummary}, which reports performance against targets. This reports
 * direction of travel, which a single point-in-time figure cannot express.
 */
export interface BacklogReport {
  from: string
  to: string
  trend: BacklogTrendPoint[]
  unresolvedAtStart: number
  unresolvedNow: number
  unresolvedBreached: number
  netChange: number
  direction: 'Growing' | 'Steady' | 'Shrinking'
  clearanceRatePercent: number | null
  ageBuckets: BacklogAgeBucket[]
  oldestUnresolvedDays: number | null
  oldestUnresolvedReference: string | null
  unresolvedByAssignee: Record<string, number>
}

/**
 * The state of the demonstration dataset.
 *
 * `isEmpty` comes from the server rather than being derived from `ticketCount` on the client, so
 * there is one definition of "empty" and the generate button cannot disagree with the endpoint that
 * enforces it.
 */
export interface SampleDataStatus {
  ticketCount: number
  isEmpty: boolean
  generatedCount: number
}

export interface SampleDataCleared {
  removedCount: number
  status: SampleDataStatus
}

export interface ApiProblem {
  error: 'Unauthenticated' | 'NotFound' | 'Forbidden' | 'Conflict' | 'Invalid'
  message: string
}

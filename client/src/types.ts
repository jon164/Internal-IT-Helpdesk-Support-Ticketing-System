export type Role = 'Airport Staff' | 'IT Technician' | 'IT Manager'
export type TicketStatus = 'New' | 'InProgress' | 'WaitingOnUser' | 'Resolved' | 'Closed'
export type TicketPriority = 'Low' | 'Medium' | 'High' | 'Critical'
export type TicketSort = 'newest' | 'oldest' | 'priority-age'

export type Technician = {
  id: string
  name: string
  email: string
  isActive: boolean
}

export type Ticket = {
  id: number
  terminal: string
  area: string
  systemType: string
  description: string
  passengerImpact: boolean
  flightOpsImpact: boolean
  reporterName: string
  reporterEmail: string
  staffId: string
  priority: TicketPriority
  status: TicketStatus
  assignee: string
  workaround: string
  isEscalated: boolean
  escalationReason: string
  createdAtUtc: string
  resolvedAtUtc?: string | null
  attachmentUrl?: string | null
  attachmentOriginalName?: string | null
  attachmentContentType?: string | null
  attachmentSize?: number | null
  slaOverdue: boolean
  isPerformanceTest: boolean
}

export type TicketNote = {
  id: number
  ticketId: number
  author: string
  body: string
  createdAtUtc: string
  isInternal?: boolean
}

export type TicketNotification = {
  id: string
  ticketId: number
  title: string
  message: string
  createdAtUtc: string
  isRead: boolean
}

export type CreateTicketRequest = {
  terminal: string
  area: string
  systemType: string
  description: string
  passengerImpact: boolean
  flightOpsImpact: boolean
  reporterName: string
  reporterEmail: string
  staffId: string
  priority: TicketPriority
}

export type StaffProfile = {
  id: number
  badgeId: string
  name: string
  email: string
}

export type FlightRecord = {
  id: number
  flightNumber: string
  destination: string
  gate: string
  status: string
  updatedAtUtc: string
}

export type DashboardMetrics = {
  openBacklog: number
  averageResolutionMinutes: number
  overdueCount: number
  recurringIssueCategory: string
  recurringIssueCount: number
  totalTickets: number
}

export type DashboardFilters = {
  status: string
  priority: string
  assignee: string
}

export type Notice = {
  type: 'success' | 'error' | 'info'
  text: string
}

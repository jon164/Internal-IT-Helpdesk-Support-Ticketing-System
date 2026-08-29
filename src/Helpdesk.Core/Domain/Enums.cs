namespace Helpdesk.Core.Domain;

/// <summary>
/// Priority bands used for triage.
/// </summary>
/// <remarks>
/// Deliberately carries no target times. Response and resolution targets are supplied at runtime by
/// <see cref="Sla.SlaPolicySet"/>, which is loaded from configuration, so the service manager can retune
/// targets without a code change or redeployment (NFR-M1, maintainability).
/// </remarks>
public enum TicketPriority
{
    Critical = 1,
    High = 2,
    Standard = 3,
    Low = 4
}

/// <summary>
/// States a ticket may occupy. Legal movement between them is defined solely by
/// <see cref="Workflow.TicketWorkflow"/>.
/// </summary>
public enum TicketStatus
{
    New = 0,
    Triaged = 1,
    Assigned = 2,
    InProgress = 3,
    OnHold = 4,
    Resolved = 5,
    Closed = 6,
    Cancelled = 7
}

/// <summary>
/// The three user roles in scope for the prototype.
/// </summary>
public enum UserRole
{
    Requester = 0,
    Technician = 1,
    TeamLead = 2
}

/// <summary>
/// Content sensitivity. <see cref="Restricted"/> marks tickets whose body may contain personal or
/// commercially sensitive information (typically HR and Finance) and is subject to tighter access
/// control in <see cref="Security.TicketAccessPolicy"/>.
/// </summary>
public enum TicketSensitivity
{
    Standard = 0,
    Restricted = 1
}

/// <summary>
/// Categories of audited event. Every state-changing operation writes one of these.
/// </summary>
public enum TicketEventType
{
    Created = 0,
    StatusChanged = 1,
    Assigned = 2,
    Unassigned = 3,
    PriorityChanged = 4,
    FirstResponseRecorded = 5,
    HoldStarted = 6,
    HoldEnded = 7,
    CommentAdded = 8,
    Reopened = 9
}

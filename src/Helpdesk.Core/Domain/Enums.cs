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
/// <remarks>
/// Account administration belongs to <see cref="TeamLead"/> rather than to a separate role. A
/// four-person support desk does not employ a dedicated systems administrator; the team lead manages
/// the accounts. Splitting the two would model an organisation this one is not.
/// </remarks>
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

/// <summary>
/// Categories of audited account-administration event.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="TicketEventType"/> because these events belong to an account, not a
/// ticket. One of the quality failures this project addresses is that two former staff retained
/// mailbox access unnoticed; an unbroken record of who changed whose access is the direct answer.
/// </remarks>
public enum UserEventType
{
    RoleChanged = 0,
    Deactivated = 1,
    Reactivated = 2
}

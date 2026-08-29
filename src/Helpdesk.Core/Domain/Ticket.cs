namespace Helpdesk.Core.Domain;

/// <summary>
/// A single internal IT support request.
/// </summary>
/// <remarks>
/// This is a plain domain object with no persistence attributes; mapping lives in Helpdesk.Data.
/// All instants are <see cref="DateTimeOffset"/> so that elapsed-time calculations are unambiguous
/// across daylight-saving transitions.
/// </remarks>
public class Ticket
{
    public int Id { get; set; }

    /// <summary>Human-facing identifier, e.g. TKT-000417.</summary>
    public string Reference { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    // --- Requester -------------------------------------------------------

    public string RequesterId { get; set; } = string.Empty;

    public string RequesterName { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public TicketSensitivity Sensitivity { get; set; } = TicketSensitivity.Standard;

    // --- Triage ----------------------------------------------------------

    public TicketPriority Priority { get; set; } = TicketPriority.Standard;

    public TicketStatus Status { get; set; } = TicketStatus.New;

    public string? AssignedTechnicianId { get; set; }

    public string? AssignedTechnicianName { get; set; }

    // --- Lifecycle instants ---------------------------------------------

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When support first made meaningful contact. Set on the first transition into
    /// <see cref="TicketStatus.InProgress"/>, or on the first comment by support staff,
    /// whichever happens first. This is the endpoint of the response SLA clock.
    /// </summary>
    public DateTimeOffset? FirstRespondedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public string? ResolutionNotes { get; set; }

    // --- Clock suspension ------------------------------------------------

    /// <summary>
    /// Periods during which the ticket was parked awaiting the requester or a third party.
    /// Time inside these windows is excluded from the resolution SLA clock but not the response clock.
    /// </summary>
    public List<HoldPeriod> HoldPeriods { get; set; } = new();

    // --- Audit -----------------------------------------------------------

    public List<TicketEvent> Events { get; set; } = new();

    // --- Derived ---------------------------------------------------------

    /// <summary>A ticket is open until it reaches a terminal status.</summary>
    public bool IsOpen => Status is not (TicketStatus.Closed or TicketStatus.Cancelled);

    /// <summary>
    /// Whether the ticket still needs work from the support team.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="IsOpen"/>: a resolved ticket is still open in lifecycle terms — the
    /// requester has yet to confirm the fix — but it is no longer outstanding work, and counting it as
    /// backlog would overstate the team's queue in management reporting.
    /// </remarks>
    public bool RequiresWork =>
        Status is not (TicketStatus.Resolved or TicketStatus.Closed or TicketStatus.Cancelled);

    /// <summary>True while the resolution clock is suspended.</summary>
    public bool IsOnHold => Status == TicketStatus.OnHold;

    public static string BuildReference(int sequence) => $"TKT-{sequence:D6}";
}

/// <summary>
/// A single window during which the resolution clock was suspended.
/// <see cref="EndedAt"/> is null while the ticket is still on hold.
/// </summary>
public class HoldPeriod
{
    public int Id { get; set; }

    public int TicketId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public string? Reason { get; set; }

    public bool IsOpen => EndedAt is null;
}

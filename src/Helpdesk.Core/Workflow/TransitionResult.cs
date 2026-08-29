namespace Helpdesk.Core.Workflow;

/// <summary>
/// Why a requested transition was refused. Codes exist so the API can map refusals to sensible HTTP
/// responses, and so tests can assert on the reason rather than on message wording.
/// </summary>
public enum TransitionFailure
{
    None = 0,

    /// <summary>The target status cannot be reached from the current one.</summary>
    IllegalTransition = 1,

    /// <summary>The ticket is closed or cancelled and accepts no further transitions.</summary>
    TerminalStatus = 2,

    /// <summary>This role may never perform this transition.</summary>
    RoleNotPermitted = 3,

    /// <summary>A requester may only act on tickets they raised.</summary>
    NotTicketOwner = 4,

    /// <summary>Only the assigned technician (or the team lead) may perform this transition.</summary>
    NotAssignedTechnician = 5,

    /// <summary>The ticket must be assigned to someone before it can move on.</summary>
    TechnicianRequired = 6,

    /// <summary>Resolution requires a note describing what was done.</summary>
    ResolutionNotesRequired = 7
}

/// <summary>
/// The outcome of a workflow check: allowed, or refused with a machine-readable code and a message
/// fit to show the user.
/// </summary>
public sealed record TransitionResult(bool IsAllowed, TransitionFailure Failure, string Reason)
{
    public static TransitionResult Allowed { get; } =
        new(true, TransitionFailure.None, string.Empty);

    public static TransitionResult Denied(TransitionFailure failure, string reason) =>
        new(false, failure, reason);
}

namespace Helpdesk.Core.Sla;

/// <summary>
/// Where a ticket stands against one of its two targets.
/// </summary>
public enum SlaState
{
    /// <summary>Target does not apply — the ticket was cancelled.</summary>
    NotApplicable = 0,

    /// <summary>Still open, comfortably inside the target.</summary>
    OnTrack = 1,

    /// <summary>Still open, past the warning threshold but not yet breached.</summary>
    AtRisk = 2,

    /// <summary>Still open and the target has already elapsed.</summary>
    Breached = 3,

    /// <summary>The event happened within its target.</summary>
    Met = 4,

    /// <summary>The event happened, but late.</summary>
    Missed = 5
}

/// <summary>
/// The evaluated position of a ticket against one target.
/// </summary>
/// <param name="State">Summary verdict.</param>
/// <param name="TargetMinutes">The allowance, in the units of the applicable clock.</param>
/// <param name="ConsumedMinutes">How much of the allowance has been used.</param>
/// <param name="RemainingMinutes">Allowance left; negative once breached.</param>
/// <param name="DueAt">
/// The wall-clock instant the target expires, with any suspension already added on. For a closed
/// target this is the deadline that applied at the time.
/// </param>
/// <param name="PercentConsumed">Consumed as a percentage of target, for progress display.</param>
public sealed record SlaStatus(
    SlaState State,
    double TargetMinutes,
    double ConsumedMinutes,
    double RemainingMinutes,
    DateTimeOffset DueAt,
    double PercentConsumed)
{
    /// <summary>True when the target was missed or has already elapsed.</summary>
    public bool IsBreach => State is SlaState.Breached or SlaState.Missed;

    /// <summary>True when the target is still running.</summary>
    public bool IsOpen => State is SlaState.OnTrack or SlaState.AtRisk or SlaState.Breached;

    public static SlaStatus NotApplicable(DateTimeOffset at) =>
        new(SlaState.NotApplicable, 0, 0, 0, at, 0);
}

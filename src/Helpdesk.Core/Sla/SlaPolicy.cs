namespace Helpdesk.Core.Sla;

using Helpdesk.Core.Domain;

/// <summary>
/// Which clock a priority band's targets are measured against.
/// </summary>
public enum SlaClockType
{
    /// <summary>Wall-clock time, running continuously including nights and weekends.</summary>
    Continuous = 0,

    /// <summary>Working time only, as defined by the business calendar.</summary>
    BusinessHours = 1
}

/// <summary>
/// The response and resolution commitment for one priority band.
/// </summary>
/// <param name="Priority">The band this policy governs.</param>
/// <param name="DisplayName">Label shown to users, e.g. "P1 Critical".</param>
/// <param name="Description">Guidance shown at triage so priority is assigned consistently.</param>
/// <param name="ResponseTargetMinutes">Minutes allowed before first meaningful contact.</param>
/// <param name="ResolutionTargetMinutes">Minutes allowed before resolution.</param>
/// <param name="ClockType">Whether the targets run continuously or in working time only.</param>
/// <param name="WarningThreshold">
/// Fraction of a target at which the ticket is flagged at risk. 0.75 means a warning appears once
/// three quarters of the allowance is gone.
/// </param>
public sealed record SlaPolicy(
    TicketPriority Priority,
    string DisplayName,
    string Description,
    double ResponseTargetMinutes,
    double ResolutionTargetMinutes,
    SlaClockType ClockType,
    double WarningThreshold = 0.75)
{
    /// <summary>
    /// Validates a single policy, returning every problem found rather than only the first.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            problems.Add($"{Priority}: display name is required.");
        }

        if (ResponseTargetMinutes <= 0)
        {
            problems.Add($"{Priority}: response target must be greater than zero.");
        }

        if (ResolutionTargetMinutes <= 0)
        {
            problems.Add($"{Priority}: resolution target must be greater than zero.");
        }

        if (ResponseTargetMinutes > 0 && ResolutionTargetMinutes > 0 &&
            ResponseTargetMinutes >= ResolutionTargetMinutes)
        {
            problems.Add(
                $"{Priority}: response target ({ResponseTargetMinutes} min) must be shorter than " +
                $"the resolution target ({ResolutionTargetMinutes} min).");
        }

        if (WarningThreshold is <= 0 or >= 1)
        {
            problems.Add($"{Priority}: warning threshold must be between 0 and 1 exclusive.");
        }

        return problems;
    }
}

namespace Helpdesk.Core.Sla;

using Helpdesk.Core.Domain;
using Helpdesk.Core.Time;

/// <summary>
/// Evaluates tickets against their response and resolution targets.
/// </summary>
/// <remarks>
/// <para>
/// This is the single source of truth for elapsed-time judgements. The queue view, the breach warnings
/// and the management report all read from it, so a change to the rules cannot leave one surface
/// disagreeing with another — which is precisely the failure the current spreadsheet process suffers.
/// </para>
/// <para>
/// Two rules are worth stating explicitly because they drive most of the test cases:
/// the response clock never pauses, and the resolution clock excludes time spent on hold awaiting the
/// requester. Support cannot be held to a target while waiting on someone else, but neither can it
/// stop the response clock simply by parking a ticket.
/// </para>
/// </remarks>
public sealed class SlaCalculator
{
    private readonly SlaPolicySet _policies;
    private readonly IBusinessCalendar _calendar;

    public SlaCalculator(SlaPolicySet policies, IBusinessCalendar calendar)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
    }

    /// <summary>Convenience constructor using the agreed defaults.</summary>
    public static SlaCalculator Default { get; } =
        new(SlaPolicySet.Default, BusinessCalendar.Default);

    public SlaPolicy PolicyFor(Ticket ticket) => _policies.For(ticket.Priority);

    /// <summary>
    /// Evaluates the response target: time from creation to first meaningful contact.
    /// The clock runs continuously through any hold.
    /// </summary>
    public SlaStatus EvaluateResponse(Ticket ticket, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (ticket.Status == TicketStatus.Cancelled)
        {
            return SlaStatus.NotApplicable(now);
        }

        var policy = PolicyFor(ticket);
        var endpoint = ticket.FirstRespondedAt ?? now;
        var consumed = Elapsed(policy, ticket.CreatedAt, endpoint);
        var dueAt = Add(policy, ticket.CreatedAt, policy.ResponseTargetMinutes);

        return Build(policy, policy.ResponseTargetMinutes, consumed, dueAt,
            isSatisfied: ticket.FirstRespondedAt.HasValue);
    }

    /// <summary>
    /// Evaluates the resolution target: time from creation to resolution, less any time the ticket
    /// spent on hold.
    /// </summary>
    public SlaStatus EvaluateResolution(Ticket ticket, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (ticket.Status == TicketStatus.Cancelled)
        {
            return SlaStatus.NotApplicable(now);
        }

        var policy = PolicyFor(ticket);
        var endpoint = ticket.ResolvedAt ?? now;
        var gross = Elapsed(policy, ticket.CreatedAt, endpoint);
        var suspended = SuspendedMinutes(policy, ticket, endpoint);
        var consumed = Math.Max(0d, gross - suspended);

        // The deadline slides out by however much time the clock was suspended.
        var dueAt = Add(policy, ticket.CreatedAt, policy.ResolutionTargetMinutes + suspended);

        return Build(policy, policy.ResolutionTargetMinutes, consumed, dueAt,
            isSatisfied: ticket.ResolvedAt.HasValue);
    }

    /// <summary>
    /// Working (or wall-clock) minutes the ticket spent on hold up to <paramref name="upTo"/>,
    /// measured in the clock that applies to this ticket's priority.
    /// </summary>
    public double SuspendedMinutes(SlaPolicy policy, Ticket ticket, DateTimeOffset upTo)
    {
        var total = 0d;

        foreach (var hold in ticket.HoldPeriods)
        {
            var start = hold.StartedAt > ticket.CreatedAt ? hold.StartedAt : ticket.CreatedAt;
            var end = hold.EndedAt ?? upTo;

            if (end > upTo)
            {
                end = upTo;
            }

            if (end > start)
            {
                total += Elapsed(policy, start, end);
            }
        }

        return total;
    }

    private static SlaStatus Build(
        SlaPolicy policy,
        double target,
        double consumed,
        DateTimeOffset dueAt,
        bool isSatisfied)
    {
        var remaining = target - consumed;
        var percent = target <= 0 ? 100d : consumed / target * 100d;

        SlaState state;

        if (isSatisfied)
        {
            state = consumed <= target ? SlaState.Met : SlaState.Missed;
        }
        else if (consumed >= target)
        {
            state = SlaState.Breached;
        }
        else if (consumed >= target * policy.WarningThreshold)
        {
            state = SlaState.AtRisk;
        }
        else
        {
            state = SlaState.OnTrack;
        }

        return new SlaStatus(state, target, consumed, remaining, dueAt, percent);
    }

    /// <summary>
    /// Minutes elapsed between two instants, measured in whichever clock the policy uses.
    /// </summary>
    public double ElapsedInClock(SlaPolicy policy, DateTimeOffset from, DateTimeOffset to) =>
        policy.ClockType == SlaClockType.Continuous
            ? Math.Max(0d, (to - from).TotalMinutes)
            : _calendar.ElapsedBusinessMinutes(from, to);

    /// <summary>
    /// The instant reached by consuming a number of minutes in whichever clock the policy uses.
    /// </summary>
    public DateTimeOffset AddInClock(SlaPolicy policy, DateTimeOffset from, double minutes) =>
        policy.ClockType == SlaClockType.Continuous
            ? from.AddMinutes(minutes)
            : _calendar.AddBusinessMinutes(from, minutes);

    private double Elapsed(SlaPolicy policy, DateTimeOffset from, DateTimeOffset to) =>
        ElapsedInClock(policy, from, to);

    private DateTimeOffset Add(SlaPolicy policy, DateTimeOffset from, double minutes) =>
        AddInClock(policy, from, minutes);
}

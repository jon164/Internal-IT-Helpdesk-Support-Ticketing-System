namespace Helpdesk.Core.Metrics;

/// <summary>
/// The unresolved count at the end of one period, with the flow that produced it.
/// </summary>
/// <param name="PeriodStart">Start of the period, inclusive.</param>
/// <param name="PeriodEnd">End of the period, exclusive.</param>
/// <param name="Raised">Tickets raised during the period.</param>
/// <param name="Resolved">Tickets resolved during the period.</param>
/// <param name="UnresolvedAtEnd">Tickets still requiring work at the moment the period closed.</param>
public sealed record BacklogTrendPoint(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int Raised,
    int Resolved,
    int UnresolvedAtEnd)
{
    /// <summary>
    /// Raised minus resolved. Positive means the queue grew that period.
    /// </summary>
    public int NetChange => Raised - Resolved;
}

/// <summary>How long unresolved work has been waiting.</summary>
public sealed record BacklogAgeBucket(string Label, int Count);

/// <summary>
/// Whether the team is keeping up.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately separate from <see cref="MetricsSummary"/>. That answers "how did we perform against
/// our targets"; this answers "is the queue growing". They are different questions with different
/// shapes — one is a point-in-time judgement over a window, the other is a series — and merging them
/// would produce a type where half the fields are meaningless to any given caller.
/// </para>
/// <para>
/// "Unresolved" here means the same thing it means everywhere else in the system: a ticket still
/// requiring work from a technician. Tickets sitting in Resolved awaiting the requester's
/// confirmation are not counted, because they are not the team's outstanding work.
/// </para>
/// </remarks>
/// <param name="From">Start of the reporting window.</param>
/// <param name="To">End of the reporting window.</param>
/// <param name="Trend">One point per period, oldest first.</param>
/// <param name="UnresolvedAtStart">Unresolved count immediately before the window opened.</param>
/// <param name="UnresolvedNow">Unresolved count at the time of the report.</param>
/// <param name="UnresolvedBreached">How many of those have already breached their resolution target.</param>
/// <param name="AgeBuckets">Unresolved work grouped by how long it has been waiting.</param>
/// <param name="OldestUnresolvedDays">Age in days of the longest-waiting unresolved ticket.</param>
/// <param name="OldestUnresolvedReference">Its reference, so the manager can go straight to it.</param>
/// <param name="UnresolvedByAssignee">Unresolved count per technician, including "Unassigned".</param>
public sealed record BacklogReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<BacklogTrendPoint> Trend,
    int UnresolvedAtStart,
    int UnresolvedNow,
    int UnresolvedBreached,
    IReadOnlyList<BacklogAgeBucket> AgeBuckets,
    double? OldestUnresolvedDays,
    string? OldestUnresolvedReference,
    IReadOnlyDictionary<string, int> UnresolvedByAssignee)
{
    /// <summary>
    /// Change in unresolved count across the window. Positive means the queue grew.
    /// </summary>
    public int NetChange => UnresolvedNow - UnresolvedAtStart;

    /// <summary>
    /// Total resolved divided by total raised across the window, as a percentage.
    /// </summary>
    /// <remarks>
    /// Below 100 means work is arriving faster than it is being cleared. Null when nothing was
    /// raised — a quiet window is not the same as a failure to clear anything.
    /// </remarks>
    public double? ClearanceRatePercent
    {
        get
        {
            var raised = Trend.Sum(p => p.Raised);
            var resolved = Trend.Sum(p => p.Resolved);

            return raised == 0 ? null : (double)resolved / raised * 100d;
        }
    }

    /// <summary>
    /// A plain verdict on the direction of travel, so the interface does not have to invent one and
    /// the rule is testable in isolation.
    /// </summary>
    public BacklogDirection Direction
    {
        get
        {
            // A queue this small cannot meaningfully be "growing" — one ticket either way is noise,
            // and reporting a crisis over it would train the manager to ignore the indicator.
            if (UnresolvedNow <= 5 && Math.Abs(NetChange) <= 2)
            {
                return BacklogDirection.Steady;
            }

            var proportional = UnresolvedAtStart == 0
                ? NetChange
                : (double)NetChange / UnresolvedAtStart * 100d;

            return proportional switch
            {
                >= 20 => BacklogDirection.Growing,
                <= -20 => BacklogDirection.Shrinking,
                _ => BacklogDirection.Steady
            };
        }
    }
}

/// <summary>Direction of travel for the unresolved queue.</summary>
public enum BacklogDirection
{
    /// <summary>The queue is materially larger than when the window opened.</summary>
    Growing = 0,

    /// <summary>Broadly unchanged.</summary>
    Steady = 1,

    /// <summary>The queue is materially smaller than when the window opened.</summary>
    Shrinking = 2
}

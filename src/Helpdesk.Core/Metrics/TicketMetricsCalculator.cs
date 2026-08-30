namespace Helpdesk.Core.Metrics;

using Helpdesk.Core.Domain;
using Helpdesk.Core.Sla;

/// <summary>
/// Produces the reporting figures the team lead currently cannot produce at all.
/// </summary>
/// <remarks>
/// Every judgement about lateness is delegated to <see cref="SlaCalculator"/> rather than recomputed
/// here, so the dashboard and the queue can never disagree about whether a given ticket breached.
/// </remarks>
public sealed class TicketMetricsCalculator
{
    private readonly SlaCalculator _sla;

    public TicketMetricsCalculator(SlaCalculator sla) =>
        _sla = sla ?? throw new ArgumentNullException(nameof(sla));

    /// <summary>
    /// Summarises a ticket population over a reporting window.
    /// </summary>
    /// <param name="tickets">
    /// The tickets to report on. Callers are expected to have applied access filtering already.
    /// </param>
    /// <param name="from">Start of the reporting window, inclusive.</param>
    /// <param name="to">End of the reporting window, exclusive.</param>
    /// <param name="now">Current time, used to evaluate still-open tickets.</param>
    public MetricsSummary Calculate(
        IEnumerable<Ticket> tickets,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tickets);

        if (to < from)
        {
            throw new ArgumentException(
                "The end of the reporting window cannot precede its start.", nameof(to));
        }

        var all = tickets.ToList();

        // Cancelled tickets are excluded throughout: they represent withdrawn demand, and counting
        // them would understate attainment against work the team was actually asked to do.
        var live = all.Where(t => t.Status != TicketStatus.Cancelled).ToList();

        var createdInWindow = live
            .Where(t => t.CreatedAt >= from && t.CreatedAt < to)
            .ToList();

        var resolvedInWindow = live
            .Where(t => t.ResolvedAt is { } resolved && resolved >= from && resolved < to)
            .ToList();

        // Backlog is work still requiring a technician. Tickets sitting in Resolved are awaiting the
        // requester's confirmation, not the team's attention, so they are reported separately.
        var open = live.Where(t => t.RequiresWork).ToList();
        var awaitingClosure = live.Count(t => t.Status == TicketStatus.Resolved);

        var responded = createdInWindow.Where(t => t.FirstRespondedAt.HasValue).ToList();

        var responseAttainment = Percentage(
            responded.Count(t => !_sla.EvaluateResponse(t, now).IsBreach),
            responded.Count);

        var resolutionAttainment = Percentage(
            resolvedInWindow.Count(t => !_sla.EvaluateResolution(t, now).IsBreach),
            resolvedInWindow.Count);

        var resolutionDurations = resolvedInWindow
            .Select(t => _sla.EvaluateResolution(t, now).ConsumedMinutes)
            .ToList();

        var openStatuses = open
            .Select(t => _sla.EvaluateResolution(t, now).State)
            .ToList();

        return new MetricsSummary(
            From: from,
            To: to,
            TotalCreated: createdInWindow.Count,
            TotalResolved: resolvedInWindow.Count,
            OpenBacklog: open.Count,
            AwaitingClosure: awaitingClosure,
            CreatedByPriority: CountBy(createdInWindow, t => t.Priority),
            OpenByStatus: CountBy(open, t => t.Status),
            CreatedByCategory: CountBy(
                createdInWindow,
                t => string.IsNullOrWhiteSpace(t.Category) ? "Uncategorised" : t.Category),
            ResponseAttainmentPercent: responseAttainment,
            ResolutionAttainmentPercent: resolutionAttainment,
            MedianResolutionMinutes: Median(resolutionDurations),
            MeanResolutionMinutes: resolutionDurations.Count == 0 ? null : resolutionDurations.Average(),
            OpenBreached: openStatuses.Count(s => s == SlaState.Breached),
            OpenAtRisk: openStatuses.Count(s => s == SlaState.AtRisk));
    }

    /// <summary>
    /// Reconstructs how the unresolved queue has moved, so "are we falling behind" can be answered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single backlog figure cannot answer that question — 28 unresolved tickets is healthy if it
    /// was 40 last month and alarming if it was 12. This walks the ticket history and reports the
    /// unresolved count at the close of each period alongside the flow that produced it.
    /// </para>
    /// <para>
    /// <b>Known limitation.</b> History is reconstructed from each ticket's current timestamps rather
    /// than from the audit trail. A ticket that was resolved and later reopened has its resolution
    /// instant cleared, so it appears never to have been resolved, and past periods will count it as
    /// unresolved when at the time it was not. Reopening is rare enough that this is acceptable for a
    /// prototype; a production implementation would replay the status events instead.
    /// </para>
    /// </remarks>
    /// <param name="tickets">Population to report on. Access filtering is the caller's responsibility.</param>
    /// <param name="from">Start of the window, inclusive.</param>
    /// <param name="to">End of the window, exclusive.</param>
    /// <param name="periodCount">How many equal periods to divide the window into.</param>
    /// <param name="now">Current time, used for ages and open-ticket SLA evaluation.</param>
    public BacklogReport CalculateBacklog(
        IEnumerable<Ticket> tickets,
        DateTimeOffset from,
        DateTimeOffset to,
        int periodCount,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tickets);

        if (to <= from)
        {
            throw new ArgumentException(
                "The end of the reporting window must be after its start.", nameof(to));
        }

        if (periodCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(periodCount), periodCount, "At least one period is required.");
        }

        // Withdrawn requests are excluded throughout, exactly as they are in the performance report.
        // Including them would make the flow figures fail to reconcile with the queue size.
        var live = tickets.Where(t => t.Status != TicketStatus.Cancelled).ToList();

        var periodLength = (to - from) / periodCount;
        var trend = new List<BacklogTrendPoint>(periodCount);

        for (var index = 0; index < periodCount; index++)
        {
            var periodStart = from + periodLength * index;

            // The final period ends exactly on `to`, so accumulated fractions cannot leave a gap.
            var periodEnd = index == periodCount - 1 ? to : periodStart + periodLength;

            trend.Add(new BacklogTrendPoint(
                periodStart,
                periodEnd,
                Raised: live.Count(t => t.CreatedAt >= periodStart && t.CreatedAt < periodEnd),
                Resolved: live.Count(t =>
                    t.ResolvedAt is { } resolved && resolved >= periodStart && resolved < periodEnd),
                UnresolvedAtEnd: live.Count(t => WasUnresolvedAt(t, periodEnd))));
        }

        var unresolved = live.Where(t => WasUnresolvedAt(t, now)).ToList();

        var oldest = unresolved
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefault();

        return new BacklogReport(
            From: from,
            To: to,
            Trend: trend,
            UnresolvedAtStart: live.Count(t => WasUnresolvedAt(t, from)),
            UnresolvedNow: unresolved.Count,
            UnresolvedBreached: unresolved.Count(t =>
                _sla.EvaluateResolution(t, now).State == SlaState.Breached),
            AgeBuckets: BuildAgeBuckets(unresolved, now),
            OldestUnresolvedDays: oldest is null ? null : (now - oldest.CreatedAt).TotalDays,
            OldestUnresolvedReference: oldest?.Reference,
            UnresolvedByAssignee: unresolved
                .GroupBy(t => string.IsNullOrWhiteSpace(t.AssignedTechnicianName)
                    ? "Unassigned"
                    : t.AssignedTechnicianName!)
                .ToDictionary(g => g.Key, g => g.Count()));
    }

    /// <summary>
    /// Whether a ticket still required work at a given instant.
    /// </summary>
    /// <remarks>
    /// Matches the definition used for <see cref="MetricsSummary.OpenBacklog"/> when evaluated at the
    /// present moment, so the two reports can never disagree about the size of the queue.
    /// </remarks>
    private static bool WasUnresolvedAt(Ticket ticket, DateTimeOffset at) =>
        ticket.CreatedAt < at && (ticket.ResolvedAt is null || ticket.ResolvedAt >= at);

    /// <summary>
    /// Groups waiting work by age. Boundaries are chosen to match how the desk actually talks about
    /// its queue rather than as even intervals: anything past a fortnight is a single bucket, because
    /// at that point the precise age has stopped mattering.
    /// </summary>
    private static IReadOnlyList<BacklogAgeBucket> BuildAgeBuckets(
        IReadOnlyList<Ticket> unresolved,
        DateTimeOffset now)
    {
        var boundaries = new (string Label, double MaxDays)[]
        {
            ("Under 1 day", 1),
            ("1 to 3 days", 3),
            ("3 to 7 days", 7),
            ("1 to 2 weeks", 14),
            ("Over 2 weeks", double.PositiveInfinity)
        };

        return boundaries
            .Select((bucket, index) =>
            {
                var lower = index == 0 ? double.NegativeInfinity : boundaries[index - 1].MaxDays;

                return new BacklogAgeBucket(
                    bucket.Label,
                    unresolved.Count(t =>
                    {
                        var ageDays = (now - t.CreatedAt).TotalDays;
                        return ageDays >= lower && ageDays < bucket.MaxDays;
                    }));
            })
            .ToList();
    }

    /// <summary>Null when the denominator is zero, so "no data" is never reported as 0%.</summary>
    private static double? Percentage(int numerator, int denominator) =>
        denominator == 0 ? null : (double)numerator / denominator * 100d;

    /// <summary>
    /// True median: the mean of the two central values for an even-sized population. Reported
    /// alongside the mean because a handful of long-running tickets skews the mean badly.
    /// </summary>
    public static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var ordered = values.OrderBy(v => v).ToList();
        var middle = ordered.Count / 2;

        return ordered.Count % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2d;
    }

    private static IReadOnlyDictionary<TKey, int> CountBy<TKey>(
        IEnumerable<Ticket> tickets,
        Func<Ticket, TKey> selector)
        where TKey : notnull =>
        tickets
            .GroupBy(selector)
            .ToDictionary(g => g.Key, g => g.Count());
}

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

namespace Helpdesk.Core.Reporting;

using Helpdesk.Core.Domain;

/// <summary>
/// USER STORY: As an IT manager, I want to export the dashboard summary, so that I can include
/// service data in reports to senior management.
///   AC1 Given the dashboard is displayed, when I choose export, then a file (e.g. CSV) of the
///       current metrics is produced.
///   AC2 Given no ticket data exists, when I export, then a valid file with headers and zero rows
///       is produced.
///
/// The one place the dashboard figures are worked out. The summary cards (GET /api/dashboard),
/// the metric summary CSV and the by-priority CSV all use it, so a report cannot show a different
/// number from the screen it was exported from.
///
/// Definitions (unchanged from the dashboard cards):
///   - Open backlog: tickets that are neither Resolved nor Closed.
///   - Overdue: <see cref="SlaPolicy.IsOverdue"/>.
///   - Resolved: tickets that have a resolution time (including ones closed since).
///   - Average resolution: mean of (resolved - created) over resolved tickets, in minutes,
///     rounded to 2 decimal places. Null when nothing has been resolved, so a report shows a
///     blank instead of a made-up "0 minutes".
///   - Most common category: the ticket type with the most tickets; ties go to the lower enum
///     value. Null when there are no tickets. (The card used to say "Incident" even with no
///     tickets at all.)
/// </summary>
public sealed record DashboardSummary(
    int TotalTickets,
    int OpenBacklog,
    int OverdueCount,
    int ResolvedCount,
    double? AverageResolutionMinutes,
    TicketType? MostCommonCategory,
    int MostCommonCategoryCount)
{
    public static DashboardSummary From(IReadOnlyCollection<Ticket> tickets, DateTime nowUtc)
    {
        var top = tickets
            .GroupBy(t => t.Type)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .FirstOrDefault();

        return new DashboardSummary(
            TotalTickets: tickets.Count,
            OpenBacklog: tickets.Count(t => SlaPolicy.IsOpen(t.Status)),
            OverdueCount: tickets.Count(t => SlaPolicy.IsOverdue(t, nowUtc)),
            ResolvedCount: tickets.Count(t => t.ResolvedAt is not null),
            AverageResolutionMinutes: AverageResolution(tickets),
            MostCommonCategory: top?.Key,
            MostCommonCategoryCount: top?.Count() ?? 0);
    }

    /// <summary>
    /// One row per priority that has at least one ticket, most urgent first. No tickets gives
    /// no rows (AC2): a priority with nothing in it is not reported as a row of zeros.
    /// </summary>
    public static IReadOnlyList<PriorityBreakdownRow> ByPriority(IReadOnlyCollection<Ticket> tickets, DateTime nowUtc) =>
        tickets
            .GroupBy(t => t.Priority)
            .OrderByDescending(g => g.Key)
            .Select(g => new PriorityBreakdownRow(
                Priority: g.Key,
                TotalTickets: g.Count(),
                OpenTickets: g.Count(t => SlaPolicy.IsOpen(t.Status)),
                OverdueTickets: g.Count(t => SlaPolicy.IsOverdue(t, nowUtc)),
                ResolvedTickets: g.Count(t => t.ResolvedAt is not null),
                AverageResolutionMinutes: AverageResolution(g.ToList())))
            .ToList();

    private static double? AverageResolution(IReadOnlyCollection<Ticket> tickets)
    {
        var minutes = tickets
            .Where(t => t.ResolvedAt is not null)
            .Select(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalMinutes)
            .ToList();

        return minutes.Count == 0 ? null : Math.Round(minutes.Average(), 2);
    }
}

public sealed record PriorityBreakdownRow(
    TicketPriority Priority,
    int TotalTickets,
    int OpenTickets,
    int OverdueTickets,
    int ResolvedTickets,
    double? AverageResolutionMinutes);

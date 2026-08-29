namespace Helpdesk.Core.Metrics;

using Helpdesk.Core.Domain;

/// <summary>
/// The management reporting figures for a period.
/// </summary>
/// <remarks>
/// Attainment percentages are nullable rather than zero when nothing qualifies. "No tickets were due a
/// response this period" and "every response was late" are different statements, and reporting the
/// second when the first is true is exactly the kind of indefensible number the team lead currently
/// cannot avoid producing.
/// </remarks>
public sealed record MetricsSummary(
    DateTimeOffset From,
    DateTimeOffset To,
    int TotalCreated,
    int TotalResolved,
    int OpenBacklog,
    int AwaitingClosure,
    IReadOnlyDictionary<TicketPriority, int> CreatedByPriority,
    IReadOnlyDictionary<TicketStatus, int> OpenByStatus,
    IReadOnlyDictionary<string, int> CreatedByCategory,
    double? ResponseAttainmentPercent,
    double? ResolutionAttainmentPercent,
    double? MedianResolutionMinutes,
    double? MeanResolutionMinutes,
    int OpenBreached,
    int OpenAtRisk)
{
    /// <summary>Resolved as a proportion of created — over 100% means backlog was cleared.</summary>
    public double? ThroughputRatioPercent =>
        TotalCreated == 0 ? null : (double)TotalResolved / TotalCreated * 100d;
}

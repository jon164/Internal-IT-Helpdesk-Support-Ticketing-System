namespace Helpdesk.Core.Reporting;

using System.Globalization;
using Helpdesk.Core.Domain;

/// <summary>
/// The dashboard export files the IT manager can choose from (see docs/dashboard-export.md).
///   tickets      the ticket list, with the dashboard filters applied (the original export)
///   summary      one row per summary card figure: metric, value, unit
///   by-priority  one row per priority that has tickets
/// The summary and by-priority files cover every ticket, like the summary cards, and ignore the
/// table filters.
/// </summary>
public static class DashboardExport
{
    public static readonly IReadOnlyList<string> SummaryHeader = ["metric", "value", "unit"];

    public static readonly IReadOnlyList<string> ByPriorityHeader =
        ["priority", "totalTickets", "openTickets", "overdueTickets", "resolvedTickets", "averageResolutionMinutes"];

    /// <summary>
    /// The summary file always has the same eight rows in the same order, so a report template
    /// can rely on row positions. With no tickets the counts are 0 and the average and category
    /// are blank: the file still describes the dashboard as it is.
    /// </summary>
    public static string SummaryCsv(DashboardSummary summary, DateTime generatedAtUtc) =>
        Csv.Document(SummaryHeader,
        [
            ["Report generated (UTC)", DateTime.SpecifyKind(generatedAtUtc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), "ISO 8601"],
            ["Total tickets", Csv.Number(summary.TotalTickets), "tickets"],
            ["Open backlog", Csv.Number(summary.OpenBacklog), "tickets"],
            ["Overdue (outside priority SLA)", Csv.Number(summary.OverdueCount), "tickets"],
            ["Resolved tickets", Csv.Number(summary.ResolvedCount), "tickets"],
            ["Average resolution time", Csv.Number(summary.AverageResolutionMinutes), "minutes"],
            ["Most common category", summary.MostCommonCategory?.ToString(), "category"],
            ["Most common category count", Csv.Number(summary.MostCommonCategoryCount), "tickets"],
        ]);

    /// <summary>Header plus one row per priority; no tickets gives the header line only (AC2).</summary>
    public static string ByPriorityCsv(IEnumerable<PriorityBreakdownRow> rows) =>
        Csv.Document(ByPriorityHeader, rows.Select(r => new string?[]
        {
            r.Priority.ToString(),
            Csv.Number(r.TotalTickets),
            Csv.Number(r.OpenTickets),
            Csv.Number(r.OverdueTickets),
            Csv.Number(r.ResolvedTickets),
            Csv.Number(r.AverageResolutionMinutes),
        }));

    /// <summary>
    /// Download name, dated in New Zealand time (where the report is read), e.g.
    /// airport-helpdesk-summary-2026-10-15.csv.
    /// </summary>
    public static string FileName(string kind, DateTime nowUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), SlaPolicy.BusinessTimeZone);
        return string.Create(CultureInfo.InvariantCulture, $"airport-helpdesk-{kind}-{local:yyyy-MM-dd}.csv");
    }
}

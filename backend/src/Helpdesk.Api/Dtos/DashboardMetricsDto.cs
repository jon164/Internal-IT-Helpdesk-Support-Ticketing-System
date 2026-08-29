namespace Helpdesk.Api.Dtos;

public record DashboardMetricsDto(
    int OpenBacklog,
    double AverageResolutionMinutes,
    int OverdueCount,
    string RecurringIssueCategory,
    int RecurringIssueCount,
    int TotalTickets
);

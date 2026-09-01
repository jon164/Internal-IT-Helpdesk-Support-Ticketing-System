namespace Helpdesk.Core.Domain;

public static class TicketStatusRules
{
    private static readonly Dictionary<TicketStatus, TicketStatus[]> ValidTransitions = new()
    {
        [TicketStatus.New] = new[] { TicketStatus.InProgress },
        [TicketStatus.InProgress] = new[] { TicketStatus.PendingEmployeeResponse, TicketStatus.Resolved },
        [TicketStatus.PendingEmployeeResponse] = new[] { TicketStatus.InProgress, TicketStatus.Resolved },
        [TicketStatus.Resolved] = new[] { TicketStatus.Closed },
        [TicketStatus.Closed] = Array.Empty<TicketStatus>()
    };

    public static bool IsValidTransition(TicketStatus from, TicketStatus to) =>
        ValidTransitions.TryGetValue(from, out var valid) && valid.Contains(to);

    public static IReadOnlyList<TicketStatus> GetValidNextStatuses(TicketStatus current) =>
        ValidTransitions.TryGetValue(current, out var valid) ? valid : Array.Empty<TicketStatus>();
}

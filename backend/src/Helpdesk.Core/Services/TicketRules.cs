using Helpdesk.Core.Models;

namespace Helpdesk.Core.Services;

public static class TicketRules
{
    public static TimeSpan GetSlaWindow(TicketPriority priority) =>
        priority switch
        {
            TicketPriority.Critical => TimeSpan.FromMinutes(30),
            TicketPriority.High => TimeSpan.FromHours(2),
            TicketPriority.Medium => TimeSpan.FromHours(8),
            TicketPriority.Low => TimeSpan.FromHours(24),
            _ => TimeSpan.FromHours(8)
        };

    public static bool IsOverdue(Ticket ticket, DateTime utcNow)
    {
        if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            return false;
        }

        return utcNow - ticket.CreatedAtUtc > GetSlaWindow(ticket.Priority);
    }

    public static bool CanTransition(TicketStatus from, TicketStatus to)
    {
        if (from == to)
        {
            return true;
        }

        return from switch
        {
            TicketStatus.New => to == TicketStatus.InProgress,
            TicketStatus.InProgress =>
                to is TicketStatus.WaitingOnUser or TicketStatus.Resolved,
            TicketStatus.WaitingOnUser =>
                to is TicketStatus.InProgress or TicketStatus.Resolved,
            TicketStatus.Resolved =>
                to is TicketStatus.Closed or TicketStatus.InProgress,
            TicketStatus.Closed => false,
            _ => false
        };
    }

    public static string[] GetValidNextStatuses(TicketStatus from) =>
        Enum.GetValues<TicketStatus>()
            .Where(to => to != from && CanTransition(from, to))
            .Select(to => ToDisplayName(to))
            .ToArray();

    public static string ToDisplayName(TicketStatus status) =>
        status switch
        {
            TicketStatus.InProgress => "In Progress",
            TicketStatus.WaitingOnUser => "Waiting on User",
            _ => status.ToString()
        };
}

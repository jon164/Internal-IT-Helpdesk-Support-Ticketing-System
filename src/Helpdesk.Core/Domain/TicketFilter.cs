using System.Diagnostics.CodeAnalysis;

namespace Helpdesk.Core.Domain;

/// <summary>
/// USER STORY: As an IT manager, I want to filter tickets by status, priority, and assignee,
/// so that I can investigate bottlenecks or a technician's workload.
///
/// One place parses the dashboard's filter query values and applies them, so the ticket table
/// and the CSV export can never disagree about which tickets match.
///
/// Rules:
///   - "All", empty or missing means "do not filter on this field".
///   - Status uses the names the UI shows: New, InProgress, WaitingOnUser, Resolved, Closed.
///   - Priority: Low, Medium, High, Critical.
///   - Assignee "Unassigned" means tickets with no technician; any other value is a technician name.
///   - Matching of names is case-insensitive and ignores surrounding spaces.
///   - An unknown status or priority is rejected. It is never guessed (an earlier version turned
///     any unknown status into "New", so a typo silently returned the wrong tickets), and numeric
///     values such as "3" are not accepted as enum shortcuts.
/// </summary>
public sealed record TicketFilter(TicketStatus? Status, TicketPriority? Priority, string? Assignee)
{
    public const string AllValue = "All";
    public const string UnassignedValue = "Unassigned";

    public static TicketFilter None { get; } = new(null, null, null);

    private static readonly IReadOnlyDictionary<string, TicketStatus> StatusNames =
        new Dictionary<string, TicketStatus>(StringComparer.OrdinalIgnoreCase)
        {
            ["New"] = TicketStatus.New,
            ["InProgress"] = TicketStatus.InProgress,
            ["WaitingOnUser"] = TicketStatus.PendingEmployeeResponse,
            ["Resolved"] = TicketStatus.Resolved,
            ["Closed"] = TicketStatus.Closed,
        };

    private static readonly IReadOnlyDictionary<string, TicketPriority> PriorityNames =
        new Dictionary<string, TicketPriority>(StringComparer.OrdinalIgnoreCase)
        {
            ["Low"] = TicketPriority.Low,
            ["Medium"] = TicketPriority.Medium,
            ["High"] = TicketPriority.High,
            ["Critical"] = TicketPriority.Critical,
        };

    /// <summary>True when the filter narrows nothing (every field is "All").</summary>
    public bool IsEmpty => Status is null && Priority is null && Assignee is null;

    /// <summary>
    /// Parses raw query-string values. Returns false and a user-readable message when a status or
    /// priority is not one of the allowed names.
    /// </summary>
    public static bool TryParse(string? status, string? priority, string? assignee, out TicketFilter filter, out string? error)
    {
        filter = None;
        error = null;

        TicketStatus? parsedStatus = null;
        if (!IsAll(status))
        {
            if (!StatusNames.TryGetValue(status.Trim(), out var s))
            {
                error = $"Unknown status '{Echo(status)}'. Use one of: All, {string.Join(", ", StatusNames.Keys)}.";
                return false;
            }

            parsedStatus = s;
        }

        TicketPriority? parsedPriority = null;
        if (!IsAll(priority))
        {
            if (!PriorityNames.TryGetValue(priority.Trim(), out var p))
            {
                error = $"Unknown priority '{Echo(priority)}'. Use one of: All, {string.Join(", ", PriorityNames.Keys)}.";
                return false;
            }

            parsedPriority = p;
        }

        var parsedAssignee = IsAll(assignee) ? null : assignee.Trim();

        filter = new TicketFilter(parsedStatus, parsedPriority, parsedAssignee);
        return true;
    }

    /// <summary>
    /// Adds the filter to a query. Works on an EF Core query (the database does the filtering,
    /// so the server no longer loads every ticket first) and on an in-memory list.
    /// </summary>
    public IQueryable<Ticket> Apply(IQueryable<Ticket> tickets)
    {
        if (Status is { } status)
        {
            tickets = tickets.Where(t => t.Status == status);
        }

        if (Priority is { } priority)
        {
            tickets = tickets.Where(t => t.Priority == priority);
        }

        if (Assignee is { } assignee)
        {
            if (string.Equals(assignee, UnassignedValue, StringComparison.OrdinalIgnoreCase))
            {
                tickets = tickets.Where(t => t.AssignedTechnicianId == null);
            }
            else
            {
                var name = assignee.ToLower();
                tickets = tickets.Where(t => t.AssignedTechnician != null && t.AssignedTechnician.Name.ToLower() == name);
            }
        }

        return tickets;
    }

    /// <summary>Repeats the bad value in the error, cut short so a huge query value is not reflected back whole.</summary>
    private static string Echo(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 40 ? trimmed : trimmed[..40] + "...";
    }

    private static bool IsAll([NotNullWhen(false)] string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), AllValue, StringComparison.OrdinalIgnoreCase);
}

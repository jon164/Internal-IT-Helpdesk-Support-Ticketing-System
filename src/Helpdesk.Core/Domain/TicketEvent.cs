namespace Helpdesk.Core.Domain;

/// <summary>
/// One immutable entry in a ticket's audit trail.
/// </summary>
/// <remarks>
/// The absence of any such trail is one of the quality failures this project sets out to fix, so
/// every state-changing operation is expected to append one of these. Entries are never updated or
/// deleted once written.
/// </remarks>
public class TicketEvent
{
    public int Id { get; set; }

    public int TicketId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public TicketEventType EventType { get; set; }

    public string ActorId { get; set; } = string.Empty;

    public string ActorName { get; set; } = string.Empty;

    public UserRole ActorRole { get; set; }

    public TicketStatus? FromStatus { get; set; }

    public TicketStatus? ToStatus { get; set; }

    /// <summary>Free-text description of what changed, e.g. "Priority Standard -> Critical".</summary>
    public string Detail { get; set; } = string.Empty;

    public static TicketEvent For(
        Ticket ticket,
        ActorContext actor,
        TicketEventType type,
        DateTimeOffset occurredAt,
        string detail = "",
        TicketStatus? from = null,
        TicketStatus? to = null) => new()
        {
            TicketId = ticket.Id,
            OccurredAt = occurredAt,
            EventType = type,
            ActorId = actor.UserId,
            ActorName = actor.DisplayName ?? actor.UserId,
            ActorRole = actor.Role,
            FromStatus = from,
            ToStatus = to,
            Detail = detail
        };
}

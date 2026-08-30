namespace Helpdesk.Core.Domain;

public class TicketAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public TicketStatus? FromStatus { get; set; }
    public TicketStatus? ToStatus { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static TicketAuditEntry Create(Guid ticketId, string changedBy, TicketStatus? from, TicketStatus? to, string action, string? comment)
    {
        return new TicketAuditEntry
        {
            TicketId = ticketId,
            ChangedBy = changedBy,
            FromStatus = from,
            ToStatus = to,
            Action = action,
            Comment = comment,
            Timestamp = DateTime.UtcNow
        };
    }
}

namespace Helpdesk.Core.Domain;

public class TicketLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceTicketId { get; set; }
    public Ticket? SourceTicket { get; set; }
    public Guid TargetTicketId { get; set; }
    public Ticket? TargetTicket { get; set; }
    public TicketLinkType LinkType { get; set; }
    public string CreatedById { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public static TicketLink Create(Guid sourceTicketId, Guid targetTicketId, TicketLinkType linkType, string changedBy, DateTime now)
    {
        return new TicketLink
        {
            SourceTicketId = sourceTicketId,
            TargetTicketId = targetTicketId,
            LinkType = linkType,
            CreatedById = changedBy,
            CreatedAt = now
        };
    }
}

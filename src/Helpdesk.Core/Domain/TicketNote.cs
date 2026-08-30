namespace Helpdesk.Core.Domain;

public class TicketNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public static TicketNote Create(Guid ticketId, string authorId, string authorName, string content, bool isInternal, DateTime now)
    {
        return new TicketNote
        {
            TicketId = ticketId,
            AuthorId = authorId,
            AuthorName = authorName,
            Content = content,
            IsInternal = isInternal,
            CreatedAt = now
        };
    }
}

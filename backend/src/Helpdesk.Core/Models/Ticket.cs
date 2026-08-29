namespace Helpdesk.Core.Models;

public class Ticket
{
    public int Id { get; set; }

    public string Terminal { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string SystemType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public bool PassengerImpact { get; set; }
    public bool FlightOpsImpact { get; set; }

    public string ReporterName { get; set; } = string.Empty;
    public string ReporterEmail { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;

    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public TicketStatus Status { get; set; } = TicketStatus.New;

    public string Assignee { get; set; } = "Unassigned";
    public string Workaround { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }

    public string? AttachmentUrl { get; set; }
    public string? AttachmentOriginalName { get; set; }
    public string? AttachmentContentType { get; set; }
    public long? AttachmentSize { get; set; }

    public bool IsPerformanceTest { get; set; }
}

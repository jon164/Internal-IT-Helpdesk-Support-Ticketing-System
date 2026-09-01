namespace Helpdesk.Core.Domain;

public class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TicketStatus Status { get; set; } = TicketStatus.New;
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public TicketType Type { get; set; } = TicketType.Incident;
    public string SubmitterName { get; set; } = string.Empty;
    public string SubmitterEmail { get; set; } = string.Empty;
    public Guid? AssignedTechnicianId { get; set; }
    public Technician? AssignedTechnician { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? PendingSince { get; set; }
    public string? ClosedReason { get; set; }

    public ICollection<TicketNote> Notes { get; set; } = new List<TicketNote>();
    public ICollection<TicketAuditEntry> AuditEntries { get; set; } = new List<TicketAuditEntry>();
    public ICollection<TicketLink> Links { get; set; } = new List<TicketLink>();

    public void AssignTo(Guid technicianId, string changedBy, DateTime now)
    {
        AssignedTechnicianId = technicianId;
        UpdatedAt = now;
        AuditEntries.Add(TicketAuditEntry.Create(Id, changedBy, Status, Status, "Assigned", $"Assigned to technician {technicianId}"));
    }

    public void Claim(Guid technicianId, string changedBy, DateTime now)
    {
        if (AssignedTechnicianId.HasValue)
            throw new InvalidOperationException($"Ticket is already assigned to {(AssignedTechnician?.Name ?? AssignedTechnicianId.ToString())}");

        AssignedTechnicianId = technicianId;
        UpdatedAt = now;
        AuditEntries.Add(TicketAuditEntry.Create(Id, changedBy, Status, Status, "Claimed", "Ticket claimed from unassigned pool"));
    }

    public void Reassign(Guid technicianId, string changedBy, DateTime now)
    {
        AssignedTechnicianId = technicianId;
        UpdatedAt = now;
        AuditEntries.Add(TicketAuditEntry.Create(Id, changedBy, Status, Status, "Reassigned", $"Reassigned to technician {technicianId}"));
    }

    public void TransitionTo(TicketStatus newStatus, string changedBy, string? comment, DateTime now)
    {
        if (!TicketStatusRules.IsValidTransition(Status, newStatus))
            throw new InvalidOperationException(
                $"Invalid transition from {Status} to {newStatus}. Valid next statuses: {string.Join(", ", TicketStatusRules.GetValidNextStatuses(Status))}");

        var from = Status;
        Status = newStatus;
        UpdatedAt = now;

        switch (newStatus)
        {
            case TicketStatus.PendingEmployeeResponse:
                PendingSince = now;
                break;
            case TicketStatus.InProgress:
                PendingSince = null;
                break;
            case TicketStatus.Resolved:
                ResolvedAt = now;
                PendingSince = null;
                break;
            case TicketStatus.Closed:
                ClosedAt = now;
                PendingSince = null;
                break;
        }

        AuditEntries.Add(TicketAuditEntry.Create(Id, changedBy, from, newStatus, "StatusChanged", comment));
    }

    public void RequestMoreInfo(string changedBy, DateTime now) =>
        TransitionTo(TicketStatus.PendingEmployeeResponse, changedBy, "Requested more information from employee", now);

    public void EmployeeResponded(string changedBy, DateTime now) =>
        TransitionTo(TicketStatus.InProgress, changedBy, "Employee responded", now);

    public void MarkResolved(string changedBy, string comment, DateTime now) =>
        TransitionTo(TicketStatus.Resolved, changedBy, comment, now);

    public void Close(string changedBy, string? comment, DateTime now)
    {
        if (Status != TicketStatus.Resolved)
            throw new InvalidOperationException("Only resolved tickets can be closed. Valid next statuses: " +
                string.Join(", ", TicketStatusRules.GetValidNextStatuses(Status)));

        Status = TicketStatus.Closed;
        ClosedAt = now;
        UpdatedAt = now;
        PendingSince = null;
        ClosedReason = comment;
        AuditEntries.Add(TicketAuditEntry.Create(Id, changedBy, TicketStatus.Resolved, TicketStatus.Closed, "Closed", comment));
    }

    public TicketNote AddNote(string authorId, string authorName, string content, bool isInternal, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Note cannot be blank");

        var note = TicketNote.Create(Id, authorId, authorName, content, isInternal, now);
        Notes.Add(note);
        UpdatedAt = now;
        return note;
    }

    public TicketLink AddLink(Guid targetTicketId, TicketLinkType linkType, string changedBy, DateTime now)
    {
        if (targetTicketId == Id)
            throw new InvalidOperationException("A ticket cannot be linked to itself");

        if (Links.Any(l => l.TargetTicketId == targetTicketId))
            throw new InvalidOperationException("A link between these tickets already exists");

        var link = TicketLink.Create(Id, targetTicketId, linkType, changedBy, now);
        Links.Add(link);
        UpdatedAt = now;
        return link;
    }
}

using Helpdesk.Core.Domain;

namespace Helpdesk.Api.Models;

public static class TicketMapper
{
    public static TechnicianDto ToDto(Technician technician) =>
        new(technician.Id, technician.Name, technician.Email, technician.IsActive);

    public static TicketSummaryDto ToSummaryDto(Ticket ticket) =>
        new(
            ticket.Id,
            ticket.Title,
            ticket.Status,
            ticket.Priority,
            ticket.Type,
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.AssignedTechnicianId,
            ticket.AssignedTechnician?.Name,
            ticket.ResolvedAt,
            ticket.ClosedAt,
            ticket.PendingSince);

    public static TicketDetailDto ToDetailDto(Ticket ticket, IReadOnlyList<TicketLink> inboundLinks)
    {
        var links = new List<TicketLinkDto>();

        foreach (var link in ticket.Links)
        {
            links.Add(new TicketLinkDto(
                link.Id,
                link.TargetTicketId,
                link.TargetTicket?.Title,
                link.LinkType,
                link.CreatedAt,
                "out"));
        }

        foreach (var link in inboundLinks)
        {
            links.Add(new TicketLinkDto(
                link.Id,
                link.SourceTicketId,
                link.SourceTicket?.Title,
                link.LinkType,
                link.CreatedAt,
                "in"));
        }

        return new TicketDetailDto(
            ticket.Id,
            ticket.Title,
            ticket.Description,
            ticket.Status,
            ticket.Priority,
            ticket.Type,
            ticket.SubmitterName,
            ticket.SubmitterEmail,
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.AssignedTechnicianId,
            ticket.AssignedTechnician?.Name,
            ticket.ResolvedAt,
            ticket.ClosedAt,
            ticket.PendingSince,
            ticket.ClosedReason,
            ticket.Notes.Select(n => new TicketNoteDto(n.Id, n.AuthorId, n.AuthorName, n.Content, n.IsInternal, n.CreatedAt)).ToList(),
            ticket.AuditEntries.Select(a => new TicketAuditDto(a.Id, a.ChangedBy, a.FromStatus, a.ToStatus, a.Action, a.Comment, a.Timestamp)).ToList(),
            links,
            TicketStatusRules.GetValidNextStatuses(ticket.Status));
    }
}

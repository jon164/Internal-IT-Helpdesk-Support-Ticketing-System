using Helpdesk.Api.Models;
using Helpdesk.Core.Domain;
using Helpdesk.Data.Repositories;

namespace Helpdesk.Api.Services;

public class TicketService
{
    private readonly ITicketRepository _repository;

    public TicketService(ITicketRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<Technician>> GetActiveTechniciansAsync() =>
        await _repository.GetActiveTechniciansAsync();

    public async Task<IReadOnlyList<Ticket>> GetAssignedTicketsAsync(Guid technicianId) =>
        await _repository.GetAssignedTicketsAsync(technicianId);

    public async Task<IReadOnlyList<Ticket>> GetUnassignedTicketsAsync() =>
        await _repository.GetUnassignedTicketsAsync();

    public async Task<Ticket> GetTicketAsync(Guid id) =>
        await _repository.GetTicketByIdAsync(id) ?? throw new NotFoundException("Ticket not found.");

    public async Task<IReadOnlyList<TicketLink>> GetInboundLinksAsync(Guid ticketId) =>
        await _repository.GetInboundLinksAsync(ticketId);

    public async Task<Ticket> ClaimAsync(Guid ticketId, Guid technicianId, DateTime now)
    {
        var technician = await _repository.GetTechnicianByIdAsync(technicianId)
            ?? throw new NotFoundException("Technician not found.");

        if (!technician.IsActive)
            throw new DomainValidationException("Cannot claim a ticket as an inactive technician.");

        var ticket = await GetTicketAsync(ticketId);

        if (ticket.AssignedTechnicianId.HasValue)
            throw new ClaimConflictException(ticket.AssignedTechnicianId.Value, ticket.AssignedTechnician?.Name);

        ticket.Claim(technicianId, technician.Name, now);
        await _repository.SaveChangesAsync();
        return await GetTicketAsync(ticketId);
    }

    public async Task<Ticket> ReassignAsync(Guid ticketId, Guid technicianId, DateTime now)
    {
        var technician = await _repository.GetTechnicianByIdAsync(technicianId)
            ?? throw new NotFoundException("Technician not found.");

        if (!technician.IsActive)
        {
            var available = (await _repository.GetActiveTechniciansAsync()).Select(TicketMapper.ToDto).ToList();
            throw new ReassignValidationException("The selected technician is inactive or unavailable.", available);
        }

        var ticket = await GetTicketAsync(ticketId);
        ticket.Reassign(technicianId, technician.Name, now);
        await _repository.SaveChangesAsync();
        return await GetTicketAsync(ticketId);
    }

    public async Task<Ticket> ChangeStatusAsync(Guid ticketId, TicketStatus newStatus, string changedBy, string? comment, DateTime now)
    {
        var ticket = await GetTicketAsync(ticketId);
        ticket.TransitionTo(newStatus, changedBy, comment, now);
        await _repository.SaveChangesAsync();
        return await GetTicketAsync(ticketId);
    }

    public async Task<Ticket> RequestInfoAsync(Guid ticketId, string changedBy, DateTime now)
    {
        var ticket = await GetTicketAsync(ticketId);
        ticket.RequestMoreInfo(changedBy, now);
        await _repository.SaveChangesAsync();
        return await GetTicketAsync(ticketId);
    }

    public async Task<Ticket> MarkResolvedAsync(Guid ticketId, string changedBy, string? comment, DateTime now)
    {
        var ticket = await GetTicketAsync(ticketId);
        ticket.MarkResolved(changedBy, comment ?? "Resolved.", now);
        await _repository.SaveChangesAsync();
        return await GetTicketAsync(ticketId);
    }

    public async Task<Ticket> AddNoteAsync(Guid ticketId, string authorId, string authorName, string content, bool isInternal, DateTime now)
    {
        var ticket = await GetTicketAsync(ticketId);
        ticket.AddNote(authorId, authorName, content, isInternal, now);
        await _repository.SaveChangesAsync();
        return await GetTicketAsync(ticketId);
    }

    public async Task<Ticket> EmployeeCommentAsync(Guid ticketId, string authorId, string authorName, string content, DateTime now)
    {
        var ticket = await GetTicketAsync(ticketId);
        ticket.AddNote(authorId, authorName, content, isInternal: false, now);

        if (ticket.Status == TicketStatus.PendingEmployeeResponse)
        {
            ticket.EmployeeResponded(authorName, now);
        }

        await _repository.SaveChangesAsync();
        return await GetTicketAsync(ticketId);
    }

    public async Task<Ticket> LinkAsync(Guid ticketId, Guid targetTicketId, TicketLinkType linkType, string changedBy, DateTime now)
    {
        if (targetTicketId == ticketId)
            throw new DomainValidationException("A ticket cannot be linked to itself.");

        if (await _repository.HasLinkBetweenAsync(ticketId, targetTicketId))
            throw new DomainValidationException("A link between these tickets already exists.");

        var targetExists = await _repository.GetTicketByIdAsync(targetTicketId);
        if (targetExists is null)
            throw new NotFoundException("Target ticket not found.");

        var ticket = await GetTicketAsync(ticketId);
        ticket.AddLink(targetTicketId, linkType, changedBy, now);
        await _repository.SaveChangesAsync();
        return await GetTicketAsync(ticketId);
    }

    public async Task<Ticket> CloseAsync(Guid ticketId, string changedBy, string? comment, DateTime now)
    {
        var ticket = await GetTicketAsync(ticketId);
        ticket.Close(changedBy, comment, now);
        await _repository.SaveChangesAsync();
        return await GetTicketAsync(ticketId);
    }

    public async Task<int> AutoCloseAsync(TimeSpan timeout, DateTime now)
    {
        var cutoff = now.Subtract(timeout);
        var tickets = await _repository.GetResolvedTicketsOlderThanAsync(cutoff);

        foreach (var ticket in tickets)
        {
            ticket.Close("system", "auto-closed, no response", now);
        }

        await _repository.SaveChangesAsync();
        return tickets.Count;
    }
}

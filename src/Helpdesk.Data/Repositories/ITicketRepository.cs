using Helpdesk.Core.Domain;

namespace Helpdesk.Data.Repositories;

public interface ITicketRepository
{
    Task<List<Technician>> GetActiveTechniciansAsync();
    Task<Technician?> GetTechnicianByIdAsync(Guid id);
    Task<List<Ticket>> GetAssignedTicketsAsync(Guid technicianId);
    Task<List<Ticket>> GetUnassignedTicketsAsync();
    Task<Ticket?> GetTicketByIdAsync(Guid id);
    Task<List<Ticket>> GetResolvedTicketsOlderThanAsync(DateTime cutoff);
    Task<bool> HasLinkBetweenAsync(Guid firstTicketId, Guid secondTicketId);
    Task<IReadOnlyList<TicketLink>> GetInboundLinksAsync(Guid ticketId);
    Task AddTicketAsync(Ticket ticket);
    Task AddTechnicianAsync(Technician technician);
    Task SaveChangesAsync();
}

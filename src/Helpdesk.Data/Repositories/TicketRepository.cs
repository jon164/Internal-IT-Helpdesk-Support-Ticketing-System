using Helpdesk.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Data.Repositories;

public class TicketRepository : ITicketRepository
{
    private readonly HelpdeskDbContext _context;

    public TicketRepository(HelpdeskDbContext context)
    {
        _context = context;
    }

    public async Task<List<Technician>> GetActiveTechniciansAsync()
    {
        return await _context.Technicians
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<Technician?> GetTechnicianByIdAsync(Guid id)
    {
        return await _context.Technicians
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<List<Ticket>> GetAssignedTicketsAsync(Guid technicianId)
    {
        return await _context.Tickets
            .Include(t => t.AssignedTechnician)
            .Where(t => t.AssignedTechnicianId == technicianId)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Ticket>> GetUnassignedTicketsAsync()
    {
        return await _context.Tickets
            .Include(t => t.AssignedTechnician)
            .Where(t => t.AssignedTechnicianId == null && t.Status != TicketStatus.Closed)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Ticket>> GetAllTicketsAsync()
    {
        return await _context.Tickets
            .Include(t => t.AssignedTechnician)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<Ticket?> GetTicketByIdAsync(Guid id)
    {
        return await _context.Tickets
            .Include(t => t.AssignedTechnician)
            .Include(t => t.Notes.OrderBy(n => n.CreatedAt))
            .Include(t => t.AuditEntries.OrderBy(a => a.Timestamp))
            .Include(t => t.Links)
                .ThenInclude(l => l.TargetTicket)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<List<Ticket>> GetResolvedTicketsOlderThanAsync(DateTime cutoff)
    {
        return await _context.Tickets
            .Where(t => t.Status == TicketStatus.Resolved
                        && t.ResolvedAt != null
                        && t.ResolvedAt < cutoff)
            .ToListAsync();
    }

    public async Task<bool> HasLinkBetweenAsync(Guid firstTicketId, Guid secondTicketId)
    {
        return await _context.TicketLinks
            .AnyAsync(l =>
                (l.SourceTicketId == firstTicketId && l.TargetTicketId == secondTicketId) ||
                (l.SourceTicketId == secondTicketId && l.TargetTicketId == firstTicketId));
    }

    public async Task<IReadOnlyList<TicketLink>> GetInboundLinksAsync(Guid ticketId)
    {
        return await _context.TicketLinks
            .Include(l => l.SourceTicket)
            .Where(l => l.TargetTicketId == ticketId)
            .ToListAsync();
    }

    public async Task AddTicketAsync(Ticket ticket)
    {
        await _context.Tickets.AddAsync(ticket);
    }

    public async Task AddTechnicianAsync(Technician technician)
    {
        await _context.Technicians.AddAsync(technician);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}

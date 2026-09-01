namespace Helpdesk.Core.Domain;

public class Technician
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<Ticket> AssignedTickets { get; set; } = new List<Ticket>();
}

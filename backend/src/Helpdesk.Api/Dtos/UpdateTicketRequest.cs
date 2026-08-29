using Helpdesk.Core.Models;

namespace Helpdesk.Api.Dtos;

public class UpdateTicketRequest
{
    public TicketStatus? Status { get; set; }
    public string? Assignee { get; set; }
    public string? Workaround { get; set; }
}

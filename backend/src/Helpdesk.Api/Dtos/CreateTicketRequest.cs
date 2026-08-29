using Helpdesk.Core.Models;

namespace Helpdesk.Api.Dtos;

public class CreateTicketRequest
{
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
}

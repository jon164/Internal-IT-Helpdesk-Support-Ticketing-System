namespace Helpdesk.Core.Models;

public class FlightRecord
{
    public int Id { get; set; }
    public string FlightNumber { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string Gate { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

namespace Helpdesk.Core.Models;

public class StaffProfile
{
    public int Id { get; set; }
    public string BadgeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

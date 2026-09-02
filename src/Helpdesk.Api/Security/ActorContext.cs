namespace Helpdesk.Api.Security;

using Helpdesk.Core.Domain;

public sealed record ActorContext(string UserId, UserRole Role, string? DisplayName = null)
{
    public bool IsSupportStaff => Role is UserRole.Technician or UserRole.TeamLead;
    public bool IsTeamLead => Role == UserRole.TeamLead;
    public bool IsRequester => Role == UserRole.Requester;
}

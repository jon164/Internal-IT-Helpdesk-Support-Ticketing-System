namespace Helpdesk.Core.Domain;

/// <summary>
/// The identity and role of whoever is attempting an operation.
/// </summary>
/// <remarks>
/// Every authorisation and workflow decision in the domain takes one of these rather than reading
/// ambient state, so the rules can be unit-tested directly without an HTTP context or a signed-in user.
/// </remarks>
public sealed record ActorContext(string UserId, UserRole Role, string? DisplayName = null)
{
    /// <summary>True for technicians and team leads — anyone who works the queue.</summary>
    public bool IsSupportStaff => Role is UserRole.Technician or UserRole.TeamLead;

    public bool IsTeamLead => Role == UserRole.TeamLead;

    public bool IsRequester => Role == UserRole.Requester;

    /// <summary>True when this actor raised the ticket in question.</summary>
    public bool Owns(Ticket ticket) =>
        string.Equals(UserId, ticket.RequesterId, StringComparison.Ordinal);

    /// <summary>True when this ticket is currently assigned to this actor.</summary>
    public bool IsAssignedTo(Ticket ticket) =>
        ticket.AssignedTechnicianId is not null &&
        string.Equals(UserId, ticket.AssignedTechnicianId, StringComparison.Ordinal);
}

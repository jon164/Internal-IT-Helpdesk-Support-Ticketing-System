using Helpdesk.Api.Models;

namespace Helpdesk.Api.Models;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class DomainValidationException : Exception
{
    public int StatusCode { get; }
    public DomainValidationException(string message, int statusCode = StatusCodes.Status400BadRequest) : base(message)
    {
        StatusCode = statusCode;
    }
}

public class ClaimConflictException : DomainValidationException
{
    public Guid OwnerId { get; }
    public string? OwnerName { get; }

    public ClaimConflictException(Guid ownerId, string? ownerName)
        : base($"Ticket is already assigned to {ownerName ?? ownerId.ToString()}.", StatusCodes.Status409Conflict)
    {
        OwnerId = ownerId;
        OwnerName = ownerName;
    }
}

public class ReassignValidationException : DomainValidationException
{
    public IReadOnlyList<TechnicianDto> AvailableTechnicians { get; }

    public ReassignValidationException(string message, IReadOnlyList<TechnicianDto> availableTechnicians)
        : base(message, StatusCodes.Status400BadRequest)
    {
        AvailableTechnicians = availableTechnicians;
    }
}

namespace Helpdesk.Core.Domain;

public enum TicketStatus
{
    New = 0,
    InProgress = 1,
    PendingEmployeeResponse = 2,
    Resolved = 3,
    Closed = 4
}

public enum TicketPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum TicketType
{
    Incident = 0,
    ServiceRequest = 1
}

public enum TicketLinkType
{
    Duplicate = 0,
    Related = 1
}

using Helpdesk.Core.Domain;

namespace Helpdesk.Api.Models;

public record TechnicianDto(Guid Id, string Name, string Email, bool IsActive);

public record TicketSummaryDto(
    Guid Id,
    string Title,
    TicketStatus Status,
    TicketPriority Priority,
    TicketType Type,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? AssignedTechnicianId,
    string? AssignedTechnicianName,
    DateTime? ResolvedAt,
    DateTime? ClosedAt,
    DateTime? PendingSince);

public record TicketDetailDto(
    Guid Id,
    string Title,
    string Description,
    TicketStatus Status,
    TicketPriority Priority,
    TicketType Type,
    string SubmitterName,
    string SubmitterEmail,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? AssignedTechnicianId,
    string? AssignedTechnicianName,
    DateTime? ResolvedAt,
    DateTime? ClosedAt,
    DateTime? PendingSince,
    string? ClosedReason,
    IReadOnlyList<TicketNoteDto> Notes,
    IReadOnlyList<TicketAuditDto> Audit,
    IReadOnlyList<TicketLinkDto> Links,
    IReadOnlyList<TicketStatus> AllowedNextStatuses);

public record TicketNoteDto(Guid Id, string AuthorId, string AuthorName, string Content, bool IsInternal, DateTime CreatedAt);

public record TicketAuditDto(Guid Id, string ChangedBy, TicketStatus? FromStatus, TicketStatus? ToStatus, string Action, string? Comment, DateTime Timestamp);

public record TicketLinkDto(Guid Id, Guid OtherTicketId, string? OtherTicketTitle, TicketLinkType LinkType, DateTime CreatedAt, string Direction);

public record ClaimRequest(Guid TechnicianId);
public record AssignRequest(Guid TechnicianId);
public record StatusChangeRequest(TicketStatus NewStatus, string? Comment, string ChangedBy);
public record NoteRequest(string AuthorId, string AuthorName, string Content, bool IsInternal);
public record LinkRequest(Guid TargetTicketId, TicketLinkType LinkType, string ChangedBy);
public record CloseRequest(string? Comment, string ChangedBy);
public record EmployeeCommentRequest(string AuthorId, string AuthorName, string Content);

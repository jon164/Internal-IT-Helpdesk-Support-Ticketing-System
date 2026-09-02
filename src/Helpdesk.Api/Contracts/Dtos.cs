namespace Helpdesk.Api.Contracts;

using Helpdesk.Core.Domain;
using Helpdesk.Core.Metrics;
using Helpdesk.Core.Sla;
using Helpdesk.Data.Services;

/// <summary>
/// Response shapes returned by the API.
/// </summary>
/// <remarks>
/// Domain entities are never serialised directly. Two reasons, both of which matter here: the
/// full-detail body of a ticket must not travel to a caller entitled only to the queue summary, and
/// the client should not be coupled to the persistence model. Mapping is explicit so that what leaves
/// the server is an inspectable decision rather than a side effect of a property being public.
/// </remarks>
public sealed record UserDto(
    string Id,
    string DisplayName,
    string Department,
    string Role,
    bool IsActive)
{
    public static UserDto From(UserAccount user) =>
        new(user.Id, user.DisplayName, user.Department, user.Role.ToString(), user.IsActive);
}

public sealed record UserAuditEntryDto(
    int Id,
    DateTimeOffset OccurredAt,
    string EventType,
    string SubjectUserId,
    string SubjectDisplayName,
    string ActorDisplayName,
    string? FromRole,
    string? ToRole,
    string Reason)
{
    public static UserAuditEntryDto From(UserAuditEntry e) =>
        new(e.Id, e.OccurredAt, e.EventType.ToString(), e.SubjectUserId, e.SubjectDisplayName,
            e.ActorDisplayName, e.FromRole?.ToString(), e.ToRole?.ToString(), e.Reason);
}

public sealed record SlaPolicyDto(
    string Priority,
    string DisplayName,
    string Description,
    double ResponseTargetMinutes,
    double ResolutionTargetMinutes,
    string Clock,
    double WarningThreshold)
{
    public static SlaPolicyDto From(SlaPolicy policy) =>
        new(policy.Priority.ToString(), policy.DisplayName, policy.Description,
            policy.ResponseTargetMinutes, policy.ResolutionTargetMinutes,
            policy.ClockType.ToString(), policy.WarningThreshold);
}

public sealed record SlaStatusDto(
    string State,
    double TargetMinutes,
    double ConsumedMinutes,
    double RemainingMinutes,
    DateTimeOffset DueAt,
    double PercentConsumed)
{
    public static SlaStatusDto From(SlaStatus status) =>
        new(status.State.ToString(), status.TargetMinutes, Math.Round(status.ConsumedMinutes, 1),
            Math.Round(status.RemainingMinutes, 1), status.DueAt,
            Math.Round(status.PercentConsumed, 1));
}

/// <summary>Queue row. Deliberately carries no free-text body.</summary>
public sealed record TicketSummaryDto(
    int Id,
    string Reference,
    string Title,
    string Category,
    string Priority,
    string PriorityLabel,
    string Status,
    string RequesterName,
    string Department,
    string? AssignedTechnicianId,
    string? AssignedTechnicianName,
    bool IsRestricted,
    DateTimeOffset CreatedAt,
    SlaStatusDto Response,
    SlaStatusDto Resolution)
{
    public static TicketSummaryDto From(Ticket ticket, TicketSlaView sla) =>
        new(ticket.Id, ticket.Reference, ticket.Title, ticket.Category,
            ticket.Priority.ToString(), sla.Policy.DisplayName, ticket.Status.ToString(),
            ticket.RequesterName, ticket.Department,
            ticket.AssignedTechnicianId, ticket.AssignedTechnicianName,
            ticket.Sensitivity == TicketSensitivity.Restricted, ticket.CreatedAt,
            SlaStatusDto.From(sla.Response), SlaStatusDto.From(sla.Resolution));
}

public sealed record TicketEventDto(
    int Id,
    DateTimeOffset OccurredAt,
    string EventType,
    string ActorName,
    string ActorRole,
    string? FromStatus,
    string? ToStatus,
    string Detail)
{
    public static TicketEventDto From(TicketEvent e) =>
        new(e.Id, e.OccurredAt, e.EventType.ToString(), e.ActorName, e.ActorRole.ToString(),
            e.FromStatus?.ToString(), e.ToStatus?.ToString(), e.Detail);
}

public sealed record HoldPeriodDto(DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string? Reason)
{
    public static HoldPeriodDto From(HoldPeriod h) => new(h.StartedAt, h.EndedAt, h.Reason);
}

/// <summary>
/// What this caller is permitted to do with this ticket, computed server-side.
/// </summary>
/// <remarks>
/// The client uses this to decide which controls to show. It is a convenience for the interface, not
/// the enforcement point — every one of these actions is re-checked on the server when attempted, so
/// a caller who forges the flags gains nothing.
/// </remarks>
public sealed record TicketPermissionsDto(
    bool CanComment,
    bool CanChangePriority,
    bool CanAssignToSelf,
    bool CanAssignToOthers,
    IReadOnlyList<string> AllowedNextStatuses);

public sealed record TicketDetailDto(
    TicketSummaryDto Summary,
    string Description,
    string? ResolutionNotes,
    DateTimeOffset? FirstRespondedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    SlaPolicyDto Policy,
    IReadOnlyList<HoldPeriodDto> HoldPeriods,
    IReadOnlyList<TicketEventDto> Events,
    TicketPermissionsDto Permissions);

public sealed record MetricsDto(
    DateTimeOffset From,
    DateTimeOffset To,
    int TotalCreated,
    int TotalResolved,
    int OpenBacklog,
    int AwaitingClosure,
    int OpenBreached,
    int OpenAtRisk,
    double? ResponseAttainmentPercent,
    double? ResolutionAttainmentPercent,
    double? MedianResolutionMinutes,
    double? MeanResolutionMinutes,
    double? ThroughputRatioPercent,
    IReadOnlyDictionary<string, int> CreatedByPriority,
    IReadOnlyDictionary<string, int> OpenByStatus,
    IReadOnlyDictionary<string, int> CreatedByCategory)
{
    // Named FromSummary rather than From: the record already has a property called From (the start of
    // the reporting window), and a member cannot share its name.
    public static MetricsDto FromSummary(MetricsSummary s) => new(
        s.From, s.To, s.TotalCreated, s.TotalResolved, s.OpenBacklog, s.AwaitingClosure,
        s.OpenBreached, s.OpenAtRisk,
        Round(s.ResponseAttainmentPercent), Round(s.ResolutionAttainmentPercent),
        Round(s.MedianResolutionMinutes), Round(s.MeanResolutionMinutes),
        Round(s.ThroughputRatioPercent),
        s.CreatedByPriority.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        s.OpenByStatus.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        s.CreatedByCategory);

    private static double? Round(double? value) => value is null ? null : Math.Round(value.Value, 1);
}

public sealed record BacklogTrendPointDto(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int Raised,
    int Resolved,
    int UnresolvedAtEnd,
    int NetChange);

public sealed record BacklogAgeBucketDto(string Label, int Count);

public sealed record BacklogReportDto(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<BacklogTrendPointDto> Trend,
    int UnresolvedAtStart,
    int UnresolvedNow,
    int UnresolvedBreached,
    int NetChange,
    string Direction,
    double? ClearanceRatePercent,
    IReadOnlyList<BacklogAgeBucketDto> AgeBuckets,
    double? OldestUnresolvedDays,
    string? OldestUnresolvedReference,
    IReadOnlyDictionary<string, int> UnresolvedByAssignee)
{
    public static BacklogReportDto FromReport(BacklogReport r) => new(
        r.From,
        r.To,
        r.Trend
            .Select(p => new BacklogTrendPointDto(
                p.PeriodStart, p.PeriodEnd, p.Raised, p.Resolved, p.UnresolvedAtEnd, p.NetChange))
            .ToList(),
        r.UnresolvedAtStart,
        r.UnresolvedNow,
        r.UnresolvedBreached,
        r.NetChange,
        r.Direction.ToString(),
        r.ClearanceRatePercent is null ? null : Math.Round(r.ClearanceRatePercent.Value, 1),
        r.AgeBuckets.Select(b => new BacklogAgeBucketDto(b.Label, b.Count)).ToList(),
        r.OldestUnresolvedDays is null ? null : Math.Round(r.OldestUnresolvedDays.Value, 1),
        r.OldestUnresolvedReference,
        r.UnresolvedByAssignee);
}

// --- Request bodies ------------------------------------------------------

public sealed record TransitionRequestDto(string Status, string? Note);

public sealed record AssignRequestDto(string TechnicianId);

public sealed record PriorityRequestDto(string Priority, string Justification);

public sealed record CommentRequestDto(string Comment);

/// <summary>
/// Sign-in payload.
/// </summary>
/// <remarks>
/// Carries an account identifier and no credential. The prototype does not authenticate — see the
/// note on the session endpoint. The shape exists so that adding a password field later is a change
/// to this record and its handler, not to every caller.
/// </remarks>
public sealed record SignInRequestDto(string UserId);

public sealed record ChangeRoleRequestDto(string Role, string Reason);

public sealed record SetActiveRequestDto(bool IsActive, string Reason);

/// <summary>
/// The state of the demonstration dataset. <paramref name="IsEmpty"/> is sent rather than left to the
/// client to derive, so both ends agree on what "empty" means.
/// </summary>
public sealed record SampleDataStatusDto(int TicketCount, bool IsEmpty, int GeneratedCount);

/// <summary>The outcome of a clear, including how much was destroyed.</summary>
public sealed record SampleDataClearedDto(int RemovedCount, SampleDataStatusDto Status);

/// <summary>A refusal, in a shape the client can display directly.</summary>
public sealed record ProblemDto(string Error, string Message);

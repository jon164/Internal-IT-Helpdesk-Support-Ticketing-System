namespace Helpdesk.Core.Domain;

/// <summary>
/// One immutable record of a change to somebody's account.
/// </summary>
/// <remarks>
/// The problem definition opens with a shared mailbox that two former staff could still read because
/// nobody recorded — or noticed — that their access was never revoked. An account whose privileges
/// can be changed without a trace reproduces exactly that failure, so every role change and every
/// deactivation writes one of these, naming both the administrator who made the change and the
/// account it was made to.
/// </remarks>
public class UserAuditEntry
{
    public int Id { get; set; }

    /// <summary>The account that was changed.</summary>
    public string SubjectUserId { get; set; } = string.Empty;

    public string SubjectDisplayName { get; set; } = string.Empty;

    /// <summary>The administrator who made the change.</summary>
    public string ActorId { get; set; } = string.Empty;

    public string ActorDisplayName { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public UserEventType EventType { get; set; }

    /// <summary>Previous role, where the event is a role change.</summary>
    public UserRole? FromRole { get; set; }

    public UserRole? ToRole { get; set; }

    /// <summary>Why the change was made. Required, so the trail explains itself.</summary>
    public string Reason { get; set; } = string.Empty;

    public static UserAuditEntry For(
        UserAccount subject,
        ActorContext actor,
        UserEventType type,
        DateTimeOffset occurredAt,
        string reason,
        UserRole? fromRole = null,
        UserRole? toRole = null) => new()
        {
            SubjectUserId = subject.Id,
            SubjectDisplayName = subject.DisplayName,
            ActorId = actor.UserId,
            ActorDisplayName = actor.DisplayName ?? actor.UserId,
            OccurredAt = occurredAt,
            EventType = type,
            FromRole = fromRole,
            ToRole = toRole,
            Reason = reason
        };
}

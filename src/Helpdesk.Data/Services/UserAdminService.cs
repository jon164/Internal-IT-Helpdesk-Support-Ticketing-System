namespace Helpdesk.Data.Services;

using Helpdesk.Core;
using Helpdesk.Core.Domain;
using Helpdesk.Core.Security;
using Helpdesk.Core.Time;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Account administration: who exists, what role they hold, and whether they are still active.
/// </summary>
/// <remarks>
/// <para>
/// Every operation here is restricted to the service manager and writes an audit entry. Two guards
/// exist that are worth being able to explain, because both prevent a system that cannot be
/// recovered without database surgery: the service manager cannot change or deactivate their own
/// account, and the last remaining active service manager cannot be removed.
/// </para>
/// <para>
/// Deactivation rather than deletion is deliberate. The audit trail names people; deleting the
/// account would leave entries pointing at nothing, and the whole reason this exists is that a
/// departed member of staff kept access nobody had recorded.
/// </para>
/// </remarks>
public sealed class UserAdminService
{
    private readonly HelpdeskDbContext _db;
    private readonly IClock _clock;

    public UserAdminService(HelpdeskDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>All accounts, active and inactive. Service manager only.</summary>
    public async Task<OperationResult<IReadOnlyList<UserAccount>>> GetAllAsync(
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (!TicketAccessPolicy.CanAdministerUsers(actor))
        {
            return OperationResult<IReadOnlyList<UserAccount>>.Forbidden(
                "Account administration is restricted to the service manager.");
        }

        var users = await _db.Users
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Ordered in memory, by privilege rather than by the enum. Sorting in the query would order
        // by the stored role *name* — the roles are persisted as text — so the order would follow the
        // alphabet and would change silently if a role were ever renamed. The population is a few
        // dozen accounts, so doing it here costs nothing.
        var ordered = users
            .OrderBy(u => RolePrecedence(u.Role))
            .ThenBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return OperationResult<IReadOnlyList<UserAccount>>.Success(ordered);
    }

    /// <summary>The account-change history, most recent first. Service manager only.</summary>
    public async Task<OperationResult<IReadOnlyList<UserAuditEntry>>> GetAuditAsync(
        ActorContext actor,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TicketAccessPolicy.CanAdministerUsers(actor))
        {
            return OperationResult<IReadOnlyList<UserAuditEntry>>.Forbidden(
                "Account administration is restricted to the service manager.");
        }

        var entries = await _db.UserAuditEntries
            .AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

        return OperationResult<IReadOnlyList<UserAuditEntry>>.Success(entries);
    }

    /// <summary>Changes somebody's role, recording who did it and why.</summary>
    public async Task<OperationResult<UserAccount>> ChangeRoleAsync(
        string userId,
        UserRole role,
        string reason,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (!TicketAccessPolicy.CanAdministerUsers(actor))
        {
            return OperationResult<UserAccount>.Forbidden(
                "Account administration is restricted to the service manager.");
        }

        if (!Enum.IsDefined(role))
        {
            return OperationResult<UserAccount>.Invalid("That role is not recognised.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult<UserAccount>.Invalid(
                "A reason is required, so the change explains itself in the audit trail.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult<UserAccount>.NotFound("No such account.");
        }

        // Self-modification is refused outright. A manager who demotes themselves by mistake cannot
        // undo it, because undoing it needs the privilege they just gave away.
        if (string.Equals(user.Id, actor.UserId, StringComparison.Ordinal))
        {
            return OperationResult<UserAccount>.Conflict(
                "You cannot change your own role. Ask another service manager.");
        }

        if (user.Role == role)
        {
            return OperationResult<UserAccount>.Success(user);
        }

        if (await WouldRemoveLastManagerAsync(user, role, user.IsActive, cancellationToken))
        {
            return OperationResult<UserAccount>.Conflict(
                "This is the only active service manager. Promote somebody else first.");
        }

        var previous = user.Role;
        user.Role = role;

        _db.UserAuditEntries.Add(UserAuditEntry.For(
            user, actor, UserEventType.RoleChanged, _clock.UtcNow, reason.Trim(), previous, role));

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<UserAccount>.Success(user);
    }

    /// <summary>
    /// Activates or deactivates an account. A deactivated account cannot be used to sign in and is
    /// rejected by the actor resolution middleware on every request.
    /// </summary>
    public async Task<OperationResult<UserAccount>> SetActiveAsync(
        string userId,
        bool isActive,
        string reason,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (!TicketAccessPolicy.CanAdministerUsers(actor))
        {
            return OperationResult<UserAccount>.Forbidden(
                "Account administration is restricted to the service manager.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult<UserAccount>.Invalid(
                "A reason is required, so the change explains itself in the audit trail.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult<UserAccount>.NotFound("No such account.");
        }

        if (string.Equals(user.Id, actor.UserId, StringComparison.Ordinal))
        {
            return OperationResult<UserAccount>.Conflict(
                "You cannot deactivate your own account.");
        }

        if (user.IsActive == isActive)
        {
            return OperationResult<UserAccount>.Success(user);
        }

        if (await WouldRemoveLastManagerAsync(user, user.Role, isActive, cancellationToken))
        {
            return OperationResult<UserAccount>.Conflict(
                "This is the only active service manager. Promote somebody else first.");
        }

        user.IsActive = isActive;

        _db.UserAuditEntries.Add(UserAuditEntry.For(
            user,
            actor,
            isActive ? UserEventType.Reactivated : UserEventType.Deactivated,
            _clock.UtcNow,
            reason.Trim()));

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<UserAccount>.Success(user);
    }

    /// <summary>Display order for the account list: most privileged first.</summary>
    private static int RolePrecedence(UserRole role) => role switch
    {
        UserRole.TeamLead => 0,
        UserRole.Technician => 1,
        UserRole.Requester => 2,
        _ => 3
    };

    /// <summary>
    /// Whether applying the proposed role and active state to this account would leave the system
    /// with no active service manager at all — and therefore nobody able to administer accounts.
    /// </summary>
    private async Task<bool> WouldRemoveLastManagerAsync(
        UserAccount subject,
        UserRole proposedRole,
        bool proposedActive,
        CancellationToken cancellationToken)
    {
        var stillManager = proposedRole == UserRole.TeamLead && proposedActive;

        if (stillManager)
        {
            return false;
        }

        var otherActiveManagers = await _db.Users
            .AsNoTracking()
            .CountAsync(
                u => u.Role == UserRole.TeamLead && u.IsActive && u.Id != subject.Id,
                cancellationToken);

        return otherActiveManagers == 0;
    }
}

namespace Helpdesk.Core.Workflow;

using Helpdesk.Core.Domain;

/// <summary>
/// The ticket state machine: which transitions exist, and who may perform them.
/// </summary>
/// <remarks>
/// <para>
/// Holding this in one place is what makes the audit trail trustworthy. If status could be set
/// directly from the API or the UI, the recorded history would only be as reliable as the least
/// careful call site — which is the situation the current shared-mailbox process is in.
/// </para>
/// <para>
/// The state graph is:
/// </para>
/// <code>
///   New ──► Triaged ──► Assigned ──► InProgress ──► Resolved ──► Closed
///    │         │            │          │    ▲          │
///    │         │            └──────────┘    │          └──► InProgress  (reopened)
///    │         │         (returned to queue)│
///    │         │                       OnHold
///    └─────────┴───────────────────────────────────────► Cancelled
/// </code>
/// </remarks>
public static class TicketWorkflow
{
    private static readonly IReadOnlyDictionary<TicketStatus, TicketStatus[]> Transitions =
        new Dictionary<TicketStatus, TicketStatus[]>
        {
            [TicketStatus.New] = [TicketStatus.Triaged, TicketStatus.Cancelled],
            [TicketStatus.Triaged] = [TicketStatus.Assigned, TicketStatus.Cancelled],
            [TicketStatus.Assigned] = [TicketStatus.InProgress, TicketStatus.Triaged, TicketStatus.Cancelled],
            [TicketStatus.InProgress] = [TicketStatus.OnHold, TicketStatus.Resolved, TicketStatus.Cancelled],
            [TicketStatus.OnHold] = [TicketStatus.InProgress, TicketStatus.Cancelled],
            [TicketStatus.Resolved] = [TicketStatus.Closed, TicketStatus.InProgress],
            [TicketStatus.Closed] = [],
            [TicketStatus.Cancelled] = []
        };

    /// <summary>Statuses from which no further transition is possible.</summary>
    public static IReadOnlySet<TicketStatus> TerminalStatuses { get; } =
        new HashSet<TicketStatus> { TicketStatus.Closed, TicketStatus.Cancelled };

    /// <summary>Every status reachable in one step from <paramref name="current"/>, ignoring role.</summary>
    public static IReadOnlyList<TicketStatus> AllowedNextStatuses(TicketStatus current) =>
        Transitions.TryGetValue(current, out var next) ? next : Array.Empty<TicketStatus>();

    public static bool IsTerminal(TicketStatus status) => TerminalStatuses.Contains(status);

    /// <summary>
    /// Decides whether <paramref name="actor"/> may move <paramref name="ticket"/> to
    /// <paramref name="target"/>, checking the state graph first and then the role rules.
    /// </summary>
    public static TransitionResult ValidateTransition(
        Ticket ticket,
        TicketStatus target,
        ActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(actor);

        if (IsTerminal(ticket.Status))
        {
            return TransitionResult.Denied(
                TransitionFailure.TerminalStatus,
                $"Ticket {ticket.Reference} is {ticket.Status} and cannot be changed further.");
        }

        if (target == ticket.Status)
        {
            return TransitionResult.Denied(
                TransitionFailure.IllegalTransition,
                $"Ticket {ticket.Reference} is already {ticket.Status}.");
        }

        if (!AllowedNextStatuses(ticket.Status).Contains(target))
        {
            return TransitionResult.Denied(
                TransitionFailure.IllegalTransition,
                $"A ticket cannot move from {ticket.Status} to {target}.");
        }

        return target switch
        {
            TicketStatus.Cancelled => ValidateCancel(ticket, actor),
            TicketStatus.Triaged => ValidateTriage(ticket, actor),
            TicketStatus.Assigned => ValidateAssign(ticket, actor),
            TicketStatus.InProgress => ValidateStartOrReopen(ticket, actor),
            TicketStatus.OnHold => ValidateHold(ticket, actor),
            TicketStatus.Resolved => ValidateResolve(ticket, actor),
            TicketStatus.Closed => ValidateClose(ticket, actor),
            _ => TransitionResult.Denied(
                TransitionFailure.IllegalTransition,
                $"Unsupported target status {target}.")
        };
    }

    // --- Per-transition rules -------------------------------------------

    /// <summary>Cancellation belongs to the person who raised the ticket, or to the team lead.</summary>
    private static TransitionResult ValidateCancel(Ticket ticket, ActorContext actor)
    {
        if (actor.IsTeamLead)
        {
            return TransitionResult.Allowed;
        }

        if (actor.IsRequester)
        {
            return actor.Owns(ticket)
                ? TransitionResult.Allowed
                : TransitionResult.Denied(
                    TransitionFailure.NotTicketOwner,
                    "A requester may only cancel a ticket they raised.");
        }

        return TransitionResult.Denied(
            TransitionFailure.RoleNotPermitted,
            "Technicians cannot cancel tickets; ask the team lead, or resolve it instead.");
    }

    private static TransitionResult ValidateTriage(Ticket ticket, ActorContext actor) =>
        actor.IsSupportStaff
            ? TransitionResult.Allowed
            : TransitionResult.Denied(
                TransitionFailure.RoleNotPermitted,
                "Only support staff may triage tickets.");

    /// <summary>Assignment requires support staff and an actual assignee on the ticket.</summary>
    private static TransitionResult ValidateAssign(Ticket ticket, ActorContext actor)
    {
        if (!actor.IsSupportStaff)
        {
            return TransitionResult.Denied(
                TransitionFailure.RoleNotPermitted,
                "Only support staff may assign tickets.");
        }

        if (string.IsNullOrWhiteSpace(ticket.AssignedTechnicianId))
        {
            return TransitionResult.Denied(
                TransitionFailure.TechnicianRequired,
                "A technician must be nominated before the ticket can be marked Assigned.");
        }

        return TransitionResult.Allowed;
    }

    /// <summary>
    /// InProgress is reached two ways: a technician starting work, or a reopen after resolution.
    /// The reopen belongs to the requester, so the rules differ.
    /// </summary>
    private static TransitionResult ValidateStartOrReopen(Ticket ticket, ActorContext actor) =>
        ticket.Status == TicketStatus.Resolved
            ? ValidateReopen(ticket, actor)
            : ValidateWorkByAssignee(ticket, actor, "start work on");

    private static TransitionResult ValidateReopen(Ticket ticket, ActorContext actor)
    {
        if (actor.IsTeamLead)
        {
            return TransitionResult.Allowed;
        }

        if (actor.IsRequester)
        {
            return actor.Owns(ticket)
                ? TransitionResult.Allowed
                : TransitionResult.Denied(
                    TransitionFailure.NotTicketOwner,
                    "A requester may only reopen a ticket they raised.");
        }

        return actor.IsAssignedTo(ticket)
            ? TransitionResult.Allowed
            : TransitionResult.Denied(
                TransitionFailure.NotAssignedTechnician,
                "Only the assigned technician, the requester, or the team lead may reopen a ticket.");
    }

    private static TransitionResult ValidateHold(Ticket ticket, ActorContext actor) =>
        ValidateWorkByAssignee(ticket, actor, "place on hold");

    private static TransitionResult ValidateResolve(Ticket ticket, ActorContext actor)
    {
        var permission = ValidateWorkByAssignee(ticket, actor, "resolve");

        if (!permission.IsAllowed)
        {
            return permission;
        }

        return string.IsNullOrWhiteSpace(ticket.ResolutionNotes)
            ? TransitionResult.Denied(
                TransitionFailure.ResolutionNotesRequired,
                "Resolution notes are required so the fix is recorded for the audit trail.")
            : TransitionResult.Allowed;
    }

    /// <summary>Closure is the requester's confirmation that the fix worked; the team lead may force it.</summary>
    private static TransitionResult ValidateClose(Ticket ticket, ActorContext actor)
    {
        if (actor.IsTeamLead)
        {
            return TransitionResult.Allowed;
        }

        if (actor.IsRequester)
        {
            return actor.Owns(ticket)
                ? TransitionResult.Allowed
                : TransitionResult.Denied(
                    TransitionFailure.NotTicketOwner,
                    "A requester may only close a ticket they raised.");
        }

        return TransitionResult.Denied(
            TransitionFailure.RoleNotPermitted,
            "A technician resolves a ticket; the requester or team lead closes it.");
    }

    /// <summary>Shared rule: work transitions belong to the assigned technician or the team lead.</summary>
    private static TransitionResult ValidateWorkByAssignee(
        Ticket ticket,
        ActorContext actor,
        string verb)
    {
        if (actor.IsTeamLead)
        {
            return TransitionResult.Allowed;
        }

        if (!actor.IsSupportStaff)
        {
            return TransitionResult.Denied(
                TransitionFailure.RoleNotPermitted,
                $"Only support staff may {verb} a ticket.");
        }

        return actor.IsAssignedTo(ticket)
            ? TransitionResult.Allowed
            : TransitionResult.Denied(
                TransitionFailure.NotAssignedTechnician,
                $"Only the assigned technician may {verb} this ticket.");
    }
}

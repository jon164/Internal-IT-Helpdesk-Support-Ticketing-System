namespace Helpdesk.Core.Security;

using Helpdesk.Core.Domain;

/// <summary>
/// Who may see and do what.
/// </summary>
/// <remarks>
/// <para>
/// The shared mailbox this system replaces is readable in full by everyone on the team, including two
/// people who left. Requests from HR and Finance routinely contain personal and commercially sensitive
/// material, so the prototype treats access as a domain rule rather than a UI concern: the API asks
/// this class, and the UI merely reflects the answer. Hiding a button is not access control.
/// </para>
/// <para>
/// The central rule is that a ticket marked <see cref="TicketSensitivity.Restricted"/> is invisible to
/// technicians who are not assigned to it. Restricted tickets are therefore triaged by the team lead.
/// </para>
/// </remarks>
public static class TicketAccessPolicy
{
    /// <summary>Whether the actor may see this ticket exists at all.</summary>
    /// <remarks>
    /// Written as an exhaustive switch on the role rather than a chain of early returns with a
    /// fall-through. The earlier form ended with the technician rule as its default, which meant
    /// adding a role to the system silently granted it queue access — the failure mode where a
    /// security control quietly widens because nobody edited it.
    /// </remarks>
    public static bool CanView(Ticket ticket, ActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(actor);

        return actor.Role switch
        {
            UserRole.TeamLead => true,

            UserRole.Requester => actor.Owns(ticket),

            // The whole queue is visible, except restricted tickets they do not hold.
            UserRole.Technician =>
                ticket.Sensitivity != TicketSensitivity.Restricted || actor.IsAssignedTo(ticket),

            _ => false
        };
    }

    /// <summary>
    /// Whether the actor may read the free-text body and the audit trail, as opposed to the queue
    /// summary. Restricted content requires ownership, assignment, or the team lead role.
    /// </summary>
    public static bool CanViewFullDetail(Ticket ticket, ActorContext actor)
    {
        if (!CanView(ticket, actor))
        {
            return false;
        }

        if (ticket.Sensitivity != TicketSensitivity.Restricted)
        {
            return true;
        }

        return actor.IsTeamLead || actor.Owns(ticket) || actor.IsAssignedTo(ticket);
    }

    /// <summary>
    /// Whether the actor may assign the ticket. Technicians may take work they can see; only the team
    /// lead may assign work to somebody else.
    /// </summary>
    public static bool CanAssign(Ticket ticket, ActorContext actor, string technicianId)
    {
        if (!CanView(ticket, actor))
        {
            return false;
        }

        if (actor.IsTeamLead)
        {
            return true;
        }

        if (actor.Role != UserRole.Technician)
        {
            return false;
        }

        // Self-assignment only.
        return string.Equals(technicianId, actor.UserId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the actor may change priority. Restricted to the team lead: priority drives the SLA
    /// clock and therefore the reported figures, so it is not left to individual judgement under load.
    /// This is the rule that fixes inconsistent prioritisation.
    /// </summary>
    public static bool CanChangePriority(Ticket ticket, ActorContext actor) =>
        actor.IsTeamLead && CanView(ticket, actor);

    /// <summary>Whether the actor may add a comment.</summary>
    public static bool CanComment(Ticket ticket, ActorContext actor) =>
        CanViewFullDetail(ticket, actor);

    /// <summary>Whether the actor may see management reporting. Team lead only.</summary>
    public static bool CanViewReports(ActorContext actor) => actor.IsTeamLead;

    /// <summary>
    /// Whether the actor may administer user accounts. Team lead only.
    /// </summary>
    /// <remarks>
    /// The service manager holds both operational and account authority, because on a desk this size
    /// there is nobody else to hold it. That concentration is a recognised weakness rather than an
    /// oversight: the compensating controls are that every account change demands a reason and is
    /// written to an immutable log, and that no account can be used to lock the system.
    /// </remarks>
    public static bool CanAdministerUsers(ActorContext actor) => actor.IsTeamLead;

    /// <summary>
    /// Filters a sequence down to what this actor is entitled to see. Used by the queue and list
    /// endpoints so filtering happens once, server-side.
    /// </summary>
    public static IEnumerable<Ticket> Visible(IEnumerable<Ticket> tickets, ActorContext actor) =>
        tickets.Where(t => CanView(t, actor));
}

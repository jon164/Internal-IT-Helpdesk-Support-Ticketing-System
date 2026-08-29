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
    public static bool CanView(Ticket ticket, ActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.IsTeamLead)
        {
            return true;
        }

        if (actor.IsRequester)
        {
            return actor.Owns(ticket);
        }

        // Technician: the whole queue is visible, except restricted tickets they do not hold.
        return ticket.Sensitivity != TicketSensitivity.Restricted || actor.IsAssignedTo(ticket);
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
    /// Filters a sequence down to what this actor is entitled to see. Used by the queue and list
    /// endpoints so filtering happens once, server-side.
    /// </summary>
    public static IEnumerable<Ticket> Visible(IEnumerable<Ticket> tickets, ActorContext actor) =>
        tickets.Where(t => CanView(t, actor));
}

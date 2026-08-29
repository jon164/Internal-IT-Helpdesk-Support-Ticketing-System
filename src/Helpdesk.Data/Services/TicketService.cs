namespace Helpdesk.Data.Services;

using Helpdesk.Core;
using Helpdesk.Core.Domain;
using Helpdesk.Core.Metrics;
using Helpdesk.Core.Security;
using Helpdesk.Core.Sla;
using Helpdesk.Core.Time;
using Helpdesk.Core.Workflow;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Orchestrates every ticket operation: authorisation, workflow, persistence and audit, in that order.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place that writes to the ticket tables. Centralising it is what makes the audit
/// trail complete by construction — there is no code path that can change a ticket's status without
/// also recording who did it and when, which is precisely the guarantee the current process cannot
/// make.
/// </para>
/// <para>
/// It lives in Helpdesk.Data rather than Helpdesk.Api because it needs the DbContext, and because
/// integration tests exercise it directly against an in-memory provider without starting a web host.
/// </para>
/// </remarks>
public sealed class TicketService
{
    private readonly HelpdeskDbContext _db;
    private readonly SlaCalculator _sla;
    private readonly TicketMetricsCalculator _metrics;
    private readonly IClock _clock;

    public TicketService(
        HelpdeskDbContext db,
        SlaCalculator sla,
        TicketMetricsCalculator metrics,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _sla = sla ?? throw new ArgumentNullException(nameof(sla));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    // -- Reads ------------------------------------------------------------

    /// <summary>
    /// Returns the tickets this actor is entitled to see, most urgent first.
    /// </summary>
    /// <remarks>
    /// Access filtering happens here, server-side, not in the query string and not in the UI. A caller
    /// cannot widen it by editing a request.
    /// </remarks>
    public async Task<IReadOnlyList<Ticket>> GetVisibleAsync(
        ActorContext actor,
        TicketQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= TicketQuery.Default;

        var candidates = _db.Tickets
            .Include(t => t.HoldPeriods)
            .AsNoTracking()
            .AsQueryable();

        if (query.OnlyOpen)
        {
            candidates = candidates.Where(t =>
                t.Status != TicketStatus.Closed && t.Status != TicketStatus.Cancelled);
        }

        if (query.Status is { } status)
        {
            candidates = candidates.Where(t => t.Status == status);
        }

        if (query.Priority is { } priority)
        {
            candidates = candidates.Where(t => t.Priority == priority);
        }

        if (!string.IsNullOrWhiteSpace(query.AssignedTo))
        {
            candidates = candidates.Where(t => t.AssignedTechnicianId == query.AssignedTo);
        }

        if (query.UnassignedOnly)
        {
            candidates = candidates.Where(t => t.AssignedTechnicianId == null);
        }

        var loaded = await candidates
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

        return TicketAccessPolicy.Visible(loaded, actor).ToList();
    }

    /// <summary>
    /// Loads one ticket with its audit trail.
    /// </summary>
    /// <remarks>
    /// A ticket the actor may not see returns NotFound rather than Forbidden, so the response does not
    /// confirm that a restricted ticket exists.
    /// </remarks>
    public async Task<OperationResult<Ticket>> GetByIdAsync(
        int id,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _db.Tickets
            .Include(t => t.Events)
            .Include(t => t.HoldPeriods)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (ticket is null || !TicketAccessPolicy.CanViewFullDetail(ticket, actor))
        {
            return OperationResult<Ticket>.NotFound();
        }

        ticket.Events = ticket.Events.OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToList();
        return OperationResult<Ticket>.Success(ticket);
    }

    /// <summary>Evaluates a ticket against both of its targets.</summary>
    public TicketSlaView EvaluateSla(Ticket ticket) => new(
        _sla.PolicyFor(ticket),
        _sla.EvaluateResponse(ticket, _clock.UtcNow),
        _sla.EvaluateResolution(ticket, _clock.UtcNow));

    /// <summary>Management reporting. Team lead only.</summary>
    public async Task<OperationResult<MetricsSummary>> GetMetricsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (!TicketAccessPolicy.CanViewReports(actor))
        {
            return OperationResult<MetricsSummary>.Forbidden(
                "Management reporting is restricted to the service manager.");
        }

        if (to < from)
        {
            return OperationResult<MetricsSummary>.Invalid(
                "The end of the reporting period cannot precede its start.");
        }

        var tickets = await _db.Tickets
            .Include(t => t.HoldPeriods)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return OperationResult<MetricsSummary>.Success(
            _metrics.Calculate(tickets, from, to, _clock.UtcNow));
    }

    // -- Writes -----------------------------------------------------------

    /// <summary>Raises a new ticket on behalf of the actor.</summary>
    public async Task<OperationResult<Ticket>> CreateAsync(
        CreateTicketRequest request,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = request.Validate();

        if (validation.Count > 0)
        {
            return OperationResult<Ticket>.Invalid(string.Join(" ", validation));
        }

        var now = _clock.UtcNow;

        var requester = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actor.UserId, cancellationToken);

        var ticket = new Ticket
        {
            Reference = "PENDING",
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Category = request.Category.Trim(),
            RequesterId = actor.UserId,
            RequesterName = requester?.DisplayName ?? actor.DisplayName ?? actor.UserId,
            Department = requester?.Department ?? request.Department?.Trim() ?? string.Empty,
            // Requesters propose a priority; the team lead is the only role that can change it later.
            Priority = request.Priority,
            Status = TicketStatus.New,
            CreatedAt = now
        };

        ticket.Sensitivity = DetermineSensitivity(ticket.Department, request.MarkRestricted);

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(cancellationToken);

        // The reference is derived from the identity assigned by the database, so it is unique
        // without a separate sequence table.
        ticket.Reference = Ticket.BuildReference(ticket.Id);

        _db.TicketEvents.Add(TicketEvent.For(
            ticket, actor, TicketEventType.Created, now,
            $"Raised as {ticket.Priority} in category {ticket.Category}.",
            to: TicketStatus.New));

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<Ticket>.Success(ticket);
    }

    /// <summary>
    /// Moves a ticket to a new status, applying every side effect the transition implies.
    /// </summary>
    public async Task<OperationResult<Ticket>> TransitionAsync(
        int ticketId,
        TicketStatus target,
        ActorContext actor,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var ticket = await LoadForWriteAsync(ticketId, cancellationToken);

        if (ticket is null || !TicketAccessPolicy.CanView(ticket, actor))
        {
            return OperationResult<Ticket>.NotFound();
        }

        // Resolution notes are part of the transition, so they must be applied before validation —
        // otherwise resolving would always fail its own precondition.
        var previousNotes = ticket.ResolutionNotes;

        if (target == TicketStatus.Resolved && !string.IsNullOrWhiteSpace(note))
        {
            ticket.ResolutionNotes = note.Trim();
        }

        var check = TicketWorkflow.ValidateTransition(ticket, target, actor);

        if (!check.IsAllowed)
        {
            ticket.ResolutionNotes = previousNotes;

            return check.Failure switch
            {
                TransitionFailure.RoleNotPermitted or
                TransitionFailure.NotTicketOwner or
                TransitionFailure.NotAssignedTechnician =>
                    OperationResult<Ticket>.Forbidden(check.Reason),
                TransitionFailure.ResolutionNotesRequired or
                TransitionFailure.TechnicianRequired =>
                    OperationResult<Ticket>.Invalid(check.Reason),
                _ => OperationResult<Ticket>.Conflict(check.Reason)
            };
        }

        var now = _clock.UtcNow;
        var from = ticket.Status;

        ApplyTransitionSideEffects(ticket, from, target, actor, now, note);

        ticket.Status = target;

        _db.TicketEvents.Add(TicketEvent.For(
            ticket, actor, TicketEventType.StatusChanged, now,
            string.IsNullOrWhiteSpace(note) ? $"{from} -> {target}." : $"{from} -> {target}. {note.Trim()}",
            from, target));

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<Ticket>.Success(ticket);
    }

    /// <summary>
    /// Assigns a technician. If the ticket is sitting in Triaged it also advances it to Assigned,
    /// because leaving it behind would be a pointless second click during triage.
    /// </summary>
    public async Task<OperationResult<Ticket>> AssignAsync(
        int ticketId,
        string technicianId,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(technicianId))
        {
            return OperationResult<Ticket>.Invalid("A technician must be nominated.");
        }

        var ticket = await LoadForWriteAsync(ticketId, cancellationToken);

        if (ticket is null || !TicketAccessPolicy.CanView(ticket, actor))
        {
            return OperationResult<Ticket>.NotFound();
        }

        if (!TicketAccessPolicy.CanAssign(ticket, actor, technicianId))
        {
            return OperationResult<Ticket>.Forbidden(
                "A technician may only take work for themselves; only the service manager may assign "
                + "work to someone else.");
        }

        if (TicketWorkflow.IsTerminal(ticket.Status))
        {
            return OperationResult<Ticket>.Conflict(
                $"Ticket {ticket.Reference} is {ticket.Status} and cannot be reassigned.");
        }

        var technician = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == technicianId, cancellationToken);

        if (technician is null || technician.Role == UserRole.Requester)
        {
            return OperationResult<Ticket>.Invalid(
                "The nominated user does not exist or is not a member of support staff.");
        }

        var now = _clock.UtcNow;
        var previous = ticket.AssignedTechnicianName;

        ticket.AssignedTechnicianId = technician.Id;
        ticket.AssignedTechnicianName = technician.DisplayName;

        _db.TicketEvents.Add(TicketEvent.For(
            ticket, actor, TicketEventType.Assigned, now,
            previous is null
                ? $"Assigned to {technician.DisplayName}."
                : $"Reassigned from {previous} to {technician.DisplayName}."));

        if (ticket.Status == TicketStatus.Triaged)
        {
            _db.TicketEvents.Add(TicketEvent.For(
                ticket, actor, TicketEventType.StatusChanged, now,
                $"{TicketStatus.Triaged} -> {TicketStatus.Assigned}.",
                TicketStatus.Triaged, TicketStatus.Assigned));

            ticket.Status = TicketStatus.Assigned;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<Ticket>.Success(ticket);
    }

    /// <summary>
    /// Changes priority. Team lead only — priority drives the SLA clock and therefore every reported
    /// figure, so it is not left to individual judgement under load.
    /// </summary>
    public async Task<OperationResult<Ticket>> ChangePriorityAsync(
        int ticketId,
        TicketPriority priority,
        string justification,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var ticket = await LoadForWriteAsync(ticketId, cancellationToken);

        if (ticket is null || !TicketAccessPolicy.CanView(ticket, actor))
        {
            return OperationResult<Ticket>.NotFound();
        }

        if (!TicketAccessPolicy.CanChangePriority(ticket, actor))
        {
            return OperationResult<Ticket>.Forbidden(
                "Only the service manager may change a ticket's priority.");
        }

        if (ticket.Priority == priority)
        {
            return OperationResult<Ticket>.Success(ticket);
        }

        if (string.IsNullOrWhiteSpace(justification))
        {
            return OperationResult<Ticket>.Invalid(
                "A reason is required when changing priority, so the change is auditable.");
        }

        var now = _clock.UtcNow;
        var from = ticket.Priority;
        ticket.Priority = priority;

        _db.TicketEvents.Add(TicketEvent.For(
            ticket, actor, TicketEventType.PriorityChanged, now,
            $"Priority {from} -> {priority}. {justification.Trim()}"));

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<Ticket>.Success(ticket);
    }

    /// <summary>
    /// Adds a comment. A comment from support staff counts as first contact, so this is one of the two
    /// ways the response clock stops.
    /// </summary>
    public async Task<OperationResult<Ticket>> AddCommentAsync(
        int ticketId,
        string comment,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return OperationResult<Ticket>.Invalid("A comment cannot be empty.");
        }

        var ticket = await LoadForWriteAsync(ticketId, cancellationToken);

        if (ticket is null || !TicketAccessPolicy.CanComment(ticket, actor))
        {
            return OperationResult<Ticket>.NotFound();
        }

        var now = _clock.UtcNow;

        _db.TicketEvents.Add(TicketEvent.For(
            ticket, actor, TicketEventType.CommentAdded, now, comment.Trim()));

        if (actor.IsSupportStaff && ticket.FirstRespondedAt is null)
        {
            ticket.FirstRespondedAt = now;

            _db.TicketEvents.Add(TicketEvent.For(
                ticket, actor, TicketEventType.FirstResponseRecorded, now,
                "First response recorded on support comment."));
        }

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<Ticket>.Success(ticket);
    }

    // -- Internals --------------------------------------------------------

    private Task<Ticket?> LoadForWriteAsync(int id, CancellationToken cancellationToken) =>
        _db.Tickets
            .Include(t => t.HoldPeriods)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    /// <summary>
    /// Applies the bookkeeping a transition implies: response timestamps, hold windows, resolution and
    /// closure instants, and reopen cleanup.
    /// </summary>
    private void ApplyTransitionSideEffects(
        Ticket ticket,
        TicketStatus from,
        TicketStatus target,
        ActorContext actor,
        DateTimeOffset now,
        string? note)
    {
        switch (target)
        {
            case TicketStatus.InProgress when from == TicketStatus.Resolved:
                // Reopened: the original resolution no longer stands, so the resolution clock restarts
                // from the time already consumed rather than being credited as met.
                ticket.ResolvedAt = null;
                ticket.ResolutionNotes = null;

                _db.TicketEvents.Add(TicketEvent.For(
                    ticket, actor, TicketEventType.Reopened, now,
                    "Reopened; the previous resolution did not hold."));
                break;

            case TicketStatus.InProgress:
                if (from == TicketStatus.OnHold)
                {
                    CloseOpenHold(ticket, actor, now);
                }

                RecordFirstResponseIfNeeded(ticket, actor, now);
                break;

            case TicketStatus.OnHold:
                ticket.HoldPeriods.Add(new HoldPeriod
                {
                    TicketId = ticket.Id,
                    StartedAt = now,
                    Reason = string.IsNullOrWhiteSpace(note) ? "Awaiting requester." : note.Trim()
                });

                _db.TicketEvents.Add(TicketEvent.For(
                    ticket, actor, TicketEventType.HoldStarted, now,
                    "Resolution clock suspended."));
                break;

            case TicketStatus.Resolved:
                // A ticket resolved without any earlier contact was still responded to at that moment.
                RecordFirstResponseIfNeeded(ticket, actor, now);
                CloseOpenHold(ticket, actor, now);
                ticket.ResolvedAt = now;
                break;

            case TicketStatus.Closed:
                ticket.ClosedAt = now;
                break;

            case TicketStatus.Cancelled:
                CloseOpenHold(ticket, actor, now);
                ticket.ClosedAt = now;
                break;
        }
    }

    private void RecordFirstResponseIfNeeded(Ticket ticket, ActorContext actor, DateTimeOffset now)
    {
        if (ticket.FirstRespondedAt is not null)
        {
            return;
        }

        ticket.FirstRespondedAt = now;

        _db.TicketEvents.Add(TicketEvent.For(
            ticket, actor, TicketEventType.FirstResponseRecorded, now,
            "First response recorded on starting work."));
    }

    private void CloseOpenHold(Ticket ticket, ActorContext actor, DateTimeOffset now)
    {
        var open = ticket.HoldPeriods.FirstOrDefault(h => h.EndedAt is null);

        if (open is null)
        {
            return;
        }

        open.EndedAt = now;

        _db.TicketEvents.Add(TicketEvent.For(
            ticket, actor, TicketEventType.HoldEnded, now,
            "Resolution clock resumed."));
    }

    /// <summary>
    /// Requests from departments that routinely handle personal or commercially sensitive material are
    /// restricted by default. A requester may also mark any request restricted; nobody can un-restrict
    /// a departmental default by omitting the flag.
    /// </summary>
    private static TicketSensitivity DetermineSensitivity(string department, bool markRestricted)
    {
        if (markRestricted)
        {
            return TicketSensitivity.Restricted;
        }

        return RestrictedDepartments.Any(d =>
            department.Contains(d, StringComparison.OrdinalIgnoreCase))
            ? TicketSensitivity.Restricted
            : TicketSensitivity.Standard;
    }

    private static readonly string[] RestrictedDepartments =
        ["Human Resources", "People", "Finance", "Payroll", "Legal"];
}

/// <summary>Filter options for the queue view.</summary>
public sealed record TicketQuery
{
    public static TicketQuery Default { get; } = new();

    public bool OnlyOpen { get; init; }

    public TicketStatus? Status { get; init; }

    public TicketPriority? Priority { get; init; }

    public string? AssignedTo { get; init; }

    public bool UnassignedOnly { get; init; }

    /// <summary>Capped so a malformed request cannot pull the entire table.</summary>
    public int Take { get; init; } = 200;
}

/// <summary>A ticket's policy and its position against both targets.</summary>
public sealed record TicketSlaView(SlaPolicy Policy, SlaStatus Response, SlaStatus Resolution);

/// <summary>Inbound payload for raising a ticket, with its own validation.</summary>
public sealed record CreateTicketRequest
{
    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string? Department { get; init; }

    public TicketPriority Priority { get; init; } = TicketPriority.Standard;

    public bool MarkRestricted { get; init; }

    /// <summary>
    /// Returns every validation problem rather than the first, so the form can show them all at once
    /// instead of making the user resubmit repeatedly.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Title))
        {
            problems.Add("A short summary is required.");
        }
        else if (Title.Trim().Length < 5)
        {
            problems.Add("The summary must be at least 5 characters so the queue is readable.");
        }
        else if (Title.Trim().Length > 200)
        {
            problems.Add("The summary must be 200 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            problems.Add("A description is required.");
        }
        else if (Description.Trim().Length > 4000)
        {
            problems.Add("The description must be 4000 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(Category))
        {
            problems.Add("A category is required so requests can be grouped for analysis.");
        }

        if (!Enum.IsDefined(Priority))
        {
            problems.Add("The selected priority is not recognised.");
        }

        return problems;
    }
}

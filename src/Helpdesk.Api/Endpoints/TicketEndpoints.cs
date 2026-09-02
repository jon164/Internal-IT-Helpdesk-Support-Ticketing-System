namespace Helpdesk.Api.Endpoints;

using Helpdesk.Api.Contracts;
using Helpdesk.Api.Security;
using Helpdesk.Core;
using Helpdesk.Core.Domain;
using Helpdesk.Core.Security;
using Helpdesk.Core.Sla;
using Helpdesk.Core.Workflow;
using Helpdesk.Data;
using Helpdesk.Data.Services;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// HTTP surface for ticket operations.
/// </summary>
/// <remarks>
/// These handlers do routing, model binding and status-code mapping, and nothing else. Every decision
/// about whether an operation is permitted belongs to <see cref="TicketService"/> and the domain
/// policies it consults, so the rules cannot drift between the API and the tests that cover them.
/// </remarks>
public static class TicketEndpoints
{
    public static void MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tickets").WithTags("Tickets");

        group.MapGet("/", GetQueue)
             .WithSummary("The tickets the caller is entitled to see, most urgent first.");

        group.MapGet("/{id:int}", GetById)
             .WithSummary("One ticket with its full audit trail.");

        group.MapPost("/", Create)
             .WithSummary("Raise a new ticket.");

        group.MapPost("/{id:int}/transition", Transition)
             .WithSummary("Move a ticket to a new status.");

        group.MapPost("/{id:int}/assign", Assign)
             .WithSummary("Assign a technician.");

        group.MapPost("/{id:int}/priority", ChangePriority)
             .WithSummary("Change priority. Service manager only.");

        group.MapPost("/{id:int}/comments", AddComment)
             .WithSummary("Add a comment. A comment from support staff records first response.");
    }

    private static async Task<IResult> GetQueue(
        HttpContext context,
        TicketService service,
        string? status,
        string? priority,
        string? assignedTo,
        bool? unassignedOnly,
        bool? openOnly,
        bool? needsWorkOnly,
        CancellationToken cancellationToken)
    {
        if (ActorMiddleware.Current(context) is not { } actor)
        {
            return Unauthorised();
        }

        if (!TryParseEnum<TicketStatus>(status, out var parsedStatus, out var statusError))
        {
            return Results.BadRequest(new ProblemDto("Invalid", statusError));
        }

        if (!TryParseEnum<TicketPriority>(priority, out var parsedPriority, out var priorityError))
        {
            return Results.BadRequest(new ProblemDto("Invalid", priorityError));
        }

        var query = new TicketQuery
        {
            OnlyOpen = openOnly ?? false,
            OnlyRequiringWork = needsWorkOnly ?? false,
            Status = parsedStatus,
            Priority = parsedPriority,
            AssignedTo = assignedTo,
            UnassignedOnly = unassignedOnly ?? false
        };

        var tickets = await service.GetVisibleAsync(actor, query, cancellationToken);

        return Results.Ok(tickets
            .Select(t => TicketSummaryDto.From(t, service.EvaluateSla(t)))
            .ToList());
    }

    private static async Task<IResult> GetById(
        HttpContext context,
        TicketService service,
        int id,
        CancellationToken cancellationToken)
    {
        if (ActorMiddleware.Current(context) is not { } actor)
        {
            return Unauthorised();
        }

        var result = await service.GetByIdAsync(id, actor, cancellationToken);

        if (!result.Succeeded)
        {
            return Map(result.Error, result.Message);
        }

        var ticket = result.Require();
        var sla = service.EvaluateSla(ticket);

        var permissions = new TicketPermissionsDto(
            CanComment: TicketAccessPolicy.CanComment(ticket, actor),
            CanChangePriority: TicketAccessPolicy.CanChangePriority(ticket, actor),
            CanAssignToSelf: TicketAccessPolicy.CanAssign(ticket, actor, actor.UserId),
            CanAssignToOthers: actor.IsTeamLead && !TicketWorkflow.IsTerminal(ticket.Status),
            AllowedNextStatuses: TicketWorkflow
                .AllowedNextStatuses(ticket.Status)
                .Where(next => TicketWorkflow.ValidateTransition(ticket, next, actor).IsAllowed
                               || RequiresInputRatherThanPermission(ticket, next, actor))
                .Select(next => next.ToString())
                .ToList());

        return Results.Ok(new TicketDetailDto(
            TicketSummaryDto.From(ticket, sla),
            ticket.Description,
            ticket.ResolutionNotes,
            ticket.FirstRespondedAt,
            ticket.ResolvedAt,
            ticket.ClosedAt,
            SlaPolicyDto.From(sla.Policy),
            ticket.HoldPeriods.Select(HoldPeriodDto.From).ToList(),
            ticket.Events.Select(TicketEventDto.From).ToList(),
            permissions));
    }

    /// <summary>
    /// Resolving is refused until notes are supplied, but the caller is still allowed to do it — the
    /// UI needs to offer the action so they can type the notes. This distinguishes "you may not" from
    /// "you have not filled this in yet".
    /// </summary>
    private static bool RequiresInputRatherThanPermission(
        Ticket ticket,
        TicketStatus next,
        ActorContext actor)
    {
        var check = TicketWorkflow.ValidateTransition(ticket, next, actor);

        return check.Failure is TransitionFailure.ResolutionNotesRequired
            or TransitionFailure.TechnicianRequired;
    }

    private static async Task<IResult> Create(
        HttpContext context,
        TicketService service,
        CreateTicketRequest request,
        CancellationToken cancellationToken)
    {
        if (ActorMiddleware.Current(context) is not { } actor)
        {
            return Unauthorised();
        }

        var result = await service.CreateAsync(request, actor, cancellationToken);

        if (!result.Succeeded)
        {
            return Map(result.Error, result.Message);
        }

        var ticket = result.Require();

        return Results.Created(
            $"/api/tickets/{ticket.Id}",
            TicketSummaryDto.From(ticket, service.EvaluateSla(ticket)));
    }

    private static async Task<IResult> Transition(
        HttpContext context,
        TicketService service,
        int id,
        TransitionRequestDto request,
        CancellationToken cancellationToken)
    {
        if (ActorMiddleware.Current(context) is not { } actor)
        {
            return Unauthorised();
        }

        if (!Enum.TryParse<TicketStatus>(request.Status, ignoreCase: true, out var target))
        {
            return Results.BadRequest(new ProblemDto(
                "Invalid", $"'{request.Status}' is not a known status."));
        }

        var result = await service.TransitionAsync(id, target, actor, request.Note, cancellationToken);

        return result.Succeeded
            ? Results.Ok(TicketSummaryDto.From(result.Require(), service.EvaluateSla(result.Require())))
            : Map(result.Error, result.Message);
    }

    private static async Task<IResult> Assign(
        HttpContext context,
        TicketService service,
        int id,
        AssignRequestDto request,
        CancellationToken cancellationToken)
    {
        if (ActorMiddleware.Current(context) is not { } actor)
        {
            return Unauthorised();
        }

        var result = await service.AssignAsync(id, request.TechnicianId, actor, cancellationToken);

        return result.Succeeded
            ? Results.Ok(TicketSummaryDto.From(result.Require(), service.EvaluateSla(result.Require())))
            : Map(result.Error, result.Message);
    }

    private static async Task<IResult> ChangePriority(
        HttpContext context,
        TicketService service,
        int id,
        PriorityRequestDto request,
        CancellationToken cancellationToken)
    {
        if (ActorMiddleware.Current(context) is not { } actor)
        {
            return Unauthorised();
        }

        if (!Enum.TryParse<TicketPriority>(request.Priority, ignoreCase: true, out var priority))
        {
            return Results.BadRequest(new ProblemDto(
                "Invalid", $"'{request.Priority}' is not a known priority."));
        }

        var result = await service.ChangePriorityAsync(
            id, priority, request.Justification, actor, cancellationToken);

        return result.Succeeded
            ? Results.Ok(TicketSummaryDto.From(result.Require(), service.EvaluateSla(result.Require())))
            : Map(result.Error, result.Message);
    }

    private static async Task<IResult> AddComment(
        HttpContext context,
        TicketService service,
        int id,
        CommentRequestDto request,
        CancellationToken cancellationToken)
    {
        if (ActorMiddleware.Current(context) is not { } actor)
        {
            return Unauthorised();
        }

        var result = await service.AddCommentAsync(id, request.Comment, actor, cancellationToken);

        return result.Succeeded
            ? Results.Ok(TicketSummaryDto.From(result.Require(), service.EvaluateSla(result.Require())))
            : Map(result.Error, result.Message);
    }

    // --- Helpers ---------------------------------------------------------

    private static bool TryParseEnum<T>(string? raw, out T? value, out string error)
        where T : struct, Enum
    {
        value = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (Enum.TryParse<T>(raw, ignoreCase: true, out var parsed))
        {
            value = parsed;
            return true;
        }

        error = $"'{raw}' is not a valid {typeof(T).Name}. Expected one of: "
                + string.Join(", ", Enum.GetNames<T>()) + ".";
        return false;
    }

    private static IResult Unauthorised() => Results.Json(
        new ProblemDto("Unauthenticated",
            $"Supply a known user in the {ActorMiddleware.HeaderName} header."),
        statusCode: StatusCodes.Status401Unauthorized);

    /// <summary>Maps a domain refusal onto the HTTP status code that means the same thing.</summary>
    private static IResult Map(OperationError error, string message) => error switch
    {
        OperationError.NotFound => Results.NotFound(new ProblemDto("NotFound", message)),
        OperationError.Forbidden => Results.Json(
            new ProblemDto("Forbidden", message), statusCode: StatusCodes.Status403Forbidden),
        OperationError.Conflict => Results.Conflict(new ProblemDto("Conflict", message)),
        _ => Results.BadRequest(new ProblemDto("Invalid", message))
    };
}

/// <summary>Reference data and reporting.</summary>
public static class ReferenceEndpoints
{
    public static void MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/users", async (HelpdeskDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Users
                .AsNoTracking()
                .Where(u => u.IsActive)
                .OrderBy(u => u.Role)
                .ThenBy(u => u.DisplayName)
                .Select(u => UserDto.From(u))
                .ToListAsync(ct)))
           .WithTags("Reference")
           .WithSummary("Accounts available in the prototype's role switcher.");

        app.MapGet("/api/reference/priorities", (SlaPolicySet policies) =>
            Results.Ok(policies.Policies
                .OrderBy(p => p.Priority)
                .Select(SlaPolicyDto.From)
                .ToList()))
           .WithTags("Reference")
           .WithSummary("Priority bands and their configured targets.");

        // The catalogue, not the categories currently in use. Deriving it from existing tickets left
        // the first requester on an empty system with nothing to choose from.
        app.MapGet("/api/reference/categories", () => Results.Ok(TicketCategories.All))
           .WithTags("Reference")
           .WithSummary("The categories a request may be filed under.");

        app.MapGet("/api/reports/summary", async (
                HttpContext context,
                TicketService service,
                DateTimeOffset? from,
                DateTimeOffset? to,
                CancellationToken ct) =>
            {
                if (ActorMiddleware.Current(context) is not { } actor)
                {
                    return Results.Json(
                        new ProblemDto("Unauthenticated",
                            $"Supply a known user in the {ActorMiddleware.HeaderName} header."),
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                var end = to ?? DateTimeOffset.UtcNow;
                var start = from ?? end.AddDays(-30);

                var result = await service.GetMetricsAsync(start, end, actor, ct);

                if (result.Succeeded)
                {
                    return Results.Ok(MetricsDto.FromSummary(result.Require()));
                }

                return result.Error == OperationError.Forbidden
                    ? Results.Json(new ProblemDto("Forbidden", result.Message),
                        statusCode: StatusCodes.Status403Forbidden)
                    : Results.BadRequest(new ProblemDto("Invalid", result.Message));
            })
           .WithTags("Reports")
           .WithSummary("Volume, throughput and SLA attainment. Service manager only.");

        app.MapGet("/api/reports/backlog", async (
                HttpContext context,
                TicketService service,
                int? weeks,
                CancellationToken ct) =>
            {
                if (ActorMiddleware.Current(context) is not { } actor)
                {
                    return Results.Json(
                        new ProblemDto("Unauthenticated",
                            $"Supply a known user in the {ActorMiddleware.HeaderName} header."),
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                var periods = weeks ?? 12;

                if (periods is < 1 or > 52)
                {
                    return Results.BadRequest(new ProblemDto(
                        "Invalid", "The number of weeks must be between 1 and 52."));
                }

                var to = DateTimeOffset.UtcNow;
                var from = to.AddDays(-7 * periods);

                var result = await service.GetBacklogAsync(from, to, periods, actor, ct);

                if (result.Succeeded)
                {
                    return Results.Ok(BacklogReportDto.FromReport(result.Require()));
                }

                return result.Error == OperationError.Forbidden
                    ? Results.Json(new ProblemDto("Forbidden", result.Message),
                        statusCode: StatusCodes.Status403Forbidden)
                    : Results.BadRequest(new ProblemDto("Invalid", result.Message));
            })
           .WithTags("Reports")
           .WithSummary(
                "Unresolved queue movement and ageing over the last N weeks. Service manager only.");
    }
}

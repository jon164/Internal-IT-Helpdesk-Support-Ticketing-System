using Helpdesk.Api.Models;
using Helpdesk.Api.Services;

namespace Helpdesk.Api.Endpoints;

public static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/technicians", async (TicketService service) =>
            Results.Ok((await service.GetActiveTechniciansAsync()).Select(TicketMapper.ToDto)));

        api.MapGet("/technicians/{technicianId:guid}/tickets", async (Guid technicianId, TicketService service) =>
        {
            var tickets = await service.GetAssignedTicketsAsync(technicianId);
            return Results.Ok(tickets.Select(TicketMapper.ToSummaryDto));
        });

        api.MapGet("/tickets/unassigned", async (TicketService service) =>
            Results.Ok((await service.GetUnassignedTicketsAsync()).Select(TicketMapper.ToSummaryDto)));

        api.MapGet("/tickets/{ticketId:guid}", async (Guid ticketId, TicketService service) =>
            await RunAsync(async () =>
            {
                var ticket = await service.GetTicketAsync(ticketId);
                var inbound = await service.GetInboundLinksAsync(ticketId);
                return Results.Ok(TicketMapper.ToDetailDto(ticket, inbound));
            }));

        api.MapPost("/tickets/{ticketId:guid}/claim", async (Guid ticketId, ClaimRequest request, TicketService service) =>
            await RunAsync(async () =>
            {
                var ticket = await service.ClaimAsync(ticketId, request.TechnicianId, DateTime.UtcNow);
                var inbound = await service.GetInboundLinksAsync(ticketId);
                return Results.Ok(TicketMapper.ToDetailDto(ticket, inbound));
            }));

        api.MapPost("/tickets/{ticketId:guid}/reassign", async (Guid ticketId, AssignRequest request, TicketService service) =>
            await RunAsync(async () =>
            {
                var ticket = await service.ReassignAsync(ticketId, request.TechnicianId, DateTime.UtcNow);
                var inbound = await service.GetInboundLinksAsync(ticketId);
                return Results.Ok(TicketMapper.ToDetailDto(ticket, inbound));
            }));

        api.MapPost("/tickets/{ticketId:guid}/status", async (Guid ticketId, StatusChangeRequest request, TicketService service) =>
            await RunAsync(async () =>
            {
                var ticket = await service.ChangeStatusAsync(ticketId, request.NewStatus, request.ChangedBy, request.Comment, DateTime.UtcNow);
                var inbound = await service.GetInboundLinksAsync(ticketId);
                return Results.Ok(TicketMapper.ToDetailDto(ticket, inbound));
            }));

        api.MapPost("/tickets/{ticketId:guid}/request-info", async (Guid ticketId, StatusChangeRequest request, TicketService service) =>
            await RunAsync(async () =>
            {
                var ticket = await service.RequestInfoAsync(ticketId, request.ChangedBy, DateTime.UtcNow);
                var inbound = await service.GetInboundLinksAsync(ticketId);
                return Results.Ok(TicketMapper.ToDetailDto(ticket, inbound));
            }));

        api.MapPost("/tickets/{ticketId:guid}/notes", async (Guid ticketId, NoteRequest request, TicketService service) =>
            await RunAsync(async () =>
            {
                var ticket = await service.AddNoteAsync(ticketId, request.AuthorId, request.AuthorName, request.Content, request.IsInternal, DateTime.UtcNow);
                var inbound = await service.GetInboundLinksAsync(ticketId);
                return Results.Ok(TicketMapper.ToDetailDto(ticket, inbound));
            }));

        api.MapPost("/tickets/{ticketId:guid}/employee-comment", async (Guid ticketId, EmployeeCommentRequest request, TicketService service) =>
            await RunAsync(async () =>
            {
                var ticket = await service.EmployeeCommentAsync(ticketId, request.AuthorId, request.AuthorName, request.Content, DateTime.UtcNow);
                var inbound = await service.GetInboundLinksAsync(ticketId);
                return Results.Ok(TicketMapper.ToDetailDto(ticket, inbound));
            }));

        api.MapPost("/tickets/{ticketId:guid}/links", async (Guid ticketId, LinkRequest request, TicketService service) =>
            await RunAsync(async () =>
            {
                var ticket = await service.LinkAsync(ticketId, request.TargetTicketId, request.LinkType, request.ChangedBy, DateTime.UtcNow);
                var inbound = await service.GetInboundLinksAsync(ticketId);
                return Results.Ok(TicketMapper.ToDetailDto(ticket, inbound));
            }));

        api.MapPost("/tickets/{ticketId:guid}/close", async (Guid ticketId, CloseRequest request, TicketService service) =>
            await RunAsync(async () =>
            {
                var ticket = await service.CloseAsync(ticketId, request.ChangedBy, request.Comment, DateTime.UtcNow);
                var inbound = await service.GetInboundLinksAsync(ticketId);
                return Results.Ok(TicketMapper.ToDetailDto(ticket, inbound));
            }));

        return app;
    }

    private static async Task<IResult> RunAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ClaimConflictException ex)
        {
            return Results.Problem(
                title: "Conflict",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["ownerId"] = ex.OwnerId, ["ownerName"] = ex.OwnerName });
        }
        catch (ReassignValidationException ex)
        {
            return Results.Problem(
                title: "Invalid reassignment",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["availableTechnicians"] = ex.AvailableTechnicians });
        }
        catch (NotFoundException ex)
        {
            return Results.Problem(title: "Not found", detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (DomainValidationException ex)
        {
            return Results.Problem(title: "Invalid request", detail: ex.Message, statusCode: ex.StatusCode);
        }
    }
}

using System.Text.Json;
using Helpdesk.Api.Models;
using Helpdesk.Api.Services;
using Helpdesk.Core.Domain;
using Helpdesk.Data;
using Microsoft.EntityFrameworkCore;

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

        api.MapGet("/tickets", async (HelpdeskDbContext db, string? sort, CancellationToken ct) =>
        {
            IQueryable<Ticket> query = db.Tickets
                .Include(t => t.AssignedTechnician)
                .AsNoTracking();

            query = sort switch
            {
                "oldest" => query.OrderBy(t => t.CreatedAt),
                "priority-age" => query.OrderByDescending(t => t.Priority).ThenBy(t => t.CreatedAt),
                _ => query.OrderByDescending(t => t.CreatedAt),
            };

            var tickets = await query.ToListAsync(ct);
            return Results.Ok(tickets.Select(MapLegacyTicket).ToList());
        });

        api.MapPost("/tickets", async (HttpContext context, HelpdeskDbContext db, CancellationToken ct) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
            {
                return Results.BadRequest(new { message = "Ticket payload is required." });
            }

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;

            var description = GetString(root, "description") ?? "Support request";
            var title = GetString(root, "title") ?? (description.Length > 80 ? description[..77] + "..." : description);
            var priority = ParsePriority(GetString(root, "priority") ?? "Medium");
            var type = ParseType(GetString(root, "systemType") ?? "Incident");
            var status = TicketStatus.New;
            var submitterName = GetString(root, "reporterName") ?? "Airport staff";
            var submitterEmail = GetString(root, "reporterEmail") ?? "airport.staff@example.com";

            var ticket = new Ticket
            {
                Title = title,
                Description = description,
                Status = status,
                Priority = priority,
                Type = type,
                SubmitterName = submitterName,
                SubmitterEmail = submitterEmail,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                AssignedTechnicianId = null,
            };

            await db.Tickets.AddAsync(ticket, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "Ticket created.", ticket = MapLegacyTicket(ticket) });
        });

        api.MapPut("/tickets/{ticketId:int}", async (int ticketId, HttpContext context, HelpdeskDbContext db, CancellationToken ct) =>
        {
            var ticket = await FindTicketByLegacyIdAsync(db, ticketId, ct);
            if (ticket is null)
            {
                return Results.NotFound(new { message = "Ticket not found." });
            }

            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var json = JsonDocument.Parse(body);
                var root = json.RootElement;

                if (root.TryGetProperty("status", out var statusValue))
                {
                    var statusText = statusValue.GetString();
                    if (!string.IsNullOrWhiteSpace(statusText))
                    {
                        ticket.Status = ParseStatus(statusText);
                    }
                }

                if (root.TryGetProperty("assignee", out var assigneeValue))
                {
                    var assignee = assigneeValue.GetString();
                    if (!string.IsNullOrWhiteSpace(assignee) && assignee != "Unassigned")
                    {
                        var technician = await db.Technicians.FirstOrDefaultAsync(t => t.Name == assignee, ct);
                        ticket.AssignedTechnicianId = technician?.Id;
                        ticket.AssignedTechnician = technician;
                    }
                    else
                    {
                        ticket.AssignedTechnicianId = null;
                        ticket.AssignedTechnician = null;
                    }
                }

                if (root.TryGetProperty("workaround", out var workaroundValue) && workaroundValue.ValueKind == JsonValueKind.String)
                {
                    // Legacy UI workaround is informational and stays in the ticket description when present.
                    if (!string.IsNullOrWhiteSpace(workaroundValue.GetString()))
                    {
                        ticket.Description = string.IsNullOrWhiteSpace(ticket.Description)
                            ? workaroundValue.GetString()!
                            : ticket.Description + Environment.NewLine + workaroundValue.GetString();
                    }
                }
            }

            ticket.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(MapLegacyTicket(ticket));
        });

        api.MapGet("/tickets/{ticketId:int}/notes", async (int ticketId, HelpdeskDbContext db, CancellationToken ct) =>
        {
            var ticket = await FindTicketByLegacyIdAsync(db, ticketId, ct);
            if (ticket is null)
            {
                return Results.NotFound(new { message = "Ticket not found." });
            }

            var notes = await db.TicketNotes
                .Where(n => n.TicketId == ticket.Id)
                .OrderBy(n => n.CreatedAt)
                .ToListAsync(ct);

            return Results.Ok(notes.Select(n => new
            {
                id = Math.Abs(n.Id.GetHashCode()),
                ticketId = Math.Abs(ticket.Id.GetHashCode()),
                author = n.AuthorName,
                body = n.Content,
                createdAtUtc = n.CreatedAt,
            }));
        });

        api.MapPost("/tickets/{ticketId:int}/notes", async (int ticketId, HttpContext context, HelpdeskDbContext db, CancellationToken ct) =>
        {
            var ticket = await FindTicketByLegacyIdAsync(db, ticketId, ct);
            if (ticket is null)
            {
                return Results.NotFound(new { message = "Ticket not found." });
            }

            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var author = GetString(root, "author") ?? "System";
            var noteBody = GetString(root, "body") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(noteBody))
            {
                return Results.BadRequest(new { message = "Note cannot be blank." });
            }

            var note = TicketNote.Create(ticket.Id, author, author, noteBody, false, DateTime.UtcNow);
            db.Add(note);
            ticket.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                id = Math.Abs(note.Id.GetHashCode()),
                ticketId = Math.Abs(ticket.Id.GetHashCode()),
                author = note.AuthorName,
                body = note.Content,
                createdAtUtc = note.CreatedAt,
            });
        });

        api.MapPost("/tickets/{ticketId:int}/claim", async (int ticketId, HttpContext context, HelpdeskDbContext db, CancellationToken ct) =>
        {
            var ticket = await FindTicketByLegacyIdAsync(db, ticketId, ct);
            if (ticket is null)
            {
                return Results.NotFound(new { message = "Ticket not found." });
            }

            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var technicianName = "Alex Morgan";
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var json = JsonDocument.Parse(body);
                technicianName = GetString(json.RootElement, "technicianName") ?? technicianName;
            }

            var technician = await db.Technicians.FirstOrDefaultAsync(t => t.Name == technicianName || t.Email == technicianName, ct)
                ?? await db.Technicians.FirstOrDefaultAsync(t => t.IsActive, ct);

            if (technician is null)
            {
                return Results.BadRequest(new { message = "No active technician is available." });
            }

            ticket.AssignedTechnicianId = technician.Id;
            ticket.AssignedTechnician = technician;
            ticket.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(MapLegacyTicket(ticket));
        });

        api.MapPost("/tickets/{ticketId:int}/reassign", async (int ticketId, HttpContext context, HelpdeskDbContext db, CancellationToken ct) =>
        {
            var ticket = await FindTicketByLegacyIdAsync(db, ticketId, ct);
            if (ticket is null)
            {
                return Results.NotFound(new { message = "Ticket not found." });
            }

            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var assignee = "Unassigned";
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var json = JsonDocument.Parse(body);
                assignee = GetString(json.RootElement, "assignee") ?? assignee;
            }

            var technician = await db.Technicians.FirstOrDefaultAsync(t => t.Name == assignee, ct);
            ticket.AssignedTechnician = technician;
            ticket.AssignedTechnicianId = technician?.Id;
            ticket.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(MapLegacyTicket(ticket));
        });

        api.MapPost("/tickets/{ticketId:int}/escalate", async (int ticketId, HttpContext context, HelpdeskDbContext db, CancellationToken ct) =>
        {
            var ticket = await FindTicketByLegacyIdAsync(db, ticketId, ct);
            if (ticket is null)
            {
                return Results.NotFound(new { message = "Ticket not found." });
            }

            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var reason = "Escalated to senior team";
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var json = JsonDocument.Parse(body);
                reason = GetString(json.RootElement, "reason") ?? reason;
            }

            ticket.Priority = TicketPriority.Critical;
            ticket.UpdatedAt = DateTime.UtcNow;
            var note = TicketNote.Create(ticket.Id, "it-manager", "IT Manager", $"Escalated: {reason}", true, DateTime.UtcNow);
            db.Add(note);
            await db.SaveChangesAsync(ct);
            return Results.Ok(MapLegacyTicket(ticket));
        });

        api.MapGet("/staff/badge/{badgeId}", (string badgeId) => Results.Ok(new
        {
            id = badgeId,
            badgeId,
            name = badgeId switch
            {
                "AIR1001" => "Jamie Santos",
                "AIR1002" => "Avery Chen",
                _ => "Airport Staff",
            },
            email = badgeId switch
            {
                "AIR1001" => "jamie.santos@airport.test",
                "AIR1002" => "avery.chen@airport.test",
                _ => "staff@airport.test",
            },
        }));

        api.MapGet("/flights/search", (string q, bool offline) =>
        {
            if (offline || string.IsNullOrWhiteSpace(q))
            {
                return Results.BadRequest(new { message = "Flight lookup is unavailable in offline mode." });
            }

            var flight = new
            {
                id = 1,
                flightNumber = q.Trim().ToUpperInvariant(),
                destination = q.Contains("Christchurch", StringComparison.OrdinalIgnoreCase) ? "Christchurch" : "Auckland",
                gate = "A12",
                status = "On time",
                updatedAtUtc = DateTime.UtcNow,
            };

            return Results.Ok(flight);
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

    private static async Task<Ticket?> FindTicketByLegacyIdAsync(HelpdeskDbContext db, int ticketId, CancellationToken ct)
    {
        var ticketList = await db.Tickets
            .Include(t => t.AssignedTechnician)
            .ToListAsync(ct);

        return ticketList.FirstOrDefault(t => Math.Abs(t.Id.GetHashCode()) == ticketId);
    }

    private static object MapLegacyTicket(Ticket ticket) => new
    {
        id = Math.Abs(ticket.Id.GetHashCode()),
        terminal = ticket.AssignedTechnicianId is null ? "T-01" : "T-03",
        area = ticket.AssignedTechnicianId is null ? "Operations" : "IT Services",
        systemType = ticket.Type.ToString(),
        description = ticket.Description,
        passengerImpact = false,
        flightOpsImpact = ticket.Type == TicketType.Incident,
        reporterName = ticket.SubmitterName,
        reporterEmail = ticket.SubmitterEmail,
        staffId = ticket.AssignedTechnicianId?.ToString() ?? string.Empty,
        priority = ToUiPriority(ticket.Priority),
        status = ToUiStatus(ticket.Status),
        assignee = ticket.AssignedTechnician?.Name ?? "Unassigned",
        workaround = string.Empty,
        isEscalated = false,
        escalationReason = string.Empty,
        createdAtUtc = ticket.CreatedAt,
        resolvedAtUtc = ticket.ResolvedAt,
        attachmentUrl = (string?)null,
        attachmentOriginalName = (string?)null,
        attachmentContentType = (string?)null,
        attachmentSize = (long?)null,
        slaOverdue = ticket.Priority == TicketPriority.Critical && ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Resolved,
        isPerformanceTest = false,
    };

    private static string ToUiStatus(TicketStatus status) => status switch
    {
        TicketStatus.PendingEmployeeResponse => "WaitingOnUser",
        _ => status.ToString(),
    };

    private static string ToUiPriority(TicketPriority priority) => priority.ToString();

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null)
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        return null;
    }

    private static TicketPriority ParsePriority(string value)
    {
        return value switch
        {
            "Low" => TicketPriority.Low,
            "Medium" => TicketPriority.Medium,
            "High" => TicketPriority.High,
            "Critical" => TicketPriority.Critical,
            _ => TicketPriority.Medium,
        };
    }

    private static TicketType ParseType(string value)
    {
        return value switch
        {
            "ServiceRequest" => TicketType.ServiceRequest,
            _ => TicketType.Incident,
        };
    }

    private static TicketStatus ParseStatus(string value)
    {
        return value switch
        {
            "New" => TicketStatus.New,
            "InProgress" => TicketStatus.InProgress,
            "WaitingOnUser" => TicketStatus.PendingEmployeeResponse,
            "Resolved" => TicketStatus.Resolved,
            "Closed" => TicketStatus.Closed,
            _ => TicketStatus.New,
        };
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

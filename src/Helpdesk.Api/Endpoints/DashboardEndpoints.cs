namespace Helpdesk.Api.Endpoints;

using System.Text;
using Helpdesk.Api.Models;
using Helpdesk.Api.Security;
using Helpdesk.Core.Domain;
using Helpdesk.Data;
using Microsoft.EntityFrameworkCore;

public static class DashboardEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/session", async (SignInRequest request, HelpdeskDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                return Results.BadRequest(new { error = "Invalid", message = "Choose an account." });
            }

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
            if (user is null || !user.IsActive)
            {
                return Results.Json(new { error = "Unauthenticated", message = "That account cannot sign in." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(new UserDto(user.Id, user.DisplayName, user.Department, user.Role.ToString(), user.IsActive));
        });
    }

    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        group.MapGet("/dashboard", async (HttpContext context, HelpdeskDbContext db, CancellationToken ct) =>
        {
            var actor = ActorMiddleware.Current(context);
            if (actor is null)
            {
                return Results.Json(new { error = "Unauthenticated", message = $"Supply a known user in the {ActorMiddleware.HeaderName} header." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (actor.Role != UserRole.TeamLead)
            {
                return Results.Json(new { error = "Forbidden", message = "Manager dashboard access is restricted to the IT Manager." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var tickets = await db.Tickets.Include(t => t.AssignedTechnician).ToListAsync(ct);
            return Results.Ok(BuildMetrics(tickets));
        });

        group.MapGet("/dashboard/tickets", async (HttpContext context, HelpdeskDbContext db, string? status, string? priority, string? assignee, CancellationToken ct) =>
        {
            var actor = ActorMiddleware.Current(context);
            if (actor is null)
            {
                return Results.Json(new { error = "Unauthenticated", message = $"Supply a known user in the {ActorMiddleware.HeaderName} header." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (actor.Role != UserRole.TeamLead)
            {
                return Results.Json(new { error = "Forbidden", message = "Manager dashboard access is restricted to the IT Manager." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var tickets = await db.Tickets.Include(t => t.AssignedTechnician).ToListAsync(ct);
            
            // Apply filters
            if (!string.IsNullOrEmpty(status) && status != "All")
            {
                var statusEnum = FromUiStatus(status);
                tickets = tickets.Where(t => t.Status == statusEnum).ToList();
            }

            if (!string.IsNullOrEmpty(priority) && priority != "All")
            {
                if (Enum.TryParse<TicketPriority>(priority, out var priorityEnum))
                {
                    tickets = tickets.Where(t => t.Priority == priorityEnum).ToList();
                }
            }

            if (!string.IsNullOrEmpty(assignee) && assignee != "All")
            {
                tickets = tickets.Where(t => t.AssignedTechnician?.Name == assignee).ToList();
            }

            return Results.Ok(tickets.Select(MapTicketRow).ToList());
        });

        group.MapGet("/dashboard/export", async (HttpContext context, HelpdeskDbContext db, string? status, string? priority, string? assignee, CancellationToken ct) =>
        {
            var actor = ActorMiddleware.Current(context);
            if (actor is null)
            {
                return Results.Json(new { error = "Unauthenticated", message = $"Supply a known user in the {ActorMiddleware.HeaderName} header." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (actor.Role != UserRole.TeamLead)
            {
                return Results.Json(new { error = "Forbidden", message = "Manager dashboard access is restricted to the IT Manager." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var tickets = await db.Tickets.Include(t => t.AssignedTechnician).ToListAsync(ct);
            
            // Apply filters
            if (!string.IsNullOrEmpty(status) && status != "All")
            {
                var statusEnum = FromUiStatus(status);
                tickets = tickets.Where(t => t.Status == statusEnum).ToList();
            }

            if (!string.IsNullOrEmpty(priority) && priority != "All")
            {
                if (Enum.TryParse<TicketPriority>(priority, out var priorityEnum))
                {
                    tickets = tickets.Where(t => t.Priority == priorityEnum).ToList();
                }
            }

            if (!string.IsNullOrEmpty(assignee) && assignee != "All")
            {
                tickets = tickets.Where(t => t.AssignedTechnician?.Name == assignee).ToList();
            }

            var csv = BuildCsv(tickets);
            return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", "airport-helpdesk-dashboard.csv");
        });

        group.MapPost("/demo/performance-data", async (HttpContext context, HelpdeskDbContext db, int? count, CancellationToken ct) =>
        {
            var actor = ActorMiddleware.Current(context);
            if (actor is null)
            {
                return Results.Json(new { error = "Unauthenticated", message = $"Supply a known user in the {ActorMiddleware.HeaderName} header." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (actor.Role != UserRole.TeamLead)
            {
                return Results.Json(new { error = "Forbidden", message = "Manager dashboard access is restricted to the IT Manager." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var requested = Math.Clamp(count ?? 500, 1, 2000);
            var start = DateTime.UtcNow;
            var rows = Enumerable.Range(0, requested).Select((_, index) => new Ticket
            {
                Title = $"Synthetic load test ticket {index + 1}",
                Description = "Generated to benchmark dashboard performance and large backlogs.",
                Status = index % 4 == 0 ? TicketStatus.Resolved : TicketStatus.InProgress,
                Priority = (TicketPriority)(index % 4),
                Type = TicketType.Incident,
                SubmitterName = $"Demo Employee {index + 1}",
                SubmitterEmail = $"demo{index + 1}@example.com",
                AssignedTechnicianId = index % 2 == 0 ? Guid.NewGuid() : null,
                CreatedAt = DateTime.UtcNow.AddDays(-(index % 15)),
                UpdatedAt = DateTime.UtcNow.AddHours(-(index % 48)),
                ResolvedAt = index % 4 == 0 ? DateTime.UtcNow.AddHours(-(index % 24)) : null,
            }).ToList();

            await db.Tickets.AddRangeAsync(rows, ct);
            await db.SaveChangesAsync(ct);

            var elapsedMs = (DateTime.UtcNow - start).TotalMilliseconds;
            return Results.Ok(new { message = $"Generated {requested} demo tickets.", count = requested, databaseSeedMilliseconds = Math.Round(elapsedMs, 2) });
        });

        group.MapDelete("/demo/performance-data", async (HttpContext context, HelpdeskDbContext db, CancellationToken ct) =>
        {
            var actor = ActorMiddleware.Current(context);
            if (actor is null)
            {
                return Results.Json(new { error = "Unauthenticated", message = $"Supply a known user in the {ActorMiddleware.HeaderName} header." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (actor.Role != UserRole.TeamLead)
            {
                return Results.Json(new { error = "Forbidden", message = "Manager dashboard access is restricted to the IT Manager." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var count = await db.Tickets.CountAsync(ct);
            if (count > 0)
            {
                db.Tickets.RemoveRange(await db.Tickets.ToListAsync(ct));
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { message = $"Removed {count} demo ticket(s)." });
        });
    }

    private static object BuildMetrics(List<Ticket> tickets)
    {
        var openBacklog = tickets.Count(t => t.Status != TicketStatus.Closed && t.Status != TicketStatus.Resolved);
        var overdue = tickets.Count(t => t.Status != TicketStatus.Closed && t.Status != TicketStatus.Resolved && t.Priority == TicketPriority.Critical);
        var resolved = tickets.Where(t => t.ResolvedAt is not null).ToList();
        var avgMinutes = resolved.Count == 0 ? 0 : resolved.Average(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalMinutes);

        var categoryCounts = tickets
            .GroupBy(t => t.Type)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .ToList();

        var category = categoryCounts.Count > 0 ? categoryCounts[0].Key.ToString() : "Incident";
        var count = categoryCounts.Count > 0 ? categoryCounts[0].Count() : 0;

        return new
        {
            openBacklog,
            averageResolutionMinutes = Math.Round(avgMinutes, 2),
            overdueCount = overdue,
            recurringIssueCategory = category,
            recurringIssueCount = count,
            totalTickets = tickets.Count,
        };
    }

    private static object MapTicketRow(Ticket ticket) => new
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

    private static TicketStatus FromUiStatus(string uiStatus) => uiStatus switch
    {
        "WaitingOnUser" => TicketStatus.PendingEmployeeResponse,
        _ => Enum.TryParse<TicketStatus>(uiStatus, out var parsed) ? parsed : TicketStatus.New,
    };

    private static string ToUiPriority(TicketPriority priority) => priority.ToString();

    private static string BuildCsv(List<Ticket> tickets)
    {
        var lines = new List<string>
        {
            "id,terminal,area,systemType,priority,status,assignee,reporterName,slaOverdue",
        };

        foreach (var ticket in tickets)
        {
            lines.Add(string.Join(",",
            [
                Math.Abs(ticket.Id.GetHashCode()).ToString(),
                ticket.AssignedTechnicianId is null ? "T-01" : "T-03",
                ticket.AssignedTechnicianId is null ? "Operations" : "IT Services",
                ticket.Type.ToString(),
                ticket.Priority.ToString(),
                ticket.Status.ToString(),
                ticket.AssignedTechnician?.Name ?? "Unassigned",
                ticket.SubmitterName,
                (ticket.Priority == TicketPriority.Critical && ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Resolved).ToString(),
            ]));
        }

        return string.Join(Environment.NewLine, lines);
    }
}

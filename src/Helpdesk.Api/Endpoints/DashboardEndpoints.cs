namespace Helpdesk.Api.Endpoints;

using Helpdesk.Api.Models;
using Helpdesk.Api.Security;
using Helpdesk.Core.Domain;
using Helpdesk.Core.Reporting;
using Helpdesk.Data;
using Microsoft.EntityFrameworkCore;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        // Every route in this group is manager-only. The check lives in one filter on the group,
        // not in each handler, so a route added here later cannot forget it.
        var group = app.MapGroup("/api").AddEndpointFilter<RequireManagerFilter>();

        group.MapGet("/dashboard", async (HelpdeskDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var tickets = await db.Tickets.Include(t => t.AssignedTechnician).ToListAsync(ct);
            return Results.Ok(BuildMetrics(tickets, clock.GetUtcNow().UtcDateTime));
        });

        // USER STORY: filter tickets by status, priority and assignee (IT manager).
        // The table and the CSV export share TicketFilter, so they always return the same tickets.
        group.MapGet("/dashboard/tickets", async (HelpdeskDbContext db, TimeProvider clock, string? status, string? priority, string? assignee, CancellationToken ct) =>
        {
            if (!TicketFilter.TryParse(status, priority, assignee, out var filter, out var error))
            {
                return InvalidFilter(error);
            }

            var tickets = await FilteredTicketsAsync(db, filter, ct);

            // No match is a normal answer: 200 with an empty list, never an error.
            var now = clock.GetUtcNow().UtcDateTime;
            return Results.Ok(tickets.Select(t => MapTicketRow(t, now)).ToList());
        });

        group.MapGet("/dashboard/export", async (HelpdeskDbContext db, TimeProvider clock, string? status, string? priority, string? assignee, CancellationToken ct) =>
        {
            if (!TicketFilter.TryParse(status, priority, assignee, out var filter, out var error))
            {
                return InvalidFilter(error);
            }

            var tickets = await FilteredTicketsAsync(db, filter, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            return CsvFile(BuildCsv(tickets, now), DashboardExport.FileName("tickets", now));
        });

        // USER STORY: export the dashboard summary (IT manager). The manager picks which file to
        // download. Both summary files cover every ticket, exactly like the summary cards, so they
        // take no filter parameters. Same manager-only group, so the same 401/403 rules apply.
        group.MapGet("/dashboard/export/summary", async (HelpdeskDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var tickets = await db.Tickets.AsNoTracking().ToListAsync(ct);
            var now = clock.GetUtcNow().UtcDateTime;
            return CsvFile(DashboardExport.SummaryCsv(DashboardSummary.From(tickets, now), now), DashboardExport.FileName("summary", now));
        });

        group.MapGet("/dashboard/export/by-priority", async (HelpdeskDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var tickets = await db.Tickets.AsNoTracking().ToListAsync(ct);
            var now = clock.GetUtcNow().UtcDateTime;
            return CsvFile(DashboardExport.ByPriorityCsv(DashboardSummary.ByPriority(tickets, now)), DashboardExport.FileName("by-priority", now));
        });

        group.MapPost("/demo/performance-data", async (HelpdeskDbContext db, int? count, CancellationToken ct) =>
        {
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

        group.MapDelete("/demo/performance-data", async (HelpdeskDbContext db, CancellationToken ct) =>
        {
            var count = await db.Tickets.CountAsync(ct);
            if (count > 0)
            {
                db.Tickets.RemoveRange(await db.Tickets.ToListAsync(ct));
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { message = $"Removed {count} demo ticket(s)." });
        });
    }

    /// <summary>The filter runs in the database query, so only matching tickets are loaded.</summary>
    private static Task<List<Ticket>> FilteredTicketsAsync(HelpdeskDbContext db, TicketFilter filter, CancellationToken ct) =>
        filter.Apply(db.Tickets.Include(t => t.AssignedTechnician).AsNoTracking())
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    private static IResult CsvFile(string document, string fileName) =>
        Results.File(Csv.ToBytes(document), "text/csv; charset=utf-8", fileName);

    private static IResult InvalidFilter(string? message) =>
        Results.Json(
            new { error = "InvalidFilter", message = message ?? "The filter values are not valid." },
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>The summary cards. Figures come from <see cref="DashboardSummary"/>, the same source as the CSV exports.</summary>
    private static object BuildMetrics(List<Ticket> tickets, DateTime nowUtc)
    {
        var summary = DashboardSummary.From(tickets, nowUtc);
        return new
        {
            openBacklog = summary.OpenBacklog,
            averageResolutionMinutes = summary.AverageResolutionMinutes ?? 0,
            overdueCount = summary.OverdueCount,
            // "None" rather than a made-up "Incident" when there are no tickets at all.
            recurringIssueCategory = summary.MostCommonCategory?.ToString() ?? "None",
            recurringIssueCount = summary.MostCommonCategoryCount,
            totalTickets = summary.TotalTickets,
        };
    }

    private static object MapTicketRow(Ticket ticket, DateTime nowUtc) => new
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
        slaOverdue = SlaPolicy.IsOverdue(ticket, nowUtc),
        isPerformanceTest = false,
    };

    private static string ToUiStatus(TicketStatus status) => status switch
    {
        TicketStatus.PendingEmployeeResponse => "WaitingOnUser",
        _ => status.ToString(),
    };

    private static string ToUiPriority(TicketPriority priority) => priority.ToString();

    /// <summary>The ticket list file. Goes through <see cref="Csv"/>, so names with commas or quotes stay in one column.</summary>
    private static string BuildCsv(List<Ticket> tickets, DateTime nowUtc) =>
        Csv.Document(
            ["id", "terminal", "area", "systemType", "priority", "status", "assignee", "reporterName", "slaOverdue"],
            tickets.Select(ticket => new string?[]
            {
                Math.Abs(ticket.Id.GetHashCode()).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ticket.AssignedTechnicianId is null ? "T-01" : "T-03",
                ticket.AssignedTechnicianId is null ? "Operations" : "IT Services",
                ticket.Type.ToString(),
                ticket.Priority.ToString(),
                ticket.Status.ToString(),
                ticket.AssignedTechnician?.Name ?? "Unassigned",
                ticket.SubmitterName,
                SlaPolicy.IsOverdue(ticket, nowUtc).ToString(),
            }));
}

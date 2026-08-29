using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Helpdesk.Api.Dtos;
using Helpdesk.Api.Services;
using Helpdesk.Core.Models;
using Helpdesk.Core.Services;
using Helpdesk.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddDbContext<HelpdeskDbContext>(options =>
{
    var connectionString =
        builder.Configuration.GetConnectionString("Helpdesk")
        ?? "Data Source=helpdesk.db";

    options.UseSqlite(connectionString);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "Frontend",
        policy =>
            policy
                .WithOrigins(
                    "http://localhost:5173",
                    "http://127.0.0.1:5173",
                    "http://localhost:5174",
                    "http://127.0.0.1:5174"
                )
                .AllowAnyHeader()
                .AllowAnyMethod()
    );
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("Frontend");
app.UseStaticFiles();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DbSeeder.SeedAsync(db);
}

app.MapGet(
    "/api/health",
    () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow })
);

app.MapGet(
    "/api/tickets",
    async (
        HelpdeskDbContext db,
        string? sort,
        string? status,
        string? priority,
        string? assignee,
        string? staffId
    ) =>
    {
        IQueryable<Ticket> query = db.Tickets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<TicketStatus>(status.Replace(" ", ""), true, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(priority)
            && Enum.TryParse<TicketPriority>(priority, true, out var parsedPriority))
        {
            query = query.Where(x => x.Priority == parsedPriority);
        }

        if (!string.IsNullOrWhiteSpace(assignee))
        {
            query = query.Where(x => x.Assignee == assignee);
        }

        if (!string.IsNullOrWhiteSpace(staffId))
        {
            query = query.Where(x => x.StaffId == staffId);
        }

        query =
            string.Equals(sort, "oldest", StringComparison.OrdinalIgnoreCase)
                ? query.OrderBy(x => x.CreatedAtUtc)
                : query.OrderByDescending(x => x.CreatedAtUtc);

        return Results.Ok(await query.ToListAsync());
    }
);

app.MapGet(
    "/api/tickets/{id:int}",
    async (int id, HelpdeskDbContext db) =>
    {
        var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

        return ticket is null
            ? Results.NotFound(new { message = "Ticket not found." })
            : Results.Ok(ticket);
    }
);

app.MapPost(
    "/api/tickets",
    async (CreateTicketRequest request, HelpdeskDbContext db) =>
    {
        var missingFields = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Terminal))
            missingFields.Add("terminal");

        if (string.IsNullOrWhiteSpace(request.Area))
            missingFields.Add("gate or area");

        if (string.IsNullOrWhiteSpace(request.SystemType))
            missingFields.Add("system type");

        if (string.IsNullOrWhiteSpace(request.Description))
            missingFields.Add("description");

        if (missingFields.Count > 0)
        {
            return Results.BadRequest(
                new
                {
                    message =
                        $"Ticket not submitted. Required: {string.Join(", ", missingFields)}."
                }
            );
        }

        StaffProfile? staff = null;

        if (!string.IsNullOrWhiteSpace(request.StaffId))
        {
            var normalisedBadge = request.StaffId.Trim().ToUpperInvariant();

            staff = await db.StaffProfiles.FirstOrDefaultAsync(
                x => x.BadgeId == normalisedBadge
            );

            if (staff is null)
            {
                return Results.BadRequest(
                    new
                    {
                        message =
                            "Badge ID not recognised. Please try again or contact a supervisor."
                    }
                );
            }
        }

        var ticket = new Ticket
        {
            Terminal = request.Terminal.Trim(),
            Area = request.Area.Trim(),
            SystemType = request.SystemType.Trim(),
            Description = request.Description.Trim(),
            PassengerImpact = request.PassengerImpact,
            FlightOpsImpact = request.FlightOpsImpact,
            ReporterName = staff?.Name ?? request.ReporterName.Trim(),
            ReporterEmail = staff?.Email ?? request.ReporterEmail.Trim(),
            StaffId = staff?.BadgeId ?? request.StaffId.Trim(),
            Priority = request.Priority,
            Status = TicketStatus.New,
            Assignee = "Unassigned",
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        return Results.Created(
            $"/api/tickets/{ticket.Id}",
            new
            {
                message =
                    $"Ticket #{ticket.Id} created successfully with status \"New\".",
                ticket
            }
        );
    }
);

app.MapPut(
    "/api/tickets/{id:int}",
    async (
        int id,
        UpdateTicketRequest request,
        HttpContext http,
        HelpdeskDbContext db
    ) =>
    {
        if (!DemoAccess.HasRole(http, "IT Technician", "IT Manager"))
        {
            return Results.Json(
                new { message = "Access denied. Technician or Manager role required." },
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        var ticket = await db.Tickets.FirstOrDefaultAsync(x => x.Id == id);

        if (ticket is null)
        {
            return Results.NotFound(new { message = "Ticket not found." });
        }

        if (request.Status.HasValue && request.Status.Value != ticket.Status)
        {
            if (!TicketRules.CanTransition(ticket.Status, request.Status.Value))
            {
                return Results.BadRequest(
                    new
                    {
                        message =
                            $"Invalid status transition from {TicketRules.ToDisplayName(ticket.Status)} "
                            + $"to {TicketRules.ToDisplayName(request.Status.Value)}.",
                        validNextStatuses =
                            TicketRules.GetValidNextStatuses(ticket.Status)
                    }
                );
            }

            ticket.Status = request.Status.Value;

            if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
            {
                ticket.ResolvedAtUtc ??= DateTime.UtcNow;
            }
            else if (ticket.Status == TicketStatus.InProgress)
            {
                ticket.ResolvedAtUtc = null;
            }
        }

        if (request.Assignee is not null)
        {
            ticket.Assignee =
                string.IsNullOrWhiteSpace(request.Assignee)
                    ? "Unassigned"
                    : request.Assignee.Trim();
        }

        if (request.Workaround is not null)
        {
            ticket.Workaround = request.Workaround.Trim();
        }

        await db.SaveChangesAsync();

        return Results.Ok(ticket);
    }
);

app.MapPost(
        "/api/tickets/{id:int}/attachment",
        async (
            int id,
            IFormFile file,
            IWebHostEnvironment environment,
            HelpdeskDbContext db
        ) =>
        {
            var ticket = await db.Tickets.FirstOrDefaultAsync(x => x.Id == id);

            if (ticket is null)
            {
                return Results.NotFound(new { message = "Ticket not found." });
            }

            const long maxFileSize = 5 * 1024 * 1024;
            var allowedContentTypes = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                "image/jpeg",
                "image/png"
            };

            if (!allowedContentTypes.Contains(file.ContentType))
            {
                return Results.BadRequest(
                    new
                    {
                        message =
                            "Upload rejected. Only JPG and PNG image files are supported."
                    }
                );
            }

            if (file.Length <= 0 || file.Length > maxFileSize)
            {
                return Results.BadRequest(
                    new
                    {
                        message =
                            "Upload rejected. The maximum attachment size is 5 MB."
                    }
                );
            }

            var extension =
                file.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
                    ? ".png"
                    : ".jpg";

            var uploadsFolder = Path.Combine(
                environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"),
                "uploads"
            );

            Directory.CreateDirectory(uploadsFolder);

            var storedFileName = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(uploadsFolder, storedFileName);

            await using (var stream = File.Create(fullPath))
            {
                await file.CopyToAsync(stream);
            }

            ticket.AttachmentUrl = $"/uploads/{storedFileName}";
            ticket.AttachmentOriginalName = Path.GetFileName(file.FileName);
            ticket.AttachmentContentType = file.ContentType;
            ticket.AttachmentSize = file.Length;

            await db.SaveChangesAsync();

            return Results.Ok(
                new
                {
                    message = "Attachment uploaded successfully.",
                    ticket.AttachmentUrl,
                    ticket.AttachmentOriginalName,
                    ticket.AttachmentContentType,
                    ticket.AttachmentSize
                }
            );
        }
    )
    .DisableAntiforgery();

app.MapGet(
    "/api/staff/badge/{badgeId}",
    async (string badgeId, HelpdeskDbContext db) =>
    {
        var normalised = badgeId.Trim().ToUpperInvariant();

        var staff = await db.StaffProfiles.AsNoTracking().FirstOrDefaultAsync(
            x => x.BadgeId == normalised
        );

        return staff is null
            ? Results.NotFound(
                new
                {
                    message =
                        "Badge ID not recognised. Please try again or contact a supervisor."
                }
            )
            : Results.Ok(staff);
    }
);

app.MapGet(
    "/api/flights/search",
    async (string? q, bool offline, HelpdeskDbContext db) =>
    {
        if (offline)
        {
            return Results.Json(
                new
                {
                    message =
                        "Flight information lookup is currently unavailable. "
                        + "Please use another verified source before directing passengers."
                },
                statusCode: StatusCodes.Status503ServiceUnavailable
            );
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return Results.BadRequest(
                new { message = "Enter a flight number or destination." }
            );
        }

        var query = q.Trim().ToLowerInvariant();

        var flight = await db.Flights.AsNoTracking().FirstOrDefaultAsync(
            x =>
                x.FlightNumber.ToLower() == query
                || x.Destination.ToLower().Contains(query)
        );

        return flight is null
            ? Results.NotFound(
                new
                {
                    message =
                        "No matching flight was found. Check the flight number or destination."
                }
            )
            : Results.Ok(flight);
    }
);

app.MapGet(
    "/api/dashboard",
    async (HttpContext http, HelpdeskDbContext db) =>
    {
        if (!DemoAccess.HasRole(http, "IT Manager"))
        {
            return Results.Json(
                new
                {
                    message =
                        "Access denied. The manager dashboard is only available to IT Managers."
                },
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        var tickets = await db.Tickets.AsNoTracking().ToListAsync();
        var now = DateTime.UtcNow;

        var openBacklog = tickets.Count(x => x.Status != TicketStatus.Closed);
        var overdue = tickets.Count(x => TicketRules.IsOverdue(x, now));

        var resolved = tickets
            .Where(x => x.ResolvedAtUtc.HasValue)
            .ToList();

        var averageResolutionMinutes =
            resolved.Count == 0
                ? 0
                : resolved.Average(
                    x => (x.ResolvedAtUtc!.Value - x.CreatedAtUtc).TotalMinutes
                );

        var recurring = tickets
            .GroupBy(x => x.SystemType)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key)
            .FirstOrDefault();

        var metrics = new DashboardMetricsDto(
            openBacklog,
            Math.Round(averageResolutionMinutes, 1),
            overdue,
            recurring?.Key ?? "No data",
            recurring?.Count() ?? 0,
            tickets.Count
        );

        return Results.Ok(metrics);
    }
);

app.MapGet(
    "/api/dashboard/tickets",
    async (
        HttpContext http,
        HelpdeskDbContext db,
        string? status,
        string? priority,
        string? assignee
    ) =>
    {
        if (!DemoAccess.HasRole(http, "IT Manager"))
        {
            return Results.Json(
                new
                {
                    message =
                        "Access denied. The manager dashboard is only available to IT Managers."
                },
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        IQueryable<Ticket> query = db.Tickets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<TicketStatus>(status.Replace(" ", ""), true, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(priority)
            && Enum.TryParse<TicketPriority>(priority, true, out var parsedPriority))
        {
            query = query.Where(x => x.Priority == parsedPriority);
        }

        if (!string.IsNullOrWhiteSpace(assignee))
        {
            query = query.Where(x => x.Assignee == assignee);
        }

        return Results.Ok(
            await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync()
        );
    }
);

app.MapGet(
    "/api/dashboard/export",
    async (
        HttpContext http,
        HelpdeskDbContext db,
        string? status,
        string? priority,
        string? assignee
    ) =>
    {
        if (!DemoAccess.HasRole(http, "IT Manager"))
        {
            return Results.Json(
                new { message = "Access denied. Manager role required." },
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        IQueryable<Ticket> query = db.Tickets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<TicketStatus>(status.Replace(" ", ""), true, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(priority)
            && Enum.TryParse<TicketPriority>(priority, true, out var parsedPriority))
        {
            query = query.Where(x => x.Priority == parsedPriority);
        }

        if (!string.IsNullOrWhiteSpace(assignee))
        {
            query = query.Where(x => x.Assignee == assignee);
        }

        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync();

        var builder = new StringBuilder();

        builder.AppendLine(
            "\"Ticket ID\",\"Terminal\",\"Area\",\"System\",\"Priority\",\"Status\","
            + "\"Assignee\",\"Passenger Impact\",\"Flight Ops Impact\",\"Created At UTC\""
        );

        foreach (var ticket in rows)
        {
            builder.AppendLine(
                string.Join(
                    ",",
                    Csv(ticket.Id),
                    Csv(ticket.Terminal),
                    Csv(ticket.Area),
                    Csv(ticket.SystemType),
                    Csv(ticket.Priority),
                    Csv(TicketRules.ToDisplayName(ticket.Status)),
                    Csv(ticket.Assignee),
                    Csv(ticket.PassengerImpact ? "Yes" : "No"),
                    Csv(ticket.FlightOpsImpact ? "Yes" : "No"),
                    Csv(ticket.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture))
                )
            );
        }

        return Results.File(
            Encoding.UTF8.GetBytes(builder.ToString()),
            "text/csv",
            "airport-helpdesk-dashboard.csv"
        );
    }
);

app.MapPost(
    "/api/demo/performance-data",
    async (HttpContext http, HelpdeskDbContext db, int count) =>
    {
        if (!DemoAccess.HasRole(http, "IT Manager"))
        {
            return Results.Json(
                new { message = "Access denied. Manager role required." },
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        count = Math.Clamp(count, 1, 1000);

        var existing = await db.Tickets
            .Where(x => x.IsPerformanceTest)
            .ToListAsync();

        if (existing.Count > 0)
        {
            db.Tickets.RemoveRange(existing);
            await db.SaveChangesAsync();
        }

        var now = DateTime.UtcNow;
        var systems = new[]
        {
            "Kiosk",
            "Network",
            "Gate Computer",
            "Flight Information Display"
        };

        var priorities = Enum.GetValues<TicketPriority>();
        var statuses = new[]
        {
            TicketStatus.New,
            TicketStatus.InProgress,
            TicketStatus.Resolved,
            TicketStatus.Closed
        };

        var stopwatch = Stopwatch.StartNew();

        var generated = Enumerable.Range(0, count).Select(index =>
        {
            var status = statuses[index % statuses.Length];
            var created = now.AddMinutes(-(index * 5 + 10));

            return new Ticket
            {
                Terminal = $"T{index % 3 + 1}",
                Area = $"Gate {index % 24 + 1}",
                SystemType = systems[index % systems.Length],
                Description = $"Performance test ticket {index + 1}",
                PassengerImpact = index % 4 == 0,
                FlightOpsImpact = index % 8 == 0,
                ReporterName = "Performance Test User",
                ReporterEmail = "performance@airport.test",
                StaffId = "PERF",
                Priority = priorities[index % priorities.Length],
                Status = status,
                Assignee = new[] { "Alex Morgan", "Priya Shah", "Unassigned" }[
                    index % 3
                ],
                CreatedAtUtc = created,
                ResolvedAtUtc =
                    status is TicketStatus.Resolved or TicketStatus.Closed
                        ? created.AddMinutes(45 + index % 120)
                        : null,
                IsPerformanceTest = true
            };
        });

        db.Tickets.AddRange(generated);
        await db.SaveChangesAsync();

        stopwatch.Stop();

        return Results.Ok(
            new
            {
                message = $"{count} performance test tickets created.",
                count,
                databaseSeedMilliseconds = Math.Round(
                    stopwatch.Elapsed.TotalMilliseconds,
                    1
                )
            }
        );
    }
);

app.MapDelete(
    "/api/demo/performance-data",
    async (HttpContext http, HelpdeskDbContext db) =>
    {
        if (!DemoAccess.HasRole(http, "IT Manager"))
        {
            return Results.Json(
                new { message = "Access denied. Manager role required." },
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        var rows = await db.Tickets
            .Where(x => x.IsPerformanceTest)
            .ToListAsync();

        db.Tickets.RemoveRange(rows);
        await db.SaveChangesAsync();

        return Results.Ok(
            new { message = $"{rows.Count} performance test tickets removed." }
        );
    }
);

app.Run();

static string Csv(object? value)
{
    var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    return $"\"{text.Replace("\"", "\"\"")}\"";
}

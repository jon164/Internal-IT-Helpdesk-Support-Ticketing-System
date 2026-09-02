namespace Helpdesk.Api.Endpoints;

using Helpdesk.Api.Contracts;
using Helpdesk.Api.Security;
using Helpdesk.Core.Security;
using Helpdesk.Core.Time;
using Helpdesk.Data;
using Helpdesk.Data.Seeding;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Generates and clears the demonstration dataset on request.
/// </summary>
/// <remarks>
/// <para>
/// The application starts with an empty ticket table. Sample data is produced only when somebody asks
/// for it, so a demonstration can begin from nothing and show the reporting views filling — and so
/// nobody is left wondering whether the figures on screen describe real work.
/// </para>
/// <para>
/// These routes are registered only when the environment is Development. That is deliberate and is
/// stronger than hiding a button: in a production build the route does not exist, so no misconfigured
/// role, forgotten feature flag or forged header can reach it. Within Development the service
/// manager check still applies, because "only developers run this build" is an assumption about
/// deployment, not an access control.
/// </para>
/// </remarks>
public static class SampleDataEndpoints
{
    public static void MapSampleDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/sample-data").WithTags("Administration");

        group.MapGet("", async (
                HttpContext context,
                HelpdeskDbContext db,
                CancellationToken ct) =>
            {
                if (Guard(context) is { } refusal)
                {
                    return refusal;
                }

                var count = await db.Tickets.CountAsync(ct);

                return Results.Ok(new SampleDataStatusDto(
                    count, count == 0, SampleDataSeeder.DefaultTicketCount));
            })
           .WithSummary("Whether demonstration data is present. Service manager only; Development only.");

        group.MapPost("", async (
                HttpContext context,
                HelpdeskDbContext db,
                IClock clock,
                CancellationToken ct) =>
            {
                if (Guard(context) is { } refusal)
                {
                    return refusal;
                }

                // Refused rather than merged. Generating on top of existing tickets would produce a
                // population nobody could reason about, and would break the guarantee that the
                // dataset is identical on every machine.
                if (await db.Tickets.AnyAsync(ct))
                {
                    return Results.Conflict(new ProblemDto(
                        "Conflict",
                        "Tickets already exist. Clear the demonstration data first."));
                }

                var created = await SampleDataSeeder.GenerateAsync(
                    db, clock.UtcNow, SampleDataSeeder.DefaultTicketCount, ct);

                return Results.Ok(new SampleDataStatusDto(
                    created, false, SampleDataSeeder.DefaultTicketCount));
            })
           .WithSummary("Generates the demonstration dataset into an empty database.");

        group.MapDelete("", async (
                HttpContext context,
                HelpdeskDbContext db,
                CancellationToken ct) =>
            {
                if (Guard(context) is { } refusal)
                {
                    return refusal;
                }

                var removed = await SampleDataSeeder.ClearTicketsAsync(db, ct);

                return Results.Ok(new SampleDataClearedDto(
                    removed, new SampleDataStatusDto(0, true, SampleDataSeeder.DefaultTicketCount)));
            })
           .WithSummary("Removes every ticket. Accounts and their change history are left intact.");
    }

    /// <summary>
    /// Returns a refusal when the caller may not manage demonstration data, or null when they may.
    /// </summary>
    private static IResult? Guard(HttpContext context)
    {
        if (ActorMiddleware.Current(context) is not { } actor)
        {
            return Results.Json(
                new ProblemDto("Unauthenticated",
                    $"Supply a known user in the {ActorMiddleware.HeaderName} header."),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TicketAccessPolicy.CanAdministerUsers(actor))
        {
            return Results.Json(
                new ProblemDto("Forbidden",
                    "Demonstration data is managed by the service manager."),
                statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }
}

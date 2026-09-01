namespace Helpdesk.Api.Endpoints;

using Helpdesk.Api.Contracts;
using Helpdesk.Api.Security;
using Helpdesk.Core;
using Helpdesk.Core.Domain;
using Helpdesk.Data;
using Helpdesk.Data.Services;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Sign-in and account administration.
/// </summary>
public static class AdminEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/session", async (
                SignInRequestDto request,
                HelpdeskDbContext db,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.UserId))
                {
                    return Results.BadRequest(new ProblemDto("Invalid", "Choose an account."));
                }

                var user = await db.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == request.UserId, ct);

                if (user is null)
                {
                    // Deliberately identical wording to the inactive case below: distinguishing them
                    // would tell an unauthenticated caller which account identifiers exist.
                    return Results.Json(
                        new ProblemDto("Unauthenticated", "That account cannot sign in."),
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                if (!user.IsActive)
                {
                    return Results.Json(
                        new ProblemDto("Unauthenticated", "That account cannot sign in."),
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                return Results.Ok(UserDto.From(user));
            })
           .WithTags("Session")
           .WithSummary(
                "Establishes which account the caller is acting as. NOTE: the prototype verifies no "
                + "credential — this selects an identity, it does not authenticate one.");
    }

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Administration");

        group.MapGet("/users", async (
                HttpContext context,
                UserAdminService service,
                CancellationToken ct) =>
            {
                if (ActorMiddleware.Current(context) is not { } actor)
                {
                    return Unauthorised();
                }

                var result = await service.GetAllAsync(actor, ct);

                return result.Succeeded
                    ? Results.Ok(result.Require().Select(UserDto.From).ToList())
                    : Map(result.Error, result.Message);
            })
           .WithSummary("All accounts, active and inactive. Administrators only.");

        group.MapGet("/audit", async (
                HttpContext context,
                UserAdminService service,
                int? take,
                CancellationToken ct) =>
            {
                if (ActorMiddleware.Current(context) is not { } actor)
                {
                    return Unauthorised();
                }

                var result = await service.GetAuditAsync(actor, take ?? 50, ct);

                return result.Succeeded
                    ? Results.Ok(result.Require().Select(UserAuditEntryDto.From).ToList())
                    : Map(result.Error, result.Message);
            })
           .WithSummary("Account change history. Administrators only.");

        group.MapPost("/users/{id}/role", async (
                HttpContext context,
                UserAdminService service,
                string id,
                ChangeRoleRequestDto request,
                CancellationToken ct) =>
            {
                if (ActorMiddleware.Current(context) is not { } actor)
                {
                    return Unauthorised();
                }

                if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
                {
                    return Results.BadRequest(new ProblemDto(
                        "Invalid", $"'{request.Role}' is not a known role."));
                }

                var result = await service.ChangeRoleAsync(id, role, request.Reason, actor, ct);

                return result.Succeeded
                    ? Results.Ok(UserDto.From(result.Require()))
                    : Map(result.Error, result.Message);
            })
           .WithSummary("Changes an account's role. Administrators only; a reason is required.");

        group.MapPost("/users/{id}/active", async (
                HttpContext context,
                UserAdminService service,
                string id,
                SetActiveRequestDto request,
                CancellationToken ct) =>
            {
                if (ActorMiddleware.Current(context) is not { } actor)
                {
                    return Unauthorised();
                }

                var result = await service.SetActiveAsync(
                    id, request.IsActive, request.Reason, actor, ct);

                return result.Succeeded
                    ? Results.Ok(UserDto.From(result.Require()))
                    : Map(result.Error, result.Message);
            })
           .WithSummary("Activates or deactivates an account. Administrators only.");
    }

    private static IResult Unauthorised() => Results.Json(
        new ProblemDto("Unauthenticated",
            $"Supply a known user in the {ActorMiddleware.HeaderName} header."),
        statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Map(OperationError error, string message) => error switch
    {
        OperationError.NotFound => Results.NotFound(new ProblemDto("NotFound", message)),
        OperationError.Forbidden => Results.Json(
            new ProblemDto("Forbidden", message), statusCode: StatusCodes.Status403Forbidden),
        OperationError.Conflict => Results.Conflict(new ProblemDto("Conflict", message)),
        _ => Results.BadRequest(new ProblemDto("Invalid", message))
    };
}

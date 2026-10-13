using Helpdesk.Api.Models;
using Helpdesk.Api.Security;
using Helpdesk.Core.Domain;
using Helpdesk.Data;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Auth;

public sealed record LoginRequest(string? Username, string? Password);

public sealed record LoginResponse(string Token, DateTime ExpiresAtUtc, UserDto User);

public static class AuthEndpoints
{
    public const string InvalidCredentialsMessage = "Username or password is incorrect.";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (LoginRequest request, AuthService auth, HttpContext context, TimeProvider clock, CancellationToken ct) =>
        {
            // The response carries a session token, so no proxy or browser cache may keep it.
            context.Response.Headers.CacheControl = "no-store";

            var result = await auth.LoginAsync(request.Username, request.Password, ct);
            switch (result.Status)
            {
                case LoginStatus.MissingCredentials:
                    return Results.BadRequest(new { error = "Invalid", message = "Enter your username and password." });

                case LoginStatus.InvalidCredentials:
                    return Results.Json(new { error = "Unauthenticated", message = InvalidCredentialsMessage }, statusCode: StatusCodes.Status401Unauthorized);

                case LoginStatus.LockedOut:
                    var remaining = result.LockoutEndUtc!.Value - clock.GetUtcNow().UtcDateTime;
                    var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
                    var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
                    context.Response.Headers.RetryAfter = seconds.ToString();
                    return Results.Json(
                        new { error = "LockedOut", message = $"Too many failed attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.", retryAfterSeconds = seconds },
                        statusCode: StatusCodes.Status429TooManyRequests);

                default:
                    return Results.Ok(new LoginResponse(result.Token!, result.ExpiresAtUtc!.Value, ToDto(result.User!)));
            }
        });

        group.MapGet("/me", async (HttpContext context, HelpdeskDbContext db, CancellationToken ct) =>
        {
            var actor = ActorMiddleware.Current(context);
            if (actor is null)
            {
                return Results.Json(new { error = "Unauthenticated", message = "Sign in to continue." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == actor.UserId, ct);
            return Results.Ok(ToDto(user));
        });

        group.MapPost("/logout", async (HttpContext context, AuthService auth, CancellationToken ct) =>
        {
            // Idempotent: signing out with a token that is already dead still succeeds,
            // so the client can always clear its state.
            await auth.LogoutAsync(ActorMiddleware.GetBearerToken(context.Request), ct);
            return Results.NoContent();
        });

        return app;
    }

    public static UserDto ToDto(UserAccount user) =>
        new(user.Id, user.DisplayName, user.Department, user.Role.ToString(), user.IsActive);
}

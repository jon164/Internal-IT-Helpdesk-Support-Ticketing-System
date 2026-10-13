namespace Helpdesk.Api.Security;

using Helpdesk.Core.Domain;

/// <summary>
/// User story: "As an IT manager, I want manager-only visibility of the dashboard, so that
/// analytics aren't exposed to employees."
/// <para>
/// The rule is an allow-list: only <see cref="UserRole.TeamLead"/> (the IT Manager) may see
/// dashboard analytics. Any other role — including one added to <see cref="UserRole"/> later —
/// is refused by default, so extending the enum can never silently widen access.
/// </para>
/// </summary>
public static class DashboardAccess
{
    public const string ForbiddenMessage = "The dashboard is available to the IT Manager only.";

    public static bool IsAllowed(ActorContext? actor) => actor is { Role: UserRole.TeamLead };
}

/// <summary>
/// Applied once to the whole dashboard route group (see <c>DashboardEndpoints</c>), so every
/// route in the group is protected — including routes added later — without each handler having
/// to repeat the check. Runs before the handler, so a refused caller never triggers a database
/// query or a change.
/// </summary>
public sealed class RequireManagerFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var actor = ActorMiddleware.Current(context.HttpContext);
        if (actor is null)
        {
            return Results.Json(
                new { error = "Unauthenticated", message = "Sign in to continue." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!DashboardAccess.IsAllowed(actor))
        {
            return Results.Json(
                new { error = "Forbidden", message = DashboardAccess.ForbiddenMessage },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Analytics must not be kept by a shared browser or proxy cache after sign-out.
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return await next(context);
    }
}

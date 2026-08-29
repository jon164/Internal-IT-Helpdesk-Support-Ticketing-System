namespace Helpdesk.Api.Security;

using Helpdesk.Core.Domain;
using Helpdesk.Data;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Establishes who is making the request.
/// </summary>
/// <remarks>
/// <para>
/// The prototype does not implement authentication — it is explicitly out of scope. The client sends
/// an <c>X-User-Id</c> header naming the account to act as, and this middleware looks that account up
/// and attaches the resulting <see cref="ActorContext"/> to the request.
/// </para>
/// <para>
/// The important property is that the header supplies an <em>identity</em> only. The role comes from
/// the database record, never from the request, and every authorisation decision downstream is made
/// server-side against that record. A caller cannot claim to be a team lead by editing a header. This
/// is what makes the security test cases meaningful despite the absence of a login screen: replacing
/// this middleware with real authentication would change how an identity is established without
/// altering a single authorisation rule.
/// </para>
/// <para>
/// A production deployment would of course require real authentication; this is recorded as a known
/// limitation rather than an oversight.
/// </para>
/// </remarks>
public sealed class ActorMiddleware
{
    public const string HeaderName = "X-User-Id";
    private const string ItemKey = "helpdesk.actor";

    private readonly RequestDelegate _next;

    public ActorMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, HelpdeskDbContext db)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var header))
        {
            var userId = header.ToString();

            if (!string.IsNullOrWhiteSpace(userId))
            {
                var user = await db.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive,
                        context.RequestAborted);

                if (user is not null)
                {
                    context.Items[ItemKey] = user.ToActor();
                }
            }
        }

        await _next(context);
    }

    /// <summary>The resolved actor, or null when the header was absent, unknown, or inactive.</summary>
    public static ActorContext? Current(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as ActorContext : null;
}

/// <summary>Registration helper.</summary>
public static class ActorMiddlewareExtensions
{
    public static IApplicationBuilder UseActorResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<ActorMiddleware>();
}

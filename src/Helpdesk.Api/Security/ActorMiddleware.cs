namespace Helpdesk.Api.Security;

using Helpdesk.Data;
using Microsoft.EntityFrameworkCore;

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
                    .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, context.RequestAborted);

                if (user is not null)
                {
                    context.Items[ItemKey] = new ActorContext(user.Id, user.Role, user.DisplayName);
                }
            }
        }

        await _next(context);
    }

    public static ActorContext? Current(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as ActorContext : null;
}

public static class ActorMiddlewareExtensions
{
    public static IApplicationBuilder UseActorResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<ActorMiddleware>();
}

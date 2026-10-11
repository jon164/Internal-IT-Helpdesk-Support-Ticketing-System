namespace Helpdesk.Api.Security;

using Helpdesk.Api.Auth;

/// <summary>
/// Resolves who is calling from an <c>Authorization: Bearer &lt;token&gt;</c> header issued by
/// <c>POST /api/auth/login</c>. The role always comes from the account's database record.
/// The earlier <c>X-User-Id</c> header is deliberately no longer read: it let any caller act
/// as any account by naming it.
/// </summary>
public sealed class ActorMiddleware
{
    private const string ItemKey = "helpdesk.actor";

    private readonly RequestDelegate _next;

    public ActorMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AuthService auth)
    {
        var token = GetBearerToken(context.Request);
        if (token is not null)
        {
            var user = await auth.ValidateAsync(token, context.RequestAborted);
            if (user is not null)
            {
                context.Items[ItemKey] = new ActorContext(user.Id, user.Role, user.DisplayName);
            }
        }

        await _next(context);
    }

    public static ActorContext? Current(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as ActorContext : null;

    /// <summary>Returns the token from a well-formed Bearer header, otherwise null.</summary>
    public static string? GetBearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header[prefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }
}

public static class ActorMiddlewareExtensions
{
    public static IApplicationBuilder UseActorResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<ActorMiddleware>();
}

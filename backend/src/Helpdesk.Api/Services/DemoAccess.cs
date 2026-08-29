namespace Helpdesk.Api.Services;

public static class DemoAccess
{
    public const string HeaderName = "X-Demo-Role";

    public static string GetRole(HttpContext httpContext) =>
        httpContext.Request.Headers[HeaderName].FirstOrDefault() ?? "Airport Staff";

    public static bool HasRole(HttpContext httpContext, params string[] allowedRoles)
    {
        var role = GetRole(httpContext);

        return allowedRoles.Any(
            allowed => string.Equals(allowed, role, StringComparison.OrdinalIgnoreCase)
        );
    }
}

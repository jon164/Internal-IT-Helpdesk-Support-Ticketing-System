using Helpdesk.Api.Auth;
using Helpdesk.Api.Security;
using Helpdesk.Core.Domain;
using Microsoft.AspNetCore.Http;

namespace Helpdesk.Tests.Auth;

/// <summary>
/// Security: who the API believes is calling. Runs the real middleware against a
/// DefaultHttpContext, so no web host or extra test package is needed.
/// </summary>
[TestClass]
public sealed class ActorMiddlewareTests
{
    private static async Task<(ActorContext? Actor, bool NextCalled)> RunAsync(AuthService auth, Action<HttpRequest> arrange)
    {
        var context = new DefaultHttpContext();
        arrange(context.Request);
        var nextCalled = false;
        var middleware = new ActorMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, auth);

        return (ActorMiddleware.Current(context), nextCalled);
    }

    [TestMethod] // AUTH-B24 — regression test for the identity-only sign-in this change replaces
    public async Task NamingAnAccountInXUserId_NoLongerAuthenticates()
    {
        var (_, auth, _, _) = await AuthTestSupport.CreateAsync();

        var (actor, nextCalled) = await RunAsync(auth, r => r.Headers["X-User-Id"] = "it-manager");

        Assert.IsNull(actor, "Anyone could previously act as the manager by sending this header.");
        Assert.IsTrue(nextCalled);
    }

    [TestMethod] // AUTH-B25
    public async Task ValidBearerToken_ResolvesActorWithRoleFromTheDatabase()
    {
        var (_, auth, _, _) = await AuthTestSupport.CreateAsync();
        var login = await auth.LoginAsync("it-manager", AuthTestSupport.ManagerPassword);

        var (actor, _) = await RunAsync(auth, r => r.Headers.Authorization = $"Bearer {login.Token}");

        Assert.IsNotNull(actor);
        Assert.AreEqual("it-manager", actor.UserId);
        Assert.AreEqual(UserRole.TeamLead, actor.Role);
        Assert.IsTrue(actor.IsTeamLead);
    }

    [TestMethod] // AUTH-B26 — every malformed header leaves the caller anonymous, and the request still proceeds
    [DataRow("")]
    [DataRow("Bearer")]
    [DataRow("Bearer    ")]
    [DataRow("Basic YWRtaW46YWRtaW4=")]
    [DataRow("Bearer not-a-real-token")]
    [DataRow("it-manager")]
    public async Task MalformedOrUnknownAuthorization_LeavesCallerAnonymous(string header)
    {
        var (_, auth, _, _) = await AuthTestSupport.CreateAsync();
        await auth.LoginAsync("it-manager", AuthTestSupport.ManagerPassword);

        var (actor, nextCalled) = await RunAsync(auth, r => r.Headers.Authorization = header);

        Assert.IsNull(actor);
        Assert.IsTrue(nextCalled);
    }

    [TestMethod] // AUTH-B27
    public async Task SignedOutToken_LeavesCallerAnonymous()
    {
        var (_, auth, _, _) = await AuthTestSupport.CreateAsync();
        var login = await auth.LoginAsync("it-manager", AuthTestSupport.ManagerPassword);
        await auth.LogoutAsync(login.Token);

        var (actor, _) = await RunAsync(auth, r => r.Headers.Authorization = $"Bearer {login.Token}");

        Assert.IsNull(actor);
    }

    [TestMethod] // AUTH-B28
    public void GetBearerToken_IsCaseInsensitiveOnTheScheme()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "bearer abc123";
        Assert.AreEqual("abc123", ActorMiddleware.GetBearerToken(context.Request));
    }
}

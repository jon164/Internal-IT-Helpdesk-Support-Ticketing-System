using System.Text;
using Helpdesk.Api.Auth;
using Helpdesk.Api.Endpoints;
using Helpdesk.Api.Security;
using Helpdesk.Core.Domain;
using Helpdesk.Core.Security;
using Helpdesk.Data;
using Helpdesk.Tests.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.Tests.Dashboard;

/// <summary>
/// User story: "As an IT manager, I want manager-only visibility of the dashboard, so that
/// analytics aren't exposed to employees."
///   AC1  Given I am logged in as an Employee, when I attempt to view the dashboard, then access is denied.
///   AC2  Given I am logged in as a Manager, when I view the dashboard, then it displays.
/// </summary>
[TestClass]
public sealed class DashboardAccessPolicyTests
{
    [TestMethod] // DASH-B01 — decision table: one row per role, plus "no one signed in"
    [DataRow(UserRole.Requester, false, DisplayName = "Employee (Requester) is refused")]
    [DataRow(UserRole.Technician, false, DisplayName = "Technician is refused")]
    [DataRow(UserRole.TeamLead, true, DisplayName = "IT Manager (TeamLead) is allowed")]
    public void OnlyTheManagerRoleIsAllowed(UserRole role, bool expected)
    {
        Assert.AreEqual(expected, DashboardAccess.IsAllowed(new ActorContext("someone", role)));
    }

    [TestMethod] // DASH-B02
    public void NobodySignedIn_IsRefused()
    {
        Assert.IsFalse(DashboardAccess.IsAllowed(null));
    }

    [TestMethod] // DASH-B03 — fail closed: a role value the code has never heard of gets no access
    public void UnknownRoleValue_IsRefused()
    {
        Assert.IsFalse(DashboardAccess.IsAllowed(new ActorContext("someone", (UserRole)99)));
    }
}

/// <summary>
/// Runs the real dashboard endpoints — real routing metadata, real endpoint filter, real handlers —
/// behind the real <see cref="ActorMiddleware"/>, with callers signed in through the real
/// <see cref="AuthService"/>. Only the database (EF Core InMemory) and the HTTP transport
/// (<see cref="DefaultHttpContext"/>) are substitutes, so no extra test package is needed.
/// </summary>
[TestClass]
public sealed class DashboardEndpointAccessTests
{
    private const string StaffPassword = "Staff-Pass-1";
    private const string TechPassword = "Tech-Pass-1";
    private const string ManagerPassword = "Manager-Pass-1";

    private sealed record Response(int Status, string Body, string? CacheControl);

    private sealed class Harness
    {
        public required WebApplication App { get; init; }
        public required IReadOnlyList<RouteEndpoint> Endpoints { get; init; }

        public async Task<string> SignInAsync(string username, string password)
        {
            using var scope = App.Services.CreateScope();
            var login = await scope.ServiceProvider.GetRequiredService<AuthService>().LoginAsync(username, password);
            Assert.IsNotNull(login.Token, $"Test setup: {username} could not sign in ({login.Status}).");
            return login.Token;
        }

        public RouteEndpoint Find(string method, string route) =>
            Endpoints.Single(e => Method(e) == method && e.RoutePattern.RawText == route);

        public async Task<Response> CallAsync(RouteEndpoint endpoint, Action<HttpRequest>? arrange = null)
        {
            using var scope = App.Services.CreateScope();
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            http.Request.Method = Method(endpoint);
            http.Request.Path = endpoint.RoutePattern.RawText;
            http.Response.Body = new MemoryStream();
            arrange?.Invoke(http.Request);

            var pipeline = new ActorMiddleware(endpoint.RequestDelegate!);
            await pipeline.InvokeAsync(http, scope.ServiceProvider.GetRequiredService<AuthService>());

            http.Response.Body.Position = 0;
            var body = await new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEndAsync();
            return new Response(http.Response.StatusCode, body, http.Response.Headers.CacheControl.ToString());
        }

        public async Task<int> TicketCountAsync()
        {
            using var scope = App.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>().Tickets.CountAsync();
        }
    }

    private static string Method(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single();

    private static Action<HttpRequest> WithToken(string token) => r => r.Headers.Authorization = $"Bearer {token}";

    private static async Task<Harness> CreateAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<HelpdeskDbContext>(o => o.UseInMemoryDatabase(databaseName));
        builder.Services.AddSingleton(new AuthOptions());
        builder.Services.AddSingleton(AuthTestSupport.FastHasher());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<AuthService>();

        var app = builder.Build();
        app.MapDashboardEndpoints();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
            db.Users.AddRange(
                new UserAccount { Id = "airport-staff", DisplayName = "Airport Staff", Department = "Operations", Role = UserRole.Requester, PasswordHash = hasher.Hash(StaffPassword) },
                new UserAccount { Id = "it-technician", DisplayName = "IT Technician", Department = "IT Services", Role = UserRole.Technician, PasswordHash = hasher.Hash(TechPassword) },
                new UserAccount { Id = "it-manager", DisplayName = "IT Manager", Department = "IT Services", Role = UserRole.TeamLead, PasswordHash = hasher.Hash(ManagerPassword) });
            db.Tickets.AddRange(
                new Ticket { Title = "Gate 4 kiosk offline", Description = "Kiosk frozen", SubmitterName = "Sam", SubmitterEmail = "sam@airport.test", Priority = TicketPriority.Critical },
                new Ticket { Title = "Printer jam", Description = "Check-in printer", SubmitterName = "Ana", SubmitterEmail = "ana@airport.test", Status = TicketStatus.Resolved, ResolvedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        return new Harness { App = app, Endpoints = endpoints };
    }

    [TestMethod] // DASH-B04 — guards the other tests: if routes were renamed they must not pass by testing nothing
    public async Task DashboardGroup_ExposesTheExpectedRoutes()
    {
        var harness = await CreateAsync();

        CollectionAssert.AreEquivalent(
            new[] { "GET /api/dashboard", "GET /api/dashboard/tickets", "GET /api/dashboard/export", "POST /api/demo/performance-data", "DELETE /api/demo/performance-data" },
            harness.Endpoints.Select(e => $"{Method(e)} {e.RoutePattern.RawText}").ToArray());
    }

    [TestMethod] // DASH-B05 — AC1 at the API, on EVERY dashboard route, not just the summary
    [DataRow("airport-staff", StaffPassword, DisplayName = "Employee")]
    [DataRow("it-technician", TechPassword, DisplayName = "Technician")]
    public async Task SignedInNonManager_IsRefusedOnEveryDashboardRoute(string username, string password)
    {
        var harness = await CreateAsync();
        var token = await harness.SignInAsync(username, password);

        foreach (var endpoint in harness.Endpoints)
        {
            var response = await harness.CallAsync(endpoint, WithToken(token));

            Assert.AreEqual(StatusCodes.Status403Forbidden, response.Status, $"{Method(endpoint)} {endpoint.RoutePattern.RawText}");
            StringAssert.Contains(response.Body, DashboardAccess.ForbiddenMessage);
        }
    }

    [TestMethod] // DASH-B06 — AC1: "analytics aren't exposed" — the refusal carries no metric or ticket data
    public async Task EmployeeRefusal_ContainsNoAnalytics()
    {
        var harness = await CreateAsync();
        var token = await harness.SignInAsync("airport-staff", StaffPassword);

        foreach (var route in new[] { "/api/dashboard", "/api/dashboard/tickets", "/api/dashboard/export" })
        {
            var body = (await harness.CallAsync(harness.Find("GET", route), WithToken(token))).Body;

            foreach (var leak in new[] { "openBacklog", "averageResolutionMinutes", "overdueCount", "Gate 4 kiosk", "sam@airport.test", "reporterName" })
            {
                Assert.IsFalse(body.Contains(leak, StringComparison.OrdinalIgnoreCase), $"{route} leaked '{leak}': {body}");
            }
        }
    }

    [TestMethod] // DASH-B07 — refusal happens before the handler runs, so an employee cannot change data either
    public async Task EmployeeCannotGenerateOrDeleteDashboardData()
    {
        var harness = await CreateAsync();
        var token = await harness.SignInAsync("airport-staff", StaffPassword);

        await harness.CallAsync(harness.Find("POST", "/api/demo/performance-data"), WithToken(token));
        Assert.AreEqual(2, await harness.TicketCountAsync(), "POST must not have added tickets.");

        await harness.CallAsync(harness.Find("DELETE", "/api/demo/performance-data"), WithToken(token));
        Assert.AreEqual(2, await harness.TicketCountAsync(), "DELETE must not have removed tickets.");
    }

    [TestMethod] // DASH-B08 — not signed in, or signed in the old way, is 401 (not 403) on every route
    [DataRow("none", DisplayName = "No credentials")]
    [DataRow("X-User-Id", DisplayName = "Regression: X-User-Id naming the manager")]
    [DataRow("forged", DisplayName = "Forged bearer token")]
    public async Task UnauthenticatedCaller_IsAskedToSignIn(string kind)
    {
        var harness = await CreateAsync();
        Action<HttpRequest>? arrange = kind switch
        {
            "X-User-Id" => r => r.Headers["X-User-Id"] = "it-manager",
            "forged" => WithToken("not-a-real-token"),
            _ => null,
        };

        foreach (var endpoint in harness.Endpoints)
        {
            var response = await harness.CallAsync(endpoint, arrange);
            Assert.AreEqual(StatusCodes.Status401Unauthorized, response.Status, $"{Method(endpoint)} {endpoint.RoutePattern.RawText}");
        }
    }

    [TestMethod] // DASH-B09 — AC2: the manager sees the dashboard, with real figures
    public async Task Manager_SeesDashboardMetrics()
    {
        var harness = await CreateAsync();
        var token = await harness.SignInAsync("it-manager", ManagerPassword);

        var response = await harness.CallAsync(harness.Find("GET", "/api/dashboard"), WithToken(token));

        Assert.AreEqual(StatusCodes.Status200OK, response.Status);
        StringAssert.Contains(response.Body, "\"openBacklog\":1");
        StringAssert.Contains(response.Body, "\"overdueCount\":1");
        StringAssert.Contains(response.Body, "\"totalTickets\":2");
        Assert.AreEqual("no-store", response.CacheControl, "Analytics must not be cached by a shared browser or proxy.");
    }

    [TestMethod] // DASH-B10 — AC2: the manager's ticket table and CSV export also work
    public async Task Manager_SeesTicketTableAndCanExport()
    {
        var harness = await CreateAsync();
        var token = await harness.SignInAsync("it-manager", ManagerPassword);

        var table = await harness.CallAsync(harness.Find("GET", "/api/dashboard/tickets"), WithToken(token));
        var export = await harness.CallAsync(harness.Find("GET", "/api/dashboard/export"), WithToken(token));

        Assert.AreEqual(StatusCodes.Status200OK, table.Status);
        StringAssert.Contains(table.Body, "sam@airport.test");
        Assert.AreEqual(StatusCodes.Status200OK, export.Status);
        StringAssert.StartsWith(export.Body.TrimStart('﻿'), "id,terminal,area");
    }
}

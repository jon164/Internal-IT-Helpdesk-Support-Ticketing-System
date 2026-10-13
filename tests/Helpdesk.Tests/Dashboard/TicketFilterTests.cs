using System.Text;
using System.Text.Json;
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
/// User story: "As an IT manager, I want to filter tickets by status, priority, and assignee,
/// so that I can investigate bottlenecks or a technician's workload."
///   AC1  Given a status filter is applied, when I search, then only tickets with that status are returned.
///   AC2  Given no tickets match the filter, when I search, then an empty result is shown gracefully.
///
/// These tests exercise <see cref="TicketFilter"/> directly. Expected results are worked out
/// independently from the seed list with plain LINQ (the "oracle"), so a test cannot pass just
/// because the filter and the expectation share the same bug.
/// </summary>
[TestClass]
public sealed class TicketFilterTests
{
    private static readonly Technician Alex = new() { Name = "Alex Morgan" };
    private static readonly Technician Jordan = new() { Name = "Jordan Lee" };

    /// <summary>Every status appears, assigned and unassigned, so each equivalence class has members.</summary>
    private static List<Ticket> Seed()
    {
        var tickets = new List<Ticket>();
        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            tickets.Add(Make(status, TicketPriority.Low, Alex));
            tickets.Add(Make(status, TicketPriority.Critical, null));
        }

        tickets.Add(Make(TicketStatus.InProgress, TicketPriority.High, Jordan));
        tickets.Add(Make(TicketStatus.Resolved, TicketPriority.Medium, null));
        return tickets;
    }

    private static Ticket Make(TicketStatus status, TicketPriority priority, Technician? tech) => new()
    {
        Title = $"{status} {priority}",
        Status = status,
        Priority = priority,
        AssignedTechnician = tech,
        AssignedTechnicianId = tech?.Id,
    };

    private static List<Ticket> Run(string? status, string? priority, string? assignee)
    {
        Assert.IsTrue(TicketFilter.TryParse(status, priority, assignee, out var filter, out var error), error);
        return filter.Apply(Seed().AsQueryable()).ToList();
    }

    [TestMethod] // FILT-B01 — AC1, equivalence partitioning: one representative per status class
    [DataRow("New", TicketStatus.New)]
    [DataRow("InProgress", TicketStatus.InProgress)]
    [DataRow("WaitingOnUser", TicketStatus.PendingEmployeeResponse)]
    [DataRow("Resolved", TicketStatus.Resolved)]
    [DataRow("Closed", TicketStatus.Closed)]
    public void StatusFilter_ReturnsOnlyAndAllTicketsWithThatStatus(string uiStatus, TicketStatus expected)
    {
        var result = Run(uiStatus, null, null);

        Assert.IsTrue(result.Count > 0, "Test data must contain this status, or the check proves nothing.");
        Assert.IsTrue(result.All(t => t.Status == expected), "A ticket with another status leaked through.");
        Assert.AreEqual(Seed().Count(t => t.Status == expected), result.Count, "A matching ticket was dropped.");
    }

    [TestMethod] // FILT-B02 — equivalence partitioning on priority
    [DataRow("Low", TicketPriority.Low)]
    [DataRow("Medium", TicketPriority.Medium)]
    [DataRow("High", TicketPriority.High)]
    [DataRow("Critical", TicketPriority.Critical)]
    public void PriorityFilter_ReturnsOnlyAndAllTicketsWithThatPriority(string uiPriority, TicketPriority expected)
    {
        var result = Run(null, uiPriority, null);

        Assert.IsTrue(result.Count > 0);
        Assert.IsTrue(result.All(t => t.Priority == expected));
        Assert.AreEqual(Seed().Count(t => t.Priority == expected), result.Count);
    }

    [TestMethod] // FILT-B03 — a technician's workload; name matching ignores case and surrounding spaces
    [DataRow("Alex Morgan")]
    [DataRow("  alex MORGAN ")]
    public void AssigneeFilter_ReturnsThatTechniciansTickets(string assignee)
    {
        var result = Run(null, null, assignee);

        Assert.AreEqual(Seed().Count(t => t.AssignedTechnician?.Name == "Alex Morgan"), result.Count);
        Assert.IsTrue(result.All(t => t.AssignedTechnician?.Name == "Alex Morgan"));
    }

    [TestMethod] // FILT-B04 — regression: "Unassigned" used to be compared with technician names and always returned nothing
    public void UnassignedFilter_ReturnsTicketsWithNoTechnician()
    {
        var result = Run(null, null, "Unassigned");

        Assert.AreEqual(Seed().Count(t => t.AssignedTechnicianId == null), result.Count);
        Assert.IsTrue(result.All(t => t.AssignedTechnicianId == null));
    }

    [TestMethod] // FILT-B05 — filters combine with AND (bottleneck: "critical tickets still New")
    public void CombinedFilters_MustAllMatch()
    {
        var result = Run("InProgress", "High", "Jordan Lee");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(TicketStatus.InProgress, result[0].Status);
        Assert.AreEqual(TicketPriority.High, result[0].Priority);
        Assert.AreEqual("Jordan Lee", result[0].AssignedTechnician!.Name);
    }

    [TestMethod] // FILT-B06 — AC2: no match gives an empty list, not null and not an exception
    public void NoMatch_ReturnsEmptyList()
    {
        var result = Run("Closed", "High", "Jordan Lee");

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod] // FILT-B07 — AC2: a technician with no tickets (zero workload) is a valid, empty answer
    public void TechnicianWithNoTickets_ReturnsEmptyList()
    {
        Assert.AreEqual(0, Run(null, null, "Priya Shah").Count);
    }

    [TestMethod] // FILT-B08 — "All", blank and missing all mean "no filter"
    [DataRow((string?)null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("All")]
    [DataRow("all")]
    public void AllOrBlank_DoesNotFilter(string? value)
    {
        Assert.IsTrue(TicketFilter.TryParse(value, value, value, out var filter, out _));
        Assert.IsTrue(filter.IsEmpty);
        Assert.AreEqual(Seed().Count, filter.Apply(Seed().AsQueryable()).Count());
    }

    [TestMethod] // FILT-B09 — invalid class: unknown status is rejected, never guessed.
    // Regression: an unknown status used to become "New", so a typo returned the wrong tickets.
    // "4" and "-1" check that numbers are not accepted as enum shortcuts (Enum.TryParse would accept "4").
    [DataRow("Bogus")]
    [DataRow("Pending")]
    [DataRow("4")]
    [DataRow("-1")]
    [DataRow("New,Closed")]
    public void UnknownStatus_IsRejected(string status)
    {
        Assert.IsFalse(TicketFilter.TryParse(status, null, null, out var filter, out var error));
        StringAssert.Contains(error, "Unknown status");
        Assert.AreEqual(TicketFilter.None, filter);
    }

    [TestMethod] // FILT-B10 — invalid class for priority (it used to be silently ignored, returning every ticket)
    [DataRow("Urgent")]
    [DataRow("3")]
    public void UnknownPriority_IsRejected(string priority)
    {
        Assert.IsFalse(TicketFilter.TryParse(null, priority, null, out _, out var error));
        StringAssert.Contains(error, "Unknown priority");
    }

    [TestMethod] // FILT-B11 — security: a huge bad value is not reflected back whole in the error
    public void VeryLongBadValue_IsCutShortInTheError()
    {
        var huge = new string('x', 500);

        Assert.IsFalse(TicketFilter.TryParse(huge, null, null, out _, out var error));
        Assert.IsFalse(error!.Contains(huge));
        Assert.IsTrue(error.Length < 200, $"Error was {error.Length} characters.");
    }
}

/// <summary>
/// Runs the real /api/dashboard/tickets and /api/dashboard/export handlers (with the real manager
/// filter and real sign-in) against an EF Core InMemory database. Same approach as
/// <see cref="DashboardEndpointAccessTests"/>.
/// </summary>
[TestClass]
public sealed class DashboardFilterEndpointTests
{
    private const string StaffPassword = "Staff-Pass-1";
    private const string ManagerPassword = "Manager-Pass-1";

    private sealed record Response(int Status, string Body);

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

        public async Task<Response> GetAsync(string route, string query, string token)
        {
            var endpoint = Endpoints.Single(e => e.RoutePattern.RawText == route
                && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));

            using var scope = App.Services.CreateScope();
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            http.Request.Method = "GET";
            http.Request.Path = route;
            http.Request.QueryString = new QueryString(query);
            http.Request.Headers.Authorization = $"Bearer {token}";
            http.Response.Body = new MemoryStream();

            var pipeline = new ActorMiddleware(endpoint.RequestDelegate!);
            await pipeline.InvokeAsync(http, scope.ServiceProvider.GetRequiredService<AuthService>());

            http.Response.Body.Position = 0;
            var body = await new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEndAsync();
            return new Response(http.Response.StatusCode, body);
        }
    }

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
                new UserAccount { Id = "it-manager", DisplayName = "IT Manager", Department = "IT Services", Role = UserRole.TeamLead, PasswordHash = hasher.Hash(ManagerPassword) });

            var alex = new Technician { Name = "Alex Morgan", Email = "alex@airport.test" };
            db.Technicians.Add(alex);
            db.Tickets.AddRange(
                new Ticket { Title = "Gate kiosk", SubmitterName = "Sam", SubmitterEmail = "sam@airport.test", Status = TicketStatus.New, Priority = TicketPriority.Critical },
                new Ticket { Title = "Printer jam", SubmitterName = "Ana", SubmitterEmail = "ana@airport.test", Status = TicketStatus.Resolved, Priority = TicketPriority.Low, AssignedTechnicianId = alex.Id, ResolvedAt = DateTime.UtcNow },
                new Ticket { Title = "Wi-Fi drop", SubmitterName = "Lee", SubmitterEmail = "lee@airport.test", Status = TicketStatus.Resolved, Priority = TicketPriority.High, ResolvedAt = DateTime.UtcNow },
                new Ticket { Title = "Badge reader", SubmitterName = "Kim", SubmitterEmail = "kim@airport.test", Status = TicketStatus.InProgress, Priority = TicketPriority.Medium, AssignedTechnicianId = alex.Id });
            await db.SaveChangesAsync();
        }

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        return new Harness { App = app, Endpoints = endpoints };
    }

    private static List<JsonElement> Rows(string body) =>
        JsonDocument.Parse(body).RootElement.EnumerateArray().ToList();

    [TestMethod] // FILT-B12 — AC1 through the real API: only Resolved tickets come back
    public async Task StatusFilter_OverTheApi_ReturnsOnlyThatStatus()
    {
        var harness = await CreateAsync();
        var token = await harness.SignInAsync("it-manager", ManagerPassword);

        var response = await harness.GetAsync("/api/dashboard/tickets", "?status=Resolved", token);

        Assert.AreEqual(StatusCodes.Status200OK, response.Status, response.Body);
        var rows = Rows(response.Body);
        Assert.AreEqual(2, rows.Count);
        Assert.IsTrue(rows.All(r => r.GetProperty("status").GetString() == "Resolved"));
    }

    [TestMethod] // FILT-B13 — AC2 through the real API: no match is 200 with [], not an error status
    public async Task NoMatch_OverTheApi_Returns200AndEmptyArray()
    {
        var harness = await CreateAsync();
        var token = await harness.SignInAsync("it-manager", ManagerPassword);

        var response = await harness.GetAsync("/api/dashboard/tickets", "?status=Closed", token);

        Assert.AreEqual(StatusCodes.Status200OK, response.Status, response.Body);
        Assert.AreEqual(0, Rows(response.Body).Count);
    }

    [TestMethod] // FILT-B14 — regression through the API: Unassigned returns the unassigned tickets
    public async Task UnassignedFilter_OverTheApi_ReturnsOnlyUnassigned()
    {
        var harness = await CreateAsync();
        var token = await harness.SignInAsync("it-manager", ManagerPassword);

        var rows = Rows((await harness.GetAsync("/api/dashboard/tickets", "?assignee=Unassigned", token)).Body);

        Assert.AreEqual(2, rows.Count);
        Assert.IsTrue(rows.All(r => r.GetProperty("assignee").GetString() == "Unassigned"));
    }

    [TestMethod] // FILT-B15 — invalid value is a clear 400 with a message, and no ticket data
    public async Task UnknownStatus_OverTheApi_Is400WithMessage()
    {
        var harness = await CreateAsync();
        var token = await harness.SignInAsync("it-manager", ManagerPassword);

        var response = await harness.GetAsync("/api/dashboard/tickets", "?status=Bogus", token);

        Assert.AreEqual(StatusCodes.Status400BadRequest, response.Status);
        StringAssert.Contains(response.Body, "Unknown status");
        Assert.IsFalse(response.Body.Contains("reporterName"), "An error must not carry ticket rows.");
    }

    [TestMethod] // FILT-B16 — the CSV export applies exactly the same filter as the table
    public async Task Export_UsesTheSameFilterAsTheTable()
    {
        var harness = await CreateAsync();
        var token = await harness.SignInAsync("it-manager", ManagerPassword);

        var table = Rows((await harness.GetAsync("/api/dashboard/tickets", "?status=Resolved&assignee=Alex%20Morgan", token)).Body);
        var csv = (await harness.GetAsync("/api/dashboard/export", "?status=Resolved&assignee=Alex%20Morgan", token)).Body;
        var csvRows = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList(); // skip header

        Assert.AreEqual(1, table.Count);
        Assert.AreEqual(table.Count, csvRows.Count);
        Assert.IsTrue(csvRows.All(line => line.Contains(",Resolved,") && line.Contains(",Alex Morgan,")));
    }

    [TestMethod] // FILT-B17 — security: adding filters does not get round the manager-only rule, and the
    // access check runs before validation, so a non-manager learns nothing from a 400 either
    [DataRow("?status=Resolved")]
    [DataRow("?status=Bogus")]
    public async Task NonManager_WithFilters_IsStill403(string query)
    {
        var harness = await CreateAsync();
        var token = await harness.SignInAsync("airport-staff", StaffPassword);

        var response = await harness.GetAsync("/api/dashboard/tickets", query, token);

        Assert.AreEqual(StatusCodes.Status403Forbidden, response.Status);
        Assert.IsFalse(response.Body.Contains("ana@airport.test"), "A refusal must not carry ticket data.");
    }
}

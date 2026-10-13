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
/// USER STORY: As an IT manager, I want tickets breaching their priority SLA flagged,
/// so that I can intervene before service levels slip.
///   AC1 Given a Critical ticket open longer than its SLA window, when I view the overdue metric,
///       then it is counted as overdue.
///   AC2 Given a ticket within its SLA window, when I view the metric, then it is not counted.
///
/// Unit tests on <see cref="SlaPolicy"/> with fixed instants: no real clock is read, so every
/// boundary is hit exactly (to the second) and the tests give the same answer on any day.
/// </summary>
[TestClass]
public sealed class SlaPolicyTests
{
    // Monday 12 Oct 2026, 13:00 in Auckland. Continuous-clock tests don't depend on the weekday.
    private static readonly DateTime Created = new(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>An Auckland wall-clock time, as UTC.</summary>
    private static DateTime Nz(int year, int month, int day, int hour, int minute = 0, int second = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified), SlaPolicy.BusinessTimeZone);

    private static Ticket Open(TicketPriority priority, DateTime createdUtc, TicketStatus status = TicketStatus.InProgress) =>
        new() { Title = "t", Priority = priority, Status = status, CreatedAt = createdUtc };

    [TestMethod] // SLA-B01 — AC1/AC2 boundary-value analysis on the 4-hour Critical window
    [DataRow(-1, false, DisplayName = "Critical, 3h59m59s open: within SLA (AC2)")]
    [DataRow(0, false, DisplayName = "Critical, exactly 4h open: still within SLA")]
    [DataRow(1, true, DisplayName = "Critical, 4h00m01s open: overdue (AC1)")]
    public void Critical_BoundaryOfFourHours(int secondsFromWindow, bool expected)
    {
        var now = Created.AddHours(4).AddSeconds(secondsFromWindow);
        Assert.AreEqual(expected, SlaPolicy.IsOverdue(Open(TicketPriority.Critical, Created), now));
    }

    [TestMethod] // SLA-B02 — boundary-value analysis on the 8-hour High window
    [DataRow(-1, false, DisplayName = "High, 7h59m59s: within SLA")]
    [DataRow(0, false, DisplayName = "High, exactly 8h: within SLA")]
    [DataRow(1, true, DisplayName = "High, 8h00m01s: overdue")]
    public void High_BoundaryOfEightHours(int secondsFromWindow, bool expected)
    {
        var now = Created.AddHours(8).AddSeconds(secondsFromWindow);
        Assert.AreEqual(expected, SlaPolicy.IsOverdue(Open(TicketPriority.High, Created), now));
    }

    [TestMethod] // SLA-B03 — equivalence partitioning on status: open statuses count, finished ones never do
    [DataRow(TicketStatus.New, true)]
    [DataRow(TicketStatus.InProgress, true)]
    [DataRow(TicketStatus.PendingEmployeeResponse, true)]
    [DataRow(TicketStatus.Resolved, false)]
    [DataRow(TicketStatus.Closed, false)]
    public void OnlyOpenTicketsCanBeOverdue(TicketStatus status, bool expected)
    {
        var ticket = Open(TicketPriority.Critical, Created, status);
        Assert.AreEqual(expected, SlaPolicy.IsOverdue(ticket, Created.AddHours(5)));
    }

    [TestMethod] // SLA-B04 — equivalence partitioning on priority: same 5-hour age, only the window differs
    [DataRow(TicketPriority.Critical, true)]
    [DataRow(TicketPriority.High, false)]
    [DataRow(TicketPriority.Medium, false)]
    [DataRow(TicketPriority.Low, false)]
    public void SameAge_JudgedAgainstEachPrioritysOwnWindow(TicketPriority priority, bool expected)
    {
        Assert.AreEqual(expected, SlaPolicy.IsOverdue(Open(priority, Created), Created.AddHours(5)));
    }

    [TestMethod] // SLA-B05 — Medium (3 working days = 36 business hours); nights are off the clock
    public void Medium_BoundaryOfThreeWorkingDays()
    {
        var ticket = Open(TicketPriority.Medium, Nz(2026, 10, 12, 7)); // Monday 07:00

        Assert.IsFalse(SlaPolicy.IsOverdue(ticket, Nz(2026, 10, 14, 18, 59, 59)), "Wed 18:59:59 is 1s inside");
        Assert.IsFalse(SlaPolicy.IsOverdue(ticket, Nz(2026, 10, 14, 19)), "Wed 19:00 is exactly 36 business hours");
        Assert.IsFalse(SlaPolicy.IsOverdue(ticket, Nz(2026, 10, 15, 7)), "Wed night is not business time");
        Assert.IsTrue(SlaPolicy.IsOverdue(ticket, Nz(2026, 10, 15, 7, 0, 1)), "Thu 07:00:01 is 1s past");
    }

    [TestMethod] // SLA-B06 — Low (10 working days); weekends are off the clock
    public void Low_BoundaryOfTenWorkingDays()
    {
        var ticket = Open(TicketPriority.Low, Nz(2026, 10, 5, 7)); // Monday 07:00

        Assert.IsFalse(SlaPolicy.IsOverdue(ticket, Nz(2026, 10, 16, 19)), "Fri of week 2, 19:00: exactly 10 working days");
        Assert.IsFalse(SlaPolicy.IsOverdue(ticket, Nz(2026, 10, 19, 7)), "the weekend added nothing");
        Assert.IsTrue(SlaPolicy.IsOverdue(ticket, Nz(2026, 10, 19, 7, 0, 1)), "Mon 07:00:01 is 1s past");
    }

    [TestMethod] // SLA-B07 — business clock: partial days at both ends, weekend skipped
    public void BusinessTime_CountsOnlyWeekdayWorkingHours()
    {
        // Friday 18:00 -> Monday 08:00 = 1h on Friday + 1h on Monday.
        Assert.AreEqual(TimeSpan.FromHours(2), SlaPolicy.BusinessTimeBetween(Nz(2026, 10, 16, 18), Nz(2026, 10, 19, 8)));
        // Raised Saturday, checked Sunday: no business time at all.
        Assert.AreEqual(TimeSpan.Zero, SlaPolicy.BusinessTimeBetween(Nz(2026, 10, 17, 10), Nz(2026, 10, 18, 16)));
        // Raised before opening, checked after closing on the same day: one full working day.
        Assert.AreEqual(TimeSpan.FromHours(12), SlaPolicy.BusinessTimeBetween(Nz(2026, 10, 13, 5), Nz(2026, 10, 13, 22)));
    }

    [TestMethod] // SLA-B08 — daylight saving began Sun 27 Sep 2026; working hours must not gain or lose an hour
    public void BusinessTime_AcrossDaylightSavingChange()
    {
        // Fri 25 Sep 07:00 NZST -> Mon 28 Sep 19:00 NZDT = two full working days.
        Assert.AreEqual(TimeSpan.FromHours(24), SlaPolicy.BusinessTimeBetween(Nz(2026, 9, 25, 7), Nz(2026, 9, 28, 19)));
    }

    [TestMethod] // SLA-B09 — robustness: a creation time in the future is zero elapsed, never overdue
    public void FutureCreationTime_IsNotOverdue()
    {
        var ticket = Open(TicketPriority.Critical, Created.AddHours(1));
        Assert.AreEqual(TimeSpan.Zero, SlaPolicy.Elapsed(ticket, Created));
        Assert.IsFalse(SlaPolicy.IsOverdue(ticket, Created));
    }

    [TestMethod] // SLA-B10 — SQLite returns Kind=Unspecified; it must be read as UTC, not local time
    public void UnspecifiedKindCreationTime_IsTreatedAsUtc()
    {
        var unspecified = DateTime.SpecifyKind(Created, DateTimeKind.Unspecified);
        var ticket = Open(TicketPriority.Critical, unspecified);
        Assert.AreEqual(TimeSpan.FromHours(4), SlaPolicy.Elapsed(ticket, Created.AddHours(4)));
    }

    [TestMethod] // SLA-B11 — fail visible: an unknown priority value gets the strictest window, not none
    public void UnknownPriority_UsesStrictestWindow()
    {
        Assert.AreEqual(SlaPolicy.ResolutionWindow(TicketPriority.Critical), SlaPolicy.ResolutionWindow((TicketPriority)99));
        Assert.IsTrue(SlaPolicy.IsOverdue(Open((TicketPriority)99, Created), Created.AddHours(5)));
    }
}

/// <summary>
/// Runs the real dashboard endpoints with a clock the test controls (<see cref="FakeClock"/>),
/// signed in as the IT Manager through the real <see cref="AuthService"/> and
/// <see cref="ActorMiddleware"/>. Proves the metric the manager sees follows <see cref="SlaPolicy"/>,
/// and that the metric, the table and the CSV export agree with each other.
/// </summary>
[TestClass]
public sealed class SlaOverdueEndpointTests
{
    private const string ManagerPassword = "Manager-Pass-1";

    private sealed class Harness
    {
        public required WebApplication App { get; init; }
        public required FakeClock Clock { get; init; }
        public required string Token { get; init; }

        public async Task<string> GetAsync(string route)
        {
            var endpoint = ((IEndpointRouteBuilder)App).DataSources
                .SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
                .Single(e => e.RoutePattern.RawText == route && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));

            using var scope = App.Services.CreateScope();
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            http.Request.Method = "GET";
            http.Request.Path = route;
            http.Request.Headers.Authorization = $"Bearer {Token}";
            http.Response.Body = new MemoryStream();

            await new ActorMiddleware(endpoint.RequestDelegate!).InvokeAsync(http, scope.ServiceProvider.GetRequiredService<AuthService>());

            Assert.AreEqual(StatusCodes.Status200OK, http.Response.StatusCode, route);
            http.Response.Body.Position = 0;
            return (await new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEndAsync()).TrimStart('﻿');
        }

        public async Task<int> OverdueCountAsync()
        {
            using var json = JsonDocument.Parse(await GetAsync("/api/dashboard"));
            return json.RootElement.GetProperty("overdueCount").GetInt32();
        }
    }

    private static async Task<Harness> CreateAsync(params Ticket[] tickets)
    {
        var clock = new FakeClock();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<HelpdeskDbContext>(o => o.UseInMemoryDatabase(databaseName));
        builder.Services.AddSingleton(new AuthOptions());
        builder.Services.AddSingleton(AuthTestSupport.FastHasher());
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddScoped<AuthService>();

        var app = builder.Build();
        app.MapDashboardEndpoints();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
            db.Users.Add(new UserAccount { Id = "it-manager", DisplayName = "IT Manager", Department = "IT Services", Role = UserRole.TeamLead, PasswordHash = hasher.Hash(ManagerPassword) });
            db.Tickets.AddRange(tickets);
            await db.SaveChangesAsync();
        }

        string token;
        using (var scope = app.Services.CreateScope())
        {
            var login = await scope.ServiceProvider.GetRequiredService<AuthService>().LoginAsync("it-manager", ManagerPassword);
            Assert.IsNotNull(login.Token, "Test setup: the manager could not sign in.");
            token = login.Token;
        }

        return new Harness { App = app, Clock = clock, Token = token };
    }

    // FakeClock starts at 2026-10-12 09:00 UTC; tickets are dated relative to that.
    private static readonly DateTime Start = new FakeClock().GetUtcNow().UtcDateTime;

    private static Ticket T(TicketPriority priority, TimeSpan age, TicketStatus status = TicketStatus.InProgress, string who = "Sam") =>
        new() { Title = $"{priority} {age}", Description = "d", SubmitterName = who, SubmitterEmail = $"{who}@airport.test", Priority = priority, Status = status, CreatedAt = Start - age };

    [TestMethod] // SLA-B12 — AC1: a Critical ticket past 4 hours is counted on the metric
    public async Task CriticalPastWindow_IsCounted()
    {
        var harness = await CreateAsync(T(TicketPriority.Critical, TimeSpan.FromHours(4) + TimeSpan.FromMinutes(1)));
        Assert.AreEqual(1, await harness.OverdueCountAsync());
    }

    [TestMethod] // SLA-B13 — AC2: a ticket inside its window is not counted
    public async Task CriticalWithinWindow_IsNotCounted()
    {
        var harness = await CreateAsync(T(TicketPriority.Critical, TimeSpan.FromHours(4) - TimeSpan.FromMinutes(1)));
        Assert.AreEqual(0, await harness.OverdueCountAsync());
    }

    [TestMethod] // SLA-B14 — the same ticket moves onto the metric when the clock crosses its window
    public async Task SameTicket_BecomesOverdueWhenClockPassesWindow()
    {
        var harness = await CreateAsync(T(TicketPriority.Critical, TimeSpan.FromHours(3)));
        Assert.AreEqual(0, await harness.OverdueCountAsync(), "3h old");

        harness.Clock.Advance(TimeSpan.FromHours(1));
        Assert.AreEqual(0, await harness.OverdueCountAsync(), "exactly 4h old");

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(1, await harness.OverdueCountAsync(), "4h00m01s old");
    }

    [TestMethod] // SLA-B15 — regression: the old rule counted every open Critical and ignored all other priorities
    public async Task Regression_OldRuleCases()
    {
        var harness = await CreateAsync(
            T(TicketPriority.Critical, TimeSpan.FromMinutes(1)),  // old rule: counted   / now: not counted
            T(TicketPriority.High, TimeSpan.FromHours(9)));       // old rule: ignored   / now: counted

        Assert.AreEqual(1, await harness.OverdueCountAsync());
        using var rows = JsonDocument.Parse(await harness.GetAsync("/api/dashboard/tickets"));
        var flagged = rows.RootElement.EnumerateArray().Where(r => r.GetProperty("slaOverdue").GetBoolean()).Select(r => r.GetProperty("priority").GetString()).ToList();
        CollectionAssert.AreEqual(new[] { "High" }, flagged);
    }

    [TestMethod] // SLA-B16 — maintainability: metric, table and CSV export give the same answer for a mixed set
    public async Task MetricTableAndExport_Agree()
    {
        var harness = await CreateAsync(
            T(TicketPriority.Critical, TimeSpan.FromHours(5)),                          // overdue
            T(TicketPriority.Critical, TimeSpan.FromHours(2)),                          // within
            T(TicketPriority.Critical, TimeSpan.FromHours(30), TicketStatus.Resolved),  // finished
            T(TicketPriority.High, TimeSpan.FromHours(10), TicketStatus.PendingEmployeeResponse), // overdue
            T(TicketPriority.Medium, TimeSpan.FromHours(1)),                            // within
            T(TicketPriority.Low, TimeSpan.FromDays(30)));                              // overdue

        var metric = await harness.OverdueCountAsync();

        using var rows = JsonDocument.Parse(await harness.GetAsync("/api/dashboard/tickets"));
        var tableCount = rows.RootElement.EnumerateArray().Count(r => r.GetProperty("slaOverdue").GetBoolean());

        var csv = await harness.GetAsync("/api/dashboard/export");
        var csvCount = csv.Split('\n').Skip(1).Count(line => line.TrimEnd('\r').EndsWith(",True"));

        Assert.AreEqual(3, metric);
        Assert.AreEqual(metric, tableCount, "table disagrees with metric");
        Assert.AreEqual(metric, csvCount, "CSV export disagrees with metric");
    }
}

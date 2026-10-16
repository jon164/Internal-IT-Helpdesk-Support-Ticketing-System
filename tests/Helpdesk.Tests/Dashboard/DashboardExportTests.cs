using System.Globalization;
using System.Text;
using System.Text.Json;
using Helpdesk.Api.Auth;
using Helpdesk.Api.Endpoints;
using Helpdesk.Api.Security;
using Helpdesk.Core.Domain;
using Helpdesk.Core.Reporting;
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
/// USER STORY: As an IT manager, I want to export the dashboard summary, so that I can include
/// service data in reports to senior management.
///   AC1 Given the dashboard is displayed, when I choose export, then a file (e.g. CSV) of the
///       current metrics is produced.
///   AC2 Given no ticket data exists, when I export, then a valid file with headers and zero rows
///       is produced.
///
/// Files are checked with <see cref="CsvTestParser"/>, a small RFC 4180 reader written only for
/// these tests, instead of string.Split: a file that only "looks" right when split on commas
/// (the old export) fails here, the same way it would in Excel.
/// </summary>
[TestClass]
public sealed class CsvWriterTests
{
    [TestMethod] // EXP-B01 — equivalence class: ordinary text is written unchanged
    public void PlainText_IsUnchanged()
    {
        Assert.AreEqual("Alex Morgan", Csv.Field("Alex Morgan"));
        Assert.AreEqual(string.Empty, Csv.Field(null));
    }

    [TestMethod] // EXP-B02 — reliability: characters that break columns or rows are quoted
    [DataRow("Lee, Ana", "\"Lee, Ana\"", DisplayName = "comma")]
    [DataRow("The \"kiosk\"", "\"The \"\"kiosk\"\"\"", DisplayName = "double quote is doubled")]
    [DataRow("line1\nline2", "\"line1\nline2\"", DisplayName = "line feed")]
    [DataRow("line1\r\nline2", "\"line1\r\nline2\"", DisplayName = "CR LF")]
    public void SpecialCharacters_AreQuoted(string input, string expected)
    {
        Assert.AreEqual(expected, Csv.Field(input));
    }

    [TestMethod] // EXP-B03 — security: text that a spreadsheet would run as a formula is made inert
    [DataRow("=HYPERLINK(\"http://evil.test\",\"Click\")", DisplayName = "=")]
    [DataRow("+1+1", DisplayName = "+")]
    [DataRow("-2+3", DisplayName = "- (not a plain number)")]
    [DataRow("@SUM(A1:A9)", DisplayName = "@")]
    [DataRow("\t=1", DisplayName = "tab")]
    [DataRow("\r=1", DisplayName = "carriage return")]
    public void FormulaLikeText_IsPrefixedWithApostrophe(string input)
    {
        var parsed = CsvTestParser.Parse(Csv.Field(input) + Csv.LineEnding).Single().Single();
        Assert.AreEqual("'" + input, parsed);
    }

    [TestMethod] // EXP-B04 — boundary of EXP-B03: real numbers keep their sign, so numeric columns stay numeric
    [DataRow("-3")]
    [DataRow("-0.25")]
    [DataRow("+5")]
    [DataRow("0")]
    public void PlainNumbers_AreNotPrefixed(string input)
    {
        Assert.AreEqual(input, Csv.Field(input));
    }

    [TestMethod] // EXP-B05 — AC2 at the lowest level: no rows gives exactly the header line
    public void NoRows_GivesHeaderOnly()
    {
        var document = Csv.Document(["a", "b"], []);

        Assert.AreEqual("a,b\r\n", document);
        var rows = CsvTestParser.Parse(document);
        Assert.HasCount(1, rows);
        CollectionAssert.AreEqual(new[] { "a", "b" }, rows[0]);
    }

    [TestMethod] // EXP-B06 — compatibility: UTF-8 BOM so Excel reads non-English names; CRLF on every OS
    public void Bytes_StartWithUtf8Bom_AndLinesEndInCrlf()
    {
        var bytes = Csv.ToBytes(Csv.Document(["name"], [["Zoë"], ["Māori"]]));

        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        Assert.AreEqual("name\r\nZoë\r\nMāori\r\n", text);
    }

    [TestMethod] // EXP-B07 — compatibility: numbers use "." even when the server's culture uses ","
    public void Numbers_IgnoreServerCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.AreEqual("1234.5", Csv.Number(1234.5));
            Assert.AreEqual("780", Csv.Number(780.0));
            Assert.AreEqual("12", Csv.Number(12));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod] // EXP-B08 — a missing value is a blank cell, not 0
    public void MissingNumber_IsBlank()
    {
        Assert.AreEqual(string.Empty, Csv.Number((double?)null));
    }

    [TestMethod] // EXP-B09 — maintainability: a row with the wrong number of fields is a bug, not a silently shifted file
    public void RowWithWrongFieldCount_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Csv.Document(["a", "b"], [["only one"]]));
    }
}

/// <summary>
/// <see cref="DashboardSummary"/> on fixed tickets and a fixed clock. Expected values are worked
/// out by hand from the fixture below, not by calling the code under test.
/// </summary>
[TestClass]
public sealed class DashboardSummaryTests
{
    // Monday 12 Oct 2026 09:00 UTC (22:00 in Auckland).
    internal static readonly DateTime Now = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// 1 Critical InProgress 5h old           open, overdue            Incident
    /// 2 Critical Resolved after 120 min      resolved                 Incident
    /// 3 High     New 1h old                  open, within SLA         ServiceRequest
    /// 4 Low      Closed, resolved after 1440 resolved                 ServiceRequest
    /// 5 Low      New 1h old                  open, within SLA         Incident
    ///
    /// Total 5, open 3, overdue 1, resolved 2, average (120 + 1440) / 2 = 780 min,
    /// most common category Incident (3). No Medium tickets.
    /// </summary>
    internal static List<Ticket> MixedTickets(string reporter = "Sam") =>
    [
        new() { Title = "1", SubmitterName = reporter, Priority = TicketPriority.Critical, Status = TicketStatus.InProgress, Type = TicketType.Incident, CreatedAt = Now.AddHours(-5) },
        new() { Title = "2", SubmitterName = "Ana", Priority = TicketPriority.Critical, Status = TicketStatus.Resolved, Type = TicketType.Incident, CreatedAt = Now.AddHours(-10), ResolvedAt = Now.AddHours(-8) },
        new() { Title = "3", SubmitterName = "Lee", Priority = TicketPriority.High, Status = TicketStatus.New, Type = TicketType.ServiceRequest, CreatedAt = Now.AddHours(-1) },
        new() { Title = "4", SubmitterName = "Kim", Priority = TicketPriority.Low, Status = TicketStatus.Closed, Type = TicketType.ServiceRequest, CreatedAt = Now.AddDays(-3), ResolvedAt = Now.AddDays(-2), ClosedAt = Now.AddDays(-1) },
        new() { Title = "5", SubmitterName = "Jo", Priority = TicketPriority.Low, Status = TicketStatus.New, Type = TicketType.Incident, CreatedAt = Now.AddHours(-1) },
    ];

    [TestMethod] // EXP-B10 — AC2 boundary: no tickets gives zeros and blanks, never invented values
    public void NoTickets_GivesZerosAndNoInventedValues()
    {
        var summary = DashboardSummary.From([], Now);

        Assert.AreEqual(new DashboardSummary(0, 0, 0, 0, null, null, 0), summary);
        Assert.IsEmpty(DashboardSummary.ByPriority([], Now));
    }

    [TestMethod] // EXP-B11 — AC1: the figures for a mixed set
    public void MixedTickets_GiveHandWorkedFigures()
    {
        var summary = DashboardSummary.From(MixedTickets(), Now);

        Assert.AreEqual(new DashboardSummary(
            TotalTickets: 5, OpenBacklog: 3, OverdueCount: 1, ResolvedCount: 2,
            AverageResolutionMinutes: 780, MostCommonCategory: TicketType.Incident, MostCommonCategoryCount: 3), summary);
    }

    [TestMethod] // EXP-B12 — by priority: most urgent first, empty priorities left out, per-row figures right
    public void ByPriority_MostUrgentFirst_SkipsEmptyPriorities()
    {
        var rows = DashboardSummary.ByPriority(MixedTickets(), Now);

        CollectionAssert.AreEqual(
            new[]
            {
                new PriorityBreakdownRow(TicketPriority.Critical, 2, 1, 1, 1, 120),
                new PriorityBreakdownRow(TicketPriority.High, 1, 1, 0, 0, null),
                new PriorityBreakdownRow(TicketPriority.Low, 2, 1, 0, 1, 1440),
            },
            rows.ToArray());
    }

    [TestMethod] // EXP-B13 — consistency: the priority rows add up to the summary totals
    public void PriorityRows_AddUpToSummary()
    {
        var tickets = MixedTickets();
        var summary = DashboardSummary.From(tickets, Now);
        var rows = DashboardSummary.ByPriority(tickets, Now);

        Assert.AreEqual(summary.TotalTickets, rows.Sum(r => r.TotalTickets));
        Assert.AreEqual(summary.OpenBacklog, rows.Sum(r => r.OpenTickets));
        Assert.AreEqual(summary.OverdueCount, rows.Sum(r => r.OverdueTickets));
        Assert.AreEqual(summary.ResolvedCount, rows.Sum(r => r.ResolvedTickets));
    }

    [TestMethod] // EXP-B14 — file names are dated in New Zealand time: boundary either side of local midnight (NZDT, UTC+13)
    [DataRow("2026-10-15T10:59:59Z", "airport-helpdesk-summary-2026-10-15.csv", DisplayName = "23:59:59 NZDT")]
    [DataRow("2026-10-15T11:00:00Z", "airport-helpdesk-summary-2026-10-16.csv", DisplayName = "00:00:00 NZDT next day")]
    public void FileName_UsesNewZealandDate(string utc, string expected)
    {
        var now = DateTime.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        Assert.AreEqual(expected, DashboardExport.FileName("summary", now));
    }
}

/// <summary>
/// Runs the real export handlers (with the real manager filter and real sign-in) against an
/// EF Core InMemory database and a fake clock, as the other dashboard endpoint tests do.
/// </summary>
[TestClass]
public sealed class DashboardExportEndpointTests
{
    private const string ManagerPassword = "Manager-Pass-1";
    private const string StaffPassword = "Staff-Pass-1";

    private static readonly string[] ExportRoutes = ["/api/dashboard/export", "/api/dashboard/export/summary", "/api/dashboard/export/by-priority"];

    private sealed record Download(int Status, string? ContentType, string? ContentDisposition, byte[] Bytes)
    {
        public string Text => new UTF8Encoding(false).GetString(Bytes);
        public List<string[]> Rows => CsvTestParser.Parse(Text.TrimStart('﻿'));
    }

    private sealed class Harness
    {
        public required WebApplication App { get; init; }

        public async Task<string> SignInAsync(string username, string password)
        {
            using var scope = App.Services.CreateScope();
            var login = await scope.ServiceProvider.GetRequiredService<AuthService>().LoginAsync(username, password);
            Assert.IsNotNull(login.Token, $"Test setup: {username} could not sign in.");
            return login.Token;
        }

        public async Task<Download> GetAsync(string route, string token)
        {
            var endpoint = ((IEndpointRouteBuilder)App).DataSources
                .SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
                .Single(e => e.RoutePattern.RawText == route && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));

            using var scope = App.Services.CreateScope();
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            http.Request.Method = "GET";
            http.Request.Path = route;
            http.Request.Headers.Authorization = $"Bearer {token}";
            http.Response.Body = new MemoryStream();

            await new ActorMiddleware(endpoint.RequestDelegate!).InvokeAsync(http, scope.ServiceProvider.GetRequiredService<AuthService>());

            return new Download(
                http.Response.StatusCode,
                http.Response.ContentType,
                http.Response.Headers.ContentDisposition.ToString(),
                ((MemoryStream)http.Response.Body).ToArray());
        }
    }

    private static async Task<Harness> CreateAsync(IEnumerable<Ticket> tickets)
    {
        // Same instant as DashboardSummaryTests.Now, so the hand-worked figures apply here too.
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
            db.Users.AddRange(
                new UserAccount { Id = "it-manager", DisplayName = "IT Manager", Department = "IT Services", Role = UserRole.TeamLead, PasswordHash = hasher.Hash(ManagerPassword) },
                new UserAccount { Id = "airport-staff", DisplayName = "Airport Staff", Department = "Operations", Role = UserRole.Requester, PasswordHash = hasher.Hash(StaffPassword) });
            db.Tickets.AddRange(tickets);
            await db.SaveChangesAsync();
        }

        return new Harness { App = app };
    }

    [TestMethod] // EXP-B15 — AC1: each export choice produces a CSV file download for the manager
    public async Task Manager_GetsACsvFileForEachChoice()
    {
        var harness = await CreateAsync(DashboardSummaryTests.MixedTickets());
        var token = await harness.SignInAsync("it-manager", ManagerPassword);

        foreach (var (route, kind) in new[] { (ExportRoutes[0], "tickets"), (ExportRoutes[1], "summary"), (ExportRoutes[2], "by-priority") })
        {
            var file = await harness.GetAsync(route, token);

            Assert.AreEqual(StatusCodes.Status200OK, file.Status, route);
            StringAssert.StartsWith(file.ContentType, "text/csv", route);
            StringAssert.Contains(file.ContentDisposition, "attachment", route);
            StringAssert.Contains(file.ContentDisposition, $"airport-helpdesk-{kind}-2026-10-12.csv", route);
            CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, file.Bytes.Take(3).ToArray(), $"{route}: no UTF-8 BOM");
        }
    }

    [TestMethod] // EXP-B16 — AC2: with no tickets every file is valid; the row-based files are header-only
    public async Task NoTickets_EveryExportIsAValidFile()
    {
        var harness = await CreateAsync([]);
        var token = await harness.SignInAsync("it-manager", ManagerPassword);

        var tickets = (await harness.GetAsync(ExportRoutes[0], token)).Rows;
        var byPriority = (await harness.GetAsync(ExportRoutes[2], token)).Rows;
        var summary = (await harness.GetAsync(ExportRoutes[1], token)).Rows;

        Assert.HasCount(1, tickets, "ticket list: header only");
        Assert.AreEqual("id", tickets[0][0]);
        Assert.HasCount(1, byPriority, "by priority: header only");
        CollectionAssert.AreEqual(DashboardExport.ByPriorityHeader.ToArray(), byPriority[0]);

        // The summary is one row per figure, so it keeps its rows and reports zeros and blanks.
        CollectionAssert.AreEqual(DashboardExport.SummaryHeader.ToArray(), summary[0]);
        Assert.HasCount(9, summary);
        Assert.AreEqual("2026-10-12T09:00:00Z", Value(summary, "Report generated (UTC)"));
        Assert.AreEqual("0", Value(summary, "Total tickets"));
        Assert.AreEqual("0", Value(summary, "Open backlog"));
        Assert.AreEqual("0", Value(summary, "Overdue (outside priority SLA)"));
        Assert.AreEqual(string.Empty, Value(summary, "Average resolution time"));
        Assert.AreEqual(string.Empty, Value(summary, "Most common category"));
    }

    [TestMethod] // EXP-B17 — AC1 "current metrics": the summary file matches what the dashboard cards show
    public async Task SummaryFile_MatchesTheDashboardCards()
    {
        var harness = await CreateAsync(DashboardSummaryTests.MixedTickets());
        var token = await harness.SignInAsync("it-manager", ManagerPassword);

        var summary = (await harness.GetAsync(ExportRoutes[1], token)).Rows;
        using var cards = JsonDocument.Parse((await harness.GetAsync("/api/dashboard", token)).Text);
        var json = cards.RootElement;

        Assert.AreEqual(json.GetProperty("totalTickets").GetInt32().ToString(CultureInfo.InvariantCulture), Value(summary, "Total tickets"));
        Assert.AreEqual(json.GetProperty("openBacklog").GetInt32().ToString(CultureInfo.InvariantCulture), Value(summary, "Open backlog"));
        Assert.AreEqual(json.GetProperty("overdueCount").GetInt32().ToString(CultureInfo.InvariantCulture), Value(summary, "Overdue (outside priority SLA)"));
        Assert.AreEqual(json.GetProperty("averageResolutionMinutes").GetDouble().ToString(CultureInfo.InvariantCulture), Value(summary, "Average resolution time"));
        Assert.AreEqual(json.GetProperty("recurringIssueCategory").GetString(), Value(summary, "Most common category"));
        Assert.AreEqual(json.GetProperty("recurringIssueCount").GetInt32().ToString(CultureInfo.InvariantCulture), Value(summary, "Most common category count"));

        // And both match the hand-worked fixture.
        Assert.AreEqual("5", Value(summary, "Total tickets"));
        Assert.AreEqual("780", Value(summary, "Average resolution time"));
        Assert.AreEqual("Incident", Value(summary, "Most common category"));
    }

    [TestMethod] // EXP-B18 — the by-priority file for the mixed set, read back as a spreadsheet would
    public async Task ByPriorityFile_HasHandWorkedRows()
    {
        var harness = await CreateAsync(DashboardSummaryTests.MixedTickets());
        var token = await harness.SignInAsync("it-manager", ManagerPassword);

        var rows = (await harness.GetAsync(ExportRoutes[2], token)).Rows;

        CollectionAssert.AreEqual(new[] { "Critical", "2", "1", "1", "1", "120" }, rows[1]);
        CollectionAssert.AreEqual(new[] { "High", "1", "1", "0", "0", "" }, rows[2]);
        CollectionAssert.AreEqual(new[] { "Low", "2", "1", "0", "1", "1440" }, rows[3]);
        Assert.HasCount(4, rows);
    }

    [TestMethod] // EXP-B19 — security + reliability regression: a hostile or comma-containing reporter name in the ticket list
    [DataRow("=HYPERLINK(\"http://evil.test\",\"Open\")", DisplayName = "formula injection")]
    [DataRow("Lee, Ana", DisplayName = "comma in name")]
    [DataRow("Ana \"AJ\" Lee", DisplayName = "quotes in name")]
    public async Task TicketList_KeepsReporterNameInOneInertCell(string reporter)
    {
        var harness = await CreateAsync(DashboardSummaryTests.MixedTickets(reporter));
        var token = await harness.SignInAsync("it-manager", ManagerPassword);

        var rows = (await harness.GetAsync(ExportRoutes[0], token)).Rows;
        var header = rows[0];
        var reporterColumn = Array.IndexOf(header, "reporterName");

        Assert.IsTrue(rows.All(r => r.Length == header.Length), "Every row must have as many cells as the header.");
        var cell = rows.Skip(1).Select(r => r[reporterColumn]).Single(c => c.Contains(reporter.TrimStart('=')));
        Assert.AreEqual(reporter.StartsWith('=') ? "'" + reporter : reporter, cell);
    }

    [TestMethod] // EXP-B20 — regression: with no tickets the recurring-issue card says "None", not a made-up "Incident"
    public async Task NoTickets_RecurringIssueIsNone()
    {
        var harness = await CreateAsync([]);
        var token = await harness.SignInAsync("it-manager", ManagerPassword);

        using var cards = JsonDocument.Parse((await harness.GetAsync("/api/dashboard", token)).Text);
        Assert.AreEqual("None", cards.RootElement.GetProperty("recurringIssueCategory").GetString());
        Assert.AreEqual(0, cards.RootElement.GetProperty("recurringIssueCount").GetInt32());
    }

    [TestMethod] // EXP-B21 — security: an employee gets 403 and no file on the new export routes
    public async Task Employee_IsRefusedTheSummaryFiles()
    {
        var harness = await CreateAsync(DashboardSummaryTests.MixedTickets());
        var token = await harness.SignInAsync("airport-staff", StaffPassword);

        foreach (var route in ExportRoutes)
        {
            var response = await harness.GetAsync(route, token);
            Assert.AreEqual(StatusCodes.Status403Forbidden, response.Status, route);
            Assert.IsFalse(response.ContentType?.StartsWith("text/csv") ?? false, $"{route} sent a CSV to an employee");
            Assert.IsFalse(response.Text.Contains("Open backlog") || response.Text.Contains("Critical"), $"{route} leaked data");
        }
    }

    private static string Value(List<string[]> summary, string metric) =>
        summary.Single(r => r[0] == metric)[1];
}

/// <summary>
/// Minimal RFC 4180 reader used only by the tests: quoted fields, doubled quotes, CRLF or LF
/// line ends, line breaks inside quotes. Written separately from <see cref="Csv"/> so the two
/// cannot share a bug.
/// </summary>
internal static class CsvTestParser
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    fieldStarted = true;
                    break;
                case ',':
                    row.Add(field.ToString()); field.Clear(); fieldStarted = true;
                    break;
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    break; // handled by the \n
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    rows.Add([.. row]); row.Clear(); fieldStarted = false;
                    break;
                default:
                    field.Append(c); fieldStarted = true;
                    break;
            }
        }

        if (fieldStarted || field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add([.. row]);
        }

        return rows;
    }
}

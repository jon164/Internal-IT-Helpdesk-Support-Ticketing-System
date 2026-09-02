using Helpdesk.Api.Configuration;
using Helpdesk.Api.Endpoints;
using Helpdesk.Api.Security;
using Helpdesk.Core.Metrics;
using Helpdesk.Core.Sla;
using Helpdesk.Core.Time;
using Helpdesk.Data;
using Helpdesk.Data.Seeding;
using Helpdesk.Data.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Persistence ---------------------------------------------------------

var connectionString = builder.Configuration.GetConnectionString("Helpdesk")
                       ?? "Data Source=helpdesk.db";

builder.Services.AddDbContext<HelpdeskDbContext>(options => options.UseSqlite(connectionString));

// --- Domain services -----------------------------------------------------

// Priority definitions, target times and working hours are read from configuration and validated
// here, at start-up. A malformed edit stops the application with a message naming the problem, rather
// than letting it run and quietly report wrong figures.
var slaOptions = builder.Configuration.GetSection(SlaOptions.SectionName).Get<SlaOptions>()
                 ?? new SlaOptions();

var policySet = slaOptions.Policies.Count > 0 ? slaOptions.ToPolicySet() : SlaPolicySet.Default;
var calendar = slaOptions.ToCalendar();

builder.Services.AddSingleton(policySet);
builder.Services.AddSingleton<IBusinessCalendar>(calendar);
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddSingleton(sp => new SlaCalculator(
    sp.GetRequiredService<SlaPolicySet>(),
    sp.GetRequiredService<IBusinessCalendar>()));
builder.Services.AddSingleton(sp => new TicketMetricsCalculator(
    sp.GetRequiredService<SlaCalculator>()));
builder.Services.AddScoped<TicketService>();
builder.Services.AddScoped<UserAdminService>();

// --- Web -----------------------------------------------------------------

builder.Services.AddOpenApi();

// Enums travel as their names, not their ordinals. Without this a client sending "High" is rejected
// with an unhelpful parse error, and any reordering of an enum would silently change the meaning of
// previously valid requests.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));

const string DevCorsPolicy = "vite-dev-server";

builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy => policy
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// --- Start-up ------------------------------------------------------------

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();

    // EnsureCreated is appropriate for a prototype with no production data to migrate. A deployed
    // system would use EF Core migrations instead; this is recorded as a known limitation.
    db.Database.EnsureCreated();

    // Must run before anything queries the tables: a database created while the separate
    // administrator role existed holds a role name the model no longer recognises.
    if (await SampleDataSeeder.RemoveRetiredAdministratorRoleAsync(db))
    {
        app.Logger.LogInformation(
            "Removed the retired separate administrator role; account administration now belongs "
            + "to the service manager.");
    }

    // Accounts only. The ticket table starts empty and stays empty until somebody asks for sample
    // data, so a demonstration begins from a state the audience can watch being filled rather than
    // one that arrived fully formed. Accounts are the exception because without them nobody can sign
    // in to press the button.
    var accountsAdded = await SampleDataSeeder.EnsureAccountsAsync(db);

    if (accountsAdded > 0)
    {
        app.Logger.LogInformation("Created {Count} demonstration accounts.", accountsAdded);
    }

    var ticketCount = await db.Tickets.CountAsync();

    if (ticketCount == 0)
    {
        app.Logger.LogInformation(
            "The ticket table is empty. Sign in as the service manager and use "
            + "Accounts -> Demonstration data to generate a sample history.");
    }
    else
    {
        app.Logger.LogInformation("{Count} tickets present.", ticketCount);
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Helpdesk API"));
    app.UseCors(DevCorsPolicy);
}

app.UseActorResolution();

app.MapSessionEndpoints();
app.MapTicketEndpoints();
app.MapReferenceEndpoints();
app.MapAdminEndpoints();

// An endpoint that can fabricate or destroy every ticket in the system has no business existing in a
// production build. It is registered only outside it, so the route is absent rather than merely
// guarded — a control that cannot be reached cannot be got wrong. Inside Development it is still
// restricted to the service manager, because "developers only" is not an authorisation model.
if (app.Environment.IsDevelopment())
{
    app.MapSampleDataEndpoints();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .WithTags("Reference")
   .WithSummary("Liveness check.");

app.Run();

/// <summary>
/// Exposed so integration tests can drive the application through
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;

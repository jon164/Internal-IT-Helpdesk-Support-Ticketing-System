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
    var clock = scope.ServiceProvider.GetRequiredService<IClock>();

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

    if (await SampleDataSeeder.EnsureSeededAsync(db, clock.UtcNow))
    {
        app.Logger.LogInformation(
            "Seeded {Count} sample tickets.", SampleDataSeeder.DefaultTicketCount);
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

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .WithTags("Reference")
   .WithSummary("Liveness check.");

app.Run();

/// <summary>
/// Exposed so integration tests can drive the application through
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;

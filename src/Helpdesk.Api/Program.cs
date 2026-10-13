using Helpdesk.Api.Auth;
using Helpdesk.Api.Endpoints;
using Helpdesk.Api.Security;
using Helpdesk.Api.Services;
using Helpdesk.Core.Security;
using Helpdesk.Data;
using Helpdesk.Data.Repositories;
using Helpdesk.Data.Seeding;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("HelpdeskClient", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var dbPath = Path.Combine(builder.Environment.ContentRootPath, "helpdesk.db");
var connectionString = $"Data Source={dbPath}";

// EnsureCreated() never alters an existing database, so an older helpdesk.db from before a
// schema change is discarded and rebuilt. This is a development convenience for the prototype
// (there are no migrations yet) and loses any locally created tickets.
if (File.Exists(dbPath) && await DatabaseNeedsRebuildAsync(connectionString))
{
    // Pooled connections keep the file open; on Windows the delete would otherwise fail.
    SqliteConnection.ClearAllPools();
    File.Delete(dbPath);
}

var authOptions = builder.Configuration.GetSection("Authentication").Get<AuthOptions>() ?? new AuthOptions();
var passwordIterations = builder.Configuration.GetValue("Authentication:PasswordIterations", PasswordHasher.DefaultIterations);
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton(new PasswordHasher(passwordIterations));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AuthService>();

builder.Services.AddDbContext<HelpdeskDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<ITicketRepository, TicketRepository>();
builder.Services.AddScoped<TicketService>();
builder.Services.AddHostedService<AutoCloseBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("HelpdeskClient");
}

app.UseHttpsRedirection();
app.UseActorResolution();

// Ensure the database is created and seeded on startup.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
    dbContext.Database.EnsureCreated();
    // Demo passwords come from configuration (appsettings.Development.json or user secrets),
    // never from source code. With none configured, the accounts exist but cannot sign in.
    var demoPasswords = app.Configuration.GetSection("DemoAccounts:Passwords").Get<Dictionary<string, string>>();
    await DbSeeder.EnsureDemoUsersAsync(dbContext, demoPasswords, scope.ServiceProvider.GetRequiredService<PasswordHasher>());
    await DbSeeder.SeedAsync(dbContext);
}

app.MapAuthEndpoints();
app.MapTicketEndpoints();
app.MapDashboardEndpoints();

app.Run();

static async Task<bool> DatabaseNeedsRebuildAsync(string connectionString)
{
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    async Task<long> CountAsync(string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    var usersTableExists = await CountAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Users';") > 0;
    if (!usersTableExists)
    {
        return false;
    }

    var hasIsActive = await CountAsync("SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name = 'IsActive';") > 0;
    var isActiveMissingDefault = await CountAsync("SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name = 'IsActive' AND (dflt_value IS NULL OR dflt_value = '');") > 0;
    var hasPasswordHash = await CountAsync("SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name = 'PasswordHash';") > 0;
    var hasSessionsTable = await CountAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'UserSessions';") > 0;

    return !hasIsActive || isActiveMissingDefault || !hasPasswordHash || !hasSessionsTable;
}

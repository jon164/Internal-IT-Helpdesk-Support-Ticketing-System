using Helpdesk.Api.Endpoints;
using Helpdesk.Api.Security;
using Helpdesk.Api.Services;
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

if (File.Exists(dbPath))
{
    await using var inspection = new SqliteConnection(connectionString);
    await inspection.OpenAsync();

    await using var tableCommand = inspection.CreateCommand();
    tableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Users';";
    var usersTableExists = Convert.ToInt32(await tableCommand.ExecuteScalarAsync()) > 0;

    if (usersTableExists)
    {
        await using var columnCommand = inspection.CreateCommand();
        columnCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name = 'IsActive';";
        var hasIsActiveColumn = Convert.ToInt32(await columnCommand.ExecuteScalarAsync()) > 0;

        if (!hasIsActiveColumn)
        {
            File.Delete(dbPath);
        }
        else
        {
            await using var defaultCommand = inspection.CreateCommand();
            defaultCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name = 'IsActive' AND (dflt_value IS NULL OR dflt_value = '');";
            var missingDefault = Convert.ToInt32(await defaultCommand.ExecuteScalarAsync()) > 0;

            if (missingDefault)
            {
                File.Delete(dbPath);
            }
        }
    }
}

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
    await DbSeeder.EnsureDemoUsersAsync(dbContext);
    await DbSeeder.SeedAsync(dbContext);
}

app.MapSessionEndpoints();
app.MapTicketEndpoints();
app.MapDashboardEndpoints();

app.Run();

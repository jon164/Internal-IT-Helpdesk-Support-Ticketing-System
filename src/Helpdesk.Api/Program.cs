using Helpdesk.Api.Endpoints;
using Helpdesk.Api.Services;
using Helpdesk.Data;
using Helpdesk.Data.Repositories;
using Helpdesk.Data.Seeding;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var dbPath = Path.Combine(builder.Environment.ContentRootPath, "helpdesk.db");
builder.Services.AddDbContext<HelpdeskDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddScoped<ITicketRepository, TicketRepository>();
builder.Services.AddScoped<TicketService>();
builder.Services.AddHostedService<AutoCloseBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Ensure the database is created and seeded on startup.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
    dbContext.Database.EnsureCreated();
    await DbSeeder.SeedAsync(dbContext);
}

app.MapTicketEndpoints();

app.Run();

using Helpdesk.Api.Services;
using Helpdesk.Core.Domain;
using Helpdesk.Data;
using Helpdesk.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Tests;

[TestClass]
public sealed class TicketServiceTests
{
    [TestMethod]
    public async Task GetTicketsAsync_OrdersNewestFirst_WhenSortIsNewest()
    {
        var options = new DbContextOptionsBuilder<HelpdeskDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new HelpdeskDbContext(options);

        var older = new Ticket
        {
            Title = "Older",
            Description = "older ticket",
            Priority = TicketPriority.Medium,
            Type = TicketType.Incident,
            Status = TicketStatus.New,
            SubmitterName = "A",
            SubmitterEmail = "a@example.com",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-2),
        };

        var newer = new Ticket
        {
            Title = "Newer",
            Description = "newer ticket",
            Priority = TicketPriority.High,
            Type = TicketType.Incident,
            Status = TicketStatus.InProgress,
            SubmitterName = "B",
            SubmitterEmail = "b@example.com",
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
        };

        await context.Tickets.AddRangeAsync(older, newer);
        await context.SaveChangesAsync();

        var service = new TicketService(new TicketRepository(context));

        var result = await service.GetTicketsAsync("newest");

        Assert.AreEqual("Newer", result[0].Title);
        Assert.AreEqual("Older", result[1].Title);
    }

    [TestMethod]
    public async Task GetTicketsAsync_OrdersByPriorityThenAge_WhenSortIsPriorityAge()
    {
        var options = new DbContextOptionsBuilder<HelpdeskDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new HelpdeskDbContext(options);
        var now = DateTime.UtcNow;

        var tickets = new[]
        {
            new Ticket
            {
                Title = "Medium priority",
                Description = "medium priority ticket",
                Priority = TicketPriority.Medium,
                Type = TicketType.Incident,
                Status = TicketStatus.New,
                SubmitterName = "A",
                SubmitterEmail = "a@example.com",
                CreatedAt = now.AddHours(-3),
                UpdatedAt = now.AddHours(-3),
            },
            new Ticket
            {
                Title = "Newer high priority",
                Description = "newer high priority ticket",
                Priority = TicketPriority.High,
                Type = TicketType.Incident,
                Status = TicketStatus.New,
                SubmitterName = "B",
                SubmitterEmail = "b@example.com",
                CreatedAt = now.AddHours(-1),
                UpdatedAt = now.AddHours(-1),
            },
            new Ticket
            {
                Title = "Older high priority",
                Description = "older high priority ticket",
                Priority = TicketPriority.High,
                Type = TicketType.Incident,
                Status = TicketStatus.New,
                SubmitterName = "C",
                SubmitterEmail = "c@example.com",
                CreatedAt = now.AddHours(-2),
                UpdatedAt = now.AddHours(-2),
            },
        };

        await context.Tickets.AddRangeAsync(tickets);
        await context.SaveChangesAsync();

        var service = new TicketService(new TicketRepository(context));
        var result = await service.GetTicketsAsync("priority-age");

        CollectionAssert.AreEqual(
            new[] { "Older high priority", "Newer high priority", "Medium priority" },
            result.Select(ticket => ticket.Title).ToArray());
    }
}

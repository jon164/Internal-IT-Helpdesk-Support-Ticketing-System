using Helpdesk.Api.Models;
using Helpdesk.Api.Services;
using Helpdesk.Core.Domain;
using Helpdesk.Data;
using Helpdesk.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Tests;

[TestClass]
public sealed class TicketWorkflowServiceTests
{
    [TestMethod]
    public async Task ClaimAsync_AssignsTicketAndWritesAuditEntry()
    {
        await using var fixture = await Fixture.CreateAsync();

        var claimed = await fixture.Service.ClaimAsync(fixture.Ticket.Id, fixture.Technician.Id, fixture.Now);

        Assert.AreEqual(fixture.Technician.Id, claimed.AssignedTechnicianId);
        Assert.AreEqual("Claimed", claimed.AuditEntries.Single().Action);
    }

    [TestMethod]
    public async Task ClaimAsync_RejectsTicketAlreadyAssigned()
    {
        await using var fixture = await Fixture.CreateAsync(assignedTechnician: true);

        await AssertThrowsAsync<ClaimConflictException>(
            () => fixture.Service.ClaimAsync(fixture.Ticket.Id, fixture.OtherTechnician.Id, fixture.Now));
    }

    [TestMethod]
    public async Task ClaimAsync_RejectsMissingTicket()
    {
        await using var fixture = await Fixture.CreateAsync();

        await AssertThrowsAsync<NotFoundException>(
            () => fixture.Service.ClaimAsync(Guid.NewGuid(), fixture.Technician.Id, fixture.Now));
    }

    [TestMethod]
    public async Task ClaimAsync_RejectsMissingTechnician()
    {
        await using var fixture = await Fixture.CreateAsync();

        await AssertThrowsAsync<NotFoundException>(
            () => fixture.Service.ClaimAsync(fixture.Ticket.Id, Guid.NewGuid(), fixture.Now));
    }

    [TestMethod]
    public async Task ReassignAsync_AssignsActiveTechnicianAndAuditsChange()
    {
        await using var fixture = await Fixture.CreateAsync();

        var reassigned = await fixture.Service.ReassignAsync(fixture.Ticket.Id, fixture.Technician.Id, fixture.Now);

        Assert.AreEqual(fixture.Technician.Id, reassigned.AssignedTechnicianId);
        Assert.AreEqual("Reassigned", reassigned.AuditEntries.Single().Action);
    }

    [TestMethod]
    public async Task ReassignAsync_RejectsInactiveTechnicianAndListsAvailableTechnicians()
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await AssertThrowsAsync<ReassignValidationException>(
            () => fixture.Service.ReassignAsync(fixture.Ticket.Id, fixture.InactiveTechnician.Id, fixture.Now));

        Assert.HasCount(2, exception.AvailableTechnicians);
    }

    [TestMethod]
    public async Task ChangeStatusAsync_ValidTransitionSetsPendingTimestampAndAudit()
    {
        await using var fixture = await Fixture.CreateAsync(TicketStatus.InProgress);

        var updated = await fixture.Service.ChangeStatusAsync(
            fixture.Ticket.Id, TicketStatus.PendingEmployeeResponse, "tech", "Need details", fixture.Now);

        Assert.AreEqual(TicketStatus.PendingEmployeeResponse, updated.Status);
        Assert.AreEqual(fixture.Now, updated.PendingSince);
        Assert.AreEqual("Need details", updated.AuditEntries.Single().Comment);
    }

    [TestMethod]
    public async Task ChangeStatusAsync_RejectsInvalidTransition()
    {
        await using var fixture = await Fixture.CreateAsync(TicketStatus.New);

        await AssertThrowsAsync<InvalidOperationException>(
            () => fixture.Service.ChangeStatusAsync(
                fixture.Ticket.Id, TicketStatus.Closed, "tech", null, fixture.Now));
    }

    [TestMethod]
    public async Task EmployeeCommentAsync_AddsPublicNoteAndReturnsPendingTicketToProgress()
    {
        await using var fixture = await Fixture.CreateAsync(TicketStatus.PendingEmployeeResponse);

        var updated = await fixture.Service.EmployeeCommentAsync(
            fixture.Ticket.Id, "requester", "Requester", "More details", fixture.Now);

        Assert.AreEqual(TicketStatus.InProgress, updated.Status);
        Assert.IsNull(updated.PendingSince);
        Assert.AreEqual("More details", updated.Notes.Single().Content);
        Assert.IsFalse(updated.Notes.Single().IsInternal);
    }

    [TestMethod]
    public async Task AddNoteAsync_PersistsInternalNote()
    {
        await using var fixture = await Fixture.CreateAsync();

        var updated = await fixture.Service.AddNoteAsync(
            fixture.Ticket.Id, "tech", "Technician", "Internal detail", true, fixture.Now);

        Assert.AreEqual("Internal detail", updated.Notes.Single().Content);
        Assert.IsTrue(updated.Notes.Single().IsInternal);
    }

    [TestMethod]
    public async Task AddNoteAsync_RejectsBlankNote()
    {
        await using var fixture = await Fixture.CreateAsync();

        await AssertThrowsAsync<InvalidOperationException>(
            () => fixture.Service.AddNoteAsync(fixture.Ticket.Id, "tech", "Technician", "  ", true, fixture.Now));
    }

    [TestMethod]
    public async Task LinkAsync_CreatesLinkToExistingTicket()
    {
        await using var fixture = await Fixture.CreateAsync();
        var target = await fixture.AddTicketAsync("Target");

        var updated = await fixture.Service.LinkAsync(
            fixture.Ticket.Id, target.Id, TicketLinkType.Related, "tech", fixture.Now);

        Assert.AreEqual(target.Id, updated.Links.Single().TargetTicketId);
    }

    [TestMethod]
    public async Task LinkAsync_RejectsSelfLink()
    {
        await using var fixture = await Fixture.CreateAsync();

        await AssertThrowsAsync<DomainValidationException>(
            () => fixture.Service.LinkAsync(
                fixture.Ticket.Id, fixture.Ticket.Id, TicketLinkType.Related, "tech", fixture.Now));
    }

    [TestMethod]
    public async Task LinkAsync_RejectsDuplicateLinkInReverseDirection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var target = await fixture.AddTicketAsync("Target");
        await fixture.Service.LinkAsync(fixture.Ticket.Id, target.Id, TicketLinkType.Related, "tech", fixture.Now);

        await AssertThrowsAsync<DomainValidationException>(
            () => fixture.Service.LinkAsync(target.Id, fixture.Ticket.Id, TicketLinkType.Duplicate, "tech", fixture.Now));
    }

    [TestMethod]
    public async Task CloseAsync_ClosesResolvedTicketAndRecordsReason()
    {
        await using var fixture = await Fixture.CreateAsync(TicketStatus.Resolved);

        var closed = await fixture.Service.CloseAsync(fixture.Ticket.Id, "tech", "Confirmed", fixture.Now);

        Assert.AreEqual(TicketStatus.Closed, closed.Status);
        Assert.AreEqual("Confirmed", closed.ClosedReason);
        Assert.AreEqual(fixture.Now, closed.ClosedAt);
    }

    [TestMethod]
    public async Task CloseAsync_RejectsTicketThatIsNotResolved()
    {
        await using var fixture = await Fixture.CreateAsync(TicketStatus.InProgress);

        await AssertThrowsAsync<InvalidOperationException>(
            () => fixture.Service.CloseAsync(fixture.Ticket.Id, "tech", null, fixture.Now));
    }

    [TestMethod]
    public async Task AutoCloseAsync_ClosesOnlyResolvedTicketsOlderThanTimeout()
    {
        await using var fixture = await Fixture.CreateAsync(TicketStatus.Resolved);
        fixture.Ticket.ResolvedAt = fixture.Now.AddHours(-25);
        var recentResolved = await fixture.AddTicketAsync("Recent", TicketStatus.Resolved);
        recentResolved.ResolvedAt = fixture.Now.AddHours(-2);
        var inProgress = await fixture.AddTicketAsync("In progress", TicketStatus.InProgress);
        inProgress.ResolvedAt = fixture.Now.AddDays(-2);
        await fixture.Context.SaveChangesAsync();

        var closedCount = await fixture.Service.AutoCloseAsync(TimeSpan.FromHours(24), fixture.Now);

        Assert.AreEqual(1, closedCount);
        Assert.AreEqual(TicketStatus.Closed, (await fixture.Service.GetTicketAsync(fixture.Ticket.Id)).Status);
        Assert.AreEqual(TicketStatus.Resolved, (await fixture.Service.GetTicketAsync(recentResolved.Id)).Status);
        Assert.AreEqual(TicketStatus.InProgress, (await fixture.Service.GetTicketAsync(inProgress.Id)).Status);
    }

    [TestMethod]
    public async Task GetUnassignedTicketsAsync_ExcludesAssignedAndClosedTickets()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Ticket.AssignedTechnicianId = fixture.Technician.Id;
        await fixture.AddTicketAsync("Unassigned", TicketStatus.New);
        await fixture.AddTicketAsync("Closed", TicketStatus.Closed);
        await fixture.Context.SaveChangesAsync();

        var tickets = await fixture.Service.GetUnassignedTicketsAsync();

        CollectionAssert.AreEqual(new[] { "Unassigned" }, tickets.Select(ticket => ticket.Title).ToArray());
    }

    [TestMethod]
    public async Task SqliteIntegration_AddNotePersistsAcrossDbContexts()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HelpdeskDbContext>()
            .UseSqlite(connection)
            .Options;
        var now = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        var ticket = new Ticket
        {
            Title = "SQLite persistence",
            Description = "Verify relational persistence.",
            SubmitterName = "Requester",
            SubmitterEmail = "requester@example.com",
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using (var setupContext = new HelpdeskDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            await setupContext.Tickets.AddAsync(ticket);
            await setupContext.SaveChangesAsync();
        }

        await using (var writeContext = new HelpdeskDbContext(options))
        {
            var service = new TicketService(new TicketRepository(writeContext));
            await service.AddNoteAsync(ticket.Id, "tech", "Technician", "Persisted note", false, now);
        }

        await using var verifyContext = new HelpdeskDbContext(options);
        var storedTicket = await verifyContext.Tickets
            .Include(t => t.Notes)
            .SingleAsync(t => t.Id == ticket.Id);
        Assert.AreEqual("Persisted note", storedTicket.Notes.Single().Content);
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        Assert.Fail($"Expected {typeof(TException).Name} to be thrown.");
        throw new AssertFailedException("Expected exception was not thrown.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(HelpdeskDbContext context, Technician technician, Technician otherTechnician, Technician inactiveTechnician, Ticket ticket)
        {
            Context = context;
            Technician = technician;
            OtherTechnician = otherTechnician;
            InactiveTechnician = inactiveTechnician;
            Ticket = ticket;
            Service = new TicketService(new TicketRepository(context));
        }

        public DateTime Now { get; } = new(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        public HelpdeskDbContext Context { get; }
        public TicketService Service { get; }
        public Technician Technician { get; }
        public Technician OtherTechnician { get; }
        public Technician InactiveTechnician { get; }
        public Ticket Ticket { get; }

        public static async Task<Fixture> CreateAsync(TicketStatus status = TicketStatus.New, bool assignedTechnician = false)
        {
            var options = new DbContextOptionsBuilder<HelpdeskDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var context = new HelpdeskDbContext(options);
            var technician = new Technician { Name = "Active One", Email = "one@example.com", IsActive = true };
            var otherTechnician = new Technician { Name = "Active Two", Email = "two@example.com", IsActive = true };
            var inactiveTechnician = new Technician { Name = "Inactive", Email = "inactive@example.com", IsActive = false };
            var ticket = new Ticket
            {
                Title = "Test ticket",
                Description = "Description",
                Status = status,
                Priority = TicketPriority.Medium,
                Type = TicketType.Incident,
                SubmitterName = "Requester",
                SubmitterEmail = "requester@example.com",
                CreatedAt = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
                ResolvedAt = status == TicketStatus.Resolved
                    ? new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc)
                    : null,
            };

            await context.Technicians.AddRangeAsync(technician, otherTechnician, inactiveTechnician);
            if (assignedTechnician)
            {
                ticket.AssignedTechnicianId = technician.Id;
            }

            await context.Tickets.AddAsync(ticket);
            await context.SaveChangesAsync();
            return new Fixture(context, technician, otherTechnician, inactiveTechnician, ticket);
        }

        public async Task<Ticket> AddTicketAsync(string title, TicketStatus status = TicketStatus.New)
        {
            var ticket = new Ticket
            {
                Title = title,
                Description = $"{title} description",
                Status = status,
                Priority = TicketPriority.Low,
                Type = TicketType.Incident,
                SubmitterName = "Requester",
                SubmitterEmail = "requester@example.com",
                CreatedAt = Now.AddHours(-1),
                UpdatedAt = Now.AddHours(-1),
                ResolvedAt = status == TicketStatus.Resolved ? Now.AddHours(-1) : null,
            };
            await Context.Tickets.AddAsync(ticket);
            await Context.SaveChangesAsync();
            return ticket;
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}

using Helpdesk.Core.Models;
using Helpdesk.Core.Services;

namespace Helpdesk.Tests;

[TestClass]
public class TicketRulesTests
{
    [TestMethod]
    public void NewTicket_CannotMoveDirectlyToClosed()
    {
        var result = TicketRules.CanTransition(
            TicketStatus.New,
            TicketStatus.Closed
        );

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void NewTicket_CanMoveToInProgress()
    {
        var result = TicketRules.CanTransition(
            TicketStatus.New,
            TicketStatus.InProgress
        );

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void CriticalTicketOutsideThirtyMinutes_IsOverdue()
    {
        var now = DateTime.UtcNow;

        var ticket = new Ticket
        {
            Priority = TicketPriority.Critical,
            Status = TicketStatus.New,
            CreatedAtUtc = now.AddMinutes(-31)
        };

        Assert.IsTrue(TicketRules.IsOverdue(ticket, now));
    }

    [TestMethod]
    public void CriticalTicketWithinThirtyMinutes_IsNotOverdue()
    {
        var now = DateTime.UtcNow;

        var ticket = new Ticket
        {
            Priority = TicketPriority.Critical,
            Status = TicketStatus.New,
            CreatedAtUtc = now.AddMinutes(-20)
        };

        Assert.IsFalse(TicketRules.IsOverdue(ticket, now));
    }

    [TestMethod]
    public void ClosedTicket_IsNeverCountedAsOverdue()
    {
        var now = DateTime.UtcNow;

        var ticket = new Ticket
        {
            Priority = TicketPriority.Critical,
            Status = TicketStatus.Closed,
            CreatedAtUtc = now.AddHours(-3)
        };

        Assert.IsFalse(TicketRules.IsOverdue(ticket, now));
    }
}

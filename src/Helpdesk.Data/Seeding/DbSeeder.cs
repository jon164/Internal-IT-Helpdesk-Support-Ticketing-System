using Helpdesk.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Data.Seeding;

public static class DbSeeder
{
    public static async Task SeedAsync(HelpdeskDbContext context, CancellationToken ct = default)
    {
        if (await context.Technicians.AnyAsync(ct))
        {
            return;
        }

        var alice = new Technician { Name = "Alice Nguyen", Email = "alice.nguyen@helpdesk.local", IsActive = true };
        var bob = new Technician { Name = "Bob Carter", Email = "bob.carter@helpdesk.local", IsActive = true };
        var carol = new Technician { Name = "Carol Reyes", Email = "carol.reyes@helpdesk.local", IsActive = true };
        var dave = new Technician { Name = "Dave Okafor", Email = "dave.okafor@helpdesk.local", IsActive = false };

        await context.Technicians.AddRangeAsync(new[] { alice, bob, carol, dave }, ct);

        var now = DateTime.UtcNow;

        var tickets = new List<Ticket>
        {
            new Ticket
            {
                Title = "Cannot access company VPN",
                Description = "Connecting to the VPN fails with error 809 after the recent update.",
                Priority = TicketPriority.Critical,
                Type = TicketType.Incident,
                Status = TicketStatus.InProgress,
                SubmitterName = "Mia Turner",
                SubmitterEmail = "mia.turner@company.com",
                AssignedTechnicianId = alice.Id,
                CreatedAt = now.AddHours(-30),
                UpdatedAt = now.AddHours(-2),
                Notes =
                {
                    new TicketNote { AuthorId = alice.Id.ToString(), AuthorName = "Alice Nguyen", Content = "Reran the VPN client update on a test machine, error persists.", IsInternal = true, CreatedAt = now.AddHours(-3) }
                },
                AuditEntries =
                {
                    TicketAuditEntry.Create(default, "System", TicketStatus.New, TicketStatus.InProgress, "StatusChanged", "Moved to In Progress"),
                }
            },
            new Ticket
            {
                Title = "Outlook crashes on startup",
                Description = "Outlook freezes and crashes a few seconds after launch since Monday.",
                Priority = TicketPriority.High,
                Type = TicketType.Incident,
                Status = TicketStatus.PendingEmployeeResponse,
                SubmitterName = "Leo Grant",
                SubmitterEmail = "leo.grant@company.com",
                AssignedTechnicianId = bob.Id,
                CreatedAt = now.AddHours(-50),
                UpdatedAt = now.AddHours(-8),
                PendingSince = now.AddHours(-8),
                Notes =
                {
                    new TicketNote { AuthorId = "leo.grant@company.com", AuthorName = "Leo Grant", Content = "It still crashes after clearing the cache.", IsInternal = false, CreatedAt = now.AddHours(-10) }
                },
                AuditEntries =
                {
                    TicketAuditEntry.Create(default, "Bob Carter", TicketStatus.InProgress, TicketStatus.PendingEmployeeResponse, "StatusChanged", "Requested more information from employee"),
                }
            },
            new Ticket
            {
                Title = "New starter laptop provisioning",
                Description = "Need a laptop with dev tooling for a new engineer starting next week.",
                Priority = TicketPriority.Medium,
                Type = TicketType.ServiceRequest,
                Status = TicketStatus.Resolved,
                SubmitterName = "HR Team",
                SubmitterEmail = "hr@company.com",
                AssignedTechnicianId = alice.Id,
                CreatedAt = now.AddDays(-4),
                UpdatedAt = now.AddHours(-20),
                ResolvedAt = now.AddHours(-20),
                Notes =
                {
                    new TicketNote { AuthorId = alice.Id.ToString(), AuthorName = "Alice Nguyen", Content = "Device imaged with dev tools, handed off to HR.", IsInternal = true, CreatedAt = now.AddHours(-20) }
                },
                AuditEntries =
                {
                    TicketAuditEntry.Create(default, "Alice Nguyen", TicketStatus.InProgress, TicketStatus.Resolved, "StatusChanged", "Resolved"),
                }
            },
            new Ticket
            {
                Title = "Printer on floor 3 jamming",
                Description = "The shared printer keeps jamming on duplex jobs.",
                Priority = TicketPriority.Low,
                Type = TicketType.Incident,
                Status = TicketStatus.New,
                SubmitterName = "Sam Ellis",
                SubmitterEmail = "sam.ellis@company.com",
                AssignedTechnicianId = null,
                CreatedAt = now.AddHours(-6),
                UpdatedAt = now.AddHours(-6),
            },
            new Ticket
            {
                Title = "Password reset for SAP",
                Description = "SAP access locked after three failed attempts, needs a reset.",
                Priority = TicketPriority.High,
                Type = TicketType.Incident,
                Status = TicketStatus.New,
                SubmitterName = "Nina Patel",
                SubmitterEmail = "nina.patel@company.com",
                AssignedTechnicianId = null,
                CreatedAt = now.AddHours(-20),
                UpdatedAt = now.AddHours(-20),
            },
            new Ticket
            {
                Title = "Monitor flickering intermittently",
                Description = "External monitor flickers every few minutes, mostly when moving a window.",
                Priority = TicketPriority.Medium,
                Type = TicketType.Incident,
                Status = TicketStatus.InProgress,
                SubmitterName = "Owen Blake",
                SubmitterEmail = "owen.blake@company.com",
                AssignedTechnicianId = bob.Id,
                CreatedAt = now.AddHours(-12),
                UpdatedAt = now.AddHours(-1),
            },
            new Ticket
            {
                Title = "Upgrade accounting software for Q3 close",
                Description = "Request to upgrade the accounting suite before the quarter-end close.",
                Priority = TicketPriority.Medium,
                Type = TicketType.ServiceRequest,
                Status = TicketStatus.New,
                SubmitterName = "Finance Team",
                SubmitterEmail = "finance@company.com",
                AssignedTechnicianId = null,
                CreatedAt = now.AddHours(-40),
                UpdatedAt = now.AddHours(-40),
            },
            new Ticket
            {
                Title = "Wi-Fi drops in meeting rooms",
                Description = "Wi-Fi drops intermittently in the east wing meeting rooms.",
                Priority = TicketPriority.High,
                Type = TicketType.Incident,
                Status = TicketStatus.InProgress,
                SubmitterName = "Priya Shah",
                SubmitterEmail = "priya.shah@company.com",
                AssignedTechnicianId = carol.Id,
                CreatedAt = now.AddHours(-28),
                UpdatedAt = now.AddHours(-4),
            },
            new Ticket
            {
                Title = "Request dual monitor setup",
                Description = "Two additional monitors for the customer support pod.",
                Priority = TicketPriority.Low,
                Type = TicketType.ServiceRequest,
                Status = TicketStatus.New,
                SubmitterName = "Support Lead",
                SubmitterEmail = "support.lead@company.com",
                AssignedTechnicianId = null,
                CreatedAt = now.AddHours(-3),
                UpdatedAt = now.AddHours(-3),
            },
        };

        await context.Tickets.AddRangeAsync(tickets, ct);
        await context.SaveChangesAsync(ct);
    }
}

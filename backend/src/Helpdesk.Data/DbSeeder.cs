using Helpdesk.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(HelpdeskDbContext db)
    {
        if (!await db.StaffProfiles.AnyAsync())
        {
            db.StaffProfiles.AddRange(
                new StaffProfile
                {
                    BadgeId = "AIR1001",
                    Name = "Jamie Santos",
                    Email = "jamie.santos@airport.test"
                },
                new StaffProfile
                {
                    BadgeId = "AIR1002",
                    Name = "Mia Chen",
                    Email = "mia.chen@airport.test"
                },
                new StaffProfile
                {
                    BadgeId = "AIR1003",
                    Name = "Noah Patel",
                    Email = "noah.patel@airport.test"
                }
            );
        }

        if (!await db.Flights.AnyAsync())
        {
            db.Flights.AddRange(
                new FlightRecord
                {
                    FlightNumber = "NZ101",
                    Destination = "Wellington",
                    Gate = "Gate 8",
                    Status = "On time"
                },
                new FlightRecord
                {
                    FlightNumber = "NZ428",
                    Destination = "Christchurch",
                    Gate = "Gate 14",
                    Status = "Delayed 20 min"
                },
                new FlightRecord
                {
                    FlightNumber = "QF144",
                    Destination = "Sydney",
                    Gate = "Gate 21",
                    Status = "Boarding"
                },
                new FlightRecord
                {
                    FlightNumber = "JQ202",
                    Destination = "Queenstown",
                    Gate = "Gate 5",
                    Status = "Gate changed"
                },
                new FlightRecord
                {
                    FlightNumber = "EK449",
                    Destination = "Dubai",
                    Gate = "Gate 17",
                    Status = "On time"
                }
            );
        }

        if (!await db.Tickets.AnyAsync(x => !x.IsPerformanceTest))
        {
            var now = DateTime.UtcNow;

            db.Tickets.AddRange(
                new Ticket
                {
                    Terminal = "T2",
                    Area = "Gate 14",
                    SystemType = "Kiosk",
                    Description =
                        "Self-service kiosk screen is frozen and passengers cannot continue check-in.",
                    PassengerImpact = true,
                    FlightOpsImpact = false,
                    ReporterName = "Jamie Santos",
                    ReporterEmail = "jamie.santos@airport.test",
                    StaffId = "AIR1001",
                    Priority = TicketPriority.High,
                    Status = TicketStatus.InProgress,
                    Assignee = "Alex Morgan",
                    Workaround =
                        "Kiosk down. Please direct passengers to Counter 4.",
                    CreatedAtUtc = now.AddMinutes(-95)
                },
                new Ticket
                {
                    Terminal = "T1",
                    Area = "Check-in Zone B",
                    SystemType = "Staff Computer",
                    Description =
                        "Staff workstation cannot connect to the internal airport network.",
                    PassengerImpact = false,
                    FlightOpsImpact = false,
                    ReporterName = "Mia Chen",
                    ReporterEmail = "mia.chen@airport.test",
                    StaffId = "AIR1002",
                    Priority = TicketPriority.Medium,
                    Status = TicketStatus.New,
                    Assignee = "Unassigned",
                    CreatedAtUtc = now.AddMinutes(-38)
                },
                new Ticket
                {
                    Terminal = "T3",
                    Area = "Gate 7",
                    SystemType = "Flight Information Display",
                    Description =
                        "Gate display is showing an old flight number.",
                    PassengerImpact = true,
                    FlightOpsImpact = true,
                    ReporterName = "Noah Patel",
                    ReporterEmail = "noah.patel@airport.test",
                    StaffId = "AIR1003",
                    Priority = TicketPriority.Critical,
                    Status = TicketStatus.New,
                    Assignee = "Priya Shah",
                    CreatedAtUtc = now.AddMinutes(-72)
                },
                new Ticket
                {
                    Terminal = "T2",
                    Area = "Baggage Service Desk",
                    SystemType = "Network",
                    Description =
                        "Intermittent network connection at baggage service desk.",
                    PassengerImpact = false,
                    FlightOpsImpact = false,
                    ReporterName = "Jamie Santos",
                    ReporterEmail = "jamie.santos@airport.test",
                    StaffId = "AIR1001",
                    Priority = TicketPriority.Low,
                    Status = TicketStatus.Closed,
                    Assignee = "Alex Morgan",
                    CreatedAtUtc = now.AddMinutes(-520),
                    ResolvedAtUtc = now.AddMinutes(-410)
                },
                new Ticket
                {
                    Terminal = "T1",
                    Area = "Gate 3",
                    SystemType = "Kiosk",
                    Description = "Bag tag printer in kiosk is not printing.",
                    PassengerImpact = true,
                    FlightOpsImpact = false,
                    ReporterName = "Mia Chen",
                    ReporterEmail = "mia.chen@airport.test",
                    StaffId = "AIR1002",
                    Priority = TicketPriority.Medium,
                    Status = TicketStatus.Closed,
                    Assignee = "Priya Shah",
                    Workaround = "Use the staffed check-in counter.",
                    CreatedAtUtc = now.AddMinutes(-820),
                    ResolvedAtUtc = now.AddMinutes(-700)
                }
            );
        }

        await db.SaveChangesAsync();
    }
}

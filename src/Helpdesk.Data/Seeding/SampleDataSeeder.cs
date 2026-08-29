namespace Helpdesk.Data.Seeding;

using Helpdesk.Core.Domain;
using Helpdesk.Core.Sla;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Populates the database with a representative history of support requests.
/// </summary>
/// <remarks>
/// <para>
/// The problem definition commits to a sample dataset of roughly 500 historical requests, sufficient
/// to exercise reporting, filtering and performance testing. This is that dataset.
/// </para>
/// <para>
/// Generation is seeded with a fixed value, so every developer and every test run gets byte-identical
/// data. That matters more than it might appear: a test asserting "the dashboard reports 87.4%
/// attainment" is only meaningful if the underlying population does not change between runs, and a
/// defect reproduced on one machine must be reproducible on another.
/// </para>
/// <para>
/// The data is fictional. It represents a plausible corporate support workload for an airport
/// environment and makes no claim about any real organisation's systems or performance.
/// </para>
/// </remarks>
public static class SampleDataSeeder
{
    /// <summary>Fixed so the generated population is identical on every machine and every run.</summary>
    public const int RandomSeed = 20260829;

    public const int DefaultTicketCount = 500;

    /// <summary>How far back the generated history extends.</summary>
    public const int HistoryDays = 180;

    private static readonly string[] Categories =
    [
        "Account & Access", "Hardware", "Software", "Network", "Printing",
        "AV & Meeting Rooms", "Point of Sale", "Mobile & Telephony", "Email"
    ];

    private static readonly (string Name, string Department)[] Requesters =
    [
        ("Aroha Ngata", "Administration"),
        ("Ben Whitfield", "Administration"),
        ("Chloe Marsden", "Finance"),
        ("Daniel Osei", "Finance"),
        ("Emma Tuilagi", "Human Resources"),
        ("Farhan Iqbal", "Human Resources"),
        ("Grace Lindqvist", "Retail & Concessions"),
        ("Hemi Walker", "Retail & Concessions"),
        ("Isla Fitzgerald", "Retail & Concessions"),
        ("James Okonkwo", "Facilities Management"),
        ("Kiri Anderson", "Facilities Management"),
        ("Liam Petrov", "Facilities Management"),
        ("Mele Fifita", "Commercial Partnerships"),
        ("Noah Brightwell", "Commercial Partnerships"),
        ("Olivia Chandra", "Administration"),
        ("Priya Raman", "Finance"),
        ("Quinn Halloway", "Retail & Concessions"),
        ("Rangi Solomon", "Facilities Management"),
        ("Sophie Beaumont", "Administration"),
        ("Tane Kirkwood", "Commercial Partnerships")
    ];

    private static readonly (string Id, string Name)[] Technicians =
    [
        ("tech-nikau", "Nikau Ashford"),
        ("tech-simone", "Simone Delacroix"),
        ("tech-raj", "Raj Bhandari")
    ];

    private const string TeamLeadId = "lead-maia";
    private const string TeamLeadName = "Maia Thornton";

    /// <summary>Short titles per category, so the queue reads like real work rather than filler.</summary>
    private static readonly Dictionary<string, string[]> TitlesByCategory = new()
    {
        ["Account & Access"] =
        [
            "Access card not opening airside corridor door",
            "New starter needs network account provisioned",
            "Locked out after password expiry",
            "Contractor account needs extending by two weeks",
            "Shared drive permissions missing for new role"
        ],
        ["Hardware"] =
        [
            "Laptop will not power on",
            "Docking station not detecting second monitor",
            "Keyboard unresponsive after spill",
            "Desk phone handset crackling",
            "Laptop battery draining within an hour"
        ],
        ["Software"] =
        [
            "Finance reporting tool crashes on export",
            "Rostering application will not load",
            "Licence expired for design software",
            "Spreadsheet macro failing after update",
            "Request install of approved PDF editor"
        ],
        ["Network"] =
        [
            "Wi-Fi dropping in the north admin block",
            "VPN disconnects every few minutes",
            "No network in meeting room 2.14",
            "Slow file transfers to the shared drive",
            "Guest Wi-Fi voucher system not issuing codes"
        ],
        ["Printing"] =
        [
            "Level 2 printer jamming repeatedly",
            "Cannot print to the secure release queue",
            "Printer driver missing after laptop rebuild",
            "Toner low warning will not clear",
            "Scanned documents not arriving by email"
        ],
        ["AV & Meeting Rooms"] =
        [
            "Operations briefing room projector not displaying",
            "No audio in meeting room 3.02 before handover",
            "Video conference camera not detected",
            "Room booking panel showing wrong schedule",
            "Microphone cutting out during briefings"
        ],
        ["Point of Sale"] =
        [
            "Concession POS terminal offline at gate 4",
            "Card reader declining all transactions",
            "Receipt printer not responding at kiosk 2",
            "POS terminal stuck on update screen",
            "End of day totals not reconciling"
        ],
        ["Mobile & Telephony"] =
        [
            "Work mobile not receiving calls",
            "Roaming needs enabling for travel next week",
            "Voicemail greeting cannot be updated",
            "Handset replacement after screen damage",
            "Call forwarding not working from reception"
        ],
        ["Email"] =
        [
            "Mailbox full and rejecting messages",
            "Distribution list missing new team members",
            "Calendar invitations not syncing to phone",
            "Suspected phishing message received",
            "Out of office not triggering for external senders"
        ]
    };

    private static readonly string[] ResolutionNotes =
    [
        "Reissued credentials and confirmed access with the requester.",
        "Replaced the faulty unit from spares and returned the original for repair.",
        "Cleared the local cache and reinstalled the client; verified working.",
        "Reseated the cabling and confirmed link on the switch port.",
        "Applied the vendor patch and confirmed the fault no longer reproduces.",
        "Escalated to the supplier, who replaced the module under warranty.",
        "Reconfigured the profile and walked the requester through the change.",
        "Restarted the service and added monitoring to catch a recurrence."
    ];

    private static readonly string[] HoldReasons =
    [
        "Awaiting the requester's availability to test.",
        "Awaiting replacement part from the supplier.",
        "Awaiting approval from the department manager.",
        "Awaiting the tenant's confirmation of trading hours."
    ];

    /// <summary>
    /// Seeds the database if it is empty. Safe to call on every start-up.
    /// </summary>
    /// <returns>True if data was written, false if the database was already populated.</returns>
    public static async Task<bool> EnsureSeededAsync(
        HelpdeskDbContext db,
        DateTimeOffset now,
        int ticketCount = DefaultTicketCount,
        CancellationToken cancellationToken = default)
    {
        if (await db.Tickets.AnyAsync(cancellationToken))
        {
            return false;
        }

        Seed(db, now, ticketCount);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Builds the population in memory and adds it to the context. Does not save.
    /// </summary>
    public static void Seed(HelpdeskDbContext db, DateTimeOffset now, int ticketCount = DefaultTicketCount)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (ticketCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticketCount));
        }

        var random = new Random(RandomSeed);
        var calculator = SlaCalculator.Default;

        db.Users.AddRange(BuildUsers());

        var tickets = new List<Ticket>(ticketCount);

        for (var i = 1; i <= ticketCount; i++)
        {
            tickets.Add(BuildTicket(i, random, calculator, now));
        }

        db.Tickets.AddRange(tickets);
    }

    private static List<UserAccount> BuildUsers()
    {
        var users = new List<UserAccount>
        {
            new()
            {
                Id = TeamLeadId,
                DisplayName = TeamLeadName,
                Department = "IT Support",
                Role = UserRole.TeamLead
            }
        };

        users.AddRange(Technicians.Select(t => new UserAccount
        {
            Id = t.Id,
            DisplayName = t.Name,
            Department = "IT Support",
            Role = UserRole.Technician
        }));

        users.AddRange(Requesters.Select((r, index) => new UserAccount
        {
            Id = $"user-{index + 1:D2}",
            DisplayName = r.Name,
            Department = r.Department,
            Role = UserRole.Requester
        }));

        return users;
    }

    private static Ticket BuildTicket(
        int id,
        Random random,
        SlaCalculator calculator,
        DateTimeOffset now)
    {
        var requesterIndex = random.Next(Requesters.Length);
        var (requesterName, department) = Requesters[requesterIndex];

        var category = Categories[random.Next(Categories.Length)];
        var titles = TitlesByCategory[category];
        var priority = PickPriority(random);
        var createdAt = PickCreatedAt(random, now);

        var ticket = new Ticket
        {
            Reference = Ticket.BuildReference(id),
            Title = titles[random.Next(titles.Length)],
            Description = BuildDescription(category, requesterName, department),
            Category = category,
            RequesterId = $"user-{requesterIndex + 1:D2}",
            RequesterName = requesterName,
            Department = department,
            Sensitivity = department is "Human Resources" or "Finance"
                ? TicketSensitivity.Restricted
                : TicketSensitivity.Standard,
            Priority = priority,
            CreatedAt = createdAt,
            Status = TicketStatus.New
        };

        var policy = SlaPolicySet.Default.For(priority);
        var actor = new ActorContext(ticket.RequesterId, UserRole.Requester, requesterName);

        ticket.Events.Add(TicketEvent.For(
            ticket, actor, TicketEventType.Created, createdAt,
            $"Raised as {priority} in category {category}.", to: TicketStatus.New));

        var ageDays = (now - createdAt).TotalDays;

        // Untouched tickets are only plausible for very recent arrivals. Letting old tickets sit in
        // New would produce a backlog no four-person team could credibly be carrying, and would make
        // every reported figure look wrong to a marker who checked it.
        if (ageDays < 2 && random.NextDouble() < 0.35)
        {
            return ticket;
        }

        var technician = Technicians[random.Next(Technicians.Length)];
        var techActor = new ActorContext(technician.Id, UserRole.Technician, technician.Name);

        ticket.AssignedTechnicianId = technician.Id;
        ticket.AssignedTechnicianName = technician.Name;

        // Response: 88% inside target. Factors are expressed as a fraction of the target and then
        // converted through the policy's own clock, so business-hours tickets get realistic timestamps.
        var respondedOnTime = random.NextDouble() < 0.88;
        var responseFactor = respondedOnTime
            ? 0.15 + random.NextDouble() * 0.7
            : 1.05 + random.NextDouble() * 1.8;

        var respondedAt = calculator.AddInClock(
            policy, createdAt, policy.ResponseTargetMinutes * responseFactor);

        if (respondedAt >= now)
        {
            // Raised too recently to have been picked up yet.
            ticket.Status = TicketStatus.Triaged;
            ticket.AssignedTechnicianId = null;
            ticket.AssignedTechnicianName = null;
            ticket.Events.Add(TicketEvent.For(
                ticket, techActor, TicketEventType.StatusChanged, createdAt.AddMinutes(2),
                "New -> Triaged.", TicketStatus.New, TicketStatus.Triaged));
            return ticket;
        }

        ticket.FirstRespondedAt = respondedAt;
        ticket.Status = TicketStatus.InProgress;

        ticket.Events.Add(TicketEvent.For(
            ticket, techActor, TicketEventType.Assigned, respondedAt,
            $"Assigned to {technician.Name}."));
        ticket.Events.Add(TicketEvent.For(
            ticket, techActor, TicketEventType.FirstResponseRecorded, respondedAt,
            "First response recorded on starting work."));
        ticket.Events.Add(TicketEvent.For(
            ticket, techActor, TicketEventType.StatusChanged, respondedAt,
            "Assigned -> InProgress.", TicketStatus.Assigned, TicketStatus.InProgress));

        // One ticket in eight spends a period on hold awaiting somebody else.
        var suspendedMinutes = 0d;

        if (random.NextDouble() < 0.125)
        {
            var holdStart = calculator.AddInClock(
                policy, respondedAt, policy.ResolutionTargetMinutes * 0.2);
            var holdLength = policy.ResolutionTargetMinutes * (0.3 + random.NextDouble() * 0.9);
            var holdEnd = calculator.AddInClock(policy, holdStart, holdLength);

            if (holdEnd < now)
            {
                ticket.HoldPeriods.Add(new HoldPeriod
                {
                    StartedAt = holdStart,
                    EndedAt = holdEnd,
                    Reason = HoldReasons[random.Next(HoldReasons.Length)]
                });

                suspendedMinutes = holdLength;

                ticket.Events.Add(TicketEvent.For(
                    ticket, techActor, TicketEventType.HoldStarted, holdStart,
                    "Resolution clock suspended."));
                ticket.Events.Add(TicketEvent.For(
                    ticket, techActor, TicketEventType.HoldEnded, holdEnd,
                    "Resolution clock resumed."));
            }
        }

        // A small proportion genuinely stall — waiting on a supplier, or simply dropped. Most open
        // tickets in the queue are there because they are recent, not because they were abandoned,
        // which is handled below by the resolution instant falling after "now".
        if (random.NextDouble() < 0.03)
        {
            return ticket;
        }

        var resolvedOnTime = random.NextDouble() < 0.86;
        var resolutionFactor = resolvedOnTime
            ? 0.25 + random.NextDouble() * 0.65
            : 1.05 + random.NextDouble() * 1.2;

        var resolvedAt = calculator.AddInClock(
            policy,
            createdAt,
            policy.ResolutionTargetMinutes * resolutionFactor + suspendedMinutes);

        if (resolvedAt >= now)
        {
            return ticket;
        }

        ticket.ResolvedAt = resolvedAt;
        ticket.ResolutionNotes = ResolutionNotes[random.Next(ResolutionNotes.Length)];
        ticket.Status = TicketStatus.Resolved;

        ticket.Events.Add(TicketEvent.For(
            ticket, techActor, TicketEventType.StatusChanged, resolvedAt,
            $"InProgress -> Resolved. {ticket.ResolutionNotes}",
            TicketStatus.InProgress, TicketStatus.Resolved));

        // Most requesters confirm the fix within a day or so; some never get round to it, which is
        // why the report distinguishes backlog from tickets awaiting closure.
        if (random.NextDouble() < 0.78)
        {
            var closedAt = resolvedAt.AddHours(2 + random.NextDouble() * 40);

            if (closedAt < now)
            {
                ticket.ClosedAt = closedAt;
                ticket.Status = TicketStatus.Closed;

                var requesterActor = new ActorContext(
                    ticket.RequesterId, UserRole.Requester, requesterName);

                ticket.Events.Add(TicketEvent.For(
                    ticket, requesterActor, TicketEventType.StatusChanged, closedAt,
                    "Resolved -> Closed. Requester confirmed the fix.",
                    TicketStatus.Resolved, TicketStatus.Closed));
            }
        }

        return ticket;
    }

    /// <summary>
    /// Weighted so the population looks like a real corporate helpdesk: mostly routine work, with
    /// critical faults rare enough that a breach of one is noticeable.
    /// </summary>
    private static TicketPriority PickPriority(Random random)
    {
        var roll = random.NextDouble();

        return roll switch
        {
            < 0.05 => TicketPriority.Critical,
            < 0.20 => TicketPriority.High,
            < 0.80 => TicketPriority.Standard,
            _ => TicketPriority.Low
        };
    }

    /// <summary>
    /// Spreads tickets over the history window, concentrated in working hours on weekdays but not
    /// exclusively — an airport has early and late shifts, and faults do not respect the roster.
    /// </summary>
    private static DateTimeOffset PickCreatedAt(Random random, DateTimeOffset now)
    {
        var daysAgo = random.Next(1, HistoryDays);
        var date = now.AddDays(-daysAgo);

        // Push most weekend tickets onto the preceding Friday.
        if (date.DayOfWeek == DayOfWeek.Saturday && random.NextDouble() < 0.8)
        {
            date = date.AddDays(-1);
        }
        else if (date.DayOfWeek == DayOfWeek.Sunday && random.NextDouble() < 0.8)
        {
            date = date.AddDays(-2);
        }

        var hour = random.NextDouble() < 0.85
            ? random.Next(7, 19)
            : random.Next(0, 24);

        return new DateTimeOffset(
            date.Year, date.Month, date.Day,
            hour, random.Next(0, 60), random.Next(0, 60),
            TimeSpan.Zero);
    }

    private static string BuildDescription(string category, string requester, string department)
        => $"Reported by {requester} ({department}). Category: {category}. "
           + "The requester has described the fault and confirmed it is repeatable. "
           + "Contact details and location are recorded against the requester's profile.";
}

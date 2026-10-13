namespace Helpdesk.Core.Domain;

/// <summary>
/// USER STORY: As an IT manager, I want tickets breaching their priority SLA flagged,
/// so that I can intervene before service levels slip.
///   AC1 Given a Critical ticket open longer than its SLA window, when I view the overdue metric,
///       then it is counted as overdue.
///   AC2 Given a ticket within its SLA window, when I view the metric, then it is not counted.
///
/// The one place that decides whether a ticket is past its SLA. The dashboard metric, the
/// dashboard table, the CSV export and the ticket queue all call <see cref="IsOverdue"/>, so they
/// cannot disagree. (Before this, each had its own copy of "open and Critical", with no time
/// window at all: a Critical ticket raised a minute ago counted as overdue, and a High ticket
/// open for three days never did.)
///
/// Resolution windows, from the project's agreed priority policy:
///   Critical  4 hours            continuous clock
///   High      8 hours            continuous clock
///   Medium    3 working days     business hours (the policy calls this level "Standard")
///   Low      10 working days     business hours
/// Business hours are Monday-Friday 07:00-19:00 New Zealand time (12 hours = one working day).
///
/// Rules:
///   - Only open tickets can be overdue. Resolved and Closed tickets are never counted.
///   - "Open longer than its window" is strict: exactly on the window is still within SLA;
///     one tick past it is overdue.
///   - A creation time in the future (clock skew) counts as zero elapsed time, never negative.
///   - A priority value the code does not know gets the strictest (Critical) window, so a bad
///     value shows up on the dashboard instead of being hidden from it.
///
/// Known limitations (documented in docs/sla-overdue.md):
///   - Time spent waiting on the employee is not taken off the clock. The ticket only records
///     when the current wait started (PendingSince), not the total of earlier waits.
///   - Public holidays are treated as working days.
/// </summary>
public static class SlaPolicy
{
    public static readonly TimeSpan BusinessDayStart = TimeSpan.FromHours(7);
    public static readonly TimeSpan BusinessDayEnd = TimeSpan.FromHours(19);
    public static readonly TimeSpan WorkingDay = BusinessDayEnd - BusinessDayStart;

    public static TimeZoneInfo BusinessTimeZone { get; } = ResolveNewZealand();

    /// <summary>How long a ticket of this priority may stay open.</summary>
    public static TimeSpan ResolutionWindow(TicketPriority priority) => priority switch
    {
        TicketPriority.Critical => TimeSpan.FromHours(4),
        TicketPriority.High => TimeSpan.FromHours(8),
        TicketPriority.Medium => WorkingDay * 3,
        TicketPriority.Low => WorkingDay * 10,
        _ => TimeSpan.FromHours(4),
    };

    /// <summary>Medium and Low run on the business-hours clock; Critical and High run continuously.</summary>
    public static bool UsesBusinessHours(TicketPriority priority) =>
        priority is TicketPriority.Medium or TicketPriority.Low;

    public static bool IsOpen(TicketStatus status) =>
        status is not TicketStatus.Resolved and not TicketStatus.Closed;

    /// <summary>Time on this ticket's SLA clock between creation and <paramref name="nowUtc"/>.</summary>
    public static TimeSpan Elapsed(Ticket ticket, DateTime nowUtc)
    {
        var created = AsUtc(ticket.CreatedAt);
        var now = AsUtc(nowUtc);
        if (now <= created) return TimeSpan.Zero;

        return UsesBusinessHours(ticket.Priority)
            ? BusinessTimeBetween(created, now)
            : now - created;
    }

    /// <summary>True when the ticket is open and has been open longer than its priority's window.</summary>
    public static bool IsOverdue(Ticket ticket, DateTime nowUtc) =>
        IsOpen(ticket.Status) && Elapsed(ticket, nowUtc) > ResolutionWindow(ticket.Priority);

    /// <summary>Working time (Mon-Fri 07:00-19:00, New Zealand) between two UTC instants.</summary>
    public static TimeSpan BusinessTimeBetween(DateTime startUtc, DateTime endUtc)
    {
        if (endUtc <= startUtc) return TimeSpan.Zero;

        var start = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(startUtc), BusinessTimeZone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(endUtc), BusinessTimeZone);

        // Daylight-saving changes in New Zealand happen at 02:00-03:00, outside business hours,
        // so wall-clock arithmetic inside a single business day is exact.
        var total = TimeSpan.Zero;
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

            var open = day + BusinessDayStart;
            var close = day + BusinessDayEnd;
            var from = start > open ? start : open;
            var to = end < close ? end : close;
            if (to > from) total += to - from;
        }

        return total;
    }

    // SQLite hands DateTime values back with Kind = Unspecified; the app always stores UTC.
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static TimeZoneInfo ResolveNewZealand()
    {
        foreach (var id in new[] { "Pacific/Auckland", "New Zealand Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        throw new InvalidOperationException("The New Zealand time zone is not available on this machine; SLA business hours cannot be calculated.");
    }
}

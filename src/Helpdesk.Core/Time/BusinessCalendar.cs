namespace Helpdesk.Core.Time;

/// <summary>
/// A working calendar defined by an opening window, a set of working days, and a holiday list.
/// </summary>
/// <remarks>
/// <para>
/// The default window is 07:00–19:00 New Zealand time, Monday to Friday. The extended day reflects an
/// airport corporate support desk covering early and late shift handovers; it is configuration, not a
/// constant, so it can be changed without touching this class.
/// </para>
/// <para>
/// Instances are immutable and safe to share.
/// </para>
/// </remarks>
public sealed class BusinessCalendar : IBusinessCalendar
{
    /// <summary>Upper bound on day-by-day iteration, so a malformed input cannot spin forever.</summary>
    private const int MaxDaysScanned = 3660; // ~10 years

    private readonly HashSet<DayOfWeek> _workingDays;
    private readonly HashSet<DateOnly> _holidays;

    public BusinessCalendar(
        TimeZoneInfo timeZone,
        TimeOnly opensAt,
        TimeOnly closesAt,
        IEnumerable<DayOfWeek>? workingDays = null,
        IEnumerable<DateOnly>? holidays = null)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        if (closesAt <= opensAt)
        {
            throw new ArgumentException(
                $"Closing time ({closesAt}) must be later than opening time ({opensAt}). " +
                "Overnight working windows are not supported.",
                nameof(closesAt));
        }

        TimeZone = timeZone;
        OpensAt = opensAt;
        ClosesAt = closesAt;

        _workingDays = workingDays is null
            ? new HashSet<DayOfWeek>
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday
            }
            : new HashSet<DayOfWeek>(workingDays);

        if (_workingDays.Count == 0)
        {
            throw new ArgumentException("At least one working day is required.", nameof(workingDays));
        }

        _holidays = holidays is null ? new HashSet<DateOnly>() : new HashSet<DateOnly>(holidays);
    }

    public TimeZoneInfo TimeZone { get; }

    public TimeOnly OpensAt { get; }

    public TimeOnly ClosesAt { get; }

    public IReadOnlyCollection<DayOfWeek> WorkingDays => _workingDays;

    public IReadOnlyCollection<DateOnly> Holidays => _holidays;

    public double MinutesPerWorkingDay => (ClosesAt - OpensAt).TotalMinutes;

    /// <summary>Mon–Fri, 07:00–19:00, New Zealand time, no holidays configured.</summary>
    public static BusinessCalendar Default { get; } =
        new(NewZealandTimeZone(), new TimeOnly(7, 0), new TimeOnly(19, 0));

    /// <summary>
    /// Resolves the New Zealand time zone across platforms, falling back to UTC rather than throwing
    /// if the host has no time zone database.
    /// </summary>
    public static TimeZoneInfo NewZealandTimeZone()
    {
        foreach (var id in new[] { "Pacific/Auckland", "New Zealand Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the next identifier.
            }
            catch (InvalidTimeZoneException)
            {
                // Corrupt entry; try the next identifier.
            }
        }

        return TimeZoneInfo.Utc;
    }

    public bool IsWorkingDay(DateOnly date) =>
        _workingDays.Contains(date.DayOfWeek) && !_holidays.Contains(date);

    public double ElapsedBusinessMinutes(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            return 0d;
        }

        var start = ToCalendarLocal(from);
        var end = ToCalendarLocal(to);

        var total = 0d;
        var day = DateOnly.FromDateTime(start);
        var lastDay = DateOnly.FromDateTime(end);

        for (var scanned = 0; day <= lastDay && scanned < MaxDaysScanned; day = day.AddDays(1), scanned++)
        {
            if (!IsWorkingDay(day))
            {
                continue;
            }

            var opens = day.ToDateTime(OpensAt);
            var closes = day.ToDateTime(ClosesAt);

            var windowStart = start > opens ? start : opens;
            var windowEnd = end < closes ? end : closes;

            if (windowEnd > windowStart)
            {
                total += (windowEnd - windowStart).TotalMinutes;
            }
        }

        return total;
    }

    public DateTimeOffset AddBusinessMinutes(DateTimeOffset from, double minutes)
    {
        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minutes), minutes, "Cannot add a negative amount of working time.");
        }

        var cursor = ToCalendarLocal(from);
        var remaining = minutes;
        var day = DateOnly.FromDateTime(cursor);

        for (var scanned = 0; scanned < MaxDaysScanned; day = day.AddDays(1), scanned++)
        {
            if (!IsWorkingDay(day))
            {
                continue;
            }

            var opens = day.ToDateTime(OpensAt);
            var closes = day.ToDateTime(ClosesAt);

            // On the first working day the clock starts at the later of the cursor and opening time;
            // on every later day the cursor is already in the past, so it starts at opening.
            var windowStart = cursor > opens ? cursor : opens;

            if (windowStart >= closes)
            {
                continue;
            }

            // A zero-minute target resolves to the first working instant at or after the start.
            if (remaining <= 0d)
            {
                return FromCalendarLocal(windowStart);
            }

            var available = (closes - windowStart).TotalMinutes;

            if (available >= remaining)
            {
                return FromCalendarLocal(windowStart.AddMinutes(remaining));
            }

            remaining -= available;
        }

        throw new InvalidOperationException(
            $"Could not consume {minutes} working minutes within {MaxDaysScanned} days. " +
            "Check the calendar's working days and holiday list.");
    }

    private DateTime ToCalendarLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime;

    private DateTimeOffset FromCalendarLocal(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        // During a daylight-saving "spring forward" gap the wall-clock time does not exist. Nudging
        // forward by the adjustment lands on a real instant rather than throwing at the user.
        if (TimeZone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        var offset = TimeZone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }
}

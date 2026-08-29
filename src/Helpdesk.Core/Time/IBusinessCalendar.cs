namespace Helpdesk.Core.Time;

/// <summary>
/// Converts between wall-clock time and working time.
/// </summary>
/// <remarks>
/// Lower-priority targets are expressed in business days rather than elapsed hours — a ten-day target
/// raised on a Friday afternoon must not expire over a weekend. This interface is what makes that
/// distinction testable in isolation.
/// </remarks>
public interface IBusinessCalendar
{
    /// <summary>Working minutes falling between two instants. Zero when <paramref name="to"/> is not after <paramref name="from"/>.</summary>
    double ElapsedBusinessMinutes(DateTimeOffset from, DateTimeOffset to);

    /// <summary>
    /// The instant reached by consuming <paramref name="minutes"/> of working time starting at
    /// <paramref name="from"/>. If <paramref name="from"/> falls outside working hours, counting
    /// begins at the next opening.
    /// </summary>
    DateTimeOffset AddBusinessMinutes(DateTimeOffset from, double minutes);

    /// <summary>Whether the given date is a working day (not a weekend, not a holiday).</summary>
    bool IsWorkingDay(DateOnly date);

    /// <summary>Working minutes available in one full working day.</summary>
    double MinutesPerWorkingDay { get; }
}

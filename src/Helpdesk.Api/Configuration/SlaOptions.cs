namespace Helpdesk.Api.Configuration;

using System.Globalization;
using Helpdesk.Core.Domain;
using Helpdesk.Core.Sla;
using Helpdesk.Core.Time;

/// <summary>
/// Binds the SLA section of configuration into the domain's policy types.
/// </summary>
/// <remarks>
/// This class is the maintainability requirement made concrete. Priority definitions, target times,
/// the warning threshold, working hours and the holiday list all live in <c>appsettings.json</c>; the
/// service manager can retune any of them and restart, with no code change and no redeployment of a
/// rebuilt binary. Everything is validated at start-up, so a malformed edit fails loudly at boot
/// rather than quietly producing wrong figures in the management report.
/// </remarks>
public sealed class SlaOptions
{
    public const string SectionName = "Sla";

    public BusinessCalendarOptions Calendar { get; set; } = new();

    public List<SlaPolicyOptions> Policies { get; set; } = [];

    public BusinessCalendar ToCalendar() => Calendar.Build();

    public SlaPolicySet ToPolicySet()
    {
        if (Policies.Count == 0)
        {
            throw new InvalidOperationException(
                $"Configuration section '{SectionName}:Policies' is empty. All four priority bands "
                + "must be defined.");
        }

        return SlaPolicySet.Create(Policies.Select(p => p.Build()));
    }
}

/// <summary>Working-hours configuration.</summary>
public sealed class BusinessCalendarOptions
{
    public string TimeZone { get; set; } = "Pacific/Auckland";

    public string OpensAt { get; set; } = "07:00";

    public string ClosesAt { get; set; } = "19:00";

    public List<string> WorkingDays { get; set; } =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];

    /// <summary>Dates the desk is closed, as yyyy-MM-dd. Public holidays go here.</summary>
    public List<string> Holidays { get; set; } = [];

    public BusinessCalendar Build()
    {
        var timeZone = ResolveTimeZone(TimeZone);

        var days = WorkingDays
            .Select(d => Enum.TryParse<DayOfWeek>(d, ignoreCase: true, out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"'{d}' in Sla:Calendar:WorkingDays is not a day of the week."))
            .ToList();

        var holidays = Holidays
            .Select(h => DateOnly.TryParse(h, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"'{h}' in Sla:Calendar:Holidays is not a date in yyyy-MM-dd form."))
            .ToList();

        return new BusinessCalendar(
            timeZone,
            ParseTime(OpensAt, nameof(OpensAt)),
            ParseTime(ClosesAt, nameof(ClosesAt)),
            days,
            holidays);
    }

    private static TimeOnly ParseTime(string value, string field) =>
        TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"'{value}' in Sla:Calendar:{field} is not a time in HH:mm form.");

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Windows and Linux disagree on time zone identifiers, and a group working across both
            // should not have the API refuse to start over it.
            var fallback = BusinessCalendar.NewZealandTimeZone();

            Console.Error.WriteLine(
                $"Time zone '{id}' was not found on this host; falling back to '{fallback.Id}'.");

            return fallback;
        }
    }
}

/// <summary>One priority band's configured targets.</summary>
public sealed class SlaPolicyOptions
{
    public string Priority { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public double ResponseTargetMinutes { get; set; }

    public double ResolutionTargetMinutes { get; set; }

    /// <summary>"Continuous" or "BusinessHours".</summary>
    public string Clock { get; set; } = nameof(SlaClockType.BusinessHours);

    public double WarningThreshold { get; set; } = 0.75;

    public SlaPolicy Build()
    {
        if (!Enum.TryParse<TicketPriority>(Priority, ignoreCase: true, out var priority))
        {
            throw new InvalidOperationException(
                $"'{Priority}' in Sla:Policies is not a known priority. Expected one of: "
                + string.Join(", ", Enum.GetNames<TicketPriority>()) + ".");
        }

        if (!Enum.TryParse<SlaClockType>(Clock, ignoreCase: true, out var clock))
        {
            throw new InvalidOperationException(
                $"'{Clock}' in Sla:Policies for {Priority} is not a known clock type. Expected "
                + string.Join(" or ", Enum.GetNames<SlaClockType>()) + ".");
        }

        return new SlaPolicy(
            priority,
            string.IsNullOrWhiteSpace(DisplayName) ? priority.ToString() : DisplayName,
            Description,
            ResponseTargetMinutes,
            ResolutionTargetMinutes,
            clock,
            WarningThreshold);
    }
}
